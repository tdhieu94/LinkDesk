using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LinkDesk
{
    /// <summary>
    /// Phat PCM 16-bit 48 kHz stereo tu dien thoai qua waveOut (winmm).
    /// Giu do tre thap: dem truoc ~80 ms, bo bot khi hang doi qua ~300 ms.
    /// </summary>
    public sealed class AudioPlayer : IDisposable
    {
        const int Rate = 48000, Channels = 2, Bits = 16;
        const int BytesPerMs = Rate * Channels * Bits / 8 / 1000; // 192
        const int PrebufferMs = 80, MaxQueuedMs = 300;
        const uint WHDR_DONE = 1;
        const int WAVE_MAPPER = -1;

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormatEx { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr h, uint vol);

        static readonly int HdrSize = Marshal.SizeOf(typeof(WaveHdr));
        static readonly int FlagsOff = (int)Marshal.OffsetOf(typeof(WaveHdr), "dwFlags");

        readonly object gate = new object();
        IntPtr dev;
        readonly LinkedList<(IntPtr hdr, IntPtr data, int len)> queued = new LinkedList<(IntPtr, IntPtr, int)>();
        int queuedBytes;
        bool paused, disposed;
        public long Played, Dropped;

        public static AudioPlayer TryCreate(out string error)
        {
            error = null;
            try { return new AudioPlayer(); }
            catch (Exception e) { error = e.Message; return null; }
        }

        AudioPlayer()
        {
            var f = new WaveFormatEx { wFormatTag = 1, nChannels = Channels, nSamplesPerSec = Rate, wBitsPerSample = Bits, nBlockAlign = Channels * Bits / 8, nAvgBytesPerSec = Rate * Channels * Bits / 8 };
            int r = waveOutOpen(out dev, WAVE_MAPPER, ref f, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new InvalidOperationException("Không mở được loa của máy tính (mã " + r + ").");
            waveOutPause(dev); paused = true;
        }

        /// <summary>Am luong 0..1.</summary>
        public void SetVolume(double v)
        {
            lock (gate)
            {
                if (dev == IntPtr.Zero) return;
                uint x = (uint)(Math.Max(0, Math.Min(1, v)) * 0xFFFF);
                waveOutSetVolume(dev, x | (x << 16));
            }
        }

        /// <summary>Them mot doan PCM (bo qua byte loai o dau: offset).</summary>
        public void Push(byte[] buf, int offset, int count)
        {
            lock (gate)
            {
                if (disposed || dev == IntPtr.Zero) return;
                Reap();
                // Mang cham roi don lai: bo bot de khong tre dan
                if (queuedBytes > MaxQueuedMs * BytesPerMs) { Dropped++; return; }
                if (queued.Count == 0 && !paused) { waveOutPause(dev); paused = true; }

                IntPtr data = Marshal.AllocHGlobal(count);
                Marshal.Copy(buf, offset, data, count);
                IntPtr hdr = Marshal.AllocHGlobal(HdrSize);
                Marshal.StructureToPtr(new WaveHdr { lpData = data, dwBufferLength = (uint)count }, hdr, false);
                if (waveOutPrepareHeader(dev, hdr, HdrSize) != 0 || waveOutWrite(dev, hdr, HdrSize) != 0)
                {
                    waveOutUnprepareHeader(dev, hdr, HdrSize);
                    Marshal.FreeHGlobal(hdr); Marshal.FreeHGlobal(data);
                    return;
                }
                queued.AddLast((hdr, data, count));
                queuedBytes += count;
                Played++;
                if (paused && queuedBytes >= PrebufferMs * BytesPerMs) { waveOutRestart(dev); paused = false; }
            }
        }

        void Reap()
        {
            while (queued.Count > 0)
            {
                var b = queued.First.Value;
                if (((uint)Marshal.ReadInt32(b.hdr, FlagsOff) & WHDR_DONE) == 0) break;
                waveOutUnprepareHeader(dev, b.hdr, HdrSize);
                Marshal.FreeHGlobal(b.hdr); Marshal.FreeHGlobal(b.data);
                queuedBytes -= b.len;
                queued.RemoveFirst();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (dev == IntPtr.Zero) return;
                waveOutReset(dev);
                foreach (var b in queued)
                {
                    waveOutUnprepareHeader(dev, b.hdr, HdrSize);
                    Marshal.FreeHGlobal(b.hdr); Marshal.FreeHGlobal(b.data);
                }
                queued.Clear(); queuedBytes = 0;
                waveOutClose(dev);
                dev = IntPtr.Zero;
            }
        }
    }
}
