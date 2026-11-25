using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Iconzinho
{
    static class Program
    {
        // Tamanhos que o Windows pede entre 100% e 300% de escala. Se faltar um, o próprio
        // Explorer redimensiona outro frame na hora, e feio.
        static readonly int[] StandardSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        const int MaxFrame = 256; // limite do formato .ico

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int ev, uint flags, string item1, IntPtr item2);

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
                    case "--queue": return RunQueued(args[1]); // menu clássico
                    case "--add-package": return MenuPackage.AddFromElevated(args[1], args[2]);
                    case "--stdin": return Convert(ReadStdin()); // menu novo do Windows 11
                    default: return Convert(args);
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
            // Um programa não apaga a si mesmo enquanto roda: um cmd espera ele fechar e apaga só o
            // nosso exe. O rmdir sem /s falha de propósito se alguém pôs outra coisa na pasta.
            if (File.Exists(App.ExePath))
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/d /c \"ping -n 3 127.0.0.1 >nul & del /f /q \"" + App.ExePath + "\" & rmdir \"" + App.InstallDir + "\"\"")
                    { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            return 0;
        }

        // ---------------------------------------------------------------- fila do Explorer

        /// <summary>
        /// O Explorer abre um processo por arquivo selecionado. Cada um deixa seu caminho na fila; o
        /// primeiro a pegar o mutex espera os outros chegarem e converte tudo junto.
        /// </summary>
        static int RunQueued(string path)
        {
            Directory.CreateDirectory(App.QueueDir);
            string id = Path.Combine(App.QueueDir, Guid.NewGuid().ToString("N"));
            File.WriteAllText(id + ".tmp", path, Encoding.UTF8);
            File.Move(id + ".tmp", id + ".txt"); // só aparece na fila já completo

            int result = 0;
            using (Mutex m = new Mutex(false, @"Local\" + App.Name + "Queue"))
            {
                // Reconfere a fila depois de soltar o mutex: alguém pode ter entrado no último instante.
                while (Directory.GetFiles(App.QueueDir, "*.txt").Length > 0)
                {
                    bool mine;
                    try { mine = m.WaitOne(0); }
                    catch (AbandonedMutexException) { mine = true; }
                    if (!mine) return 0;
                    try
                    {
                        List<string> files = Drain();
                        if (files.Count > 0) result |= Convert(files);
                    }
                    finally { m.ReleaseMutex(); }
                }
            }
            return result;
        }

        static List<string> Drain()
        {
            Stopwatch quietFor = Stopwatch.StartNew();
            int last = -1;
            while (quietFor.ElapsedMilliseconds < 600)
            {
                int n = Directory.GetFiles(App.QueueDir, "*.txt").Length;
                if (n != last) { last = n; quietFor.Restart(); }
                Thread.Sleep(40);
            }
            List<string> files = new List<string>();
            foreach (string f in Directory.GetFiles(App.QueueDir, "*.txt"))
            {
                // sobra de uma execução que morreu no meio: descarta
                bool stale = DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(10);
                if (!stale) files.Add(File.ReadAllText(f, Encoding.UTF8));
                File.Delete(f);
            }
            return files;
        }

        // ---------------------------------------------------------------- conversão

        static int Convert(IEnumerable<string> paths)
        {
            List<Source> sources = new List<Source>();
            List<string> errors = new List<string>();
            foreach (string p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { sources.Add(Loader.Load(Path.GetFullPath(p))); }
                catch (Exception ex) { errors.Add(Path.GetFileName(p) + ": " + (ex is ArgumentException ? App.T("not a valid image", "não é uma imagem válida") : ex.Message)); }
            }

            foreach (List<Source> icon in Group(sources))
            {
                try
                {
                    string name = icon.Count == 1 && icon[0].BaseName == "" ? icon[0].FileName : icon[0].BaseName;
                    if (name == "") name = new DirectoryInfo(icon[0].Dir).Name.TrimEnd('\\', ':');
                    string dest = FreeName(icon[0].Dir, name);
                    File.WriteAllBytes(dest, BuildIco(icon));
                    SHChangeNotify(0x00000002, 0x0005, dest, IntPtr.Zero); // SHCNE_CREATE, SHCNF_PATHW
                    if (quiet) Console.WriteLine(dest);
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(icon[0].Path) + ": " + ex.Message); }
            }
            return errors.Count == 0 ? 0 : Fail(string.Join("\n", errors));
        }

        /// <summary>
        /// Mesma pasta + mesmo nome fora o tamanho = um ícone só ("logo-32.png", "logo-16.png" e até um
        /// "logo.svg" junto). Se não dá para saber qual imagem vale, cada uma vira o seu ícone.
        /// </summary>
        static IEnumerable<List<Source>> Group(List<Source> sources)
        {
            foreach (var g in sources.GroupBy(s => (s.Dir + "|" + s.BaseName).ToLowerInvariant()))
            {
                List<Source> sized = g.Where(s => s.HasSizeInName).ToList(), plain = g.Where(s => !s.HasSizeInName).ToList();
                List<Source> all = sized.Concat(plain).ToList();
                if (plain.Count <= 1 && !Clash(all)) { yield return all; continue; }
                foreach (Source s in plain) yield return new List<Source> { s };
                if (sized.Count > 0 && !Clash(sized)) { yield return sized; continue; }
                foreach (Source s in sized)
                {
                    s.BaseName = s.FileName; // sozinha, mantém o nome inteiro
                    yield return new List<Source> { s };
                }
            }
        }

        static bool Clash(List<Source> l) { return l.Select(s => s.Size).Distinct().Count() != l.Count; }
        static string FreeName(string dir, string name)
        {
            string p = Path.Combine(dir, name + ".ico");
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, name + " (" + i + ").ico");
            return p;
        }

        static byte[] BuildIco(List<Source> sources)
        {
            Source master = sources.OrderByDescending(s => s.Size).First();
            SortedDictionary<int, FloatImage> frames = new SortedDictionary<int, FloatImage>();
            foreach (Source s in sources) if (s.Size <= MaxFrame) frames[s.Size] = s.Image; // intocados
            foreach (int size in StandardSizes)
                if (!frames.ContainsKey(size) && size < master.Size) // nunca amplia
                    // Lanczos é o mais parecido com o Photoshop, mas em desenho chapado cria um contorno
                    // claro nas bordas; no SVG a cobertura exata fica suave sem alterar as cores.
                    frames[size] = Resampler.Resize(master.Image, size, master.Vector ? Kernel.Box : Kernel.Lanczos3);

            List<KeyValuePair<int, byte[]>> blobs = frames.OrderByDescending(f => f.Key)
                .Select(f => new KeyValuePair<int, byte[]>(f.Key, f.Key >= MaxFrame ? PngFrame(f.Value) : BmpFrame(f.Value))).ToList();

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)blobs.Count);
                int offset = 6 + 16 * blobs.Count;
                foreach (var b in blobs)
                {
                    byte dim = (byte)(b.Key >= 256 ? 0 : b.Key); // 0 significa 256
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(b.Value.Length); w.Write(offset);
                    offset += b.Value.Length;
                }
                foreach (var b in blobs) w.Write(b.Value);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] PngFrame(FloatImage im)
        {
            using (Bitmap b = new Bitmap(im.W, im.H, PixelFormat.Format32bppArgb))
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] px = im.ToBgra();
                BitmapData bd = b.LockBits(new Rectangle(0, 0, im.W, im.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(px, 0, bd.Scan0, px.Length);
                b.UnlockBits(bd);
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>DIB de 32 bits + máscara AND: o formato mais compatível para frames pequenos.</summary>
        static byte[] BmpFrame(FloatImage im)
        {
            int n = im.W, maskStride = (n + 31) / 32 * 4;
            byte[] px = im.ToBgra(), mask = new byte[maskStride * n];
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(n); w.Write(n * 2); // altura dobrada: imagem + máscara
                w.Write((short)1); w.Write((short)32); w.Write(0);
                w.Write(px.Length + mask.Length);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = n - 1; y >= 0; y--) w.Write(px, y * n * 4, n * 4);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (px[(y * n + x) * 4 + 3] == 0) mask[(n - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(mask);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
