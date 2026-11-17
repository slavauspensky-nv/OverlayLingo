using System.Runtime.InteropServices;

namespace WordPeek;

/// <summary>
/// Собственный аудиовыход через waveOut. PlaySound в системе срабатывает
/// только на первый вызов, поэтому микшируем короткие щелчки сами.
/// </summary>
internal sealed class WaveOutMixer : IDisposable
{
    private const int Rate = 22050;
    private const int BufferMs = 10;
    private const int BufferCount = 3;
    private const int WaveMapper = -1;

    private const uint CallbackFunction = 0x00030000;
    private const uint WaveHeaderDone = 0x00000001;
    private const uint WaveHeaderPrepared = 0x00000002;

    private delegate void WaveOutProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint Length;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr handle, int device, ref WaveFormatEx format,
        WaveOutProc? callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr handle);

    private readonly object _lock = new();
    private readonly List<(short[] Samples, long Start, float Gain)> _pending = new();
    private readonly IntPtr[] _headers;
    private readonly IntPtr[] _buffers;
    private readonly WaveOutProc _callback;
    private readonly int _frames;

    private IntPtr _device;
    private long _written;
    private int _buffersDone;
    private bool _disposed;

    public WaveOutMixer()
    {
        _frames = Rate * BufferMs / 1000;
        _headers = new IntPtr[BufferCount];
        _buffers = new IntPtr[BufferCount];
        _callback = OnBufferDone;

        var format = new WaveFormatEx
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSec = Rate,
            AvgBytesPerSec = Rate * 2,
            BlockAlign = 2,
            BitsPerSample = 16,
            Size = 0
        };

        int result = waveOutOpen(out _device, WaveMapper, ref format, _callback, IntPtr.Zero, CallbackFunction);

        if (result != 0)
        {
            Log.Write($"mixer: waveOutOpen не удался, код {result}");
            return;
        }

        for (int i = 0; i < BufferCount; i++)
        {
            int bytes = _frames * 2;
            _buffers[i] = Marshal.AllocHGlobal(bytes);
            _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());

            var header = new WaveHeader { Data = _buffers[i], Length = (uint)bytes };
            Marshal.StructureToPtr(header, _headers[i], false);

            int prepared = waveOutPrepareHeader(_device, _headers[i], Marshal.SizeOf<WaveHeader>());
            int written = waveOutWrite(_device, _headers[i], Marshal.SizeOf<WaveHeader>());

            if (i == 0)
            {
                Log.Write($"mixer: prepare={prepared}, write={written}, буфер {_frames} сэмплов");
            }

            _written += _frames;
        }

        Log.Write("mixer: устройство открыто, звук через waveOut");
    }

    public bool IsReady => _device != IntPtr.Zero;

    /// <summary>Ставит щелчок в очередь с учётом громкости (0..1).</summary>
    public void Add(short[] samples, float gain = 1f)
    {
        if (!IsReady || samples.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            long start = _written;
            _pending.Add((samples, start, Math.Clamp(gain, 0f, 1f)));
        }
    }

    private void OnBufferDone(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (_disposed)
        {
            return;
        }

        if (message != 0x3BD) // WOM_DONE
        {
            return;
        }

        try
        {
            var header = Marshal.PtrToStructure<WaveHeader>(param1);

            lock (_lock)
            {
                long start = _written;

                var mix = new float[_frames];

                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var (samples, position, gain) = _pending[i];

                    for (int n = 0; n < _frames; n++)
                    {
                        long index = start + n - position;

                        if (index >= 0 && index < samples.Length)
                        {
                            mix[n] += samples[index] / 32768f * gain;
                        }
                    }

                    if (position + samples.Length <= start)
                    {
                        _pending.RemoveAt(i);
                    }
                }

                for (int n = 0; n < _frames; n++)
                {
                    short value = (short)(Math.Clamp(mix[n], -1f, 1f) * 32000);
                    Marshal.WriteInt16(header.Data, n * 2, value);
                }

                header.Length = (uint)(_frames * 2);
                header.Flags &= ~WaveHeaderDone;
                Marshal.StructureToPtr(header, param1, false);

                _written += _frames;

                _buffersDone++;

            }

            int requeued = waveOutWrite(_device, param1, Marshal.SizeOf<WaveHeader>());

            if (requeued != 0 && _buffersDone < 3)
            {
                Log.Write($"mixer: буфер не принят, код {requeued}");
            }
        }
        catch (Exception ex)
        {
            Log.Write("mixer: ошибка в буфере " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed || _device == IntPtr.Zero)
        {
            _disposed = true;
            return;
        }

        _disposed = true;

        try
        {
            waveOutReset(_device);
            waveOutClose(_device);

            for (int i = 0; i < _headers.Length; i++)
            {
                Marshal.FreeHGlobal(_headers[i]);
                Marshal.FreeHGlobal(_buffers[i]);
            }
        }
        catch
        {
        }

        _device = IntPtr.Zero;
    }
}
