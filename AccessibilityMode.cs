using System.Runtime.InteropServices;

namespace OverlayLingo;

/// <summary>
/// Chromium-приложения (Chrome, Electron, Telegram) не строят дерево доступности,
/// пока система не сообщит, что работает экранный диктор. Этот флаг и включает
/// у них UI Automation — иначе видны только заголовок окна и кнопки.
/// При выходе состояние возвращается как было.
/// </summary>
internal static class AccessibilityMode
{
    private const uint SpiGetScreenReader = 0x0046;
    private const uint SpiSetScreenReader = 0x0047;
    private const uint SpifSendChange = 0x0002;

    private static bool _wasEnabledBefore;
    private static bool _changedByUs;

    public static bool IsCurrentlyEnabled()
    {
        return IsEnabled();
    }

    public static bool TryEnable()
    {
        try
        {
            _wasEnabledBefore = IsEnabled();

            bool ok = SystemParametersInfo(SpiSetScreenReader, 1, IntPtr.Zero, SpifSendChange);
            _changedByUs = ok && !_wasEnabledBefore;

            Log.Write($"accessibility: request={ok} previous={_wasEnabledBefore}" +
                      (ok ? "" : $" error={Marshal.GetLastWin32Error()}"));
            return ok;
        }
        catch (Exception ex)
        {
            Log.Write("accessibility: " + ex.Message);
            return false;
        }
    }

    public static void Restore()
    {
        if (!_changedByUs)
        {
            return;
        }

        try
        {
            SystemParametersInfo(SpiSetScreenReader, 0, IntPtr.Zero, SpifSendChange);
            Log.Write("accessibility: restored");
        }
        catch (Exception ex)
        {
            Log.Write("accessibility restore: " + ex.Message);
        }

        _changedByUs = false;
    }

    private static bool IsEnabled()
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            if (SystemParametersInfo(SpiGetScreenReader, 0, buffer, 0))
            {
                return Marshal.ReadInt32(buffer) != 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
}
