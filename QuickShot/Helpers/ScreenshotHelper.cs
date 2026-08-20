using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QuickShot.Helpers
{
    public static class ScreenshotHelper
    {
        public static Bitmap CaptureCurrentScreen()
        {
            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            IntPtr hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                int x = mi.rcMonitor.Left;
                int y = mi.rcMonitor.Top;
                int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                return CaptureRegion(x, y, width, height);
            }
            return CaptureScreen();
        }

        public static Bitmap CaptureScreen()
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
            {
                width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
                height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
                x = 0;
                y = 0;
            }

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Bitmap CropBitmap(Bitmap source, int x, int y, int width, int height)
        {
            x = Math.Max(0, Math.Min(x, source.Width - 1));
            y = Math.Max(0, Math.Min(y, source.Height - 1));
            width = Math.Max(1, Math.Min(width, source.Width - x));
            height = Math.Max(1, Math.Min(height, source.Height - y));

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(source, new Rectangle(0, 0, width, height), x, y, width, height, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        public static Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Bitmap CaptureWindow(IntPtr hWnd)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hWnd, out rect))
                return null;
            return CaptureRegion(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        public static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
        {
            try
            {
                bitmap.SetResolution(96f, 96f);
            }
            catch { }

            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = stream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        public static void SaveToClipboard(Bitmap bitmap)
        {
            System.Windows.Forms.Clipboard.SetImage(bitmap);
        }

        public static void SaveToFile(Bitmap bitmap, string path)
        {
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
