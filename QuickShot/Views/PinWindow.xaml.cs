using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QuickShot.Helpers;

namespace QuickShot.Views
{
    public partial class PinWindow : Window
    {
        private Bitmap _bitmap;

        public PinWindow(Bitmap bitmap)
        {
            InitializeComponent();
            _bitmap = bitmap;
            PinImage.Source = ScreenshotHelper.BitmapToBitmapSource(bitmap);
            Width = bitmap.Width + 12;
            Height = bitmap.Height + 12;
            Left = 100;
            Top = 100;

            Loaded += (s, e) =>
            {
                Topmost = false;
                Topmost = true;
            };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void RootBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            ToolBar.Visibility = Visibility.Visible;
        }

        private void RootBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            ToolBar.Visibility = Visibility.Collapsed;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotHelper.SaveToClipboard(_bitmap);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = "pin_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            };
            if (dlg.ShowDialog() == true)
            {
                _bitmap.Save(dlg.FileName, ImageFormat.Png);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (PinImage != null)
                {
                    PinImage.Source = null;
                }
                if (_bitmap != null)
                {
                    _bitmap.Dispose();
                    _bitmap = null;
                }
                MemoryHelper.TrimWorkingSet();
            }
            catch { }
        }
    }
}
