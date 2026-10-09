using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Tachograph.App;

public enum HotkeyAction
{
    SpeakStatus, SpeakToday, ToggleHistory, HistoryPrevDay, HistoryNextDay,
    ToggleSpeech, ToggleWindow, Silence
}

public static class HotkeyInfo
{
    public static string Title(HotkeyAction a) => a switch
    {
        HotkeyAction.SpeakStatus => "Speak full status",
        HotkeyAction.SpeakToday => "Speak today's summary",
        HotkeyAction.ToggleHistory => "Show / hide history",
        HotkeyAction.HistoryPrevDay => "History: previous day",
        HotkeyAction.HistoryNextDay => "History: next day",
        HotkeyAction.ToggleSpeech => "Spoken warnings on / off",
        HotkeyAction.ToggleWindow => "Show / hide instrument",
        HotkeyAction.Silence => "Silence voice and dismiss banner",
        _ => a.ToString()
    };

    public static string Default(HotkeyAction a) => a switch
    {
        HotkeyAction.SpeakStatus => "Ctrl+Alt+T",
        HotkeyAction.SpeakToday => "Ctrl+Alt+S",
        HotkeyAction.ToggleHistory => "Ctrl+Alt+H",
        HotkeyAction.HistoryPrevDay => "Ctrl+Alt+PageUp",
        HotkeyAction.HistoryNextDay => "Ctrl+Alt+PageDown",
        HotkeyAction.ToggleSpeech => "Ctrl+Alt+M",
        HotkeyAction.ToggleWindow => "Ctrl+Alt+W",
        HotkeyAction.Silence => "Ctrl+Alt+D",
        _ => ""
    };
}

/// <summary>System-wide hotkeys (they work while ETS2 has focus). Spec format: "Ctrl+Alt+T".</summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotkey = 0x0312, BaseId = 0x7A00;
    private readonly HwndSource _source;
    private readonly HashSet<HotkeyAction> _registered = new();

    public event Action<HotkeyAction>? Pressed;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
    }

    /// <summary>Registers the combination. An empty spec just unregisters the action and succeeds.</summary>
    public bool Register(HotkeyAction action, string spec)
    {
        Unregister(action);
        if (string.IsNullOrWhiteSpace(spec)) return true;
        if (!TryParse(spec, out uint mods, out uint vk)) return false;
        if (!RegisterHotKey(_source.Handle, BaseId + (int)action, mods | 0x4000 /* NOREPEAT */, vk)) return false;
        _registered.Add(action);
        return true;
    }

    public void Unregister(HotkeyAction action)
    {
        if (_registered.Remove(action)) UnregisterHotKey(_source.Handle, BaseId + (int)action);
    }

    public void UnregisterAll()
    {
        foreach (var a in _registered.ToList()) Unregister(a);
    }

    public static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var token in spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "alt": mods |= 0x1; break;
                case "ctrl": case "control": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win": mods |= 0x8; break;
                case "pagedown": vk = (uint)KeyInterop.VirtualKeyFromKey(Key.Next); break;
                case "pageup": vk = (uint)KeyInterop.VirtualKeyFromKey(Key.Prior); break;
                default:
                    if (!Enum.TryParse<Key>(token, true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    public static string Format(ModifierKeys mods, Key key)
    {
        var p = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) p.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) p.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) p.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) p.Add("Win");
        p.Add(key switch { Key.Next => "PageDown", Key.Prior => "PageUp", _ => key.ToString() });
        return string.Join("+", p);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32() - BaseId;
            if (Enum.IsDefined(typeof(HotkeyAction), id)) { Pressed?.Invoke((HotkeyAction)id); handled = true; }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(Hook);
    }
}
