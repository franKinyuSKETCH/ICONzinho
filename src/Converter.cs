using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Iconzinho
{
    enum IfExists { Rename, Skip, Overwrite }

    /// <summary>Onde e como gravar, e para quem contar o que aconteceu.</summary>
    class Job
    {
        public string OutDir;                 // null: ao lado das imagens
        public IfExists IfExists = IfExists.Rename;
        public bool DryRun;
        public Action<string> Created = p => { };
        public Action<string> Skipped = p => { };
        public Action<string, string> Error = (path, message) => { };

        readonly HashSet<string> produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Onde gravar, ou null para pular. Dois ícones da mesma execução nunca ficam com o mesmo nome, e
        /// repetir o comando dá os mesmos nomes na mesma ordem.
        /// </summary>
        public string Destination(string dir, string name, string ext)
        {
            dir = OutDir ?? dir;
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, name + (i == 1 ? "" : " (" + i + ")") + ext);
                if (produced.Contains(p)) continue;
                bool exists = File.Exists(p);
                if (exists && IfExists == IfExists.Rename) continue;
                produced.Add(p);
                if (!exists || IfExists == IfExists.Overwrite) return p;
                Skipped(p);
                return null;
            }
        }
    }

    static class Converter
    {
        // Tamanhos que o Windows pede entre 100% e 300% de escala. Se faltar um, o próprio
        // Explorer redimensiona outro frame na hora, e feio.
        static readonly int[] StandardSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        const int MaxFrame = 256; // limite do formato .ico

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int ev, uint flags, string item1, IntPtr item2);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        static string FolderName(string dir) { return new DirectoryInfo(dir).Name.TrimEnd('\\', ':'); }

        static string Message(Exception ex)
        {
            return ex is ArgumentException ? App.T("not a valid image", "não é uma imagem válida") : ex.Message;
        }

        // ---------------------------------------------------------------- imagens -> .ico

        public static void CreateIcons(IEnumerable<string> paths, Job job)
        {
            // Só os cabeçalhos: os pixels são lidos ícone por ícone, para um lote grande caber na memória.
            List<Source> sources = new List<Source>();
            foreach (string p in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { sources.Add(Loader.Probe(p)); }
                catch (Exception ex) { job.Error(p, Message(ex)); }
            }

            foreach (List<Source> icon in Group(sources))
            {
                try
                {
                    string name = icon.Count == 1 && icon[0].BaseName == "" ? icon[0].FileName : icon[0].BaseName;
                    if (name == "") name = FolderName(icon[0].Dir);
                    string dest = job.Destination(icon[0].Dir, name, ".ico");
                    if (dest == null) continue;
                    if (!job.DryRun)
                    {
                        List<Source> good = Decode(icon, job);
                        if (good.Count == 0) continue;
                        File.WriteAllBytes(dest, BuildIco(good));
                        SHChangeNotify(0x00000002, 0x0005, dest, IntPtr.Zero); // SHCNE_CREATE, SHCNF_PATHW
                    }
                    job.Created(dest);
                }
                catch (Exception ex) { job.Error(icon[0].Path, Message(ex)); }
                finally { foreach (Source s in icon) s.Image = null; }
            }
        }

        /// <summary>Um arquivo estragado não derruba o ícone se os outros do grupo servirem.</summary>
        static List<Source> Decode(List<Source> icon, Job job)
        {
            List<Source> good = new List<Source>();
            foreach (Source s in icon)
            {
                try { Loader.Decode(s); good.Add(s); }
                catch (Exception ex) { job.Error(s.Path, Message(ex)); }
            }
            return good;
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

        // ---------------------------------------------------------------- .ico -> biblioteca .dll

        /// <summary>
        /// Todos os .ico numa biblioteca só, na ordem dos nomes, como o Explorer mostra ("2" antes de
        /// "10"). Se algum for inválido não gera nada: faltaria um em silêncio. Sem nome, usa o da pasta.
        /// </summary>
        public static void CreateLibrary(IEnumerable<string> paths, Job job, string name)
        {
            List<string> files = Sorted(paths);
            List<byte[]> icos = ReadIcos(files, job);
            if (icos == null) return;
            string dir = Path.GetDirectoryName(files[0]);
            string dest = job.Destination(dir, name ?? FolderName(dir), ".dll");
            if (dest == null) return;
            try
            {
                bool exists = File.Exists(dest);
                if (exists && IconLibrary.Read(File.ReadAllBytes(dest)) == null) throw NotOurs(dest);
                if (!job.DryRun)
                {
                    byte[] dll = IconLibrary.Build(icos);
                    if (exists) ReplaceFile(dest, dll); else File.WriteAllBytes(dest, dll);
                    SHChangeNotify(0x00000002, 0x0005, dest, IntPtr.Zero); // SHCNE_CREATE, SHCNF_PATHW
                }
                job.Created(dest);
            }
            catch (Exception ex) { job.Error(dest, ex.Message); }
        }

        /// <summary>
        /// Os ícones que já estão na biblioteca ficam onde estão, porque os atalhos guardam a posição;
        /// os novos vão para o fim. O que já estiver lá é pulado. Devolve o resumo, ou null se deu erro.
        /// </summary>
        public static string AddToLibrary(string dll, IEnumerable<string> paths, Job job)
        {
            List<byte[]> icos = ReadIcos(Sorted(paths), job);
            if (icos == null) return null;
            try
            {
                List<byte[]> all = IconLibrary.Read(File.ReadAllBytes(dll));
                if (all == null) throw NotOurs(dll);
                int before = all.Count;
                HashSet<string> known = new HashSet<string>(all.Select(Convert.ToBase64String));
                foreach (byte[] ico in icos) if (known.Add(Convert.ToBase64String(ico))) all.Add(ico);
                int added = all.Count - before;

                if (added > 0 && !job.DryRun) ReplaceFile(dll, IconLibrary.Build(all));
                SHChangeNotify(0x00002000, 0x0005, dll, IntPtr.Zero); // SHCNE_UPDATEITEM, SHCNF_PATHW
                return App.T("Added: " + added + ". Already there: " + (icos.Count - added) + ". Total: " + all.Count + ".",
                             "Adicionados: " + added + ". Já estavam lá: " + (icos.Count - added) + ". Total: " + all.Count + ".");
            }
            catch (Exception ex) { job.Error(dll, ex.Message); return null; }
        }

        static Exception NotOurs(string dll)
        {
            return new InvalidDataException(App.T("not created by " + App.Name + ", so it was not changed", "não foi criada pelo " + App.Name + ", então não foi alterada"));
        }

        static List<string> Sorted(IEnumerable<string> paths)
        {
            List<string> files = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            files.Sort((a, b) =>
            {
                int c = StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
                return c != 0 ? c : StrCmpLogicalW(a, b);
            });
            return files;
        }

        /// <summary>null se algum não servir.</summary>
        static List<byte[]> ReadIcos(List<string> files, Job job)
        {
            List<byte[]> icos = new List<byte[]>();
            foreach (string f in files)
            {
                try { icos.Add(IconLibrary.Normalize(File.ReadAllBytes(f))); }
                catch (Exception ex) { job.Error(f, ex.Message); }
            }
            return icos.Count == files.Count && icos.Count > 0 ? icos : null;
        }

        /// <summary>Troca por renomeação: funciona com a DLL aberta para leitura e não deixa arquivo pela metade.</summary>
        static void ReplaceFile(string path, byte[] data)
        {
            // A renomeação passaria por cima do atributo, que está lá para proteger o arquivo.
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException(App.T("the file is read-only", "o arquivo é somente leitura"));
            string id = Guid.NewGuid().ToString("N"), temp = path + "." + id + ".tmp", old = path + "." + id + ".old";
            try
            {
                File.WriteAllBytes(temp, data);
                File.Move(path, old);
                try { File.Move(temp, path); }
                catch (Exception) { File.Move(old, path); throw; }
            }
            catch (Exception)
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }
            // Se alguém ainda a tiver aberta, o .old fica ao lado e sai na próxima vez.
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".*.old"))
            {
                if (!Regex.IsMatch(f, @"\.[0-9a-f]{32}\.old$")) continue; // só os nossos
                try { File.Delete(f); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
