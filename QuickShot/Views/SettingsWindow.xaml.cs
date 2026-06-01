using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Microsoft.Win32;

namespace QuickShot.Views
{
    public partial class SettingsWindow : Window
    {
        private static bool _autoCopy = true;
        private static bool _autoSave = false;
        private static int _mosaicSize = 15;
        private static int _fillAlpha = 0;
        private static int _defaultFontSize = 16;
        private static bool _startWithWindows = false;

        private static ModifierKeys _hotkeyRegionModifiers = ModifierKeys.Control | ModifierKeys.Alt;
        private static Key _hotkeyRegionKey = Key.A;

        private static ModifierKeys _hotkeyWindowModifiers = ModifierKeys.Control | ModifierKeys.Alt;
        private static Key _hotkeyWindowKey = Key.W;

        private static ModifierKeys _hotkeyFullModifiers = ModifierKeys.Control | ModifierKeys.Alt;
        private static Key _hotkeyFullKey = Key.F;

        public static bool AutoCopy { get { return _autoCopy; } set { _autoCopy = value; } }
        public static bool AutoSave { get { return _autoSave; } set { _autoSave = value; } }
        public static int MosaicSize { get { return _mosaicSize; } set { _mosaicSize = value; } }
        public static int FillAlpha { get { return _fillAlpha; } set { _fillAlpha = value; } }
        public static int DefaultFontSize { get { return _defaultFontSize; } set { _defaultFontSize = value; } }
        public static bool StartWithWindows { get { return _startWithWindows; } set { _startWithWindows = value; } }

        public static ModifierKeys HotkeyRegionModifiers { get { return _hotkeyRegionModifiers; } set { _hotkeyRegionModifiers = value; } }
        public static Key HotkeyRegionKey { get { return _hotkeyRegionKey; } set { _hotkeyRegionKey = value; } }

        public static ModifierKeys HotkeyWindowModifiers { get { return _hotkeyWindowModifiers; } set { _hotkeyWindowModifiers = value; } }
        public static Key HotkeyWindowKey { get { return _hotkeyWindowKey; } set { _hotkeyWindowKey = value; } }

        public static ModifierKeys HotkeyFullModifiers { get { return _hotkeyFullModifiers; } set { _hotkeyFullModifiers = value; } }
        public static Key HotkeyFullKey { get { return _hotkeyFullKey; } set { _hotkeyFullKey = value; } }

        private ModifierKeys _tempRegionModifiers;
        private Key _tempRegionKey;

        private ModifierKeys _tempWindowModifiers;
        private Key _tempWindowKey;

        private ModifierKeys _tempFullModifiers;
        private Key _tempFullKey;

        public SettingsWindow()
        {
            InitializeComponent();
            
            SavePathBox.Text = MainWindow.DefaultSavePath ?? "";
            AutoCopyCheck.IsChecked = AutoCopy;
            AutoSaveCheck.IsChecked = AutoSave;
            MosaicSlider.Value = MosaicSize;
            FillAlphaSlider.Value = FillAlpha;
            FontSizeSlider.Value = DefaultFontSize;
            StartWithWindowsCheck.IsChecked = StartWithWindows;

            _tempRegionModifiers = HotkeyRegionModifiers;
            _tempRegionKey = HotkeyRegionKey;

            _tempWindowModifiers = HotkeyWindowModifiers;
            _tempWindowKey = HotkeyWindowKey;

            _tempFullModifiers = HotkeyFullModifiers;
            _tempFullKey = HotkeyFullKey;

            UpdateHotkeyTextBoxDisplays();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择默认保存文件夹";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(SavePathBox.Text) && Directory.Exists(SavePathBox.Text))
                {
                    dialog.SelectedPath = SavePathBox.Text;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SavePathBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.DefaultSavePath = SavePathBox.Text.Trim();
            AutoCopy = AutoCopyCheck.IsChecked == true;
            AutoSave = AutoSaveCheck.IsChecked == true;
            MosaicSize = (int)MosaicSlider.Value;
            FillAlpha = (int)FillAlphaSlider.Value;
            DefaultFontSize = (int)FontSizeSlider.Value;
            
            HotkeyRegionModifiers = _tempRegionModifiers;
            HotkeyRegionKey = _tempRegionKey;

            HotkeyWindowModifiers = _tempWindowModifiers;
            HotkeyWindowKey = _tempWindowKey;

            HotkeyFullModifiers = _tempFullModifiers;
            HotkeyFullKey = _tempFullKey;

            StartWithWindows = StartWithWindowsCheck.IsChecked == true;

            SaveSettings();

            var mainWin = Application.Current.MainWindow as MainWindow;
            if (mainWin != null)
            {
                mainWin.UpdateSettingsAndHotkey();
            }

            Close();
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            TextBox tb = sender as TextBox;
            if (tb == null) return;

            ModifierKeys modifiers = Keyboard.Modifiers;
            Key key = e.Key;
            if (key == Key.System)
            {
                key = e.SystemKey;
            }

            ModifierKeys targetModifiers = ModifierKeys.None;
            Key targetKey = Key.None;

            if (key != Key.Escape && key != Key.Back)
            {
                if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                    key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LeftShift || key == Key.RightShift ||
                    key == Key.LWin || key == Key.RWin)
                {
                    return;
                }
                targetModifiers = modifiers;
                targetKey = key;
            }

            if (tb.Name == "HotkeyRegionBox")
            {
                _tempRegionModifiers = targetModifiers;
                _tempRegionKey = targetKey;
            }
            else if (tb.Name == "HotkeyWindowBox")
            {
                _tempWindowModifiers = targetModifiers;
                _tempWindowKey = targetKey;
            }
            else if (tb.Name == "HotkeyFullBox")
            {
                _tempFullModifiers = targetModifiers;
                _tempFullKey = targetKey;
            }

            UpdateHotkeyTextBoxDisplays();
        }

        private void UpdateHotkeyTextBoxDisplays()
        {
            HotkeyRegionBox.Text = FormatHotkeyText(_tempRegionModifiers, _tempRegionKey);
            HotkeyWindowBox.Text = FormatHotkeyText(_tempWindowModifiers, _tempWindowKey);
            HotkeyFullBox.Text = FormatHotkeyText(_tempFullModifiers, _tempFullKey);
        }

        private string FormatHotkeyText(ModifierKeys modifiers, Key key)
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

        public static void LoadSettings()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickShot");
                string path = Path.Combine(dir, "settings.txt");

                MainWindow.DefaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                AutoCopy = true;
                AutoSave = false;
                MosaicSize = 15;
                FillAlpha = 0;
                DefaultFontSize = 16;
                StartWithWindows = false;

                HotkeyRegionModifiers = ModifierKeys.Control | ModifierKeys.Alt;
                HotkeyRegionKey = Key.A;
                HotkeyWindowModifiers = ModifierKeys.Control | ModifierKeys.Alt;
                HotkeyWindowKey = Key.W;
                HotkeyFullModifiers = ModifierKeys.Control | ModifierKeys.Alt;
                HotkeyFullKey = Key.F;

                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        string key = line.Substring(0, idx).Trim();
                        string val = line.Substring(idx + 1).Trim();

                        switch (key)
                        {
                            case "DefaultSavePath":
                                MainWindow.DefaultSavePath = val;
                                break;
                            case "AutoCopy":
                                AutoCopy = bool.Parse(val);
                                break;
                            case "AutoSave":
                                AutoSave = bool.Parse(val);
                                break;
                            case "MosaicSize":
                                MosaicSize = int.Parse(val);
                                break;
                            case "FillAlpha":
                                FillAlpha = int.Parse(val);
                                break;
                            case "FontSize":
                                DefaultFontSize = int.Parse(val);
                                break;
                            case "HotkeyRegionModifiers":
                                HotkeyRegionModifiers = (ModifierKeys)Enum.Parse(typeof(ModifierKeys), val);
                                break;
                            case "HotkeyRegionKey":
                                HotkeyRegionKey = (Key)Enum.Parse(typeof(Key), val);
                                break;
                            case "HotkeyWindowModifiers":
                                HotkeyWindowModifiers = (ModifierKeys)Enum.Parse(typeof(ModifierKeys), val);
                                break;
                            case "HotkeyWindowKey":
                                HotkeyWindowKey = (Key)Enum.Parse(typeof(Key), val);
                                break;
                            case "HotkeyFullModifiers":
                                HotkeyFullModifiers = (ModifierKeys)Enum.Parse(typeof(ModifierKeys), val);
                                break;
                            case "HotkeyFullKey":
                                HotkeyFullKey = (Key)Enum.Parse(typeof(Key), val);
                                break;
                            case "StartWithWindows":
                                StartWithWindows = bool.Parse(val);
                                break;
                        }
                    }
                }

                using (RegistryKey rkey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    StartWithWindows = rkey != null && rkey.GetValue("QuickShot") != null;
                }
            }
            catch { }
        }

        public static void SaveSettings()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickShot");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "settings.txt");
                using (StreamWriter sw = new StreamWriter(path, false))
                {
                    sw.WriteLine("DefaultSavePath=" + (MainWindow.DefaultSavePath ?? ""));
                    sw.WriteLine("AutoCopy=" + AutoCopy.ToString());
                    sw.WriteLine("AutoSave=" + AutoSave.ToString());
                    sw.WriteLine("MosaicSize=" + MosaicSize.ToString());
                    sw.WriteLine("FillAlpha=" + FillAlpha.ToString());
                    sw.WriteLine("FontSize=" + DefaultFontSize.ToString());
                    sw.WriteLine("HotkeyRegionModifiers=" + HotkeyRegionModifiers.ToString());
                    sw.WriteLine("HotkeyRegionKey=" + HotkeyRegionKey.ToString());
                    sw.WriteLine("HotkeyWindowModifiers=" + HotkeyWindowModifiers.ToString());
                    sw.WriteLine("HotkeyWindowKey=" + HotkeyWindowKey.ToString());
                    sw.WriteLine("HotkeyFullModifiers=" + HotkeyFullModifiers.ToString());
                    sw.WriteLine("HotkeyFullKey=" + HotkeyFullKey.ToString());
                    sw.WriteLine("StartWithWindows=" + StartWithWindows.ToString());
                }

                SetStartup(StartWithWindows);
            }
            catch { }
        }

        public static void SetStartup(bool start)
        {
            try
            {
                string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (RegistryKey rkey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (start)
                    {
                        rkey.SetValue("QuickShot", "\"" + path + "\" -startup");
                    }
                    else
                    {
                        rkey.DeleteValue("QuickShot", false);
                    }
                }
            }
            catch { }
        }
    }
}
