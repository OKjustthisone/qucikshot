using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickShot.Helpers;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace QuickShot.Views
{
    public partial class CaptureOverlay : Window
    {
        public event EventHandler<Bitmap> Captured;

        private Point _mouseDownPoint;
        private DateTime _mouseDownTime;
        private bool _isDragging;
        private bool _hasSelection;
        private Bitmap _screenBitmap;
        private IntPtr _highlightedWindow;
        private const int CLICK_THRESHOLD_MS = 250;
        private const int CLICK_THRESHOLD_PX = 5;

        private readonly Bitmap _preCapturedBitmap;
        private double _dpiX = 1.0;
        private double _dpiY = 1.0;

        public CaptureOverlay()
        {
            InitializeComponent();
            KeyDown += OnKeyDown;
        }

        public CaptureOverlay(Bitmap preCapturedBitmap) : this()
        {
            _preCapturedBitmap = preCapturedBitmap;
            _screenBitmap = preCapturedBitmap ?? ScreenshotHelper.CaptureScreen();

            double sw = SystemParameters.VirtualScreenWidth;
            double sh = SystemParameters.VirtualScreenHeight;
            double sl = SystemParameters.VirtualScreenLeft;
            double st = SystemParameters.VirtualScreenTop;

            Left = sl;
            Top = st;
            Width = sw;
            Height = sh;

            ScreenImage.Source = ScreenshotHelper.BitmapToBitmapSource(_screenBitmap);
            ScreenImage.Width = sw;
            ScreenImage.Height = sh;

            DimmerTop.Width = sw; DimmerTop.Height = sh;
            DimmerBottom.Width = sw; DimmerBottom.Height = sh;
            DimmerLeft.Width = sw; DimmerLeft.Height = sh;
            DimmerRight.Width = sw; DimmerRight.Height = sh;

            HideAllDimmer();

            SourceInitialized += (s, e) =>
            {
                var source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    _dpiX = source.CompositionTarget.TransformToDevice.M11;
                    _dpiY = source.CompositionTarget.TransformToDevice.M22;
                }
            };

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
        }

        private void HideAllDimmer()
        {
            DimmerTop.Visibility = Visibility.Collapsed;
            DimmerBottom.Visibility = Visibility.Collapsed;
            DimmerLeft.Visibility = Visibility.Collapsed;
            DimmerRight.Visibility = Visibility.Collapsed;
        }

        private void UpdateDimmer(double x, double y, double w, double h)
        {
            double sw = SystemParameters.VirtualScreenWidth;
            double sh = SystemParameters.VirtualScreenHeight;

            DimmerTop.Visibility = Visibility.Visible;
            Canvas.SetLeft(DimmerTop, 0);
            Canvas.SetTop(DimmerTop, 0);
            DimmerTop.Width = sw;
            DimmerTop.Height = y;

            DimmerBottom.Visibility = Visibility.Visible;
            Canvas.SetLeft(DimmerBottom, 0);
            Canvas.SetTop(DimmerBottom, y + h);
            DimmerBottom.Width = sw;
            DimmerBottom.Height = sh - (y + h);

            DimmerLeft.Visibility = Visibility.Visible;
            Canvas.SetLeft(DimmerLeft, 0);
            Canvas.SetTop(DimmerLeft, y);
            DimmerLeft.Width = x;
            DimmerLeft.Height = h;

            DimmerRight.Visibility = Visibility.Visible;
            Canvas.SetLeft(DimmerRight, x + w);
            Canvas.SetTop(DimmerRight, y);
            DimmerRight.Width = sw - (x + w);
            DimmerRight.Height = h;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                if (Captured != null)
                    Captured(this, null);
            }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _mouseDownPoint = e.GetPosition(OverlayCanvas);
                _mouseDownTime = DateTime.Now;
                _isDragging = false;
                _hasSelection = false;
                HideAllDimmer();
                WindowHighlight.Visibility = Visibility.Collapsed;
                SelectionBorder.Visibility = Visibility.Collapsed;
                TipText.Visibility = Visibility.Collapsed;
            }
            else if (e.RightButton == MouseButtonState.Pressed)
            {
                ShowColorContextMenu(e.GetPosition(OverlayCanvas));
                e.Handled = true;
            }
        }

        private void ShowColorContextMenu(Point pos)
        {
            int ix = (int)((pos.X + SystemParameters.VirtualScreenLeft) * _dpiX);
            int iy = (int)((pos.Y + SystemParameters.VirtualScreenTop) * _dpiY);

            if (ix >= 0 && ix < _screenBitmap.Width && iy >= 0 && iy < _screenBitmap.Height)
            {
                var pixel = _screenBitmap.GetPixel(ix, iy);
                string rgbStr = string.Format("RGB: {0}, {1}, {2}", pixel.R, pixel.G, pixel.B);
                string hexStr = string.Format("#{0:X2}{1:X2}{2:X2}", pixel.R, pixel.G, pixel.B);

                var menu = new ContextMenu();

                var rgbItem = new MenuItem { Header = rgbStr + " (点击复制)" };
                rgbItem.Click += (s, ev) =>
                {
                    Clipboard.SetText(rgbStr);
                };

                var hexItem = new MenuItem { Header = hexStr + " (点击复制)" };
                hexItem.Click += (s, ev) =>
                {
                    Clipboard.SetText(hexStr);
                };

                menu.Items.Add(rgbItem);
                menu.Items.Add(hexItem);

                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            int ix = (int)((pos.X + SystemParameters.VirtualScreenLeft) * _dpiX);
            int iy = (int)((pos.Y + SystemParameters.VirtualScreenTop) * _dpiY);

            if (ix >= 0 && ix < _screenBitmap.Width && iy >= 0 && iy < _screenBitmap.Height)
            {
                var pixel = _screenBitmap.GetPixel(ix, iy);
                var wpfColor = Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
                ColorFill.Fill = new SolidColorBrush(wpfColor);
                ColorPreview.Visibility = Visibility.Visible;
                Canvas.SetLeft(ColorPreview, Math.Min(pos.X + 20, Width - 60));
                Canvas.SetTop(ColorPreview, Math.Min(pos.Y + 20, Height - 60));

                InfoPanel.Visibility = Visibility.Visible;
                InfoText.Text = string.Format("{0}x{1}  RGB:{2},{3},{4}  #{5:X2}{6:X2}{7:X2}",
                    ix, iy, pixel.R, pixel.G, pixel.B, pixel.R, pixel.G, pixel.B);
                Canvas.SetLeft(InfoPanel, Math.Min(pos.X + 20, Width - 180));
                Canvas.SetTop(InfoPanel, Math.Max(10, pos.Y - 50));
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                double dx = pos.X - _mouseDownPoint.X;
                double dy = pos.Y - _mouseDownPoint.Y;
                if (Math.Abs(dx) > CLICK_THRESHOLD_PX || Math.Abs(dy) > CLICK_THRESHOLD_PX)
                {
                    _isDragging = true;
                    WindowHighlight.Visibility = Visibility.Collapsed;
                }

                if (_isDragging)
                {
                    _hasSelection = true;
                    double x = Math.Min(_mouseDownPoint.X, pos.X);
                    double y = Math.Min(_mouseDownPoint.Y, pos.Y);
                    double w = Math.Abs(pos.X - _mouseDownPoint.X);
                    double h = Math.Abs(pos.Y - _mouseDownPoint.Y);

                    Canvas.SetLeft(SelectionBorder, x);
                    Canvas.SetTop(SelectionBorder, y);
                    SelectionBorder.Width = w;
                    SelectionBorder.Height = h;
                    SelectionBorder.Visibility = Visibility.Visible;
                    UpdateDimmer(x, y, w, h);
                }
            }
            else
            {
                var pt = new NativeMethods.POINT(ix, iy);
                IntPtr hWnd = NativeMethods.WindowFromPoint(pt);
                if (hWnd != IntPtr.Zero)
                {
                    hWnd = NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT);
                    if (hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd))
                    {
                        string title = NativeMethods.GetWindowTitle(hWnd);
                        if (!string.IsNullOrEmpty(title) && hWnd != NativeMethods.GetWindowHandle(this))
                        {
                            _highlightedWindow = hWnd;
                            var rect = NativeMethods.GetDwmWindowRect(hWnd);
                            Canvas.SetLeft(WindowHighlight, rect.Left / _dpiX - SystemParameters.VirtualScreenLeft);
                            Canvas.SetTop(WindowHighlight, rect.Top / _dpiY - SystemParameters.VirtualScreenTop);
                            WindowHighlight.Width = rect.Width / _dpiX;
                            WindowHighlight.Height = rect.Height / _dpiY;
                            WindowHighlight.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
                WindowHighlight.Visibility = Visibility.Collapsed;
                _highlightedWindow = IntPtr.Zero;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            double elapsed = (DateTime.Now - _mouseDownTime).TotalMilliseconds;
            double dx = pos.X - _mouseDownPoint.X;
            double dy = pos.Y - _mouseDownPoint.Y;

            if (!_isDragging && elapsed < CLICK_THRESHOLD_MS && _highlightedWindow != IntPtr.Zero)
            {
                var rect = NativeMethods.GetDwmWindowRect(_highlightedWindow);
                int x = rect.Left;
                int y = rect.Top;
                int w = rect.Width;
                int h = rect.Height;
                if (w > 0 && h > 0)
                {
                    int px = (int)(x - SystemParameters.VirtualScreenLeft * _dpiX);
                    int py = (int)(y - SystemParameters.VirtualScreenTop * _dpiY);
                    var captured = ScreenshotHelper.CropBitmap(_screenBitmap, px, py, w, h);
                    FinishCapture(captured);
                    return;
                }
            }

            if (_isDragging && _hasSelection)
            {
                double w = Math.Abs(pos.X - _mouseDownPoint.X);
                double h = Math.Abs(pos.Y - _mouseDownPoint.Y);

                if (w > 5 && h > 5)
                {
                    int px = (int)(Math.Min(_mouseDownPoint.X, pos.X) * _dpiX);
                    int py = (int)(Math.Min(_mouseDownPoint.Y, pos.Y) * _dpiY);
                    int pw = (int)(w * _dpiX);
                    int ph = (int)(h * _dpiY);

                    var captured = ScreenshotHelper.CropBitmap(_screenBitmap, px, py, pw, ph);
                    FinishCapture(captured);
                    return;
                }
            }

            HideAllDimmer();
            SelectionBorder.Visibility = Visibility.Collapsed;
            WindowHighlight.Visibility = Visibility.Collapsed;
            TipText.Visibility = Visibility.Visible;
        }

        private void FinishCapture(Bitmap bmp)
        {
            ScreenshotHelper.SaveToClipboard(bmp);
            Close();
            if (Captured != null)
                Captured(this, bmp);
        }
    }
}