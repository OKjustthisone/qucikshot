using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
        private double _scaleX = 1.0;
        private double _scaleY = 1.0;
        private int _virtLeft = 0;
        private int _virtTop = 0;
        private Color _currentPixelColor = Colors.White;
        private string _currentHex = "#FFFFFF";
        private string _currentRgb = "RGB: (255, 255, 255)";

        public CaptureOverlay()
        {
            InitializeComponent();
            KeyDown += OnKeyDown;
        }

        public CaptureOverlay(Bitmap preCapturedBitmap = null) : this()
        {
            _preCapturedBitmap = preCapturedBitmap;
            _screenBitmap = preCapturedBitmap ?? ScreenshotHelper.CaptureScreen();

            _virtLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _virtTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);

            double sw = SystemParameters.VirtualScreenWidth;
            double sh = SystemParameters.VirtualScreenHeight;
            double sl = SystemParameters.VirtualScreenLeft;
            double st = SystemParameters.VirtualScreenTop;

            Left = sl;
            Top = st;
            Width = sw;
            Height = sh;

            // Calculate precise scaling ratio between WPF DIPs and physical pixels
            _scaleX = (double)_screenBitmap.Width / sw;
            _scaleY = (double)_screenBitmap.Height / sh;

            ScreenImage.Source = ScreenshotHelper.BitmapToBitmapSource(_screenBitmap);
            ScreenImage.Width = sw;
            ScreenImage.Height = sh;

            DimmerTop.Width = sw; DimmerTop.Height = sh;
            DimmerBottom.Width = sw; DimmerBottom.Height = sh;
            DimmerLeft.Width = sw; DimmerLeft.Height = sh;
            DimmerRight.Width = sw; DimmerRight.Height = sh;

            HideAllDimmer();

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
            double sw = Width;
            double sh = Height;

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
            else if (e.Key == Key.C)
            {
                Clipboard.SetText(_currentHex);
                MagnifierHexText.Text = "已复制: " + _currentHex;
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
            int px = (int)Math.Round(pos.X * _scaleX);
            int py = (int)Math.Round(pos.Y * _scaleY);

            if (px >= 0 && px < _screenBitmap.Width && py >= 0 && py < _screenBitmap.Height)
            {
                var pixel = _screenBitmap.GetPixel(px, py);
                string rgbStr = string.Format("RGB: ({0}, {1}, {2})", pixel.R, pixel.G, pixel.B);
                string hexStr = string.Format("#{0:X2}{1:X2}{2:X2}", pixel.R, pixel.G, pixel.B);

                var menu = new ContextMenu();

                var hexItem = new MenuItem { Header = "复制 HEX: " + hexStr };
                hexItem.Click += (s, ev) => Clipboard.SetText(hexStr);

                var rgbItem = new MenuItem { Header = "复制 RGB: " + rgbStr };
                rgbItem.Click += (s, ev) => Clipboard.SetText(rgbStr);

                menu.Items.Add(hexItem);
                menu.Items.Add(rgbItem);

                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            int px = (int)Math.Round(pos.X * _scaleX);
            int py = (int)Math.Round(pos.Y * _scaleY);

            if (px >= 0 && px < _screenBitmap.Width && py >= 0 && py < _screenBitmap.Height)
            {
                var pixel = _screenBitmap.GetPixel(px, py);
                _currentPixelColor = Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
                _currentHex = string.Format("#{0:X2}{1:X2}{2:X2}", pixel.R, pixel.G, pixel.B);
                _currentRgb = string.Format("RGB: ({0}, {1}, {2})", pixel.R, pixel.G, pixel.B);

                MagnifierColorFill.Fill = new SolidColorBrush(_currentPixelColor);
                MagnifierHexText.Text = _currentHex;
                MagnifierRgbText.Text = _currentRgb;

                int screenX = _virtLeft + px;
                int screenY = _virtTop + py;
                MagnifierPosText.Text = string.Format("POS: ({0}, {1})", screenX, screenY);

                // Update zoomed pixel grid view
                UpdateMagnifierImage(px, py);

                MagnifierPanel.Visibility = Visibility.Visible;

                // Smart positioning to keep magnifier visible and avoid covering cursor
                double panelWidth = 152;
                double panelHeight = 160;
                double panelLeft = pos.X + 20;
                double panelTop = pos.Y + 20;

                if (panelLeft + panelWidth > Width)
                {
                    panelLeft = pos.X - panelWidth - 10;
                }
                if (panelTop + panelHeight > Height)
                {
                    panelTop = pos.Y - panelHeight - 10;
                }
                if (panelLeft < 0) panelLeft = 10;
                if (panelTop < 0) panelTop = 10;

                Canvas.SetLeft(MagnifierPanel, panelLeft);
                Canvas.SetTop(MagnifierPanel, panelTop);
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

                    int selW = (int)Math.Round(w * _scaleX);
                    int selH = (int)Math.Round(h * _scaleY);
                    MagnifierPosText.Text = string.Format("尺寸: {0} x {1}", selW, selH);
                }
            }
            else
            {
                int screenX = _virtLeft + px;
                int screenY = _virtTop + py;
                var pt = new NativeMethods.POINT(screenX, screenY);
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

                            int x = rect.Left;
                            int y = rect.Top;
                            int w = rect.Width;
                            int h = rect.Height;

                            IntPtr hMon = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                            var mi = new NativeMethods.MONITORINFOEX();
                            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                            {
                                if (x <= mi.rcMonitor.Left && x + w >= mi.rcMonitor.Right)
                                {
                                    x = mi.rcWork.Left;
                                    y = mi.rcWork.Top;
                                    w = mi.rcWork.Width;
                                    h = mi.rcWork.Height;
                                }
                            }

                            double winLeft = (x - _virtLeft) / _scaleX;
                            double winTop = (y - _virtTop) / _scaleY;
                            double winWidth = w / _scaleX;
                            double winHeight = h / _scaleY;

                            Canvas.SetLeft(WindowHighlight, winLeft);
                            Canvas.SetTop(WindowHighlight, winTop);
                            WindowHighlight.Width = Math.Max(0, winWidth);
                            WindowHighlight.Height = Math.Max(0, winHeight);
                            WindowHighlight.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
                WindowHighlight.Visibility = Visibility.Collapsed;
                _highlightedWindow = IntPtr.Zero;
            }
        }

        private void UpdateMagnifierImage(int cx, int cy)
        {
            try
            {
                int zoomW = 17;
                int zoomH = 10;
                int startX = cx - zoomW / 2;
                int startY = cy - zoomH / 2;

                using (Bitmap zoomBmp = new Bitmap(zoomW, zoomH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(zoomBmp))
                    {
                        g.Clear(System.Drawing.Color.Black);
                        g.DrawImage(_screenBitmap, new Rectangle(0, 0, zoomW, zoomH), startX, startY, zoomW, zoomH, GraphicsUnit.Pixel);
                    }
                    MagnifierImage.Source = ScreenshotHelper.BitmapToBitmapSource(zoomBmp);
                }
            }
            catch { }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            double elapsed = (DateTime.Now - _mouseDownTime).TotalMilliseconds;

            if (!_isDragging && elapsed < CLICK_THRESHOLD_MS && _highlightedWindow != IntPtr.Zero)
            {
                var rect = NativeMethods.GetDwmWindowRect(_highlightedWindow);
                int x = rect.Left;
                int y = rect.Top;
                int w = rect.Width;
                int h = rect.Height;

                IntPtr hMon = NativeMethods.MonitorFromWindow(_highlightedWindow, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                {
                    if (x <= mi.rcMonitor.Left && x + w >= mi.rcMonitor.Right)
                    {
                        x = mi.rcWork.Left;
                        y = mi.rcWork.Top;
                        w = mi.rcWork.Width;
                        h = mi.rcWork.Height;
                    }
                }

                if (w > 0 && h > 0)
                {
                    int px = x - _virtLeft;
                    int py = y - _virtTop;

                    if (px < 0) { w += px; px = 0; }
                    if (py < 0) { h += py; py = 0; }
                    if (px + w > _screenBitmap.Width) w = _screenBitmap.Width - px;
                    if (py + h > _screenBitmap.Height) h = _screenBitmap.Height - py;

                    if (w > 0 && h > 0)
                    {
                        var captured = ScreenshotHelper.CropBitmap(_screenBitmap, px, py, w, h);
                        FinishCapture(captured);
                        return;
                    }
                }
            }

            if (_isDragging && _hasSelection)
            {
                double x1 = _mouseDownPoint.X;
                double x2 = pos.X;
                double y1 = _mouseDownPoint.Y;
                double y2 = pos.Y;

                double minX = Math.Min(x1, x2);
                double maxX = Math.Max(x1, x2);
                double minY = Math.Min(y1, y2);
                double maxY = Math.Max(y1, y2);

                if (maxX - minX > 5 && maxY - minY > 5)
                {
                    int px = (int)Math.Round(minX * _scaleX);
                    int py = (int)Math.Round(minY * _scaleY);
                    int pw = (int)Math.Round(maxX * _scaleX) - px;
                    int ph = (int)Math.Round(maxY * _scaleY) - py;

                    if (px < 0) { pw += px; px = 0; }
                    if (py < 0) { ph += py; py = 0; }
                    if (px + pw > _screenBitmap.Width) pw = _screenBitmap.Width - px;
                    if (py + ph > _screenBitmap.Height) ph = _screenBitmap.Height - py;

                    if (pw > 0 && ph > 0)
                    {
                        var captured = ScreenshotHelper.CropBitmap(_screenBitmap, px, py, pw, ph);
                        FinishCapture(captured);
                        return;
                    }
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (ScreenImage != null)
                {
                    ScreenImage.Source = null;
                }
                if (MagnifierImage != null)
                {
                    MagnifierImage.Source = null;
                }
                if (_screenBitmap != null)
                {
                    _screenBitmap.Dispose();
                    _screenBitmap = null;
                }
                MemoryHelper.TrimWorkingSet();
            }
            catch { }
        }
    }
}