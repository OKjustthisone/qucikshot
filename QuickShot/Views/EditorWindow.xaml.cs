using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using QuickShot.Helpers;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DColor = System.Drawing.Color;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace QuickShot.Views
{
    public partial class EditorWindow : Window
    {
        private Bitmap _originalBitmap;
        private string _currentTool = "";
        private Point _drawStart;
        private bool _isDrawing;
        private bool _isMoving;
        private Point _moveStart;
        private Point _moveOffset;
        private UIElement _currentElement;
        private UIElement _selectedElement;
        private List<UIElement> _annotations = new List<UIElement>();
        private List<UIElement> _undoStack = new List<UIElement>();
        private Color _strokeColor = Color.FromArgb(255, 255, 107, 53);
        private Color _fillColor = Color.FromArgb(0, 255, 107, 53);
        private double _strokeThickness = 2;
        private int _fillAlpha = 0;
        private bool _isDraggingHandle;
        private Point _handleMoveStart;
        private bool _isResizing;
        private Point _resizeStartPoint;
        private double _initialWidth;
        private double _initialHeight;
        private Point _initialLineStart;
        private Point _initialLineEnd;
        private bool _isRotating;
        private double _initialAngle;
        private Point _rotateCenter;
        private System.Windows.Threading.DispatcherTimer _autoCloseTimer;
        private DateTime _lastActivityTime;
        private double _zoomFactor = 1.0;
        private const double MIN_ZOOM = 0.1;
        private const double MAX_ZOOM = 10.0;
        private const double ZOOM_STEP = 1.15;

        public EditorWindow(Bitmap bitmap, string autoSavedPath = null)
        {
            InitializeComponent();
            _originalBitmap = bitmap;
            ScreenshotImage.Source = ScreenshotHelper.BitmapToBitmapSource(bitmap);
            EditorCanvas.Width = bitmap.Width;
            EditorCanvas.Height = bitmap.Height;
            Width = Math.Min(bitmap.Width + 100, SystemParameters.PrimaryScreenWidth * 0.9);
            Height = Math.Min(bitmap.Height + 260, SystemParameters.PrimaryScreenHeight * 0.9);

            _strokeColor = Color.FromArgb(255, 255, 107, 53);
            _fillAlpha = SettingsWindow.FillAlpha;
            _fillColor = Color.FromArgb((byte)_fillAlpha, 255, 107, 53);
            ActiveFillBtn.Background = _fillAlpha > 0 ? new SolidColorBrush(_fillColor) : Brushes.Transparent;
            ActiveFillBtn.BorderBrush = new SolidColorBrush(Colors.Gray);

            ThicknessSlider.ValueChanged += (s, e) =>
            {
                _strokeThickness = ThicknessSlider.Value;
                UpdateSelectedElementStyle();
            };
            FillAlphaSlider.Value = _fillAlpha;
            FillAlphaSlider.ValueChanged += (s, e) =>
            {
                _fillAlpha = (int)FillAlphaSlider.Value;
                UpdateFillColor();
                UpdateSelectedElementStyle();
            };
            KeyDown += Window_KeyDown;
            _currentTool = "";

            // Initialize auto-close timer (5 minutes inactivity)
            _lastActivityTime = DateTime.Now;
            _autoCloseTimer = new System.Windows.Threading.DispatcherTimer();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(10);
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            _autoCloseTimer.Start();

            // Display auto-saved status if present
            if (!string.IsNullOrEmpty(autoSavedPath))
            {
                Loaded += (s, e) => ShowSavedStatus("已自动保存: ", autoSavedPath);
            }
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            if (DateTime.Now - _lastActivityTime >= TimeSpan.FromMinutes(5))
            {
                _autoCloseTimer.Stop();
                Close();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            _lastActivityTime = DateTime.Now;
            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            _lastActivityTime = DateTime.Now;
            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            _lastActivityTime = DateTime.Now;
            base.OnPreviewMouseMove(e);
        }

        private void UpdateFillColor()
        {
            _fillColor = Color.FromArgb((byte)_fillAlpha, _fillColor.R, _fillColor.G, _fillColor.B);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.D0 || e.Key == Key.NumPad0))
            {
                _zoomFactor = 1.0;
                ApplyZoom();
                return;
            }
            if (e.Key == Key.Delete && _selectedElement != null)
            {
                if (e.OriginalSource is TextBox) return;
                EditorCanvas.Children.Remove(_selectedElement);
                _annotations.Remove(_selectedElement);
                DeselectAll();
                StatusText.Text = "已删除";
            }
        }

        private void ApplyZoom()
        {
            if (CanvasScaleTransform != null)
            {
                CanvasScaleTransform.ScaleX = _zoomFactor;
                CanvasScaleTransform.ScaleY = _zoomFactor;
            }
            if (ZoomText != null)
            {
                ZoomText.Text = string.Format("{0:0}%", _zoomFactor * 100);
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            _lastActivityTime = DateTime.Now;

            Point mousePosOnCanvas = e.GetPosition(EditorCanvas);
            Point mousePosOnScrollViewer = e.GetPosition(EditorScrollViewer);

            double oldZoom = _zoomFactor;
            if (e.Delta > 0)
            {
                _zoomFactor = Math.Min(MAX_ZOOM, _zoomFactor * ZOOM_STEP);
            }
            else if (e.Delta < 0)
            {
                _zoomFactor = Math.Max(MIN_ZOOM, _zoomFactor / ZOOM_STEP);
            }

            if (Math.Abs(_zoomFactor - oldZoom) > 0.001)
            {
                ApplyZoom();

                EditorScrollViewer.UpdateLayout();
                double newScrollX = mousePosOnCanvas.X * _zoomFactor - mousePosOnScrollViewer.X + EditorScrollViewer.Padding.Left;
                double newScrollY = mousePosOnCanvas.Y * _zoomFactor - mousePosOnScrollViewer.Y + EditorScrollViewer.Padding.Top;
                EditorScrollViewer.ScrollToHorizontalOffset(newScrollX);
                EditorScrollViewer.ScrollToVerticalOffset(newScrollY);
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Min(MAX_ZOOM, _zoomFactor * ZOOM_STEP);
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Max(MIN_ZOOM, _zoomFactor / ZOOM_STEP);
            ApplyZoom();
        }

        private void ZoomReset_Click(object sender, MouseButtonEventArgs e)
        {
            _zoomFactor = 1.0;
            ApplyZoom();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as ToggleButton;
            if (btn == null) return;
            if (btn.IsChecked == false)
            {
                _currentTool = "";
                StatusText.Text = "Ready";
                return;
            }
            UncheckAllTools();
            btn.IsChecked = true;
            _currentTool = btn.Tag as string;
            DeselectAll();
            StatusText.Text = string.IsNullOrEmpty(_currentTool) ? "Ready" : "Tool: " + _currentTool;
        }

        private void UncheckAllTools()
        {
            ToolRect.IsChecked = false;
            ToolEllipse.IsChecked = false;
            ToolArrow.IsChecked = false;
            ToolLine.IsChecked = false;
            ToolText.IsChecked = false;
            ToolBlur.IsChecked = false;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var colorStr = btn.Tag as string;
            if (colorStr != null)
            {
                var converter = new System.Windows.Media.BrushConverter();
                var brush = converter.ConvertFromString(colorStr) as SolidColorBrush;
                if (brush != null)
                {
                    _strokeColor = brush.Color;
                    ActiveColorBtn.Background = new SolidColorBrush(_strokeColor);
                    UpdateSelectedElementStyle();
                }
            }
        }

        private void FillColor_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var colorStr = btn.Tag as string;
            if (colorStr == "Transparent")
            {
                _fillColor = Colors.Transparent;
                ActiveFillBtn.Background = Brushes.Transparent;
                ActiveFillBtn.BorderBrush = new SolidColorBrush(Colors.Gray);
            }
            else if (colorStr != null)
            {
                var converter = new System.Windows.Media.BrushConverter();
                var brush = converter.ConvertFromString(colorStr) as SolidColorBrush;
                if (brush != null)
                {
                    _fillColor = Color.FromArgb((byte)_fillAlpha, brush.Color.R, brush.Color.G, brush.Color.B);
                    ActiveFillBtn.Background = brush;
                    ActiveFillBtn.BorderBrush = Brushes.White;
                }
            }
            UpdateFillColor();
            UpdateSelectedElementStyle();
        }

        private void UpdateSelectedElementStyle()
        {
            if (_selectedElement == null) return;
            var brush = new SolidColorBrush(_strokeColor);
            if (_selectedElement is Rectangle || _selectedElement is Ellipse)
            {
                var shape = _selectedElement as Shape;
                shape.Stroke = brush;
                shape.StrokeThickness = _strokeThickness;
                if (_fillColor.A > 0)
                    shape.Fill = new SolidColorBrush(_fillColor);
                else
                    shape.Fill = Brushes.Transparent;
            }
            else if (_selectedElement is Line)
            {
                var line = _selectedElement as Line;
                line.Stroke = brush;
                line.StrokeThickness = _strokeThickness;
            }
            else if (_selectedElement is Path)
            {
                var path = _selectedElement as Path;
                path.Stroke = brush;
                path.StrokeThickness = _strokeThickness;
                path.Fill = brush;
                var pts = path.Tag as Tuple<Point, Point>;
                if (pts != null)
                {
                    path.Data = CreateArrowGeometry(pts.Item1.X, pts.Item1.Y, pts.Item2.X, pts.Item2.Y);
                }
            }
            else if (_selectedElement is TextBox)
            {
                var tb = _selectedElement as TextBox;
                tb.Foreground = brush;
                tb.BorderBrush = brush;
            }
        }

        private void DeselectAll()
        {
            _selectedElement = null;
            SelectionAdorner.Visibility = Visibility.Collapsed;
        }

        private void SelectElement(UIElement element)
        {
            _selectedElement = element;
            UpdateSelectionIndicator();
            SelectionAdorner.Visibility = Visibility.Visible;

            var shape = element as Shape;
            if (shape != null)
            {
                var strokeBrush = shape.Stroke as SolidColorBrush;
                if (strokeBrush != null)
                {
                    _strokeColor = strokeBrush.Color;
                    ActiveColorBtn.Background = strokeBrush;
                }
                _strokeThickness = shape.StrokeThickness;
                ThicknessSlider.Value = _strokeThickness;

                var fillBrush = shape.Fill as SolidColorBrush;
                if (fillBrush != null)
                {
                    if (fillBrush.Color == Colors.Transparent)
                    {
                        _fillColor = Colors.Transparent;
                        _fillAlpha = 0;
                        FillAlphaSlider.Value = 0;
                        ActiveFillBtn.Background = Brushes.Transparent;
                        ActiveFillBtn.BorderBrush = new SolidColorBrush(Colors.Gray);
                    }
                    else
                    {
                        _fillColor = fillBrush.Color;
                        _fillAlpha = _fillColor.A;
                        FillAlphaSlider.Value = _fillAlpha;
                        ActiveFillBtn.Background = new SolidColorBrush(Color.FromRgb(_fillColor.R, _fillColor.G, _fillColor.B));
                        ActiveFillBtn.BorderBrush = Brushes.White;
                    }
                }
            }
            else
            {
                var tb = element as TextBox;
                if (tb != null)
                {
                    var fgBrush = tb.Foreground as SolidColorBrush;
                    if (fgBrush != null)
                    {
                        _strokeColor = fgBrush.Color;
                        ActiveColorBtn.Background = fgBrush;
                    }
                }
            }
        }

        private void UpdateSelectionIndicator()
        {
            if (_selectedElement == null) return;
            double x = Canvas.GetLeft(_selectedElement);
            double y = Canvas.GetTop(_selectedElement);
            double w = 0, h = 0;
            if (_selectedElement is FrameworkElement)
            {
                var fe = _selectedElement as FrameworkElement;
                w = fe.Width;
                h = fe.Height;
                if (double.IsNaN(w)) w = fe.ActualWidth;
                if (double.IsNaN(h)) h = fe.ActualHeight;
            }
            if (_selectedElement is Line)
            {
                var line = _selectedElement as Line;
                x = Math.Min(line.X1, line.X2);
                y = Math.Min(line.Y1, line.Y2);
                w = Math.Abs(line.X2 - line.X1);
                h = Math.Abs(line.Y2 - line.Y1);
            }
            if (_selectedElement is Path)
            {
                var path = _selectedElement as Path;
                if (path.Data != null)
                {
                    var bounds = path.Data.Bounds;
                    x = bounds.X;
                    y = bounds.Y;
                    w = bounds.Width;
                    h = bounds.Height;
                }
            }

            Canvas.SetLeft(SelectionAdorner, x);
            Canvas.SetTop(SelectionAdorner, y);
            SelectionAdorner.Width = Math.Max(w, 6);
            SelectionAdorner.Height = Math.Max(h, 6);

            // Position handles inside SelectionAdorner
            Canvas.SetLeft(DragHandle, SelectionAdorner.Width - 9);
            Canvas.SetTop(DragHandle, -9);

            Canvas.SetLeft(ResizeHandle, SelectionAdorner.Width - 6);
            Canvas.SetTop(ResizeHandle, SelectionAdorner.Height - 6);

            Canvas.SetLeft(RotateHandle, SelectionAdorner.Width / 2 - 6);
            Canvas.SetTop(RotateHandle, -20);

            // Rotate SelectionAdorner to match element
            var rotateTransform = _selectedElement.RenderTransform as RotateTransform;
            double angle = rotateTransform != null ? rotateTransform.Angle : 0;
            SelectionAdorner.RenderTransform = new RotateTransform(angle);
        }

        private void AttachElementEvents(UIElement element)
        {
            element.MouseDown += Element_MouseDown;
            element.MouseMove += Element_MouseMove;
            element.MouseUp += Element_MouseUp;
        }

        private void Element_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as UIElement;
            if (element != null && element != ScreenshotImage && element != SelectionAdorner)
            {
                SelectElement(element);

                var tb = element as TextBox;
                if (tb != null)
                {
                    if (tb.IsFocused)
                    {
                        return; // Let default textbox cursor handling take place
                    }
                    if (e.ClickCount >= 2)
                    {
                        tb.Focus();
                        tb.SelectAll();
                        e.Handled = true;
                        return;
                    }
                }

                _isMoving = true;
                _moveStart = e.GetPosition(EditorCanvas);
                if (element is FrameworkElement)
                {
                    _moveOffset = new Point(
                        _moveStart.X - Canvas.GetLeft(element),
                        _moveStart.Y - Canvas.GetTop(element));
                }
                else
                {
                    _moveOffset = new Point(0, 0);
                }
                element.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMoving && _selectedElement != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _isMoving = false;
                    var el = sender as UIElement;
                    if (el != null) el.ReleaseMouseCapture();
                    return;
                }

                var pos = e.GetPosition(EditorCanvas);
                if (_selectedElement is Line)
                {
                    var line = _selectedElement as Line;
                    double dx = pos.X - _moveStart.X;
                    double dy = pos.Y - _moveStart.Y;
                    line.X1 += dx; line.Y1 += dy;
                    line.X2 += dx; line.Y2 += dy;
                    _moveStart = pos;
                }
                else if (_selectedElement is Path)
                {
                    double dx = pos.X - _moveStart.X;
                    double dy = pos.Y - _moveStart.Y;
                    MovePath(_selectedElement as Path, dx, dy);
                    _moveStart = pos;
                }
                else
                {
                    Canvas.SetLeft(_selectedElement, pos.X - _moveOffset.X);
                    Canvas.SetTop(_selectedElement, pos.Y - _moveOffset.Y);
                }
                UpdateSelectionIndicator();
                e.Handled = true;
            }
        }

        private void Element_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMoving)
            {
                _isMoving = false;
                var el = sender as UIElement;
                if (el != null) el.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void MovePath(Path path, double dx, double dy)
        {
            if (path.Data == null) return;
            var pts = path.Tag as Tuple<Point, Point>;
            if (pts != null)
            {
                var newPt1 = new Point(pts.Item1.X + dx, pts.Item1.Y + dy);
                var newPt2 = new Point(pts.Item2.X + dx, pts.Item2.Y + dy);
                path.Tag = new Tuple<Point, Point>(newPt1, newPt2);
                path.Data = CreateArrowGeometry(newPt1.X, newPt1.Y, newPt2.X, newPt2.Y);
            }
            else
            {
                var transform = new TranslateTransform(dx, dy);
                path.Data = Geometry.Combine(path.Data, Geometry.Empty, GeometryCombineMode.Union, transform);
            }
        }

        private void Canvas_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedElement != null)
            {
                if (e.OriginalSource is TextBox) return;
                EditorCanvas.Children.Remove(_selectedElement);
                _annotations.Remove(_selectedElement);
                DeselectAll();
                StatusText.Text = "已删除";
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            EditorCanvas.Focus();
            if (string.IsNullOrEmpty(_currentTool))
            {
                DeselectAll();
                return;
            }

            EditorCanvas.CaptureMouse();
            _drawStart = e.GetPosition(EditorCanvas);
            _isDrawing = true;
            DeselectAll();
            var colorBrush = new SolidColorBrush(_strokeColor);
            var fillBrush = _fillColor.A > 0 ? new SolidColorBrush(_fillColor) : Brushes.Transparent;

            switch (_currentTool)
            {
                case "rect":
                    _currentElement = new Rectangle
                    {
                        Stroke = colorBrush,
                        StrokeThickness = _strokeThickness,
                        Fill = fillBrush
                    };
                    break;
                case "ellipse":
                    _currentElement = new Ellipse
                    {
                        Stroke = colorBrush,
                        StrokeThickness = _strokeThickness,
                        Fill = fillBrush
                    };
                    break;
                case "line":
                    _currentElement = new Line
                    {
                        Stroke = colorBrush,
                        StrokeThickness = _strokeThickness,
                        X1 = 0, Y1 = 0, X2 = 0, Y2 = 0,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    break;
                case "arrow":
                    _currentElement = CreateArrow(_drawStart.X, _drawStart.Y, _drawStart.X, _drawStart.Y, colorBrush);
                    break;
                case "text":
                    var tb = new TextBox
                    {
                        Text = "",
                        Foreground = new SolidColorBrush(_strokeColor),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(1),
                        BorderBrush = colorBrush,
                        FontSize = SettingsWindow.DefaultFontSize,
                        MinWidth = 80,
                        MaxWidth = 300,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    tb.Loaded += (s, ev) => tb.Focus();
                    tb.TextChanged += (s, ev) => UpdateSelectionIndicator();
                    tb.LostFocus += (s, ev) =>
                    {
                        if (string.IsNullOrEmpty(tb.Text.Trim()))
                        {
                            EditorCanvas.Children.Remove(tb);
                            _annotations.Remove(tb);
                        }
                    };
                    _currentElement = tb;
                    Canvas.SetLeft(_currentElement, _drawStart.X);
                    Canvas.SetTop(_currentElement, _drawStart.Y);
                    EditorCanvas.Children.Add(_currentElement);
                    AttachElementEvents(_currentElement);
                    _annotations.Add(_currentElement);
                    _isDrawing = false;
                    EditorCanvas.ReleaseMouseCapture();
                    return;
                case "blur":
                    _currentElement = new System.Windows.Controls.Image
                    {
                        Stretch = Stretch.Fill
                    };
                    break;
            }

            if (_currentElement != null)
            {
                Canvas.SetLeft(_currentElement, _drawStart.X);
                Canvas.SetTop(_currentElement, _drawStart.Y);
                
                if (_currentElement is Rectangle || _currentElement is Ellipse || _currentElement is System.Windows.Controls.Image)
                {
                    var fe = _currentElement as FrameworkElement;
                    if (fe != null)
                    {
                        fe.Width = 0;
                        fe.Height = 0;
                    }
                }
                if (_currentElement is Line)
                {
                    Canvas.SetLeft(_currentElement, 0);
                    Canvas.SetTop(_currentElement, 0);
                }
                if (_currentElement is Path)
                {
                    Canvas.SetLeft(_currentElement, 0);
                    Canvas.SetTop(_currentElement, 0);
                }
                EditorCanvas.Children.Add(_currentElement);
            }
        }

        private Geometry CreateArrowGeometry(double x1, double y1, double x2, double y2)
        {
            double angle = Math.Atan2(y2 - y1, x2 - x1);
            double arrowSize = Math.Max(15, _strokeThickness * 4);
            double arrowAngle = Math.PI / 6;

            var p1 = new Point(x2, y2);
            var p2 = new Point(
                x2 - arrowSize * Math.Cos(angle - arrowAngle),
                y2 - arrowSize * Math.Sin(angle - arrowAngle));
            var p3 = new Point(
                x2 - arrowSize * Math.Cos(angle + arrowAngle),
                y2 - arrowSize * Math.Sin(angle + arrowAngle));

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x1, y1), false, false);
                ctx.LineTo(new Point(x2, y2), true, false);
                ctx.BeginFigure(p1, true, true);
                ctx.LineTo(p2, true, false);
                ctx.LineTo(p3, true, false);
            }
            return geometry;
        }

        private Path CreateArrow(double x1, double y1, double x2, double y2, SolidColorBrush brush)
        {
            return new Path
            {
                Stroke = brush,
                StrokeThickness = _strokeThickness,
                Fill = brush,
                Data = CreateArrowGeometry(x1, y1, x2, y2),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Tag = new Tuple<Point, Point>(new Point(x1, y1), new Point(x2, y2))
            };
        }

        private UIElement CreateMosaicElement(double x, double y, double w, double h)
        {
            if (w < 1 || h < 1) return new Rectangle { Fill = Brushes.Transparent };
            int blockSize = SettingsWindow.MosaicSize > 0 ? SettingsWindow.MosaicSize : 15;
            var mosaicBmp = CreateMosaicBitmap(_originalBitmap, (int)x, (int)y, (int)w, (int)h, blockSize);
            var img = new System.Windows.Controls.Image
            {
                Source = ScreenshotHelper.BitmapToBitmapSource(mosaicBmp),
                Stretch = Stretch.None,
                Width = w,
                Height = h
            };
            return img;
        }

        private Bitmap CreateMosaicBitmap(Bitmap source, int srcX, int srcY, int width, int height, int blockSize)
        {
            srcX = Math.Max(0, Math.Min(srcX, source.Width - 1));
            srcY = Math.Max(0, Math.Min(srcY, source.Height - 1));
            width = Math.Min(width, source.Width - srcX);
            height = Math.Min(height, source.Height - srcY);
            if (width <= 0 || height <= 0) return new Bitmap(1, 1);

            var result = new Bitmap(width, height);
            for (int y = 0; y < height; y += blockSize)
            {
                for (int x = 0; x < width; x += blockSize)
                {
                    int blockW = Math.Min(blockSize, width - x);
                    int blockH = Math.Min(blockSize, height - y);
                    long r = 0, g = 0, b = 0, count = 0;
                    for (int py = 0; py < blockH; py++)
                    {
                        for (int px = 0; px < blockW; px++)
                        {
                            var pixel = source.GetPixel(srcX + x + px, srcY + y + py);
                            r += pixel.R; g += pixel.G; b += pixel.B;
                            count++;
                        }
                    }
                    DColor avg = DColor.FromArgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
                    for (int py = 0; py < blockH; py++)
                        for (int px = 0; px < blockW; px++)
                            result.SetPixel(x + px, y + py, avg);
                }
            }
            return result;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _currentElement == null) return;
            var mpos = e.GetPosition(EditorCanvas);

            if (_currentElement is Rectangle || _currentElement is Ellipse)
            {
                double x = Math.Min(_drawStart.X, mpos.X);
                double y = Math.Min(_drawStart.Y, mpos.Y);
                double w = Math.Abs(mpos.X - _drawStart.X);
                double h = Math.Abs(mpos.Y - _drawStart.Y);
                Canvas.SetLeft(_currentElement, x);
                Canvas.SetTop(_currentElement, y);
                (_currentElement as FrameworkElement).Width = w;
                (_currentElement as FrameworkElement).Height = h;
            }
            else if (_currentElement is Line)
            {
                var line = _currentElement as Line;
                line.X1 = _drawStart.X; line.Y1 = _drawStart.Y;
                line.X2 = mpos.X; line.Y2 = mpos.Y;
            }
            else if (_currentElement is Path)
            {
                var path = _currentElement as Path;
                path.Data = CreateArrowGeometry(_drawStart.X, _drawStart.Y, mpos.X, mpos.Y);
                path.Tag = new Tuple<Point, Point>(new Point(_drawStart.X, _drawStart.Y), new Point(mpos.X, mpos.Y));
            }
            else if (_currentElement is System.Windows.Controls.Image)
            {
                var img = _currentElement as System.Windows.Controls.Image;
                double x = Math.Min(_drawStart.X, mpos.X);
                double y = Math.Min(_drawStart.Y, mpos.Y);
                double w = Math.Abs(mpos.X - _drawStart.X);
                double h = Math.Abs(mpos.Y - _drawStart.Y);
                if (w > 1 && h > 1)
                {
                    Canvas.SetLeft(img, x);
                    Canvas.SetTop(img, y);
                    img.Width = w;
                    img.Height = h;
                    int blockSize = SettingsWindow.MosaicSize > 0 ? SettingsWindow.MosaicSize : 15;
                    var mosaicBmp = CreateMosaicBitmap(_originalBitmap, (int)x, (int)y, (int)w, (int)h, blockSize);
                    img.Source = ScreenshotHelper.BitmapToBitmapSource(mosaicBmp);
                }
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            EditorCanvas.ReleaseMouseCapture();
            if (_currentElement != null)
            {
                AttachElementEvents(_currentElement);
                _annotations.Add(_currentElement);
            }
            _currentElement = null;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement != null)
            {
                EditorCanvas.Children.Remove(_selectedElement);
                _annotations.Remove(_selectedElement);
                DeselectAll();
                StatusText.Text = "已删除";
            }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_annotations.Count == 0) return;
            var last = _annotations[_annotations.Count - 1];
            _annotations.RemoveAt(_annotations.Count - 1);
            _undoStack.Add(last);
            EditorCanvas.Children.Remove(last);
            if (_selectedElement == last) DeselectAll();
            StatusText.Text = "已撤销";
        }

        private Bitmap GetFinalBitmap()
        {
            DeselectAll();

            // Temporarily reset zoom to 1.0 to render pixel-perfect original resolution
            double originalScaleX = CanvasScaleTransform != null ? CanvasScaleTransform.ScaleX : 1.0;
            double originalScaleY = CanvasScaleTransform != null ? CanvasScaleTransform.ScaleY : 1.0;
            if (CanvasScaleTransform != null)
            {
                CanvasScaleTransform.ScaleX = 1.0;
                CanvasScaleTransform.ScaleY = 1.0;
                EditorCanvas.UpdateLayout();
            }

            int width = (int)Math.Round(EditorCanvas.Width);
            int height = (int)Math.Round(EditorCanvas.Height);
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;

            var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new VisualBrush(EditorCanvas)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }
            renderBitmap.Render(visual);

            // Restore zoom scale
            if (CanvasScaleTransform != null)
            {
                CanvasScaleTransform.ScaleX = originalScaleX;
                CanvasScaleTransform.ScaleY = originalScaleY;
                EditorCanvas.UpdateLayout();
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                return new Bitmap(stream);
            }
        }

        private void ShowSavedStatus(string prefix, string filePath)
        {
            StatusText.Inlines.Clear();
            StatusText.Inlines.Add(new System.Windows.Documents.Run(prefix));
            
            var hyperlink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(filePath))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0, 103, 192)),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand
            };
            
            hyperlink.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", filePath));
                }
                catch { }
            };
            
            StatusText.Inlines.Add(hyperlink);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var bmp = GetFinalBitmap();
            ScreenshotHelper.SaveToClipboard(bmp);
            StatusText.Text = "已复制到剪贴板";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string defaultPath = MainWindow.DefaultSavePath;
            if (string.IsNullOrEmpty(defaultPath) || !Directory.Exists(defaultPath))
            {
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            try
            {
                var bmp = GetFinalBitmap();
                string filename = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                string fullPath = System.IO.Path.Combine(defaultPath, filename);
                
                bmp.Save(fullPath, ImageFormat.Png);
                ShowSavedStatus("已自动保存: ", fullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("自动保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            string defaultPath = MainWindow.DefaultSavePath;
            if (string.IsNullOrEmpty(defaultPath) || !Directory.Exists(defaultPath))
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dlg = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png",
                InitialDirectory = defaultPath
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bmp = GetFinalBitmap();
                    if (dlg.FileName.EndsWith(".jpg")) bmp.Save(dlg.FileName, ImageFormat.Jpeg);
                    else bmp.Save(dlg.FileName, ImageFormat.Png);
                    ShowSavedStatus("已保存: ", dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            var bmp = GetFinalBitmap();
            var pin = new PinWindow(bmp);
            pin.Show();
            StatusText.Text = "已贴图";
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer = null;
                }
                if (_originalBitmap != null)
                {
                    _originalBitmap.Dispose();
                    _originalBitmap = null;
                }
                if (_annotations != null)
                {
                    _annotations.Clear();
                }
                if (_undoStack != null)
                {
                    _undoStack.Clear();
                }
                if (ScreenshotImage != null)
                {
                    ScreenshotImage.Source = null;
                }
                MemoryHelper.TrimWorkingSet();
            }
            catch { }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedElement != null)
            {
                _isDraggingHandle = true;
                _handleMoveStart = e.GetPosition(EditorCanvas);
                DragHandle.CaptureMouse();
                e.Handled = true;
            }
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingHandle && _selectedElement != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _isDraggingHandle = false;
                    DragHandle.ReleaseMouseCapture();
                    return;
                }
                var pos = e.GetPosition(EditorCanvas);
                double dx = pos.X - _handleMoveStart.X;
                double dy = pos.Y - _handleMoveStart.Y;

                if (_selectedElement is Line)
                {
                    var line = _selectedElement as Line;
                    line.X1 += dx; line.Y1 += dy;
                    line.X2 += dx; line.Y2 += dy;
                }
                else if (_selectedElement is Path)
                {
                    MovePath(_selectedElement as Path, dx, dy);
                }
                else
                {
                    Canvas.SetLeft(_selectedElement, Canvas.GetLeft(_selectedElement) + dx);
                    Canvas.SetTop(_selectedElement, Canvas.GetTop(_selectedElement) + dy);
                }

                _handleMoveStart = pos;
                UpdateSelectionIndicator();
                e.Handled = true;
            }
        }

        private void DragHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingHandle)
            {
                _isDraggingHandle = false;
                DragHandle.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedElement != null)
            {
                _isResizing = true;
                _resizeStartPoint = e.GetPosition(EditorCanvas);
                ResizeHandle.CaptureMouse();

                if (_selectedElement is FrameworkElement)
                {
                    var fe = _selectedElement as FrameworkElement;
                    _initialWidth = double.IsNaN(fe.Width) ? fe.ActualWidth : fe.Width;
                    _initialHeight = double.IsNaN(fe.Height) ? fe.ActualHeight : fe.Height;
                }
                else if (_selectedElement is Line)
                {
                    var line = _selectedElement as Line;
                    _initialLineEnd = new Point(line.X2, line.Y2);
                }
                else if (_selectedElement is Path)
                {
                    var path = _selectedElement as Path;
                    var pts = path.Tag as Tuple<Point, Point>;
                    if (pts != null)
                    {
                        _initialLineStart = pts.Item1;
                        _initialLineEnd = pts.Item2;
                    }
                }
                e.Handled = true;
            }
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing && _selectedElement != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _isResizing = false;
                    ResizeHandle.ReleaseMouseCapture();
                    return;
                }

                var pos = e.GetPosition(EditorCanvas);
                double dx = pos.X - _resizeStartPoint.X;
                double dy = pos.Y - _resizeStartPoint.Y;

                if (_selectedElement is FrameworkElement)
                {
                    var fe = _selectedElement as FrameworkElement;
                    double newW = Math.Max(10, _initialWidth + dx);
                    double newH = Math.Max(10, _initialHeight + dy);
                    fe.Width = newW;
                    fe.Height = newH;
                }
                else if (_selectedElement is Line)
                {
                    var line = _selectedElement as Line;
                    line.X2 = _initialLineEnd.X + dx;
                    line.Y2 = _initialLineEnd.Y + dy;
                }
                else if (_selectedElement is Path)
                {
                    var path = _selectedElement as Path;
                    var newPt2 = new Point(_initialLineEnd.X + dx, _initialLineEnd.Y + dy);
                    path.Tag = new Tuple<Point, Point>(_initialLineStart, newPt2);
                    path.Data = CreateArrowGeometry(_initialLineStart.X, _initialLineStart.Y, newPt2.X, newPt2.Y);
                }

                UpdateSelectionIndicator();
                e.Handled = true;
            }
        }

        private void ResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                ResizeHandle.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void RotateHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedElement != null)
            {
                _isRotating = true;
                RotateHandle.CaptureMouse();

                double x = Canvas.GetLeft(_selectedElement);
                double y = Canvas.GetTop(_selectedElement);
                double w = 0, h = 0;
                if (_selectedElement is FrameworkElement)
                {
                    var fe = _selectedElement as FrameworkElement;
                    w = double.IsNaN(fe.Width) ? fe.ActualWidth : fe.Width;
                    h = double.IsNaN(fe.Height) ? fe.ActualHeight : fe.Height;
                }
                else if (_selectedElement is Line)
                {
                    var line = _selectedElement as Line;
                    x = Math.Min(line.X1, line.X2);
                    y = Math.Min(line.Y1, line.Y2);
                    w = Math.Abs(line.X2 - line.X1);
                    h = Math.Abs(line.Y2 - line.Y1);
                }
                else if (_selectedElement is Path)
                {
                    var path = _selectedElement as Path;
                    if (path.Data != null)
                    {
                        var bounds = path.Data.Bounds;
                        x = bounds.X;
                        y = bounds.Y;
                        w = bounds.Width;
                        h = bounds.Height;
                    }
                }

                _rotateCenter = new Point(x + w / 2, y + h / 2);

                Point pos = e.GetPosition(EditorCanvas);
                double dx = pos.X - _rotateCenter.X;
                double dy = pos.Y - _rotateCenter.Y;
                double mouseAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

                var rotateTransform = _selectedElement.RenderTransform as RotateTransform;
                double currentAngle = rotateTransform != null ? rotateTransform.Angle : 0;
                _initialAngle = currentAngle - mouseAngle;

                e.Handled = true;
            }
        }

        private void RotateHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isRotating && _selectedElement != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _isRotating = false;
                    RotateHandle.ReleaseMouseCapture();
                    return;
                }

                Point pos = e.GetPosition(EditorCanvas);
                double dx = pos.X - _rotateCenter.X;
                double dy = pos.Y - _rotateCenter.Y;
                double mouseAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
                double newAngle = mouseAngle + _initialAngle;

                var rotateTransform = _selectedElement.RenderTransform as RotateTransform;
                if (rotateTransform == null)
                {
                    rotateTransform = new RotateTransform(0);
                    _selectedElement.RenderTransformOrigin = new Point(0.5, 0.5);
                    _selectedElement.RenderTransform = rotateTransform;
                }
                rotateTransform.Angle = newAngle;

                UpdateSelectionIndicator();
                e.Handled = true;
            }
        }

        private void RotateHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRotating)
            {
                _isRotating = false;
                RotateHandle.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
    }
}