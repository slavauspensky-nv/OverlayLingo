using System.Windows.Interop;

namespace OverlayLingo;

internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x5750;

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    public bool TryRegister(uint modifiers, uint key)
    {
        EnsureSource();

        Unregister();

        bool ok = Native.RegisterHotKey(_source!.Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, key);
        _registered = ok;

        if (!ok)
        {
            Log.Write($"RegisterHotKey failed for mods={modifiers} key={key}");
        }

        return ok;
    }

    public void Unregister()
    {
        if (_source is not null && _registered)
        {
            Native.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("OverlayLingoHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = Native.HWND_MESSAGE
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            try
            {
                Pressed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Write("hotkey: " + ex.Message);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }
}
