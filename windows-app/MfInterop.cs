using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LinkDesk
{
    // Giao dien COM cua Media Foundation (chi phan can cho giai ma H.264).
    // Thu tu phuong thuc phai dung y vtable cua Windows SDK (mfobjects.h, mftransform.h).

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int v);
        [PreserveSig] int GetUINT64(ref Guid key, out long v);
        [PreserveSig] int GetDouble(ref Guid key, out double v);
        [PreserveSig] int GetGUID(ref Guid key, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid key, out int len);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr s, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int v);
        [PreserveSig] int SetUINT64(ref Guid key, long v);
        [PreserveSig] int SetDouble(ref Guid key, double v);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid v);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string v);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int c);
        [PreserveSig] int GetItemByIndex(int idx, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int v);
        [PreserveSig] int GetUINT64(ref Guid key, out long v);
        [PreserveSig] int GetDouble(ref Guid key, out double v);
        [PreserveSig] int GetGUID(ref Guid key, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid key, out int len);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr s, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int v);
        [PreserveSig] int SetUINT64(ref Guid key, long v);
        [PreserveSig] int SetDouble(ref Guid key, double v);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid v);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string v);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int c);
        [PreserveSig] int GetItemByIndex(int idx, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
        [PreserveSig] int GetMajorType(out Guid g);
        [PreserveSig] int IsCompressedFormat(out int b);
        [PreserveSig] int IsEqual(IntPtr other, out int flags);
        [PreserveSig] int GetRepresentation(Guid g, out IntPtr rep);
        [PreserveSig] int FreeRepresentation(Guid g, IntPtr rep);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buf, out int max, out int cur);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int len);
        [PreserveSig] int SetCurrentLength(int len);
        [PreserveSig] int GetMaxLength(out int len);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int v);
        [PreserveSig] int GetUINT64(ref Guid key, out long v);
        [PreserveSig] int GetDouble(ref Guid key, out double v);
        [PreserveSig] int GetGUID(ref Guid key, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid key, out int len);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr s, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, out int len);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int v);
        [PreserveSig] int SetUINT64(ref Guid key, long v);
        [PreserveSig] int SetDouble(ref Guid key, double v);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid v);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string v);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int c);
        [PreserveSig] int GetItemByIndex(int idx, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
        [PreserveSig] int GetSampleFlags(out int f);
        [PreserveSig] int SetSampleFlags(int f);
        [PreserveSig] int GetSampleTime(out long t);
        [PreserveSig] int SetSampleTime(long t);
        [PreserveSig] int GetSampleDuration(out long d);
        [PreserveSig] int SetSampleDuration(long d);
        [PreserveSig] int GetBufferCount(out int c);
        [PreserveSig] int GetBufferByIndex(int i, out IMFMediaBuffer b);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer b);
        [PreserveSig] int AddBuffer(IMFMediaBuffer b);
        [PreserveSig] int RemoveBufferByIndex(int i);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int len);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer b);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MftInputStreamInfo { public long hnsMaxLatency; public int dwFlags, cbSize, cbMaxLookahead, cbAlignment; }
    [StructLayout(LayoutKind.Sequential)]
    struct MftOutputStreamInfo { public int dwFlags, cbSize, cbAlignment; }
    [StructLayout(LayoutKind.Sequential)]
    struct MftOutputDataBuffer { public int dwStreamID; public IntPtr pSample; public int dwStatus; public IntPtr pEvents; }

    [ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFTransform
    {
        [PreserveSig] int GetStreamLimits(out int inMin, out int inMax, out int outMin, out int outMax);
        [PreserveSig] int GetStreamCount(out int inputs, out int outputs);
        [PreserveSig] int GetStreamIDs(int inSize, IntPtr inIds, int outSize, IntPtr outIds);
        [PreserveSig] int GetInputStreamInfo(int id, out MftInputStreamInfo info);
        [PreserveSig] int GetOutputStreamInfo(int id, out MftOutputStreamInfo info);
        [PreserveSig] int GetAttributes(out IMFAttributes a);
        [PreserveSig] int GetInputStreamAttributes(int id, out IntPtr a);
        [PreserveSig] int GetOutputStreamAttributes(int id, out IntPtr a);
        [PreserveSig] int DeleteInputStream(int id);
        [PreserveSig] int AddInputStreams(int count, IntPtr ids);
        [PreserveSig] int GetInputAvailableType(int id, int index, out IMFMediaType t);
        [PreserveSig] int GetOutputAvailableType(int id, int index, out IMFMediaType t);
        [PreserveSig] int SetInputType(int id, IMFMediaType t, int flags);
        [PreserveSig] int SetOutputType(int id, IMFMediaType t, int flags);
        [PreserveSig] int GetInputCurrentType(int id, out IMFMediaType t);
        [PreserveSig] int GetOutputCurrentType(int id, out IMFMediaType t);
        [PreserveSig] int GetInputStatus(int id, out int flags);
        [PreserveSig] int GetOutputStatus(out int flags);
        [PreserveSig] int SetOutputBounds(long lower, long upper);
        [PreserveSig] int ProcessEvent(int id, IntPtr evt);
        [PreserveSig] int ProcessMessage(int msg, IntPtr param);
        [PreserveSig] int ProcessInput(int id, IMFSample sample, int flags);
        [PreserveSig] int ProcessOutput(int flags, int count, ref MftOutputDataBuffer buf, out int status);
    }
}
