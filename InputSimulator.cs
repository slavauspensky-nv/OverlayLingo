using System.Runtime.InteropServices;

namespace OverlayLingo;

/// <summary>Синтез ввода: выделить всё и набрать текст. Используется для замены текста переводом.</summary>
internal static class InputSimulator
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;

    private const ushort VkControl = 0x11;
    private const ushort VkA = 0x41;
    private const ushort VkV = 0x56;

    public static void SelectAll()
    {
        SendKey(VkControl, false);
        SendKey(VkA, false);
        SendKey(VkA, true);
        SendKey(VkControl, true);
    }

    public static void Paste()
    {
        SendKey(VkControl, false);
        SendKey(VkV, false);
        SendKey(VkV, true);
        SendKey(VkControl, true);
    }

    /// <summary>Кликает левой кнопкой в точку экрана — так поле ввода точно получает фокус.</summary>
    public static void ClickAt(int x, int y)
    {
        if (!GetCursorPos(out POINT previous))
        {
            previous = new POINT { X = x, Y = y };
        }

        SetCursorPos(x, y);
        Thread.Sleep(40);

        SendMouse(LeftDown);
        Thread.Sleep(30);
        SendMouse(LeftUp);
        Thread.Sleep(60);

        SetCursorPos(previous.X, previous.Y);
    }

    private static void SendMouse(uint flags)
    {
        var input = new INPUT
        {
            type = InputMouse,
            data = new InputUnion
            {
                mouse = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        INPUT[] array = { input };
        SendInput(1, array, Marshal.SizeOf<INPUT>());
    }

    public static void TypeUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char symbol in text)
        {
            inputs.Add(MakeUnicode(symbol, false));
            inputs.Add(MakeUnicode(symbol, true));
        }

        INPUT[] array = inputs.ToArray();
        uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        if (sent != array.Length)
        {
            Log.Write($"input: отправлено {sent} из {array.Length}, ошибка {Marshal.GetLastWin32Error()}");
        }
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            data = new InputUnion
            {
                keyboard = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KeyEventKeyUp : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        INPUT[] array = { input };
        SendInput(1, array, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeUnicode(char symbol, bool keyUp)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
