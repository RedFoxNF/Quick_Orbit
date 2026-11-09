using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickOrbit.Models;
using QuickOrbit.Services;
using QuickOrbit.Utilities;
using ShapePath = System.Windows.Shapes.Path;

namespace QuickOrbit.Views;

/// <summary>
/// Retained-mode orbital renderer. A frame callback only moves transforms, opacity and z-order;
/// geometry and icons are built once per configuration change.
/// </summary>
public sealed class OrbitSurface : Canvas
{
    private const double DefaultIconSize = 52.0;
    private const double IconBoxPadding = 20.0;

    private readonly Canvas _stage = new();
    private readonly ScaleTransform _fitScale = new(1, 1);
    private readonly RotateTransform _parallax = new();
    private readonly TranslateTransform _parallaxShift = new();
    private readonly List<RingVisual> _rings = new();
    private readonly List<ItemVisual> _visuals = new();
    private readonly Stopwatch _clock = new();

    private AppConfig? _config;
    private IconService _icons = new();
    private Ellipse? _centerGlow;
    private Point _center = new(900, 400);
    private double _rotationSeconds;
    private double _lastFrame;
    private double _parallaxAngle;
    private double _parallaxOffsetX;
    private double _parallaxOffsetY;
    private int _hoverCount;
    private bool _running;
    private bool _loaded;
    private bool _subscribed;

    private bool CircleIcons => string.Equals(_config?.IconShape, "Circle", StringComparison.OrdinalIgnoreCase);

    private double IconSizeForOrbit(int orbitIndex)
    {
        var fallback = Math.Clamp(_config?.IconSize ?? DefaultIconSize, 32.0, 96.0);
        if (_config is null || orbitIndex < 0 || orbitIndex >= _config.Orbits.Count)
        {
            return fallback;
        }

        var size = _config.Orbits[orbitIndex].IconSize;
        return size > 0 ? Math.Clamp(size, 32.0, 96.0) : fallback;
    }

    public OrbitSurface()
    {
        ClipToBounds = true;
        Background = null;
        SnapsToDevicePixels = false;
        UseLayoutRounding = true;
        IsHitTestVisible = true;

        var transforms = new TransformGroup();
        transforms.Children.Add(_fitScale);
        transforms.Children.Add(_parallax);
        transforms.Children.Add(_parallaxShift);
        _stage.RenderTransform = transforms;
        _stage.IsHitTestVisible = true;

        Children.Add(_stage);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateLayoutMetrics(rebuildPaths: true);
        MouseMove += (_, _) => UpdatePointerTarget();
        MouseLeave += (_, _) =>
        {
            _parallaxAngle = 0;
            _parallaxOffsetX = 0;
            _parallaxOffsetY = 0;
        };
    }

    public bool AutoFit { get; set; }

    public Point AnchorPoint => new(_center.X * _fitScale.ScaleX, _center.Y * _fitScale.ScaleY);

    /// <summary>Returns the ring closest to a point in surface coordinates.</summary>
    public int GetOrbitIndexAt(Point point)
    {
        if (_config is null || _config.Orbits.Count == 0)
        {
            return 0;
        }

        var fit = Math.Max(0.01, _fitScale.ScaleX);
        var x = point.X / fit;
        var y = point.Y / fit;
        var bestIndex = 0;
        var bestDistance = double.MaxValue;

        for (var index = 0; index < _config.Orbits.Count; index++)
        {
            var orbit = _config.Orbits[index];
            var verticalRadius = Math.Max(1.0, orbit.Radius * Math.Sin(orbit.Tilt * Math.PI / 180.0));
            var dx = (x - _center.X) / Math.Max(1.0, orbit.Radius);
            var dy = (y - _center.Y) / verticalRadius;
            var distance = Math.Abs(Math.Sqrt((dx * dx) + (dy * dy)) - 1.0);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    public event Action<OrbitItem>? ItemActivated;

    public void SetConfig(AppConfig config, IconService icons)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        Build();
    }

    public void Refresh(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Build();
    }

    public void StartAnimation()
    {
        _running = true;
        _lastFrame = 0;

        if (!_loaded || _config is null)
        {
            return;
        }

        if (!NeedsFrameCallback())
        {
            UpdateItems();
            return;
        }

        Subscribe();
    }

    public void StopAnimation()
    {
        _running = false;
        Unsubscribe();
    }

    private bool NeedsFrameCallback()
    {
        if (_config is null)
        {
            return false;
        }

        return _config.ParallaxDegrees > 0.01 ||
               _config.Orbits.Any(orbit => Math.Abs(orbit.Speed) > 0.01);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _clock.Restart();
        UpdateLayoutMetrics(rebuildPaths: true);

        if (_running && NeedsFrameCallback())
        {
            Subscribe();
        }
        else
        {
            UpdateItems();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _subscribed = true;
        _lastFrame = 0;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running || !_loaded || _config is null)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (_lastFrame <= 0)
        {
            _lastFrame = now;
            UpdateItems();
            return;
        }

        var delta = Math.Clamp(now - _lastFrame, 0, 0.1);
        _lastFrame = now;

        if (_hoverCount == 0)
        {
            _rotationSeconds += delta;
        }

        UpdateParallax(delta);
        UpdateItems();
    }

    private void Build()
    {
        _hoverCount = 0;
        _stage.Children.Clear();
        _rings.Clear();
        _visuals.Clear();
        _centerGlow = null;

        if (_config is null)
        {
            return;
        }

        var accent = ColorUtil.Parse(_config.AccentColor, Color.FromRgb(100, 227, 255));
        var isDark = !string.Equals(_config.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        var lineColor = isDark ? accent : Blend(accent, Colors.Black, 0.28);

        var ringLayer = new Canvas { IsHitTestVisible = false };
        _stage.Children.Add(ringLayer);

        _centerGlow = BuildCenterGlow(accent, isDark);
        _stage.Children.Add(_centerGlow);

        for (var orbitIndex = 0; orbitIndex < _config.Orbits.Count; orbitIndex++)
        {
            var orbit = _config.Orbits[orbitIndex];
            var visual = new RingVisual(orbit);
            visual.WideGlow = CreateRingPath(lineColor, 24, 10, _config.GlowIntensity, glow: true, isDark);
            visual.SoftGlow = CreateRingPath(lineColor, 9, 20, _config.GlowIntensity, glow: true, isDark);
            visual.Halo = CreateRingPath(Colors.Black, 3.2, (byte)(isDark ? 45 : 80), 1.0, glow: false, isDark);
            visual.Line = CreateRingPath(lineColor, 1.15, 255, 1.0, glow: false, isDark);

            ringLayer.Children.Add(visual.WideGlow);
            ringLayer.Children.Add(visual.SoftGlow);
            ringLayer.Children.Add(visual.Halo);
            ringLayer.Children.Add(visual.Line);
            _rings.Add(visual);
        }

        var iconLayer = new Canvas();
        _stage.Children.Add(iconLayer);

        for (var orbitIndex = 0; orbitIndex < _config.Orbits.Count; orbitIndex++)
        {
            var orbit = _config.Orbits[orbitIndex];
            for (var itemIndex = 0; itemIndex < orbit.Items.Count; itemIndex++)
            {
                var visual = BuildItemVisual(orbit.Items[itemIndex], orbitIndex, itemIndex, accent, isDark);
                iconLayer.Children.Add(visual.Button);
                _visuals.Add(visual);
            }
        }

        _stage.Opacity = Math.Clamp(_config.OverlayOpacity, 0.2, 1.0);
        UpdateLayoutMetrics(rebuildPaths: true);
        UpdateItems();

        if (_running && NeedsFrameCallback())
        {
            StartAnimation();
        }
        else if (_running)
        {
            Unsubscribe();
            UpdateItems();
        }
    }

    private Ellipse BuildCenterGlow(Color accent, bool isDark)
    {
        var glow = new Ellipse
        {
            IsHitTestVisible = false,
            Opacity = Math.Clamp(_config?.GlowIntensity ?? 1.0, 0, 1.5)
        };

        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, (byte)(isDark ? 52 : 30)), 0));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, (byte)(isDark ? 20 : 12)), 0.42));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, 0), 1));
        brush.Freeze();
        glow.Fill = brush;
        return glow;
    }

    private static ShapePath CreateRingPath(Color color, double thickness, byte alpha, double intensity, bool glow, bool isDark)
    {
        var actualAlpha = glow
            ? (byte)Math.Clamp(alpha * intensity, 0, 255)
            : alpha;

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, (byte)(actualAlpha * 0.28)), 0));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, (byte)(actualAlpha * 0.55)), 0.34));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, actualAlpha), 0.63));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, (byte)(actualAlpha * 0.40)), 1));
        brush.Freeze();

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (glow)
        {
            pen.Freeze();
        }

        return new ShapePath
        {
            Stroke = pen.Brush,
            StrokeThickness = pen.Thickness,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false,
            Opacity = glow ? 1 : 1
        };
    }

    private ItemVisual BuildItemVisual(OrbitItem item, int orbitIndex, int itemIndex, Color accent, bool isDark)
    {
        var imageSource = _icons.GetIcon(item);
        var hasImage = imageSource is not null;
        var fallbackColor = ColorUtil.StableColor(item.Name);
        var plateColor = hasImage ? accent : fallbackColor;

        var iconSize = IconSizeForOrbit(orbitIndex);
        var iconBox = iconSize + IconBoxPadding;
        var iconCornerRadius = IconCornerRadius(iconSize);

        var glow = new Ellipse
        {
            Width = iconBox,
            Height = iconBox,
            Opacity = 0.1,
            IsHitTestVisible = false,
            Fill = CreateIconGlowBrush(plateColor)
        };

        var plate = new Border
        {
            Width = iconSize,
            Height = iconSize,
            CornerRadius = iconCornerRadius,
            Clip = CreateIconClip(iconSize),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = hasImage
                ? ColorUtil.Brush(isDark ? Color.FromArgb(226, 17, 26, 37) : Color.FromArgb(228, 239, 247, 250))
                : ColorUtil.Brush(plateColor, 180),
            BorderBrush = ColorUtil.Brush(plateColor, hasImage ? (byte)105 : (byte)210)
        };

        var image = new Image
        {
            Source = imageSource,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(Math.Max(3.0, iconSize * 0.15)),
            Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed,
            SnapsToDevicePixels = true
        };

        var fallback = new TextBlock
        {
            Text = item.FallbackLetter,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = Math.Clamp(iconSize * 0.40, 11.0, 40.0),
            Foreground = ColorUtil.Brush(ColorUtil.ContrastText(plateColor)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible
        };

        var plateContent = new Grid();
        plateContent.Children.Add(image);
        plateContent.Children.Add(fallback);
        plate.Child = plateContent;

        var hoverScale = new ScaleTransform(1, 1);
        var content = new Grid
        {
            Width = iconBox,
            Height = iconBox,
            RenderTransform = hoverScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Background = null
        };
        content.Children.Add(glow);
        content.Children.Add(plate);

        var button = new Button
        {
            Width = iconBox,
            Height = iconBox,
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            IsTabStop = false
        };
        button.Template = BuildTransparentButtonTemplate();
        button.ToolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = item.Name + (string.IsNullOrWhiteSpace(item.Target) ? string.Empty : "\n" + item.Target),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 330,
                FontSize = 12,
                Foreground = ColorUtil.Brush(Color.FromRgb(234, 242, 252))
            },
            Style = TryFindResource("ToolTipStyle") as Style
        };
        ToolTipService.SetInitialShowDelay(button, 420);
        ToolTipService.SetShowDuration(button, 8000);
        ToolTipService.SetPlacement(button, System.Windows.Controls.Primitives.PlacementMode.Mouse);
        AutomationProperties.SetName(button, item.Name);

        var visual = new ItemVisual
        {
            Item = item,
            OrbitIndex = orbitIndex,
            ItemIndex = itemIndex,
            Button = button,
            DepthTransform = new MatrixTransform(),
            HoverScale = hoverScale,
            Glow = glow,
            Plate = plate,
            IconBox = iconBox,
            Image = image,
            Fallback = fallback,
            PhaseOffset = (360.0 / Math.Max(1, _config!.Orbits[orbitIndex].Items.Count) * itemIndex) + orbitIndex * 19.0
        };
        button.RenderTransform = visual.DepthTransform;

        button.MouseEnter += (_, _) =>
        {
            if (visual.Hovered)
            {
                return;
            }

            visual.Hovered = true;
            _hoverCount++;
            AnimateHover(visual, true);
        };
        button.MouseLeave += (_, _) =>
        {
            if (!visual.Hovered)
            {
                return;
            }

            visual.Hovered = false;
            _hoverCount = Math.Max(0, _hoverCount - 1);
            AnimateHover(visual, false);
        };
        image.ImageFailed += (_, _) =>
        {
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
            plate.CornerRadius = IconCornerRadius(iconSize);
            plate.Background = ColorUtil.Brush(plateColor, 180);
            plate.BorderBrush = ColorUtil.Brush(plateColor, 210);
        };
        button.Click += (_, _) => ItemActivated?.Invoke(item);

        return visual;
    }

    private CornerRadius IconCornerRadius(double size)
    {
        return CircleIcons
            ? new CornerRadius(size / 2.0)
            : new CornerRadius(Math.Max(4.0, size * 0.27));
    }

    private Geometry CreateIconClip(double size)
    {
        Geometry clip = CircleIcons
            ? new EllipseGeometry(new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0)
            : new RectangleGeometry(new Rect(0, 0, size, size), Math.Max(4.0, size * 0.27),
                Math.Max(4.0, size * 0.27));
        clip.Freeze();
        return clip;
    }

    private static RadialGradientBrush CreateIconGlowBrush(Color color)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 135), 0));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 52), 0.48));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static ControlTemplate BuildTransparentButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static void AnimateHover(ItemVisual visual, bool entered)
    {
        var duration = TimeSpan.FromMilliseconds(entered ? 135 : 180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var targetScale = entered ? 1.15 : 1.0;
        var targetGlow = entered ? 0.95 : 0.1;

        visual.HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        visual.HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
        visual.Glow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(targetGlow, duration) { EasingFunction = easing });
    }

    private void UpdateLayoutMetrics(bool rebuildPaths)
    {
        if (_config is null)
        {
            return;
        }

        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);

        var iconBox = _config.Orbits.Count == 0
            ? DefaultIconSize + IconBoxPadding
            : _config.Orbits.Select((_, index) => IconSizeForOrbit(index) + IconBoxPadding).Max();
        var maxRadius = Math.Max(60, _config.Orbits.Count == 0 ? 180 : _config.Orbits.Max(o => o.Radius));
        var maxVertical = Math.Max(60, _config.Orbits.Count == 0
            ? 100
            : _config.Orbits.Max(o => o.Radius * Math.Sin(o.Tilt * Math.PI / 180.0)) + (iconBox / 2.0) + 4.0);
        var margin = Math.Clamp(_config.EdgeMargin, 0, 300);

        var fit = 1.0;
        if (AutoFit)
        {
            var availableWidth = Math.Max(120, width - (2 * margin) - 60);
            var availableHeight = Math.Max(120, height - (2 * margin) - 60);
            fit = Math.Min(availableWidth / ((2 * maxRadius) + iconBox + 12),
                availableHeight / ((2 * maxVertical) + iconBox + 12));
            fit = Math.Clamp(fit, 0.28, 1.0);
        }

        _fitScale.ScaleX = fit;
        _fitScale.ScaleY = fit;

        var virtualWidth = width / fit;
        var virtualHeight = height / fit;
        _stage.Width = virtualWidth;
        _stage.Height = virtualHeight;

        var pad = (iconBox / 2.0) + 4.0;
        var right = string.Equals(_config.Corner, "TopRight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_config.Corner, "BottomRight", StringComparison.OrdinalIgnoreCase);
        var bottom = string.Equals(_config.Corner, "BottomLeft", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_config.Corner, "BottomRight", StringComparison.OrdinalIgnoreCase);

        var cx = right
            ? virtualWidth - margin - maxRadius - pad
            : margin + maxRadius + pad;
        var cy = bottom
            ? virtualHeight - margin - maxVertical - pad
            : margin + maxVertical + pad;

        _center = new Point(cx, cy);
        _parallax.CenterX = cx;
        _parallax.CenterY = cy;

        if (_centerGlow is not null)
        {
            var glowWidth = maxRadius * 1.9;
            var glowHeight = maxVertical * 1.8;
            _centerGlow.Width = glowWidth;
            _centerGlow.Height = glowHeight;
            Canvas.SetLeft(_centerGlow, cx - (glowWidth / 2));
            Canvas.SetTop(_centerGlow, cy - (glowHeight / 2));
        }

        if (!rebuildPaths)
        {
            return;
        }

        for (var index = 0; index < _rings.Count && index < _config.Orbits.Count; index++)
        {
            var orbit = _config.Orbits[index];
            var visual = _rings[index];
            var verticalRadius = orbit.Radius * Math.Sin(orbit.Tilt * Math.PI / 180.0);
            var geometry = new EllipseGeometry(_center, orbit.Radius, verticalRadius);

            visual.WideGlow.Data = geometry;
            visual.SoftGlow.Data = geometry;
            visual.Halo.Data = geometry;
            visual.Line.Data = geometry;
        }

        UpdateItems();
    }

    private void UpdateItems()
    {
        if (_config is null)
        {
            return;
        }

        var states = new List<(ItemVisual Visual, double Depth)>(_visuals.Count);

        foreach (var visual in _visuals)
        {
            if (visual.OrbitIndex >= _config.Orbits.Count)
            {
                continue;
            }

            var orbit = _config.Orbits[visual.OrbitIndex];
            var angleDegrees = visual.PhaseOffset + (_rotationSeconds * orbit.Speed);
            var angle = angleDegrees * Math.PI / 180.0;
            var verticalRadius = orbit.Radius * Math.Sin(orbit.Tilt * Math.PI / 180.0);
            var x = _center.X + (orbit.Radius * Math.Cos(angle));
            var y = _center.Y + (verticalRadius * Math.Sin(angle));
            var depth = (Math.Sin(angle) + 1.0) / 2.0; // 0 = far top, 1 = near bottom
            var scale = 0.62 + (0.48 * depth);
            var opacity = visual.Hovered ? 1.0 : 0.36 + (0.64 * depth);
            var offset = (visual.IconBox / 2.0) * scale;

            // A matrix transform avoids a Canvas layout pass for every icon on every frame.
            visual.DepthTransform.Matrix = new Matrix(scale, 0, 0, scale, x - offset, y - offset);
            visual.Button.Opacity = opacity;
            states.Add((visual, depth));
        }

        // Only the relative depth order matters. Updating ZIndex solely when the order
        // changes avoids needless visual-tree reordering.
        states.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        for (var index = 0; index < states.Count; index++)
        {
            var visual = states[index].Visual;
            var z = 100 + (index * 10);
            if (visual.LastZIndex != z)
            {
                visual.LastZIndex = z;
                Panel.SetZIndex(visual.Button, z);
            }
        }
    }

    private void UpdatePointerTarget()
    {
        if (_config is null || _config.ParallaxDegrees <= 0.01 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var position = Mouse.GetPosition(this);
        var dx = (position.X / ActualWidth) - 0.5;
        var dy = (position.Y / ActualHeight) - 0.5;

        _parallaxAngle = Math.Clamp(dx * 2.0, -1.0, 1.0) * _config.ParallaxDegrees;
        _parallaxOffsetX = Math.Clamp(-dx * 2.0, -1.0, 1.0) * 4.0;
        _parallaxOffsetY = Math.Clamp(-dy * 2.0, -1.0, 1.0) * 3.0;
    }

    private void UpdateParallax(double delta)
    {
        var ease = 1.0 - Math.Exp(-9.0 * delta);
        _parallax.Angle += (_parallaxAngle - _parallax.Angle) * ease;
        _parallaxShift.X += (_parallaxOffsetX - _parallaxShift.X) * ease;
        _parallaxShift.Y += (_parallaxOffsetY - _parallaxShift.Y) * ease;
    }

    private static Color Blend(Color color, Color target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(color.R + ((target.R - color.R) * amount)),
            (byte)Math.Round(color.G + ((target.G - color.G) * amount)),
            (byte)Math.Round(color.B + ((target.B - color.B) * amount)));
    }

    private sealed class RingVisual
    {
        public RingVisual(OrbitConfig orbit)
        {
            Orbit = orbit;
        }

        public OrbitConfig Orbit { get; }
        public ShapePath WideGlow { get; set; } = null!;
        public ShapePath SoftGlow { get; set; } = null!;
        public ShapePath Halo { get; set; } = null!;
        public ShapePath Line { get; set; } = null!;
    }

    private sealed class ItemVisual
    {
        public OrbitItem Item { get; init; } = null!;
        public int OrbitIndex { get; init; }
        public int ItemIndex { get; init; }
        public Button Button { get; init; } = null!;
        public MatrixTransform DepthTransform { get; init; } = null!;
        public ScaleTransform HoverScale { get; init; } = null!;
        public Ellipse Glow { get; init; } = null!;
        public Border Plate { get; init; } = null!;
        public double IconBox { get; init; }
        public Image Image { get; init; } = null!;
        public TextBlock Fallback { get; init; } = null!;
        public double PhaseOffset { get; set; }
        public bool Hovered { get; set; }
        public int LastZIndex { get; set; } = -1;
    }
}
