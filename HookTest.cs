using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OverlayLingo;

/// <summary>
/// Проверяет пассивный перехват клавиатуры: сам вводит текст через SendInput
/// и смотрит, попал ли он в буфер.
/// </summary>
internal static class HookTest
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;

    public static void Run()
    {
        var log = new StringBuilder();
        var hook = new KeyboardFallback();
        int rawCount = 0;

        hook.RawKey += (vk, scan, flags) =>
        {
            if (rawCount++ < 12)
            {
                log.AppendLine($"raw vk=0x{vk:X2} scan=0x{scan:X2} flags=0x{flags:X}");
            }
        };

        hook.TextChanged += (text, x, y) => log.AppendLine($"text=\"{text}\" at ({x},{y})");

        hook.Start();
        Thread.Sleep(900);

        SendUnicode("hello world");
        Thread.Sleep(600);
        SendUnicode(" привет");
        Thread.Sleep(900);

        hook.Dispose();
        log.AppendLine("done");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "hooktest.log"), log.ToString());
    }

    private static void SendUnicode(string text)
    {
        var inputs = new List<INPUT>();

        foreach (char symbol in text)
        {
            inputs.Add(MakeKey(symbol, false));
            inputs.Add(MakeKey(symbol, true));
        }

        if (inputs.Count > 0)
        {
            INPUT[] array = inputs.ToArray();
            uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
            Log.Write($"hooktest: SendInput отправлено={sent} из {array.Length}");
        }
    }

    private static INPUT MakeKey(char symbol, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            data = new InputUnion
            {
                keyboard = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = symbol,
                    dwFlags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public HARDWAREINPUT hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
