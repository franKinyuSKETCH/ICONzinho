using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Iconzinho
{
    static class Program
    {
        static bool quiet;

        [STAThread]
        static int Main(string[] argv)
        {
            List<string> args = argv.ToList();
            quiet = args.Remove("--quiet");
            try
            {
                if (args.Count == 0)
                {
                    Info(App.T("Select one or more images, right-click and choose \"" + App.MenuText + "\".\n\nOn Windows 11 it is under \"Show more options\" (or Shift + right-click).",
                               "Selecione uma ou mais imagens, clique com o botão direito e escolha \"" + App.MenuText + "\".\n\nNo Windows 11 fica em \"Mostrar mais opções\" (ou Shift + clique direito)."));
                    return 0;
                }
                switch (args[0])
                {
                    case "--uninstall": return Uninstall();
                    case "--queue": return RunQueued(args[1], App.QueueDir, MakeIcons); // menu clássico
                    case "--queue-lib": return RunQueued(args[1], Path.Combine(App.QueueDir, "lib"), MakeLibrary);
                    case "--queue-add": return RunQueued(args[1], Path.Combine(App.QueueDir, "add"), ChooseAndAdd);
                    case "--add-package": return MenuPackage.AddFromElevated(args[1], args[2]);
                    case "--stdin": return MakeIcons(ReadStdin()); // menu novo do Windows 11
                    case "--stdin-lib": return MakeLibrary(ReadStdin());
                    case "--stdin-add": return ChooseAndAdd(ReadStdin());
                    case "--add": return AddToLibrary(Path.GetFullPath(args[1]), args.Skip(2).ToList());
                    default: return args.All(IsIco) ? MakeLibrary(args) : MakeIcons(args); // arquivos arrastados
                }
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        static List<string> ReadStdin()
        {
            using (StreamReader r = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8))
                return r.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        static void Info(string msg)
        {
            if (!quiet) MessageBox.Show(msg, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static int Fail(string msg)
        {
            if (quiet) Console.Error.WriteLine(msg);
            else MessageBox.Show(msg, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        // ---------------------------------------------------------------- desinstalação

        static int Uninstall()
        {
            string ask = App.T("Remove " + App.Name + " from this computer?", "Remover o " + App.Name + " deste computador?");
            if (!quiet && MessageBox.Show(ask, App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
            string done = App.T(App.Name + " was removed. The icons you created were kept.",
                                "O " + App.Name + " foi removido. Os ícones que você criou foram mantidos.");

            Uninstaller.RemoveSettings();
            Info(done);
            // Um programa não apaga a si mesmo enquanto roda: um cmd espera ele fechar e apaga só os
            // nossos exes. O rmdir sem /s falha de propósito se alguém pôs outra coisa na pasta.
            if (File.Exists(App.ExePath))
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/d /c \"ping -n 3 127.0.0.1 >nul & del /f /q \"" + App.ExePath + "\" \"" + App.CliPath + "\" & rmdir \"" + App.InstallDir + "\"\"")
                    { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            return 0;
        }

        // ---------------------------------------------------------------- fila do Explorer

        /// <summary>
        /// O Explorer abre um processo por arquivo selecionado. Cada um deixa seu caminho na fila; o
        /// primeiro a pegar o mutex espera os outros chegarem e converte tudo junto.
        /// </summary>
        static int RunQueued(string path, string dir, Func<List<string>, int> work)
        {
            Directory.CreateDirectory(dir);
            string id = Path.Combine(dir, Guid.NewGuid().ToString("N"));
            File.WriteAllText(id + ".tmp", path, Encoding.UTF8);
            File.Move(id + ".tmp", id + ".txt"); // só aparece na fila já completo

            int result = 0;
            using (Mutex m = new Mutex(false, @"Local\" + App.Name + Path.GetFileName(dir)))
            {
                // Reconfere a fila depois de soltar o mutex: alguém pode ter entrado no último instante.
                while (Directory.GetFiles(dir, "*.txt").Length > 0)
                {
                    bool mine;
                    try { mine = m.WaitOne(0); }
                    catch (AbandonedMutexException) { mine = true; }
                    if (!mine) return 0;
                    try
                    {
                        List<string> files = Drain(dir);
                        if (files.Count > 0) result |= work(files);
                    }
                    finally { m.ReleaseMutex(); }
                }
            }
            return result;
        }

        static List<string> Drain(string dir)
        {
            Stopwatch quietFor = Stopwatch.StartNew();
            int last = -1;
            while (quietFor.ElapsedMilliseconds < 600)
            {
                int n = Directory.GetFiles(dir, "*.txt").Length;
                if (n != last) { last = n; quietFor.Restart(); }
                Thread.Sleep(40);
            }
            List<string> files = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "*.txt"))
            {
                // sobra de uma execução que morreu no meio: descarta
                bool stale = DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(10);
                if (!stale) files.Add(File.ReadAllText(f, Encoding.UTF8));
                File.Delete(f);
            }
            return files;
        }

        // ---------------------------------------------------------------- ações

        /// <summary>Roda uma ação juntando os erros numa mensagem só.</summary>
        static int Run(string errorTitle, Action<Job> action)
        {
            List<string> errors = new List<string>();
            Job job = new Job { Error = (path, message) => errors.Add(Path.GetFileName(path) + ": " + message) };
            if (quiet) job.Created = Console.WriteLine;
            action(job);
            return errors.Count == 0 ? 0 : Fail(errorTitle + string.Join("\n", errors));
        }

        static int MakeIcons(List<string> paths) { return Run("", job => Converter.CreateIcons(paths, job)); }

        static int MakeLibrary(List<string> paths)
        {
            return Run(App.T("The library was not created:", "A biblioteca não foi criada:") + "\n\n", job => Converter.CreateLibrary(paths, job, null));
        }

        static int ChooseAndAdd(List<string> paths)
        {
            // A janela dona fica por cima: aberto pelo Explorer, o diálogo poderia nascer atrás dele.
            using (Form owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterScreen })
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = App.T("Choose the icon library", "Escolha a biblioteca de ícones");
                d.Filter = App.T("Icon library", "Biblioteca de ícones") + " (*.dll)|*.dll";
                d.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(paths[0]));
                owner.Show();
                owner.Activate();
                return d.ShowDialog(owner) == DialogResult.OK ? AddToLibrary(d.FileName, paths) : 0;
            }
        }

        static int AddToLibrary(string dll, List<string> paths)
        {
            return Run(App.T("The library was not changed:", "A biblioteca não foi alterada:") + "\n\n", job =>
            {
                string result = Converter.AddToLibrary(dll, paths, job);
                if (result == null) return;
                if (quiet) Console.WriteLine(result);
                Info(result);
            });
        }

        static bool IsIco(string path) { return Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase); }
    }
}
