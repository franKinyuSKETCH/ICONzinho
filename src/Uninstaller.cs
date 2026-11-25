using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Iconzinho
{
    /// <summary>A parte da remoção que o programa e o instalador têm em comum (tudo menos o exe).</summary>
    static class Uninstaller
    {
        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int ev, uint flags, IntPtr item1, IntPtr item2);

        public static void RemoveSettings()
        {
            MenuPackage.Remove();
            foreach (string key in App.VerbKeys) Registry.CurrentUser.DeleteSubKeyTree(key, false);
            Registry.CurrentUser.DeleteSubKeyTree(App.LibraryVerbKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(App.UninstallKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(App.SettingsKey, false);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
            try
            {
                if (Directory.Exists(App.QueueDir)) Directory.Delete(App.QueueDir, true);
                if (Directory.Exists(App.DataDir)) Directory.Delete(App.DataDir); // só se estiver vazia
            }
            catch (IOException) { }
        }
    }
}
