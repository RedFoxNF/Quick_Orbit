using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickOrbit.Models;
using QuickOrbit.Services;

namespace QuickOrbit.Views;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;

    private AppConfig? _config;
    private bool _shown;
    private bool _hiding;
    private bool _stickyShow;
    private bool _allowClose;
    private DateTime _ignoreDeactivationUntil;
    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Surface.ItemActivated += item =>
        {
            _stickyShow = false;
            ItemActivated?.Invoke(item);
        };
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideOverlay(immediate: true);
            }
        };
        Deactivated += (_, _) =>
        {
            if (!_shown || _hiding || _stickyShow || DateTime.UtcNow < _ignoreDeactivationUntil)
            {
                return;
            }

            // A click outside the compact overlay activates another window.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_shown && !_hiding && !IsActive)
                {
                    HideOverlay();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    public event Action<OrbitItem>? ItemActivated;

    public event Action<IReadOnlyList<OrbitItem>, Point>? ItemsDropped;

    public bool IsOverlayVisible => _shown && !_hiding;

    public int GetOrbitIndexAt(Point point) => Surface.GetOrbitIndexAt(point);

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!_shown || !DropItemFactory.CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        DropHint.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (!_shown)
        {
            return;
        }

        var items = DropItemFactory.FromDataObject(e.Data);
        if (items.Count == 0)
        {
            return;
        }

        ItemsDropped?.Invoke(items, e.GetPosition(Surface));
        e.Handled = true;
    }

    public void Configure(AppConfig config, IconService icons)
    {
        _config = config;
        Surface.SetConfig(config, icons);
        UpdateWindowBounds();
        UpdateLayout();
        UpdateAnchorOrigin();
    }

    public void ShowOverlay(bool sticky = false)
    {
        if (_shown || _hiding || _config is null)
        {
            return;
        }

        _shown = true;
        _stickyShow = sticky;
        _ignoreDeactivationUntil = DateTime.UtcNow.AddMilliseconds(2000);
        UpdateWindowBounds();

        VisualHost.Opacity = 0;
        ShowScale.ScaleX = 0.92;
        ShowScale.ScaleY = 0.92;

        Show();
        Activate();
        Root.Focus();
        Keyboard.Focus(Root);
        UpdateLayout();
        UpdateAnchorOrigin();
        Surface.StartAnimation();

        Dispatcher.BeginInvoke(new Action(AnimateIn), System.Windows.Threading.DispatcherPriority.Render);
    }

    public void HideOverlay(bool immediate = false)
    {
        if (!_shown || _hiding)
        {
            return;
        }

        _hiding = true;
        _stickyShow = false;

        if (immediate)
        {
            FinishHide();
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(115))
        {
            EasingFunction = easing
        };
        var scale = new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(115))
        {
            EasingFunction = easing
        };

        fade.Completed += (_, _) => FinishHide();
        VisualHost.BeginAnimation(OpacityProperty, fade);
        ShowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        ShowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    public void ToggleOverlay()
    {
        if (IsOverlayVisible)
        {
            HideOverlay();
        }
        else if (!_hiding)
        {
            ShowOverlay();
        }
    }

    public void ShutdownOverlay()
    {
        _allowClose = true;
        Surface.StopAnimation();
        Close();
    }

    private void AnimateIn()
    {
        if (!_shown || _hiding)
        {
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(115))
        {
            EasingFunction = easing
        };
        var scale = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(125))
        {
            EasingFunction = easing
        };

        VisualHost.BeginAnimation(OpacityProperty, fade);
        ShowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        ShowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void FinishHide()
    {
        VisualHost.BeginAnimation(OpacityProperty, null);
        ShowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        VisualHost.Opacity = 0;
        ShowScale.ScaleX = 1;
        ShowScale.ScaleY = 1;
        Surface.StopAnimation();
        Hide();
        _shown = false;
        _hiding = false;
    }

    private void UpdateWindowBounds()
    {
        if (_config is null)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var margin = Math.Clamp(_config.EdgeMargin, 0, 300);
        var maxRadius = Math.Max(60, _config.Orbits.Count == 0 ? 180 : _config.Orbits.Max(o => o.Radius));
        var maxVertical = Math.Max(60, _config.Orbits.Count == 0
            ? 120
            : _config.Orbits.Max(o => o.Radius * Math.Sin(o.Tilt * Math.PI / 180.0)) + 36);

        // A compact corner window keeps the layered surface small. This is important
        // because WPF per-pixel layered windows are composited on the CPU.
        var width = Math.Min(workArea.Width, Math.Max(360, (2 * maxRadius) + (2 * margin) + 108));
        var height = Math.Min(workArea.Height, Math.Max(320, (2 * maxVertical) + (2 * margin) + 108));

        var right = string.Equals(_config.Corner, "TopRight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_config.Corner, "BottomRight", StringComparison.OrdinalIgnoreCase);
        var bottom = string.Equals(_config.Corner, "BottomLeft", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_config.Corner, "BottomRight", StringComparison.OrdinalIgnoreCase);

        Width = width;
        Height = height;
        Left = right ? workArea.Right - width : workArea.Left;
        Top = bottom ? workArea.Bottom - height : workArea.Top;
    }

    private void UpdateAnchorOrigin()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var anchor = Surface.AnchorPoint;
        VisualHost.RenderTransformOrigin = new Point(
            Math.Clamp(anchor.X / ActualWidth, 0.0, 1.0),
            Math.Clamp(anchor.Y / ActualHeight, 0.0, 1.0));
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _stickyShow = false;
            return;
        }

        if (FindAncestor<OrbitSurface>(e.OriginalSource as DependencyObject) is not null)
        {
            // OrbitSurface itself is transparent; only its icon buttons are interactive.
            // A click that reaches the surface is a click on empty space.
        }

        e.Handled = true;
        HideOverlay();
    }

    private void Root_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideOverlay();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow);

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest)
        {
            // Fully transparent pixels of a layered window would otherwise be skipped.
            // While the menu is visible the whole compact surface must receive the click
            // so that a click on empty space can dismiss it.
            handled = true;
            return new IntPtr(HtClient);
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
        base.OnClosed(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
