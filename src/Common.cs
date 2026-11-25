using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Iconzinho
{
    /// <summary>O que o programa e o instalador compartilham: nomes, caminhos, chaves e idioma.</summary>
    static class App
    {
        public const string Version = "1.4.0";
        // Versão do pacote do menu novo: só muda com o que está em menu/, para uma atualização comum
        // não pedir administrador à toa.
        public const string MenuVersion = "1.3.1";
        public const string Author = "frankinyu_SKETCH";
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg", ".bmp", ".gif" };

#if TEST    // compilação de teste: registro e pastas à parte, não toca na instalação real
        public const string Name = "ICONzinhoTest";
        const string Root = @"Software\ICONzinhoTest\HKCU\";
#else
        public const string Name = "ICONzinho";
        const string Root = "";
#endif
        public const string PackageName = "frankinyuSKETCH." + Name; // igual ao AppxManifest.xml
        public static readonly string[] VerbKeys = Extensions.Select(
            e => Root + @"Software\Classes\SystemFileAssociations\" + e + @"\shell\" + Name).ToArray();
        public const string UninstallKey = Root + @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Name;
        public const string EnvironmentKey = Root + "Environment";
        public const string SettingsKey = Root + @"Software\" + Name;

        public const string LibraryVerbKey = Root + @"Software\Classes\SystemFileAssociations\.ico\shell\" + Name;
        public const string AddVerbKey = LibraryVerbKey + "Add";

        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string InstallDir = Path.Combine(Local, "Programs", Name);
        public static readonly string ExePath = Path.Combine(InstallDir, Name + ".exe");
        public static readonly string CliPath = Path.Combine(InstallDir, "iconz.exe");
        public static readonly string DataDir = Path.Combine(Local, Name);
        public static readonly string QueueDir = Path.Combine(DataDir, "queue");

        /// <summary>"en" ou "pt": o escolhido na instalação ou, sem instalação, o do Windows.</summary>
        public static string Language = SavedLanguage() ?? SystemLanguage();

        public static string T(string en, string pt) { return Language == "pt" ? pt : en; }

        public static string MenuText { get { return T("Create icon (.ico)", "Criar ícone (.ico)"); } }

        public static string LibraryText { get { return T("Create icon library (.dll)", "Criar biblioteca de ícones (.dll)"); } }

        public static string AddText { get { return T("Add to icon library...", "Adicionar à biblioteca de ícones..."); } }

        static string SavedLanguage()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return k == null ? null : k.GetValue("Language") as string;
        }

        static string SystemLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt" : "en";
        }
    }
}
