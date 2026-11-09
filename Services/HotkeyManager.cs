using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickOrbit.Services;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x514F;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private HotkeyGesture _current;
    private bool _registered;
    private bool _disposed;

    public HotkeyManager(string hotkey)
    {
        var parameters = new HwndSourceParameters("QuickOrbit.HotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = HwndMessage
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        try
        {
            if (!TryParse(hotkey, out var gesture, out var error))
            {
                throw new HotkeyException(error);
            }

            if (!Register(gesture))
            {
                throw new HotkeyException(GetRegistrationError());
            }

            _current = gesture;
            _registered = true;
        }
        catch
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            throw;
        }
    }

    public string CurrentText => _current.Text;

    public event Action? Pressed;

    public bool TryChange(string text, out string error)
    {
        if (!TryParse(text, out var gesture, out error))
        {
            return false;
        }

        if (_registered && string.Equals(_current.Text, gesture.Text, StringComparison.Ordinal))
        {
            return true;
        }

        var previous = _current;
        var hadPrevious = _registered;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        if (Register(gesture))
        {
            _current = gesture;
            _registered = true;
            return true;
        }

        error = GetRegistrationError();
        if (hadPrevious && Register(previous))
        {
            _current = previous;
            _registered = true;
        }

        return false;
    }

    public static bool Validate(string text, out string error)
    {
        return TryParse(text, out _, out error);
    }

    private bool Register(HotkeyGesture gesture)
    {
        return RegisterHotKey(_source.Handle, HotkeyId, gesture.Modifiers | ModNoRepeat, gesture.VirtualKey);
    }

    private static string GetRegistrationError()
    {
        var code = Marshal.GetLastWin32Error();
        return code == 1409
            ? "Это сочетание клавиш уже занято другой программой. Выберите другое."
            : "Не удалось зарегистрировать сочетание клавиш (код Windows " + code + ").";
    }

    private static bool TryParse(string text, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Сочетание клавиш не задано.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            error = "Добавьте хотя бы один модификатор: Ctrl, Alt, Shift или Win.";
            return false;
        }

        uint modifiers = 0;
        var key = Key.None;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    if (key != Key.None)
                    {
                        error = "В сочетании должна быть ровно одна основная клавиша.";
                        return false;
                    }

                    if (!TryParseKey(part, out key))
                    {
                        error = $"Неизвестная клавиша «{part}».";
                        return false;
                    }

                    break;
            }
        }

        if (modifiers == 0)
        {
            error = "Добавьте хотя бы один модификатор: Ctrl, Alt, Shift или Win.";
            return false;
        }

        if (key == Key.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            error = "Не указана основная клавиша.";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = "Эта клавиша не поддерживается как глобальный hotkey.";
            return false;
        }

        var canonicalParts = new List<string>(4);
        if ((modifiers & ModControl) != 0) canonicalParts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) canonicalParts.Add("Alt");
        if ((modifiers & ModShift) != 0) canonicalParts.Add("Shift");
        if ((modifiers & ModWin) != 0) canonicalParts.Add("Win");
        canonicalParts.Add(KeyName(key));

        gesture = new HotkeyGesture(modifiers, virtualKey, string.Join("+", canonicalParts));
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        var alias = text.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "spacebar" => "Space",
            "del" => "Delete",
            "ins" => "Insert",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "arrowup" => "Up",
            "arrowdown" => "Down",
            "arrowleft" => "Left",
            "arrowright" => "Right",
            _ => text
        };

        return Enum.TryParse(alias, ignoreCase: true, out key) && key != Key.None;
    }

    private static string KeyName(Key key) => key switch
    {
        Key.OemPlus => "OemPlus",
        Key.OemMinus => "OemMinus",
        Key.OemComma => "OemComma",
        Key.OemPeriod => "OemPeriod",
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        _ => key.ToString()
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey, string Text);
}

public sealed class HotkeyException : Exception
{
    public HotkeyException(string message) : base(message)
    {
    }
}
