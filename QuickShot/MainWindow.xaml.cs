using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuickShot.Helpers;
using QuickShot.Views;

namespace QuickShot
{
    public partial class MainWindow : Window
    {
        public static string DefaultSavePath { get; set; }

        private HwndSource _hwndSource;
        private const int WM_HOTKEY = 0x0312;

        private const int HOTKEY_REGION_ID = 9000;
        private const int HOTKEY_WINDOW_ID = 9001;
        private const int HOTKEY_FULL_ID = 9002;
        private const int HOTKEY_OCR_ID = 9003;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _isShuttingDown = false;

        public MainWindow()
        {
            InitializeComponent();
            SettingsWindow.LoadSettings();
            Loaded += OnLoaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue)
            {
                MemoryHelper.TrimWorkingSet();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializeTrayIcon();
            UpdateHotkeyLabel();

            // Check if launched via startup parameter
            string[] args = Environment.GetCommandLineArgs();
            bool isStartup = false;
            foreach (var arg in args)
            {
                if (arg.Equals("-startup", StringComparison.OrdinalIgnoreCase) || 
                    arg.Equals("/startup", StringComparison.OrdinalIgnoreCase))
                {
                    isStartup = true;
                    break;
                }
            }

            Hide();
            if (!isStartup)
            {
                StartRegionCapture();
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                
                string location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (File.Exists(location))
                {
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(location);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }

                _notifyIcon.Text = "QuickShot 截图工具";
                _notifyIcon.Visible = true;

                var contextMenu = new System.Windows.Forms.ContextMenu();
                contextMenu.MenuItems.Add("显示主界面", (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); });
                contextMenu.MenuItems.Add("区域截图", (s, e) => StartRegionCapture());
                contextMenu.MenuItems.Add("文字识别 (OCR)", (s, e) => StartOcrCapture());
                contextMenu.MenuItems.Add("设置", (s, e) => OpenSettings());
                contextMenu.MenuItems.Add("关闭所有截图", (s, e) => CloseAllEditorWindows());
                contextMenu.MenuItems.Add("-");
                contextMenu.MenuItems.Add("退出", (s, e) => {
                    _isShuttingDown = true;
                    Application.Current.Shutdown();
                });

                _notifyIcon.ContextMenu = contextMenu;
                _notifyIcon.DoubleClick += (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isShuttingDown)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                base.OnClosing(e);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(handle);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(HwndHook);
            }
            RegisterGlobalHotkey();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }
            UnregisterGlobalHotkey();
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_REGION_ID)
                {
                    StartRegionCapture();
                    handled = true;
                }
                else if (id == HOTKEY_WINDOW_ID)
                {
                    WindowCapture_Click(null, null);
                    handled = true;
                }
                else if (id == HOTKEY_FULL_ID)
                {
                    FullCapture_Click(null, null);
                    handled = true;
                }
                else if (id == HOTKEY_OCR_ID)
                {
                    StartOcrCapture();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void RegisterGlobalHotkey()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                NativeMethods.UnregisterHotKey(handle, HOTKEY_REGION_ID);
                NativeMethods.UnregisterHotKey(handle, HOTKEY_WINDOW_ID);
                NativeMethods.UnregisterHotKey(handle, HOTKEY_FULL_ID);
                NativeMethods.UnregisterHotKey(handle, HOTKEY_OCR_ID);

                // Region Hotkey
                uint regMod = GetWin32Modifiers(SettingsWindow.HotkeyRegionModifiers);
                uint regVk = (uint)KeyInterop.VirtualKeyFromKey(SettingsWindow.HotkeyRegionKey);
                if (regVk != 0)
                {
                    NativeMethods.RegisterHotKey(handle, HOTKEY_REGION_ID, regMod, regVk);
                }

                // Window Hotkey
                uint winMod = GetWin32Modifiers(SettingsWindow.HotkeyWindowModifiers);
                uint winVk = (uint)KeyInterop.VirtualKeyFromKey(SettingsWindow.HotkeyWindowKey);
                if (winVk != 0)
                {
                    NativeMethods.RegisterHotKey(handle, HOTKEY_WINDOW_ID, winMod, winVk);
                }

                // Full Hotkey
                uint fullMod = GetWin32Modifiers(SettingsWindow.HotkeyFullModifiers);
                uint fullVk = (uint)KeyInterop.VirtualKeyFromKey(SettingsWindow.HotkeyFullKey);
                if (fullVk != 0)
                {
                    NativeMethods.RegisterHotKey(handle, HOTKEY_FULL_ID, fullMod, fullVk);
                }

                // OCR Hotkey
                uint ocrMod = GetWin32Modifiers(SettingsWindow.HotkeyOcrModifiers);
                uint ocrVk = (uint)KeyInterop.VirtualKeyFromKey(SettingsWindow.HotkeyOcrKey);
                if (ocrVk != 0)
                {
                    NativeMethods.RegisterHotKey(handle, HOTKEY_OCR_ID, ocrMod, ocrVk);
                }
            }
            catch { }
        }

        private void UnregisterGlobalHotkey()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.UnregisterHotKey(handle, HOTKEY_REGION_ID);
                    NativeMethods.UnregisterHotKey(handle, HOTKEY_WINDOW_ID);
                    NativeMethods.UnregisterHotKey(handle, HOTKEY_FULL_ID);
                    NativeMethods.UnregisterHotKey(handle, HOTKEY_OCR_ID);
                }
            }
            catch { }
        }

        private uint GetWin32Modifiers(ModifierKeys modifiers)
        {
            uint win32Modifiers = 0;
            if ((modifiers & ModifierKeys.Alt) != 0) win32Modifiers |= NativeMethods.MOD_ALT;
            if ((modifiers & ModifierKeys.Control) != 0) win32Modifiers |= NativeMethods.MOD_CONTROL;
            if ((modifiers & ModifierKeys.Shift) != 0) win32Modifiers |= NativeMethods.MOD_SHIFT;
            if ((modifiers & ModifierKeys.Windows) != 0) win32Modifiers |= NativeMethods.MOD_WIN;
            return win32Modifiers;
        }

        public void UpdateSettingsAndHotkey()
        {
            RegisterGlobalHotkey();
            UpdateHotkeyLabel();
        }

        private void UpdateHotkeyLabel()
        {
            if (HotkeyRegionText != null)
                HotkeyRegionText.Text = FormatHotkeyText(SettingsWindow.HotkeyRegionModifiers, SettingsWindow.HotkeyRegionKey);
            if (HotkeyWindowText != null)
                HotkeyWindowText.Text = FormatHotkeyText(SettingsWindow.HotkeyWindowModifiers, SettingsWindow.HotkeyWindowKey);
            if (HotkeyFullText != null)
                HotkeyFullText.Text = FormatHotkeyText(SettingsWindow.HotkeyFullModifiers, SettingsWindow.HotkeyFullKey);
            if (HotkeyOcrText != null)
                HotkeyOcrText.Text = FormatHotkeyText(SettingsWindow.HotkeyOcrModifiers, SettingsWindow.HotkeyOcrKey);
        }

        public static string FormatHotkeyText(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None) return "无";
            StringBuilder sb = new StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl + ");
            if ((modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt + ");
            if ((modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift + ");
            if ((modifiers & ModifierKeys.Windows) != 0) sb.Append("Win + ");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            var settings = new SettingsWindow();
            settings.ShowDialog();
        }

        private void RegionCapture_Click(object sender, RoutedEventArgs e)
        {
            StartRegionCapture();
        }

        private async void WindowCapture_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            await Task.Delay(50);
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            Bitmap bmp = ScreenshotHelper.CaptureWindow(hWnd);
            if (bmp != null)
            {
                ShowEditor(bmp);
            }
        }

        private async void FullCapture_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            await Task.Delay(50);
            Bitmap bmp = ScreenshotHelper.CaptureCurrentScreen();
            if (bmp != null)
            {
                ShowEditor(bmp);
            }
        }

        private async void StartRegionCapture()
        {
            Hide();
            await Task.Delay(50);
            Bitmap bmp = ScreenshotHelper.CaptureScreen();

            var overlay = new CaptureOverlay(bmp);
            overlay.Captured += (s, capBmp) =>
            {
                if (capBmp != null)
                    ShowEditor(capBmp);
            };
            overlay.Show();
        }

        private void OcrCapture_Click(object sender, RoutedEventArgs e)
        {
            StartOcrCapture();
        }

        public async void StartOcrCapture()
        {
            Hide();
            await Task.Delay(50);
            Bitmap bmp = ScreenshotHelper.CaptureScreen();

            var overlay = new CaptureOverlay(bmp, isOcrMode: true);
            overlay.Captured += async (s, capBmp) =>
            {
                if (capBmp != null)
                {
                    try
                    {
                        string text = await OcrHelper.RecognizeTextAsync(capBmp);
                        if (string.IsNullOrEmpty(text))
                        {
                            text = "（未识别到文字）";
                        }
                        else
                        {
                            Clipboard.SetText(text);
                        }

                        var ocrWin = new OcrResultWindow(text);
                        ocrWin.Show();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("OCR 识别失败: " + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        capBmp.Dispose();
                    }
                }
            };
            overlay.Show();
        }

        private void ShowEditor(Bitmap bitmap)
        {
            if (SettingsWindow.AutoCopy)
            {
                ScreenshotHelper.SaveToClipboard(bitmap);
            }

            string autoSavedPath = null;
            if (SettingsWindow.AutoSave)
            {
                string defaultPath = DefaultSavePath;
                if (string.IsNullOrEmpty(defaultPath) || !Directory.Exists(defaultPath))
                {
                    defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                }

                try
                {
                    string filename = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                    string fullPath = Path.Combine(defaultPath, filename);
                    bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
                    autoSavedPath = fullPath;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("自动保存截图失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            var editor = new EditorWindow(bitmap, autoSavedPath);
            editor.Show();
        }

        public static void CloseAllEditorWindows()
        {
            var windowsToClose = new System.Collections.Generic.List<Window>();
            foreach (Window win in Application.Current.Windows)
            {
                if (win is EditorWindow || win is PinWindow)
                {
                    windowsToClose.Add(win);
                }
            }
            foreach (var win in windowsToClose)
            {
                try { win.Close(); } catch { }
            }
        }
    }
}
