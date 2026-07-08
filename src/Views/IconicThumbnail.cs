using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediumMetrics.Views;

/// <summary>
/// Replaces the window's taskbar thumbnail / Aero-Peek preview with a branded card
/// (from <see cref="AppIcon.RenderThumbnail"/>) via the DWM iconic-bitmap API, so a
/// minimized window shows something distinctive instead of a blank/boring snapshot.
/// Pure P/Invoke (dwmapi + gdi32), no extra dependencies; failures are swallowed
/// because the thumbnail is purely cosmetic.
/// </summary>
public static class IconicThumbnail
{
    private const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
    private const int DWMWA_HAS_ICONIC_BITMAP = 10;
    private const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
    private const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;

    public static void Enable(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var source = HwndSource.FromHwnd(hwnd);
                if (source is null) return;

                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_HAS_ICONIC_BITMAP, ref on, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_FORCE_ICONIC_REPRESENTATION, ref on, sizeof(int));

                source.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == WM_DWMSENDICONICTHUMBNAIL)
                    {
                        int maxW = (int)(((long)l >> 16) & 0xFFFF);
                        int maxH = (int)((long)l & 0xFFFF);
                        Set(h, AppIcon.RenderThumbnail(maxW, maxH), thumbnail: true);
                        handled = true;
                    }
                    else if (msg == WM_DWMSENDICONICLIVEPREVIEWBITMAP)
                    {
                        int w2 = Math.Max(1, (int)window.ActualWidth);
                        int h2 = Math.Max(1, (int)window.ActualHeight);
                        Set(h, AppIcon.RenderThumbnail(w2, h2), thumbnail: false);
                        handled = true;
                    }
                    return IntPtr.Zero;
                });
            }
            catch { /* thumbnail is cosmetic — never let it break the window */ }
        };
    }

    private static void Set(IntPtr hwnd, BitmapSource bmp, bool thumbnail)
    {
        var hbmp = ToHBitmap(bmp);
        if (hbmp == IntPtr.Zero) return;
        try
        {
            if (thumbnail) DwmSetIconicThumbnail(hwnd, hbmp, 0);
            else DwmSetIconicLivePreviewBitmap(hwnd, hbmp, IntPtr.Zero, 0);
        }
        finally { DeleteObject(hbmp); }
    }

    /// <summary>Converts an opaque WPF bitmap to a top-down 32bpp GDI HBITMAP for DWM.</summary>
    private static IntPtr ToHBitmap(BitmapSource src)
    {
        var bmp = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,   // negative = top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        var hbmp = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;
        bmp.CopyPixels(new Int32Rect(0, 0, w, h), bits, h * stride, stride);
        return hbmp;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, int flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr pos, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }
}
