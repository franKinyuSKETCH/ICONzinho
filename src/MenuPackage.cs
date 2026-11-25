using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace Iconzinho
{
    /// <summary>
    /// O pacote que põe o item no menu novo do Windows 11. Ele não tem assinatura digital, e o Windows
    /// só aceita um pacote assim com administrador; por isso a instalação dele passa pelo UAC.
    /// Os métodos que usam WinRT ficam separados para nem serem carregados no Windows 10.
    /// </summary>
    static class MenuPackage
    {
        public static bool Supported { get { return Environment.OSVersion.Version.Build >= 22000 && Environment.Is64BitOperatingSystem; } }

        /// <summary>Versão do pacote instalada para este usuário, ou null.</summary>
        public static string InstalledVersion()
        {
            if (!Supported) return null;
            Package p = Find();
            if (p == null) return null;
            PackageVersion v = p.Id.Version;
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Package Find()
        {
            foreach (Package p in new PackageManager().FindPackagesForUser(""))
                if (p.Id.Name == App.PackageName) return p;
            return null;
        }

        public static void Remove()
        {
            if (Supported && Find() != null) RemoveCore(Find().Id.FullName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RemoveCore(string fullName)
        {
            Wait(new PackageManager().RemovePackageAsync(fullName));
        }

        /// <summary>Chamado pelo instalador: abre o ICONzinho.exe já instalado como administrador.</summary>
        public static void InstallElevated(string msix)
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            Process p;
            try
            {
                p = Process.Start(new ProcessStartInfo(App.ExePath, "--add-package \"" + msix + "\" " + sid) { UseShellExecute = true, Verb = "runas" });
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                    throw new OperationCanceledException(App.T("The new menu needs administrator permission, which was not granted.",
                        "O menu novo precisa de permissão de administrador, que não foi concedida."));
                throw;
            }
            using (p)
            {
                p.WaitForExit();
                if (p.ExitCode == 0) return;
                string why = p.ExitCode == WrongUser
                    ? App.T("the administrator account is not the same as yours", "a conta de administrador não é a mesma que a sua")
                    : new Win32Exception(p.ExitCode).Message + " (0x" + p.ExitCode.ToString("X8") + ")";
                throw new InvalidOperationException(App.T("The new menu could not be enabled: ", "Não foi possível ativar o menu novo: ") + why);
            }
        }

        const int WrongUser = 0x20001392; // código próprio (bit de cliente), não conflita com HRESULT

        /// <summary>Já como administrador. Devolve 0 ou o HRESULT.</summary>
        public static int AddFromElevated(string msix, string sid)
        {
            // Com a senha de outra conta, o pacote seria instalado para ela, e não para quem pediu.
            if (WindowsIdentity.GetCurrent().User.Value != sid) return WrongUser;
            try { Add(msix); return 0; }
            catch (Exception ex) { return ex.HResult != 0 ? ex.HResult : 1; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Add(string msix)
        {
            AddPackageOptions o = new AddPackageOptions { AllowUnsigned = true, ForceTargetAppShutdown = true };
            Wait(new PackageManager().AddPackageByUriAsync(new Uri(msix), o));
        }

        static void Wait(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(50);
            if (op.Status == AsyncStatus.Error) Marshal.ThrowExceptionForHR(op.ErrorCode.HResult);
        }
    }
}
