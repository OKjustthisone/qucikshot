using System;
using System.Windows;
using System.Runtime.InteropServices;

namespace QuickShot
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        static App()
        {
            try
            {
                if (Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    try
                    {
                        SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
                    }
                    catch
                    {
                        SetProcessDPIAware();
                    }
                }
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                if (Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
            }
            catch { }
            base.OnStartup(e);
        }
    }
}
