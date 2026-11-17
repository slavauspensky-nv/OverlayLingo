using System.IO;
using Microsoft.Win32;

namespace OverlayLingo;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OverlayLingo";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                string exe = Path.Combine(AppContext.BaseDirectory, "OverlayLingo.exe");
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Write("autostart: " + ex.Message);
            return false;
        }
    }
}
