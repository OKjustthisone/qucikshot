using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickShot.Views
{
    public partial class OcrResultWindow : Window
    {
        private DispatcherTimer _autoCloseTimer;
        private bool _isPinned = false;

        public OcrResultWindow(string text)
        {
            InitializeComponent();

            ResultTextBox.Text = text ?? string.Empty;
            CharCountText.Text = string.Format("共 {0} 字", ResultTextBox.Text.Length);

            PositionInBottomRight();

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };

            // 8-second auto close timer
            _autoCloseTimer = new DispatcherTimer();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(8);
            _autoCloseTimer.Tick += (s, e) =>
            {
                if (!_isPinned)
                {
                    _autoCloseTimer.Stop();
                    Close();
                }
            };
            _autoCloseTimer.Start();
        }

        private void PositionInBottomRight()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                CancelAutoClose();
                DragMove();
            }
        }

        private void CancelAutoClose()
        {
            if (_autoCloseTimer != null && _autoCloseTimer.IsEnabled)
            {
                _autoCloseTimer.Stop();
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            CancelAutoClose();
            if (_isPinned)
            {
                PinIconText.Text = "📌 (固定)";
                StatusSubText.Text = "窗口已固定";
                PinButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 240, 255));
            }
            else
            {
                PinIconText.Text = "📌";
                StatusSubText.Text = "已自动复制到剪贴板";
                PinButton.ClearValue(BackgroundProperty);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAutoClose();
            Clipboard.SetText(ResultTextBox.Text ?? string.Empty);
            CopyButton.Content = "已复制 ✓";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, ev) =>
            {
                CopyButton.Content = "重新复制";
                timer.Stop();
            };
            timer.Start();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAutoClose();
            Close();
        }

        private void ResultTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            CancelAutoClose();
        }
    }
}
