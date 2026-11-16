using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LinkDesk
{
    /// <summary>
    /// Giai ma H.264 bang bo giai ma co san cua Windows (Media Foundation, CLSID_MSH264DecoderMFT),
    /// xuat NV12 roi doi sang anh 32-bit. Chi dung tren 1 luong.
    /// </summary>
    public sealed class H264Decoder : IDisposable
    {
        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType t);
        [DllImport("mfplat.dll")] static extern int MFCreateSample(out IMFSample s);
        [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer b);

        static readonly Guid CLSID_H264Decoder = new Guid("62CE7E72-4C71-4d20-B15D-452831A87D9D");
        static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        static Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        static Guid MF_LOW_LATENCY = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
        static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");

        const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
        const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
        const int MF_E_NOTACCEPTING = unchecked((int)0xC00D36B5);
        const int MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x100;
        const int MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000;
        const int MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003;

        static bool started;
        readonly IMFTransform dec;
        public readonly int Width, Height;
        int outW, outH, outStride, outSize;
        bool providesSamples;
        long ts;
        IMFSample outSample;
        public int FramesOut;

        static void Check(int hr, string what) { if (hr < 0) throw new COMException("Media Foundation " + what + " lỗi 0x" + hr.ToString("X8"), hr); }

        public H264Decoder(int width, int height)
        {
            Width = width; Height = height;
            if (!started) { Check(MFStartup(0x00020070, 0), "MFStartup"); started = true; }
            dec = (IMFTransform)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_H264Decoder, true));
            try
            {
                if (dec.GetAttributes(out var a) >= 0 && a != null) { a.SetUINT32(ref MF_LOW_LATENCY, 1); Marshal.ReleaseComObject(a); }
            }
            catch { }
            Check(MFCreateMediaType(out var it), "MFCreateMediaType");
            it.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
            it.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_H264);
            it.SetUINT64(ref MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height);
            it.SetUINT64(ref MF_MT_FRAME_RATE, (30L << 32) | 1);
            it.SetUINT32(ref MF_MT_INTERLACE_MODE, 2); // progressive
            Check(dec.SetInputType(0, it, 0), "SetInputType");
            Marshal.ReleaseComObject(it);
            PickOutputType();
            dec.ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
            dec.ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
        }

        void PickOutputType()
        {
            for (int i = 0; ; i++)
            {
                int hr = dec.GetOutputAvailableType(0, i, out var t);
                if (hr < 0) throw new COMException("Bộ giải mã H.264 của Windows không có định dạng NV12", hr);
                t.GetGUID(ref MF_MT_SUBTYPE, out var sub);
                if (sub == MFVideoFormat_NV12)
                {
                    Check(dec.SetOutputType(0, t, 0), "SetOutputType");
                    long fs = 0;
                    t.GetUINT64(ref MF_MT_FRAME_SIZE, out fs);
                    outW = (int)(fs >> 32); outH = (int)(fs & 0xFFFFFFFF);
                    if (outW <= 0 || outH <= 0) { outW = Width; outH = Height; }
                    outStride = t.GetUINT32(ref MF_MT_DEFAULT_STRIDE, out int st) >= 0 && st > 0 ? st : outW;
                    Marshal.ReleaseComObject(t);
                    break;
                }
                Marshal.ReleaseComObject(t);
            }
            Check(dec.GetOutputStreamInfo(0, out var info), "GetOutputStreamInfo");
            providesSamples = (info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
            outSize = Math.Max(info.cbSize, outStride * outH * 3 / 2);
            if (outSample != null) { Marshal.ReleaseComObject(outSample); outSample = null; }
        }

        /// <summary>Dua 1 goi Annex-B vao; goi onFrame cho moi anh giai ma xong.</summary>
        public void Decode(byte[] data, int offset, int count, Action<Bitmap, int, int> onFrame)
        {
            Check(MFCreateMemoryBuffer(count, out var buf), "MFCreateMemoryBuffer");
            Check(buf.Lock(out IntPtr p, out _, out _), "Lock");
            Marshal.Copy(data, offset, p, count);
            buf.Unlock();
            buf.SetCurrentLength(count);
            Check(MFCreateSample(out var s), "MFCreateSample");
            s.AddBuffer(buf);
            s.SetSampleTime(ts); s.SetSampleDuration(333333); ts += 333333;
            try
            {
                int hr = dec.ProcessInput(0, s, 0);
                if (hr == MF_E_NOTACCEPTING) { Drain(onFrame); hr = dec.ProcessInput(0, s, 0); }
                Check(hr, "ProcessInput");
                Drain(onFrame);
            }
            finally
            {
                Marshal.ReleaseComObject(s);
                Marshal.ReleaseComObject(buf);
            }
        }

        void Drain(Action<Bitmap, int, int> onFrame)
        {
            for (int guard = 0; guard < 16; guard++)
            {
                IntPtr sp = IntPtr.Zero;
                if (!providesSamples)
                {
                    if (outSample == null)
                    {
                        Check(MFCreateSample(out outSample), "MFCreateSample");
                        Check(MFCreateMemoryBuffer(outSize, out var ob), "MFCreateMemoryBuffer");
                        outSample.AddBuffer(ob);
                        Marshal.ReleaseComObject(ob);
                    }
                    sp = Marshal.GetComInterfaceForObject(outSample, typeof(IMFSample));
                }
                var db = new MftOutputDataBuffer { dwStreamID = 0, pSample = sp };
                int hr;
                try { hr = dec.ProcessOutput(0, 1, ref db, out _); }
                finally { if (db.pEvents != IntPtr.Zero) Marshal.Release(db.pEvents); }
                if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) { if (sp != IntPtr.Zero) Marshal.Release(sp); return; }
                if (hr == MF_E_TRANSFORM_STREAM_CHANGE) { if (sp != IntPtr.Zero) Marshal.Release(sp); PickOutputType(); continue; }
                if (hr < 0) { if (sp != IntPtr.Zero) Marshal.Release(sp); Check(hr, "ProcessOutput"); }
                IMFSample got;
                if (providesSamples) got = (IMFSample)Marshal.GetObjectForIUnknown(db.pSample);
                else got = outSample;
                try { Emit(got, onFrame); }
                finally
                {
                    if (providesSamples) { Marshal.ReleaseComObject(got); Marshal.Release(db.pSample); }
                    else if (sp != IntPtr.Zero) Marshal.Release(sp);
                }
            }
        }

        void Emit(IMFSample s, Action<Bitmap, int, int> onFrame)
        {
            Check(s.ConvertToContiguousBuffer(out var b), "ConvertToContiguousBuffer");
            try
            {
                Check(b.Lock(out IntPtr p, out _, out int cur), "Lock");
                try
                {
                    int stride = outStride;
                    // Phong khi stride bao sai: suy ra tu do dai buffer
                    if (cur > 0 && outH > 0 && (long)stride * outH * 3 / 2 > cur) stride = cur * 2 / 3 / outH;
                    int w = Math.Min(outW, Width), h = Math.Min(outH, Height);
                    var bmp = Nv12.ToBitmap(p, stride, outH, w, h);
                    FramesOut++;
                    onFrame(bmp, w, h);
                }
                finally { b.Unlock(); }
            }
            finally { Marshal.ReleaseComObject(b); }
        }

        public void Dispose()
        {
            try { if (outSample != null) Marshal.ReleaseComObject(outSample); } catch { }
            try { Marshal.ReleaseComObject(dec); } catch { }
        }
    }

    public static class Nv12
    {
        /// <summary>NV12 (BT.601, dai gioi han) -> anh 32-bit.</summary>
        public static unsafe Bitmap ToBitmap(IntPtr src, int stride, int planeH, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte* y0 = (byte*)src;
                byte* uv0 = y0 + (long)stride * planeH;
                for (int y = 0; y < h; y++)
                {
                    byte* yr = y0 + (long)y * stride;
                    byte* uvr = uv0 + (long)(y >> 1) * stride;
                    uint* dst = (uint*)((byte*)bd.Scan0 + (long)y * bd.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        int c = (yr[x] - 16) * 298;
                        int u = uvr[x & ~1] - 128, v = uvr[(x & ~1) + 1] - 128;
                        int r = (c + 409 * v + 128) >> 8;
                        int g = (c - 100 * u - 208 * v + 128) >> 8;
                        int b = (c + 516 * u + 128) >> 8;
                        if (r < 0) r = 0; else if (r > 255) r = 255;
                        if (g < 0) g = 0; else if (g > 255) g = 255;
                        if (b < 0) b = 0; else if (b > 255) b = 255;
                        dst[x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }
}
