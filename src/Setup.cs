using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Iconzinho
{
    [Flags]
    enum Menus { Classic = 1, New = 2, Both = 3 }

    static class Setup
    {
        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int ev, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Sem argumentos abre a janela. "/S" não pergunta nada; "/uninstall" remove; "/lang=en" ou "/lang=pt"; "/menu=classic", "/menu=new" ou "/menu=both".</summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = false, remove = false;
            Menus menus = Menus.Classic;
            foreach (string a in args)
            {
                string s = a.ToLowerInvariant();
                if (s == "/s") silent = true;
                else if (s == "/uninstall") remove = true;
                else if (s == "/menu=new") menus = Menus.New;
                else if (s == "/menu=both") menus = Menus.Both;
                else if (s == "/lang=en") App.Language = "en";
                else if (s == "/lang=pt") App.Language = "pt";
            }
            if (silent)
            {
                try { if (remove) Uninstall(); else Install(menus); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }

        /// <summary>Versão instalada, ou null.</summary>
        public static string InstalledVersion()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(App.UninstallKey))
                if (k != null) return k.GetValue("DisplayVersion") as string ?? "?";
            return File.Exists(App.ExePath) ? "?" : null;
        }

        public static void Uninstall()
        {
            // O exe primeiro: se estiver em uso e der erro, o registro continua inteiro para tentar de novo.
            if (File.Exists(App.ExePath)) File.Delete(App.ExePath);
            Uninstaller.RemoveSettings();
            // Sem recursão de propósito: se alguém pôs outra coisa na pasta, ela fica.
            try { if (Directory.Exists(App.InstallDir)) Directory.Delete(App.InstallDir); } catch (IOException) { }
        }

        /// <summary>Menus ativos na instalação atual.</summary>
        public static Menus InstalledMenus()
        {
            Menus m = 0;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(App.VerbKeys[0])) if (k != null) m |= Menus.Classic;
            if (MenuPackage.InstalledVersion() != null) m |= Menus.New;
            return m;
        }

        static void Extract(string resource, string path)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (FileStream f = File.Create(path))
                s.CopyTo(f);
        }

        public static void Install(Menus menus)
        {
            bool fresh = InstalledVersion() == null;
            Directory.CreateDirectory(App.InstallDir);
            Extract("ICONzinho.exe", App.ExePath);

            // O menu novo primeiro, porque é o que pode falhar (UAC negado, por exemplo): até aqui os
            // menus que já existiam continuam como estavam.
            try
            {
                if ((menus & Menus.New) == 0) MenuPackage.Remove();
                else if (MenuPackage.InstalledVersion() != App.Version) AddPackage();
            }
            catch (Exception)
            {
                if (fresh) Uninstall();
                throw;
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(App.SettingsKey))
                k.SetValue("Language", App.Language);

            foreach (string key in App.VerbKeys)
            if ((menus & Menus.Classic) == 0) Registry.CurrentUser.DeleteSubKeyTree(key, false);
            else using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
            {
                k.SetValue("MUIVerb", App.MenuText);
                k.SetValue("Icon", "\"" + App.ExePath + "\"");
                k.SetValue("MultiSelectModel", "Player"); // sem isso o item some acima de 15 arquivos
                using (RegistryKey c = k.CreateSubKey("command"))
                    c.SetValue("", "\"" + App.ExePath + "\" --queue \"%1\"");
            }

            // É esta chave que faz o programa aparecer em Configurações > Aplicativos instalados.
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                k.SetValue("DisplayName", App.Name);
                k.SetValue("DisplayVersion", App.Version);
                k.SetValue("Publisher", App.Author);
                k.SetValue("DisplayIcon", App.ExePath);
                k.SetValue("InstallLocation", App.InstallDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + App.ExePath + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + App.ExePath + "\" --uninstall --quiet");
                k.SetValue("EstimatedSize", (int)(new FileInfo(App.ExePath).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }

        static void AddPackage()
        {
            string msix = Path.Combine(App.InstallDir, "menu.msix");
            try
            {
                Extract("ICONzinho.msix", msix);
                MenuPackage.InstallElevated(msix);
            }
            finally { File.Delete(msix); }
        }    }

    class SetupForm : Form
    {
        readonly Label title = new Label(), body = new Label(), langLabel = new Label(), author = new Label(), tipsTitle = new Label(), tips = new Label();
        readonly RadioButton en = new RadioButton(), pt = new RadioButton();
        readonly Label menuLabel = new Label();
        readonly Panel menuBox = new Panel(); // separa este grupo de opções do idioma
        readonly RadioButton classic = new RadioButton(), modern = new RadioButton(), both = new RadioButton();
        readonly Button ok = new Button(), cancel = new Button(), remove = new Button();
        enum Stage { Start, Installed, Removed }
        Stage stage = Stage.Start;
        readonly string found = Setup.InstalledVersion();

        public SetupForm()
        {
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.75f);
            ClientSize = new Size(460, 384);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);

            PictureBox pic = new PictureBox { Bounds = new Rectangle(24, 22, 48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            using (Icon i = new Icon(s, 96, 96)) pic.Image = i.ToBitmap();

            title.Bounds = new Rectangle(88, 20, 350, 32);
            author.Bounds = new Rectangle(90, 52, 350, 20);
            author.ForeColor = Color.DimGray;
            title.Font = new Font("Segoe UI Semibold", 15f);
            body.Bounds = new Rectangle(24, 86, 412, 66);
            menuLabel.Bounds = new Rectangle(24, 150, 412, 22);
            menuBox.Bounds = new Rectangle(24, 172, 412, 84);
            classic.Bounds = new Rectangle(0, 0, 410, 26);
            modern.Bounds = new Rectangle(0, 27, 410, 26);
            both.Bounds = new Rectangle(0, 54, 410, 26);
            menuBox.Controls.AddRange(new Control[] { classic, modern, both });
            langLabel.Bounds = new Rectangle(24, 270, 180, 24);
            en.Bounds = new Rectangle(24, 294, 110, 26);
            pt.Bounds = new Rectangle(140, 294, 130, 26);
            en.Text = "English";
            pt.Text = "Português";

            // só aparecem na segunda tela
            tipsTitle.Bounds = new Rectangle(24, 222, 412, 22);
            tipsTitle.Font = new Font("Segoe UI Semibold", 9.75f);
            tips.Bounds = new Rectangle(24, 246, 412, 196);
            tipsTitle.Visible = tips.Visible = false;

            Panel bar = new Panel { Bounds = new Rectangle(0, 334, 460, 50), BackColor = Color.FromArgb(243, 243, 243), Dock = DockStyle.Bottom };
            ok.Bounds = new Rectangle(236, 10, 100, 30);
            cancel.Bounds = new Rectangle(344, 10, 100, 30);
            remove.Bounds = new Rectangle(16, 10, 110, 30);
            remove.Visible = found != null;
            ok.UseVisualStyleBackColor = cancel.UseVisualStyleBackColor = remove.UseVisualStyleBackColor = true;
            bar.Controls.AddRange(new Control[] { ok, cancel, remove });
            Controls.AddRange(new Control[] { pic, title, author, body, menuLabel, menuBox, langLabel, en, pt, tipsTitle, tips, bar });
            AcceptButton = ok;
            CancelButton = cancel;

            Menus now = Setup.InstalledMenus();
            if (now == 0) now = MenuPackage.Supported ? Menus.Both : Menus.Classic;
            (now == Menus.Both ? both : now == Menus.New ? modern : classic).Checked = true;
            modern.Enabled = both.Enabled = MenuPackage.Supported;
            (App.Language == "pt" ? pt : en).Checked = true;
            en.CheckedChanged += delegate { App.Language = en.Checked ? "en" : "pt"; Texts(); };
            ok.Click += delegate { if (stage == Stage.Start) DoInstall(); else Close(); };
            remove.Click += delegate { DoUninstall(); };
            cancel.Click += delegate { Close(); };
            Texts();
        }

        Menus Chosen { get { return both.Checked ? Menus.Both : modern.Checked ? Menus.New : Menus.Classic; } }

        void DoInstall()
        {
            if (!Try(delegate { Setup.Install(Chosen); })) return;
            Finish(Stage.Installed);
            tipsTitle.Visible = tips.Visible = true;
            body.Height = tipsTitle.Top - body.Top;
            int grow = tips.Bottom - (ClientSize.Height - ok.Parent.Height);
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + grow);
            Top = Math.Max(0, Top - grow / 2); // continua centralizada
        }

        void DoUninstall()
        {
            if (Try(Setup.Uninstall)) Finish(Stage.Removed);
        }

        bool Try(Action action)
        {
            try { action(); return true; }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void Finish(Stage s)
        {
            stage = s;
            menuLabel.Visible = menuBox.Visible = langLabel.Visible = en.Visible = pt.Visible = cancel.Visible = remove.Visible = false;
            ok.Left = cancel.Left;
            CancelButton = ok;
            ok.Focus();
            Texts();
        }

        void Texts()
        {
            Text = App.T(App.Name + " Setup", "Instalação do " + App.Name);
            langLabel.Text = App.T("Language", "Idioma");
            menuLabel.Text = MenuPackage.Supported
                ? App.T("Right-click menu (the new one asks for administrator)", "Menu do clique direito (o novo pede administrador)")
                : App.T("Right-click menu (the new one needs Windows 11)", "Menu do clique direito (o novo exige Windows 11)");
            classic.Text = App.T("Classic menu only", "Só no menu clássico");
            modern.Text = App.T("New Windows 11 menu only", "Só no menu novo do Windows 11");
            both.Text = App.T("Both menus", "Nos dois menus");
            cancel.Text = App.T("Cancel", "Cancelar");
            author.Text = App.T("by ", "por ") + App.Author;
            remove.Text = App.T("Uninstall", "Desinstalar");
            if (stage == Stage.Removed)
            {
                title.Text = App.T("Removed", "Removido");
                body.Text = App.T(App.Name + " was removed. The icons you created were kept.",
                                  "O " + App.Name + " foi removido. Os ícones que você criou foram mantidos.");
                ok.Text = App.T("Close", "Fechar");
            }
            else if (stage == Stage.Start && found != null)
            {
                title.Text = App.T(App.Name + " is already installed", "O " + App.Name + " já está instalado");
                string v = found == "?" ? "" : App.T("Installed version: ", "Versão instalada: ") + found + ". ";
                body.Text = found == App.Version
                    ? v + App.T("You can reinstall it, change the menus or the language, or uninstall it.",
                                "Você pode reinstalar, trocar os menus ou o idioma, ou desinstalar.")
                    : v + App.T("You can update it to version " + App.Version + " or uninstall it.",
                                "Você pode atualizar para a versão " + App.Version + " ou desinstalar.");
                ok.Text = found == App.Version ? App.T("Reinstall", "Reinstalar") : App.T("Update", "Atualizar");
            }
            else if (stage == Stage.Start)
            {
                title.Text = App.T("Install " + App.Name, "Instalar o " + App.Name);
                body.Text = App.T(
                    "Adds \"" + App.MenuText + "\" to the right-click menu of PNG, JPG, WebP, SVG, BMP and GIF files. Installs for your user only.",
                    "Adiciona \"" + App.MenuText + "\" ao clique direito de arquivos PNG, JPG, WebP, SVG, BMP e GIF. Instala só para o seu usuário.");
                ok.Text = App.T("Install", "Instalar");
            }
            else
            {
                title.Text = App.T("All set", "Tudo pronto");
                bool hidden = Chosen == Menus.Classic && MenuPackage.Supported;
                body.Text = App.T("Select one or more images, right-click and choose \"" + App.MenuText + "\".",
                                  "Selecione uma ou mais imagens, clique com o botão direito e escolha \"" + App.MenuText + "\".")
                    + "\n\n" + (!hidden ? "" : App.T("On Windows 11 it is under \"Show more options\" (or Shift + right-click). ",
                                                     "No Windows 11 fica em \"Mostrar mais opções\" (ou Shift + clique direito). "))
                    + App.T("To remove it, run this installer again or go to Settings > Apps > Installed apps.",
                            "Para remover, abra este instalador de novo ou vá em Configurações > Aplicativos > Aplicativos instalados.");                tipsTitle.Text = App.T("Good to know", "Segredinhos:");
                tips.Text = App.T(
                    "•  One art per size: select 512.png, 256.png, 48.png, 16.png… together to get a single icon that uses your own art at each size. The number must be the image's real size.\n\n" +
                    "•  logo-32.png, logo-16.png… also works, and adding logo.svg fills in the missing sizes.\n\n" +
                    "•  Images with different names become one icon each.\n\n" +
                    "•  Non-square images are fitted and centered, never stretched.",
                    "•  Uma arte por tamanho: selecione 512.png, 256.png, 48.png, 16.png… juntos e saia com um único ícone, com a sua arte em cada tamanho. O número deve ser o tamanho real da imagem.\n\n" +
                    "•  logo-32.png, logo-16.png… também funciona, e um logo.svg junto completa os tamanhos que faltarem.\n\n" +
                    "•  Imagens com nomes diferentes viram um ícone cada.\n\n" +
                    "•  Imagem não quadrada é encaixada e centralizada, sem esticar.");
                ok.Text = App.T("Close", "Fechar");
            }
        }
    }
}
