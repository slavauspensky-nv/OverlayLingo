using System.Runtime.InteropServices;
using System.Text;

namespace WordPeek;

/// <summary>
/// Запасной способ узнать, что печатает человек, для приложений, которые не отдают
/// текст через UI Automation (Chrome, Telegram, Electron). Пассивный перехват
/// клавиатуры: ничего не блокирует и не подменяет, только запоминает набранное.
/// </summary>
internal sealed class KeyboardFallback : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmQuit = 0x0012;

    private const uint VkBack = 0x08;
    private const uint VkTab = 0x09;
    private const uint VkReturn = 0x0D;
    private const uint VkEscape = 0x1B;
    private const uint VkEnd = 0x23;
    private const uint VkHome = 0x24;
    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;
    private const uint VkDelete = 0x2E;
    private const uint VkPacket = 0xE7;

    private const int MaxLength = 600;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _proc;
    private readonly StringBuilder _buffer = new();
    private readonly object _gate = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _stop;
    private IntPtr _window;
    private string _windowTitle = "";
    private bool _started;
    private int _eventCount;
    private int _callCount;
    private volatile bool _polling;
    private Thread? _pollThread;

    public KeyboardFallback()
    {
        _proc = HookCallback;
    }

    /// <summary>Первый параметр — набранный текст, второй и третий — позиция курсора мыши.</summary>
    public event Action<string, int, int>? TextChanged;

    /// <summary>Каждое перехваченное нажатие: виртуальный код, скан-код, флаги.</summary>
    public event Action<uint, uint, uint>? RawKey;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _stop = false;

        _thread = new Thread(ThreadLoop)
        {
            IsBackground = true,
            Name = "wordpeek-keys"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Если хук за это время не отзовётся ни разу — включаем обходной опрос клавиш.
        var watchdog = new Thread(Watchdog)
        {
            IsBackground = true,
            Name = "wordpeek-keys-watchdog"
        };
        watchdog.Start();
    }

    private void Watchdog()
    {
        Thread.Sleep(6000);

        if (_stop || Volatile.Read(ref _callCount) > 0)
        {
            return;
        }

        Log.Write("keyboard hook: нет ни одного вызова, включаю опрос клавиш");

        _polling = true;
        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "wordpeek-keys-poll"
        };
        _pollThread.Start();
    }

    /// <summary>Клавиши, которые реально участвуют в наборе текста.</summary>
    private static readonly int[] PolledKeys = BuildPolledKeys();

    private static int[] BuildPolledKeys()
    {
        var keys = new List<int> { 0x08, 0x09, 0x0D, 0x1B, 0x20, 0x2E, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28 };

        for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk);   // цифры
        for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk);   // буквы
        for (int vk = 0x60; vk <= 0x69; vk++) keys.Add(vk);   // цифровой блок
        for (int vk = 0xBA; vk <= 0xC0; vk++) keys.Add(vk);   // ; = , - . / `
        for (int vk = 0xDB; vk <= 0xDE; vk++) keys.Add(vk);   // [ ] ' \

        return keys.ToArray();
    }

    private void PollLoop()
    {
        var pressed = new bool[256];
        int seen = 0;

        try
        {
            _window = GetForegroundWindow();
            _windowTitle = WindowTitle(_window);
            Log.Write($"опрос клавиш: старт, окно={_window} \"{_windowTitle}\"");

            while (!_stop && _polling)
            {
                // Хук всё-таки заработал — опрос больше не нужен.
                if (Volatile.Read(ref _callCount) > 0)
                {
                    Log.Write("keyboard hook: заработал, опрос отключаю");
                    _polling = false;
                    break;
                }

                foreach (int vk in PolledKeys)
                {
                    bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                    if (down && !pressed[vk])
                    {
                        if (seen < 5)
                        {
                            seen++;
                            Log.Write($"опрос клавиш: нажатие vk=0x{vk:X2}");
                        }

                        HandleKey(new KbdLlHookStruct
                        {
                            vkCode = (uint)vk,
                            scanCode = MapVirtualKey((uint)vk, 0)
                        });
                    }

                    pressed[vk] = down;
                }

                Thread.Sleep(22);
            }

            Log.Write("опрос клавиш: остановлен");
        }
        catch (Exception ex)
        {
            Log.Write("опрос клавиш: исключение " + ex);
        }
    }

    private void ThreadLoop()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Write("keyboard hook: не установлен, код " + Marshal.GetLastWin32Error());
            return;
        }

        Log.Write("keyboard hook: установлен");

        try
        {
            while (!_stop && GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            Log.Write("keyboard hook: исключение в цикле " + ex.Message);
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        Log.Write("keyboard hook: поток завершён");
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                int call = Interlocked.Increment(ref _callCount);
                if (call <= 3 || call % 1000 == 0)
                {
                    Log.Write($"hook call #{call}: code={code} wParam=0x{wParam.ToInt64():X}");
                }
            }

            if (code >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

                int count = Interlocked.Increment(ref _eventCount);
                if (count == 1 || count % 500 == 0)
                {
                    Log.Write($"hook event #{count}: vk=0x{data.vkCode:X2} scan=0x{data.scanCode:X2}");
                }

                RawKey?.Invoke(data.vkCode, data.scanCode, data.flags);
                HandleKey(data);
            }
        }
        catch
        {
            // Хук не должен ронять приложение.
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void HandleKey(KbdLlHookStruct data)
    {
        IntPtr foreground = GetForegroundWindow();
        string title = WindowTitle(foreground);

        // Смена окна или документа (вкладки, файла) — старый буфер больше не актуален.
        if (foreground != _window || !string.Equals(title, _windowTitle, StringComparison.Ordinal))
        {
            _window = foreground;
            _windowTitle = title;
            Reset();
        }

        if (data.vkCode == VkPacket)
        {
            // Юникодный ввод (SendInput с KEYEVENTF_UNICODE, часть IME).
            Append((char)data.scanCode);
            return;
        }

        switch (data.vkCode)
        {
            case VkBack:
                RemoveLast();
                return;
            case VkReturn:
            case VkEscape:
            case VkTab:
            case VkLeft:
            case VkRight:
            case VkUp:
            case VkDown:
            case VkHome:
            case VkEnd:
            case VkDelete:
                Reset();
                return;
        }

        // Ctrl+комбинации меняют текст непредсказуемо — буфер больше не верим.
        bool control = (GetKeyState(0x11) & 0x8000) != 0;
        if (control)
        {
            switch (data.vkCode)
            {
                case 0x41: // A
                case 0x43: // C
                case 0x56: // V
                case 0x58: // X
                case 0x5A: // Z
                case 0x59: // Y
                    Reset();
                    return;
            }
        }

        string? typed = TranslateKey(data);
        if (!string.IsNullOrEmpty(typed))
        {
            Append(typed);
        }
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        try
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return "";
            }

            var buffer = new StringBuilder(length + 1);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string? TranslateKey(KbdLlHookStruct data)
    {
        try
        {
            uint threadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            IntPtr layout = GetKeyboardLayout(threadId);

            // Состояние собираем сами: GetKeyboardState у неактивного потока бывает пустым.
            var state = new byte[256];
            for (int vk = 0; vk < 256; vk++)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    state[vk] = 0x80;
                }
            }

            if ((GetKeyState(0x14) & 1) != 0)
            {
                state[0x14] = 1;
            }

            var buffer = new char[8];
            int count = ToUnicodeEx(data.vkCode, data.scanCode, state, buffer, buffer.Length, 0, layout);
            if (count <= 0 || count > buffer.Length)
            {
                return null;
            }

            return new string(buffer, 0, count);
        }
        catch
        {
            return null;
        }
    }

    private void Append(char symbol)
    {
        Append(symbol.ToString());
    }

    private void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_buffer.Length + text.Length > MaxLength)
            {
                _buffer.Remove(0, _buffer.Length - MaxLength / 2);
            }

            _buffer.Append(text);
            Raise();
        }
    }

    private void RemoveLast()
    {
        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            // Не разрываем суррогатную пару.
            if (_buffer.Length >= 2 && char.IsSurrogatePair(_buffer[^2], _buffer[^1]))
            {
                _buffer.Length -= 2;
            }
            else
            {
                _buffer.Length -= 1;
            }

            Raise();
        }
    }

    /// <summary>Тихо очищает буфер, не уведомляя подписчиков.</summary>
    public void ResetText()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    private void Reset()
    {
        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            _buffer.Clear();
            Raise();
        }
    }

    private void Raise()
    {
        string text = _buffer.ToString();

        GetCursorPos(out POINT cursor);

        try
        {
            TextChanged?.Invoke(text, cursor.X, cursor.Y);
        }
        catch (Exception ex)
        {
            Log.Write("keyboard fallback: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(500);
        _thread = null;
        _started = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scanCode, byte[] state, [Out] char[] buffer, int size, uint flags, IntPtr layout);
}
