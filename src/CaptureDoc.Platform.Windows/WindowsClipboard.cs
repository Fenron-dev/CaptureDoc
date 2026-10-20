using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CaptureDoc.Core.Platform;
using static CaptureDoc.Platform.Windows.Native;

namespace CaptureDoc.Platform.Windows;

/// <summary>Writes CF_DIB (works everywhere: Office, Paint, Teams, browsers) plus the registered "PNG" format (keeps transparency).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboard : IClipboardImageService
{
    public bool SetImage(RawImage image, byte[]? png)
    {
        var opened = false;
        for (var i = 0; i < 10 && !opened; i++)
        {
            opened = OpenClipboard(0);
            if (!opened) Thread.Sleep(30); // another app holds the clipboard
        }
        if (!opened) return false;
        try
        {
            EmptyClipboard();
            var ok = SetGlobal(CF_DIB, BuildDib(image));
            if (png is not null) SetGlobal(RegisterClipboardFormat("PNG"), png);
            return ok;
        }
        finally { CloseClipboard(); }
    }

    private static bool SetGlobal(uint format, byte[] data)
    {
        var mem = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        if (mem == 0) return false;
        var p = GlobalLock(mem);
        if (p == 0) { GlobalFree(mem); return false; }
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(mem);
        if (SetClipboardData(format, mem) == 0) { GlobalFree(mem); return false; }
        return true; // clipboard owns the memory now
    }

    /// <summary>Bottom-up 32-bit DIB (most compatible).</summary>
    internal static byte[] BuildDib(RawImage img)
    {
        var header = Marshal.SizeOf<BITMAPINFOHEADER>();
        var stride = img.Width * 4;
        var data = new byte[header + stride * img.Height];
        var bih = new BITMAPINFOHEADER
        {
            biSize = (uint)header, biWidth = img.Width, biHeight = img.Height, biPlanes = 1, biBitCount = 32,
            biCompression = 0, biSizeImage = (uint)(stride * img.Height), biXPelsPerMeter = 3780, biYPelsPerMeter = 3780,
        };
        var h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { Marshal.StructureToPtr(bih, h.AddrOfPinnedObject(), false); }
        finally { h.Free(); }
        for (var y = 0; y < img.Height; y++)
            Buffer.BlockCopy(img.Pixels, y * stride, data, header + (img.Height - 1 - y) * stride, stride);
        return data;
    }
}
