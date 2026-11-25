using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Iconzinho
{
    /// <summary>iconz: o ICONzinho no terminal.</summary>
    static class Cli
    {
        const int Ok = 0, Failed = 1, Usage = 2;

        class UsageException : Exception { public UsageException(string m) : base(m) { } }

        static bool recursive, perFolder, quiet;
        static string output;
        static string[] extensions = App.Extensions;
        static readonly List<string> inputs = new List<string>();
        static readonly Job job = new Job();
        static int created, skipped, errors;

        [STAThread]
        static int Main()
        {
            try
            {
                string[] args = Tokens(Environment.CommandLine).Skip(1).ToArray();
                string command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
                job.Created = p => { created++; if (!quiet) Console.WriteLine("+ " + p); };
                job.Skipped = p => { skipped++; if (!quiet) Console.WriteLine("= " + p); };
                job.Error = Error;
                switch (command)
                {
                    case "help": case "-h": case "--help": case "/?": Console.WriteLine(Help()); return Ok;
                    case "version": case "-v": case "--version": Console.WriteLine(App.Name + " " + App.Version); return Ok;
                    case "create": Parse(args, "-r", "-o", "--ext", "--skip", "--overwrite", "-n", "-q"); return Create();
                    case "library": Parse(args, "-r", "-o", "--per-folder", "--skip", "--overwrite", "-n", "-q"); return Library();
                    case "add": Parse(args, "-r", "-n", "-q"); return Add();
                    case "list": Parse(args); return List();
                    default: throw new UsageException(App.T("unknown command: ", "comando desconhecido: ") + args[0]);
                }
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine("iconz: " + ex.Message);
                Console.Error.WriteLine(App.T("Run \"iconz help\" to see the commands.", "Rode \"iconz help\" para ver os comandos."));
                return Usage;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("iconz: " + ex.Message);
                return Failed;
            }
        }

        static void Error(string path, string message)
        {
            errors++;
            Console.Error.WriteLine("! " + path + ": " + message);
        }

        // ---------------------------------------------------------------- comandos

        static int Create()
        {
            if (inputs.Any(i => IsIco(i) && File.Exists(i)))
                throw new UsageException(App.T("\"create\" reads images; for .ico files use \"library\" or \"add\"", "\"create\" lê imagens; para .ico use \"library\" ou \"add\""));
            if (output != null)
            {
                if (File.Exists(output)) throw new UsageException(App.T("-o must be a folder: ", "-o precisa ser uma pasta: ") + output);
                job.OutDir = output;
                if (!job.DryRun) Directory.CreateDirectory(output);
            }
            Converter.CreateIcons(Expand(inputs, extensions), job);
            return Summary();
        }

        static int Library()
        {
            string name = null;
            if (output != null)
            {
                if (perFolder) throw new UsageException(App.T("-o and --per-folder cannot be used together", "-o e --per-folder não podem ser usados juntos"));
                if (!output.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new UsageException(App.T("-o must end in .dll", "-o precisa terminar em .dll"));
                name = Path.GetFileNameWithoutExtension(output);
                job.OutDir = Path.GetDirectoryName(output);
                if (!job.DryRun) Directory.CreateDirectory(job.OutDir);
            }
            List<string> files = Expand(inputs, new[] { ".ico" });
            if (perFolder)
                foreach (var g in files.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)) Converter.CreateLibrary(g, job, null);
            else if (files.Count > 0) Converter.CreateLibrary(files, job, name);
            return Summary();
        }

        static int Add()
        {
            if (inputs.Count < 2) throw new UsageException(App.T("usage: iconz add LIBRARY.dll ICONS...", "uso: iconz add BIBLIOTECA.dll ÍCONES..."));
            string dll = inputs[0];
            if (!File.Exists(dll)) { Error(dll, NotFound); return Failed; }
            List<string> files = Expand(inputs.Skip(1), new[] { ".ico" });
            // Um item que faltou não deve virar uma biblioteca incompleta em silêncio.
            if (errors > 0) return Failed;
            if (files.Count == 0) { Nothing(); return Ok; }
            string result = Converter.AddToLibrary(dll, files, job);
            if (result != null && !quiet) Console.WriteLine((job.DryRun ? DryNote : "") + result);
            return errors == 0 ? Ok : Failed;
        }

        static int List()
        {
            if (inputs.Count != 1) throw new UsageException(App.T("usage: iconz list LIBRARY.dll", "uso: iconz list BIBLIOTECA.dll"));
            if (!File.Exists(inputs[0])) { Error(inputs[0], NotFound); return Failed; }
            List<byte[]> icos = IconLibrary.Read(File.ReadAllBytes(inputs[0]));
            if (icos == null) { Error(inputs[0], App.T("not a library created by " + App.Name, "não é uma biblioteca criada pelo " + App.Name)); return Failed; }
            for (int i = 0; i < icos.Count; i++)
            {
                byte[] b = icos[i];
                IEnumerable<int> sizes = Enumerable.Range(0, BitConverter.ToUInt16(b, 4)).Select(k => b[6 + 16 * k] == 0 ? 256 : (int)b[6 + 16 * k]);
                Console.WriteLine(i.ToString().PadLeft(4) + "  " + string.Join(" ", sizes.Distinct().OrderByDescending(x => x)));
            }
            return Ok;
        }

        static int Summary()
        {
            if (created + skipped + errors == 0) Nothing();
            else if (!quiet)
                Console.WriteLine((job.DryRun ? DryNote : "") + App.T(
                    "Created: " + created + ". Skipped: " + skipped + ". Errors: " + errors + ".",
                    "Criados: " + created + ". Pulados: " + skipped + ". Erros: " + errors + "."));
            return errors == 0 ? Ok : Failed;
        }

        static void Nothing() { if (!quiet) Console.WriteLine(App.T("Nothing to do: no matching files.", "Nada a fazer: nenhum arquivo encontrado.")); }

        static string DryNote { get { return App.T("(dry run, nothing written) ", "(simulação, nada foi gravado) "); } }
        static string NotFound { get { return App.T("not found", "não encontrado"); } }
        static bool IsIco(string p) { return p.EndsWith(".ico", StringComparison.OrdinalIgnoreCase); }

        // ---------------------------------------------------------------- argumentos

        static void Parse(string[] args, params string[] allowed)
        {
            bool onlyFiles = false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (onlyFiles || !a.StartsWith("-")) { inputs.Add(a); continue; }
                if (a == "--") { onlyFiles = true; continue; }
                a = Long(a.ToLowerInvariant());
                if (!allowed.Contains(a)) throw new UsageException(App.T("unknown option: ", "opção desconhecida: ") + args[i]);
                switch (a)
                {
                    case "-r": recursive = true; break;
                    case "-n": job.DryRun = true; break;
                    case "-q": quiet = true; break;
                    case "--per-folder": perFolder = true; break;
                    case "--skip": SetIfExists(IfExists.Skip); break;
                    case "--overwrite": SetIfExists(IfExists.Overwrite); break;
                    case "-o": output = Path.GetFullPath(Value(args, ref i)); break;
                    case "--ext":
                        extensions = Value(args, ref i).Split(',').Select(e => "." + e.Trim().TrimStart('.').ToLowerInvariant()).ToArray();
                        foreach (string e in extensions)
                            if (!App.Extensions.Contains(e)) throw new UsageException(App.T("unsupported format: ", "formato não suportado: ") + e);
                        break;
                }
            }
            if (inputs.Count == 0) throw new UsageException(App.T("no files or folders given", "nenhum arquivo ou pasta informado"));
        }

        static string Long(string a)
        {
            switch (a)
            {
                case "--recursive": return "-r";
                case "--out": return "-o";
                case "--dry-run": return "-n";
                case "--quiet": return "-q";
                default: return a;
            }
        }

        static void SetIfExists(IfExists v)
        {
            if (job.IfExists != IfExists.Rename && job.IfExists != v)
                throw new UsageException(App.T("--skip and --overwrite cannot be used together", "--skip e --overwrite não podem ser usados juntos"));
            job.IfExists = v;
        }

        static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new UsageException(App.T("missing value for ", "falta o valor de ") + args[i]);
            return args[++i];
        }

        /// <summary>
        /// Separa os argumentos sem deixar a barra escapar a aspa: no padrão do Windows, "C:\pasta\"
        /// engoliria o resto da linha. Caminhos não têm aspas, então nada se perde.
        /// </summary>
        static IEnumerable<string> Tokens(string line)
        {
            StringBuilder token = new StringBuilder();
            bool quoted = false, any = false;
            foreach (char c in line)
            {
                if (c == '"') { quoted = !quoted; any = true; }
                else if (!quoted && char.IsWhiteSpace(c))
                {
                    if (any) yield return token.ToString();
                    token.Clear();
                    any = false;
                }
                else { token.Append(c); any = true; }
            }
            if (any) yield return token.ToString();
        }

        // ---------------------------------------------------------------- arquivos e pastas

        static List<string> Expand(IEnumerable<string> items, string[] exts)
        {
            List<string> files = new List<string>();
            foreach (string item in items)
            {
                try
                {
                    if (item.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        string dir = Path.GetDirectoryName(item);
                        Walk(Path.GetFullPath(string.IsNullOrEmpty(dir) ? "." : dir), Path.GetFileName(item), exts, files);
                    }
                    else if (Directory.Exists(item)) Walk(Path.GetFullPath(item), "*", exts, files);
                    else if (!File.Exists(item)) Error(item, NotFound);
                    else if (!exts.Contains(Path.GetExtension(item).ToLowerInvariant())) Error(item, App.T("unsupported format", "formato não suportado"));
                    else files.Add(Path.GetFullPath(item));
                }
                catch (Exception ex) { Error(item, ex.Message); }
            }
            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void Walk(string dir, string pattern, string[] exts, List<string> files)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, pattern).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (exts.Contains(Path.GetExtension(f).ToLowerInvariant())) files.Add(f);
                if (!recursive) return;
                foreach (string d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (!IsLink(d)) Walk(d, pattern, exts, files);
            }
            catch (UnauthorizedAccessException ex) { Error(dir, ex.Message); }
            catch (IOException ex) { Error(dir, ex.Message); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct FindData
        {
            public FileAttributes Attributes;
            public long Created, Accessed, Written; // só ocupam o lugar
            public uint SizeHigh, SizeLow, ReparseTag, Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string ShortName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindFirstFileW(string path, out FindData data);
        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr h);

        /// <summary>
        /// Atalhos de pasta (junção, link simbólico) podem dar a volta para sempre. Olha o tipo exato
        /// porque as pastas do OneDrive também são pontos de nova análise, e essas valem.
        /// </summary>
        static bool IsLink(string dir)
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) return false;
            FindData d;
            IntPtr h = FindFirstFileW(dir, out d);
            if (h == new IntPtr(-1)) return true;
            FindClose(h);
            return d.ReparseTag == 0xA0000003 || d.ReparseTag == 0xA000000C;
        }

        // ---------------------------------------------------------------- ajuda

        static string Help()
        {
            return App.T(
@"iconz - " + App.Name + " " + App.Version + @" on the command line

  iconz create  FILES|FOLDERS...           images to .ico
  iconz library FILES|FOLDERS...           .ico files to an icon library (.dll)
  iconz add     LIBRARY.dll FILES|FOLDERS  adds .ico files to the end of a library
  iconz list    LIBRARY.dll                shows the position and sizes of each icon

Options
  -r, --recursive    includes subfolders
  -o, --out PATH     create: folder for the icons; library: the .dll to write
      --ext LIST     create: only these formats, such as png,svg
      --per-folder   library: one .dll in each folder
      --skip         keeps files that already exist
      --overwrite    replaces files that already exist
  -n, --dry-run      shows what would be done, without writing
  -q, --quiet        shows only errors

Without --skip or --overwrite, an existing name becomes ""name (2)"".
Files named by size (512.png, 256.png, logo-32.png...) in a folder become one icon.
Exit code: 0 all good, 1 something failed, 2 wrong command.

Examples
  iconz create ""D:\Projects"" -r --skip
  iconz create *.svg -o icons
  iconz library icons -o my-icons.dll
  iconz add my-icons.dll new\*.ico",
@"iconz - " + App.Name + " " + App.Version + @" na linha de comando

  iconz create  ARQUIVOS|PASTAS...              imagens para .ico
  iconz library ARQUIVOS|PASTAS...              .ico para uma biblioteca de ícones (.dll)
  iconz add     BIBLIOTECA.dll ARQUIVOS|PASTAS  adiciona .ico ao fim de uma biblioteca
  iconz list    BIBLIOTECA.dll                  mostra a posição e os tamanhos de cada ícone

Opções
  -r, --recursive     inclui as subpastas
  -o, --out CAMINHO   create: pasta dos ícones; library: a .dll a gravar
      --ext LISTA     create: só estes formatos, como png,svg
      --per-folder    library: uma .dll em cada pasta
      --skip          mantém os arquivos que já existem
      --overwrite     substitui os arquivos que já existem
  -n, --dry-run       mostra o que seria feito, sem gravar
  -q, --quiet         mostra só os erros

Sem --skip nem --overwrite, um nome que já existe vira ""nome (2)"".
Arquivos com o tamanho no nome (512.png, 256.png, logo-32.png...) na mesma pasta viram um ícone só.
Código de saída: 0 tudo certo, 1 algo falhou, 2 comando errado.

Exemplos
  iconz create ""D:\Projetos"" -r --skip
  iconz create *.svg -o icones
  iconz library icones -o meus-icones.dll
  iconz add meus-icones.dll novos\*.ico");
        }
    }
}
