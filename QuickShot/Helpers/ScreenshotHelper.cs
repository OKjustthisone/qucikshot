using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QuickShot.Helpers
{
    public static class ScreenshotHelper
    {
        public static Bitmap CaptureScreen()
        {
            int x = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
            int y = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
            int width = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
            int height = System.Windows.Forms.SystemInformation.VirtualScreen.Height;

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
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
