using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickShot.Helpers
{
    public static class MemoryHelper
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        public static void TrimWorkingSet()
        {
            try
            {
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Empty working set
                using (Process proc = Process.GetCurrentProcess())
                {
                    SetProcessWorkingSetSize(proc.Handle, -1, -1);
                }
            }
            catch { }
        }
    }
}
