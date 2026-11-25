using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Iconzinho
{
    /// <summary>Põe e tira a pasta do programa do PATH do usuário, sem tocar no resto dele.</summary>
    static class UserPath
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr window, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

        static bool IsOurs(string entry)
        {
            return entry.Trim().Trim('"').TrimEnd('\\').Equals(App.InstallDir, StringComparison.OrdinalIgnoreCase);
        }

        // Sem expandir: um %USERPROFILE% de outro programa precisa voltar como estava.
        static string Read(RegistryKey k) { return k == null ? "" : k.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? ""; }

        public static bool Contains()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(App.EnvironmentKey))
                return Read(k).Split(';').Any(IsOurs);
        }

        public static void Set(bool on)
        {
            if (on == Contains()) return;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(App.EnvironmentKey))
            {
                string path = Read(k);
                string changed = on
                    ? path + (path == "" || path.EndsWith(";") ? "" : ";") + App.InstallDir
                    : string.Join(";", path.Split(';').Where(e => !IsOurs(e)));
                RegistryValueKind kind = k.GetValueNames().Contains("Path", StringComparer.OrdinalIgnoreCase) ? k.GetValueKind("Path") : RegistryValueKind.ExpandString;
                k.SetValue("Path", changed, kind);
            }
            IntPtr r; // avisa os programas abertos; terminais já abertos continuam com o PATH antigo
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 2, 2000, out r);
        }
    }
}
