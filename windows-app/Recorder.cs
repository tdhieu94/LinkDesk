using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace LinkDesk
{
    /// <summary>
    /// Quay man hinh dien thoai (hinh dang hien tren may tinh) ra tep.
    /// Uu tien MP4 (H.264 + AAC qua Media Foundation cua Windows); neu khong duoc thi ghi AVI MJPEG (tu viet, luon chay).
    /// </summary>
    public sealed class ScreenRecorder : IDisposable
    {
        interface ISink
        {
            void Video(Bitmap frame, long t);   // frame dung kich thuoc W x H, t don vi 100 ns
            void Audio(byte[] pcm, int off, int len);
            void Close(long endT);
        }

        public string FilePath { get; private set; }
        public bool IsMp4 { get; private set; }
        public int W { get; private set; }
        public int H { get; private set; }
        public long Frames;
        public TimeSpan Elapsed => clock.Elapsed;
        public string Error;

        readonly Stopwatch clock = new Stopwatch();
        readonly object gate = new object();
        readonly Queue<object> queue = new Queue<object>();   // Bitmap+thoi gian hoac byte[] am thanh
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        readonly bool withAudio;
        ISink sink;
        volatile bool stopping;
        long audioSamples; // so mau am thanh da ghi (48 kHz)
        bool audioStarted;

        sealed class FrameItem { public Bitmap B; public long T; }

        ScreenRecorder(string basePath, int w, int h, bool audio)
        {
            W = Math.Max(2, w & ~1); H = Math.Max(2, h & ~1);
            withAudio = audio;
            string err = null;
            try { sink = new Mp4Sink(basePath + ".mp4", W, H, audio); FilePath = basePath + ".mp4"; IsMp4 = true; }
            catch (Exception e) { err = e.Message; sink = null; try { File.Delete(basePath + ".mp4"); } catch { } }
            if (sink == null) { sink = new AviSink(basePath + ".avi", W, H, audio); FilePath = basePath + ".avi"; }
            Error = err;
            clock.Start();
            thread = new Thread(Loop) { IsBackground = true, Name = "recorder" };
            thread.Start();
        }

        public static ScreenRecorder Start(string basePath, int w, int h, bool audio) => new ScreenRecorder(basePath, w, h, audio);

        /// <summary>Goi tren luong giao dien moi khi co khung moi. Sao chep anh ngay (anh goc se bi huy sau do).</summary>
        public void AddFrame(Bitmap src, int w, int h)
        {
            if (stopping || src == null) return;
            lock (gate) { if (queue.OfType<FrameItem>().Count() >= 3) return; } // may cham: bo bot khung
            var b = new Bitmap(W, H, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(b))
            {
                if (w == W && h == H) g.DrawImage(src, new Rectangle(0, 0, W, H), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
                else
                {
                    // Dien thoai xoay hoac doi do net: giu kich thuoc tep, them vien den
                    g.Clear(Color.Black);
                    float s = Math.Min((float)W / w, (float)H / h);
                    int dw = (int)(w * s), dh = (int)(h * s);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    g.DrawImage(src, new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
                }
            }
            lock (gate) queue.Enqueue(new FrameItem { B = b, T = clock.Elapsed.Ticks });
            wake.Set();
        }

        /// <summary>Goi am thanh [3][PCM 16-bit 48 kHz stereo].</summary>
        public void AddAudio(byte[] pkt)
        {
            if (!withAudio || stopping || pkt == null || pkt.Length < 5) return;
            var copy = new byte[pkt.Length - 1];
            Buffer.BlockCopy(pkt, 1, copy, 0, copy.Length);
            lock (gate) queue.Enqueue(copy);
            wake.Set();
        }

        void Loop()
        {
            byte[] zeros = new byte[48000 * 4 / 10];
            while (true)
            {
                object item = null;
                lock (gate) if (queue.Count > 0) item = queue.Dequeue();
                if (item == null)
                {
                    if (stopping) break;
                    wake.WaitOne(200);
                    continue;
                }
                try
                {
                    if (item is FrameItem f) { using (f.B) sink.Video(f.B, f.T); Interlocked.Increment(ref Frames); }
                    else if (item is byte[] pcm)
                    {
                        // Dien thoai bo qua khoang lang -> chen lai im lang cho khop thoi gian
                        long now = clock.Elapsed.Ticks;
                        long expect = now * 48 / 10000 - pcm.Length / 4;
                        if (!audioStarted) { audioStarted = true; audioSamples = Math.Max(0, expect); WriteSilence(audioSamples, zeros); }
                        else if (expect - audioSamples > 4800) { long gap = expect - audioSamples; WriteSilence(gap, zeros); audioSamples += gap; }
                        sink.Audio(pcm, 0, pcm.Length);
                        audioSamples += pcm.Length / 4;
                    }
                }
                catch (Exception e) { Error = e.Message; }
            }
            try { sink.Close(clock.Elapsed.Ticks); } catch (Exception e) { Error = e.Message; }
        }

        void WriteSilence(long samples, byte[] zeros)
        {
            long bytes = samples * 4;
            while (bytes > 0) { int n = (int)Math.Min(zeros.Length, bytes); sink.Audio(zeros, 0, n); bytes -= n; }
        }

        /// <summary>Dung va dong tep (cho toi da 15 giay).</summary>
        public void Stop()
        {
            if (stopping) return;
            stopping = true;
            clock.Stop();
            wake.Set();
            thread.Join(15000);
        }

        public void Dispose() { Stop(); }

        // ================================================================== MP4 qua Media Foundation
        sealed class Mp4Sink : ISink
        {
            [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFSinkWriter
            {
                [PreserveSig] int AddStream(IMFMediaType t, out int idx);
                [PreserveSig] int SetInputMediaType(int idx, IMFMediaType t, IntPtr enc);
                [PreserveSig] int BeginWriting();
                [PreserveSig] int WriteSample(int idx, IMFSample s);
                [PreserveSig] int SendStreamTick(int idx, long t);
                [PreserveSig] int PlaceMarker(int idx, IntPtr ctx);
                [PreserveSig] int NotifyEndOfSegment(int idx);
                [PreserveSig] int Flush(int idx);
                [PreserveSig] int DoFinalize();
            }

            [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
            [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType t);
            [DllImport("mfplat.dll")] static extern int MFCreateSample(out IMFSample s);
            [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer b);
            [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes a, int size);
            [DllImport("mfreadwrite.dll")] static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr bs, IMFAttributes attr, out IMFSinkWriter w);

            static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
            static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
            static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
            static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
            static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
            static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
            static Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
            static Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
            static Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
            static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
            static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
            static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
            static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
            static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
            static Guid MF_SINK_WRITER_DISABLE_THROTTLING = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
            static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
            static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
            static Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
            static Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
            static Guid MFAudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");

            static void Check(int hr, string what) { if (hr < 0) throw new COMException("Media Foundation " + what + " lỗi 0x" + hr.ToString("X8"), hr); }

            readonly IMFSinkWriter w;
            readonly int vs, aStream = -1, width, height;
            long lastT = -1;
            long audioT;

            public Mp4Sink(string file, int width, int height, bool audio)
            {
                this.width = width; this.height = height;
                Check(MFStartup(0x00020070, 0), "MFStartup");
                Check(MFCreateAttributes(out var attr, 2), "MFCreateAttributes");
                attr.SetUINT32(ref MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                attr.SetUINT32(ref MF_SINK_WRITER_DISABLE_THROTTLING, 1);
                Check(MFCreateSinkWriterFromURL(file, IntPtr.Zero, attr, out w), "MFCreateSinkWriterFromURL");

                // Hinh: H.264
                Check(MFCreateMediaType(out var vo), "MFCreateMediaType");
                vo.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
                vo.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_H264);
                vo.SetUINT32(ref MF_MT_AVG_BITRATE, Math.Min(12000000, Math.Max(3000000, width * height * 4)));
                vo.SetUINT32(ref MF_MT_INTERLACE_MODE, 2);
                vo.SetUINT64(ref MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height);
                vo.SetUINT64(ref MF_MT_FRAME_RATE, (30L << 32) | 1);
                vo.SetUINT64(ref MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
                Check(w.AddStream(vo, out vs), "AddStream video");
                Check(MFCreateMediaType(out var vi), "MFCreateMediaType");
                vi.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
                vi.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_RGB32);
                vi.SetUINT32(ref MF_MT_INTERLACE_MODE, 2);
                vi.SetUINT64(ref MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height);
                vi.SetUINT64(ref MF_MT_FRAME_RATE, (30L << 32) | 1);
                vi.SetUINT64(ref MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
                vi.SetUINT32(ref MF_MT_DEFAULT_STRIDE, width * 4); // duong = anh tu tren xuong
                Check(w.SetInputMediaType(vs, vi, IntPtr.Zero), "SetInputMediaType video");

                if (audio)
                {
                    try
                    {
                        Check(MFCreateMediaType(out var ao), "MFCreateMediaType");
                        ao.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                        ao.SetGUID(ref MF_MT_SUBTYPE, ref MFAudioFormat_AAC);
                        ao.SetUINT32(ref MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                        ao.SetUINT32(ref MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000);
                        ao.SetUINT32(ref MF_MT_AUDIO_NUM_CHANNELS, 2);
                        ao.SetUINT32(ref MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000);
                        Check(w.AddStream(ao, out int a), "AddStream audio");
                        Check(MFCreateMediaType(out var ai), "MFCreateMediaType");
                        ai.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                        ai.SetGUID(ref MF_MT_SUBTYPE, ref MFAudioFormat_PCM);
                        ai.SetUINT32(ref MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                        ai.SetUINT32(ref MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000);
                        ai.SetUINT32(ref MF_MT_AUDIO_NUM_CHANNELS, 2);
                        ai.SetUINT32(ref MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
                        ai.SetUINT32(ref MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 48000 * 4);
                        Check(w.SetInputMediaType(a, ai, IntPtr.Zero), "SetInputMediaType audio");
                        aStream = a;
                    }
                    catch { aStream = -1; }
                }
                Check(w.BeginWriting(), "BeginWriting");
            }

            IMFSample Sample(IntPtr src, byte[] managed, int off, int len, long t, long d)
            {
                Check(MFCreateMemoryBuffer(len, out var buf), "MFCreateMemoryBuffer");
                Check(buf.Lock(out IntPtr p, out _, out _), "Lock");
                if (managed != null) Marshal.Copy(managed, off, p, len);
                else CopyMemory(p, src, (uint)len);
                buf.Unlock();
                buf.SetCurrentLength(len);
                Check(MFCreateSample(out var s), "MFCreateSample");
                s.AddBuffer(buf);
                s.SetSampleTime(t); s.SetSampleDuration(d);
                Marshal.ReleaseComObject(buf);
                return s;
            }

            [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dst, IntPtr src, uint len);

            public void Video(Bitmap frame, long t)
            {
                if (t <= lastT) t = lastT + 1;
                long d = lastT < 0 ? 333333 : Math.Max(1, t - lastT);
                lastT = t;
                var bd = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var s = Sample(bd.Scan0, null, 0, bd.Stride * height, t, d);
                    try { Check(w.WriteSample(vs, s), "WriteSample video"); } finally { Marshal.ReleaseComObject(s); }
                }
                finally { frame.UnlockBits(bd); }
            }

            public void Audio(byte[] pcm, int off, int len)
            {
                if (aStream < 0 || len <= 0) return;
                long d = (long)len / 4 * 10000000 / 48000;
                var s = Sample(IntPtr.Zero, pcm, off, len, audioT, d);
                audioT += d;
                try { Check(w.WriteSample(aStream, s), "WriteSample audio"); } finally { Marshal.ReleaseComObject(s); }
            }

            public void Close(long endT)
            {
                try { Check(w.DoFinalize(), "Finalize"); }
                finally { Marshal.ReleaseComObject(w); }
            }
        }

        // ================================================================== AVI MJPEG (du phong)
        /// <summary>AVI 1.0 MJPEG toc do khung co dinh (lap lai khung cu cho dung thoi gian) + PCM tuy chon.</summary>
        sealed class AviSink : ISink
        {
            const int Fps = 20;
            readonly FileStream fs;
            readonly BinaryWriter bw;
            readonly int width, height;
            readonly bool audio;
            readonly List<(uint id, uint flags, uint off, uint size)> index = new List<(uint, uint, uint, uint)>();
            readonly ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            readonly EncoderParameters ep = new EncoderParameters(1);
            long moviStart, hdrlFramesPos, strhVidLenPos, strhAudLenPos;
            byte[] lastJpeg;
            long frames, audioBytes;
            int maxChunk;

            static uint FourCC(string s) => (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

            public AviSink(string file, int width, int height, bool audio)
            {
                this.width = width; this.height = height; this.audio = audio;
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
                fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
                bw = new BinaryWriter(fs);
                WriteHeaders();
            }

            void WriteHeaders()
            {
                bw.Write(FourCC("RIFF")); bw.Write(0u); bw.Write(FourCC("AVI "));
                long hdrl = BeginList("hdrl");
                // avih
                bw.Write(FourCC("avih")); bw.Write(56u);
                bw.Write((uint)(1000000 / Fps)); bw.Write(0u); bw.Write(0u); bw.Write(0x10u /*HASINDEX*/);
                hdrlFramesPos = fs.Position; bw.Write(0u); bw.Write(0u); bw.Write(audio ? 2u : 1u); bw.Write(0u);
                bw.Write((uint)width); bw.Write((uint)height); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);
                // video stream
                long strl = BeginList("strl");
                bw.Write(FourCC("strh")); bw.Write(56u);
                bw.Write(FourCC("vids")); bw.Write(FourCC("MJPG")); bw.Write(0u); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write(0u);
                bw.Write(1u); bw.Write((uint)Fps); bw.Write(0u); strhVidLenPos = fs.Position; bw.Write(0u); bw.Write(0u); bw.Write(0xFFFFFFFFu); bw.Write(0u);
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)width); bw.Write((short)height);
                bw.Write(FourCC("strf")); bw.Write(40u);
                bw.Write(40u); bw.Write(width); bw.Write(height); bw.Write((ushort)1); bw.Write((ushort)24); bw.Write(FourCC("MJPG"));
                bw.Write((uint)(width * height * 3)); bw.Write(0); bw.Write(0); bw.Write(0u); bw.Write(0u);
                EndList(strl);
                if (audio)
                {
                    long astrl = BeginList("strl");
                    bw.Write(FourCC("strh")); bw.Write(56u);
                    bw.Write(FourCC("auds")); bw.Write(0u); bw.Write(0u); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write(0u);
                    bw.Write(4u); bw.Write(48000u * 4); bw.Write(0u); strhAudLenPos = fs.Position; bw.Write(0u); bw.Write(48000u); bw.Write(0xFFFFFFFFu); bw.Write(4u);
                    bw.Write((short)0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);
                    bw.Write(FourCC("strf")); bw.Write(16u);
                    bw.Write((ushort)1); bw.Write((ushort)2); bw.Write(48000u); bw.Write(48000u * 4); bw.Write((ushort)4); bw.Write((ushort)16);
                    EndList(astrl);
                }
                EndList(hdrl);
                moviStart = BeginList("movi");
            }

            long BeginList(string type)
            {
                bw.Write(FourCC("LIST")); long p = fs.Position; bw.Write(0u); bw.Write(FourCC(type));
                return p;
            }

            void EndList(long sizePos)
            {
                long end = fs.Position;
                fs.Position = sizePos; bw.Write((uint)(end - sizePos - 4)); fs.Position = end;
            }

            void Chunk(string id, byte[] data, int off, int len, bool key)
            {
                uint rel = (uint)(fs.Position - (moviStart + 4));
                bw.Write(FourCC(id)); bw.Write((uint)len); bw.Write(data, off, len);
                if ((len & 1) != 0) bw.Write((byte)0);
                index.Add((FourCC(id), key ? 0x10u : 0u, rel, (uint)len));
                maxChunk = Math.Max(maxChunk, len);
            }

            void Fill(long upToFrame)
            {
                while (lastJpeg != null && frames < upToFrame) { Chunk("00dc", lastJpeg, 0, lastJpeg.Length, true); frames++; }
            }

            public void Video(Bitmap frame, long t)
            {
                long n = t * Fps / 10000000;
                Fill(n);
                if (frames > n) return; // khung den qua dày, bo bot
                using (var ms = new MemoryStream()) { frame.Save(ms, jpeg, ep); lastJpeg = ms.ToArray(); }
                Chunk("00dc", lastJpeg, 0, lastJpeg.Length, true); frames++;
            }

            public void Audio(byte[] pcm, int off, int len)
            {
                if (!audio || len <= 0) return;
                Chunk("01wb", pcm, off, len, true);
                audioBytes += len;
            }

            public void Close(long endT)
            {
                Fill(endT * Fps / 10000000);
                EndList(moviStart);
                bw.Write(FourCC("idx1")); bw.Write((uint)(index.Count * 16));
                foreach (var e in index) { bw.Write(e.id); bw.Write(e.flags); bw.Write(e.off); bw.Write(e.size); }
                long end = fs.Position;
                fs.Position = 4; bw.Write((uint)(end - 8));
                fs.Position = hdrlFramesPos; bw.Write((uint)frames);
                fs.Position = hdrlFramesPos + 12; bw.Write((uint)(maxChunk + 8));
                fs.Position = strhVidLenPos; bw.Write((uint)frames);
                if (audio) { fs.Position = strhAudLenPos; bw.Write((uint)(audioBytes / 4)); }
                bw.Flush(); fs.Dispose();
            }
        }
    }
}
