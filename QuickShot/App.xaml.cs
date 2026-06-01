using System.Windows;
using System.Runtime.InteropServices;

namespace QuickShot
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch { }
            base.OnStartup(e);
        }
    }
}
