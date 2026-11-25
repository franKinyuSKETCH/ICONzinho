using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Iconzinho
{
    /// <summary>Um arquivo de imagem.</summary>
    class Source
    {
        public string Path;
        public FloatImage Image;  // já quadrada; só existe enquanto o ícone é montado
        public int Size;          // lado do quadrado no arquivo original (a Image pode ter sido reduzida)
        public bool Vector;
        public string BaseName;   // nome sem o tamanho: "logo-32.png" -> "logo", "32.png" -> ""
        public string FileName { get { return System.IO.Path.GetFileNameWithoutExtension(Path); } }
        public string Dir { get { return System.IO.Path.GetDirectoryName(Path); } }
        public bool HasSizeInName { get { return BaseName != FileName; } }
    }

    static class Loader
    {
        // Acima disto a imagem é reduzida já na leitura: uma foto de câmera inteira em float
        // ocuparia gigabytes, e o maior frame do ícone tem 256.
        const int MaxSide = 1024;

        /// <summary>Só o tamanho e o nome, sem ler os pixels.</summary>
        public static Source Probe(string path)
        {
            Source s = new Source { Path = path };
            s.BaseName = s.FileName;
            if (System.IO.Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                s.Vector = true;
                s.Size = MaxSide;
                return s;
            }

            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (IsWebp(f)) s.Size = WebpSide(f);
                else
                    using (Image i = Image.FromStream(f, false, false)) s.Size = Math.Max(i.Width, i.Height);
            }
            // Só corta o número se ele for mesmo o tamanho da imagem: "foto2.png" continua "foto2".
            s.BaseName = Regex.Replace(s.FileName, @"[\s._\-(\[]*(?<!\d)" + s.Size + @"(\s*x\s*" + s.Size + @")?(px)?[)\]]?$", "", RegexOptions.IgnoreCase);
            return s;
        }

        /// <summary>Lê os pixels para s.Image.</summary>
        public static void Decode(Source s)
        {
            byte[] data = File.ReadAllBytes(s.Path); // não trava o arquivo
            if (s.Vector)
            {
                using (Bitmap b = RenderSvg(data)) s.Image = FloatImage.FromBitmap(b, MaxSide);
                return;
            }
            using (MemoryStream ms = new MemoryStream(data))
            using (Bitmap b = IsWebp(ms) ? DecodeWebp(ms) : new Bitmap(ms))
            {
                FixOrientation(b);
                s.Image = FloatImage.FromBitmap(b, MaxSide).PadToSquare();
            }
        }

        static bool IsWebp(Stream s)
        {
            byte[] h = new byte[12];
            int n = s.Read(h, 0, 12);
            s.Position = 0;
            return n == 12 && Encoding.ASCII.GetString(h, 0, 4) == "RIFF" && Encoding.ASCII.GetString(h, 8, 4) == "WEBP";
        }
        /// <summary>Fotos de celular vêm deitadas, com a rotação só anotada no EXIF.</summary>
        static void FixOrientation(Bitmap b)
        {
            const int Orientation = 0x0112;
            if (!b.PropertyIdList.Contains(Orientation)) return;
            byte[] v = b.GetPropertyItem(Orientation).Value;
            if (v == null || v.Length < 2) return;
            switch (BitConverter.ToUInt16(v, 0))
            {
                case 2: b.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: b.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: b.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                case 5: b.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: b.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: b.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: b.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        // ---------------------------------------------------------------- WebP

        /// <summary>
        /// Usa o codec de WebP do próprio Windows. Fica num método separado para o WPF só ser
        /// carregado quando aparece um WebP.
        /// </summary>
        static Bitmap DecodeWebp(Stream data)
        {
            return ReadWebp(data, delegate(System.Windows.Media.Imaging.BitmapFrame frame)
            {
                var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                Bitmap b = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppArgb);
                BitmapData bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, b.PixelFormat);
                src.CopyPixels(System.Windows.Int32Rect.Empty, bd.Scan0, bd.Stride * b.Height, bd.Stride);
                b.UnlockBits(bd);
                return b;
            });
        }

        static int WebpSide(Stream data) { return ReadWebp(data, f => Math.Max(f.PixelWidth, f.PixelHeight)); }

        static T ReadWebp<T>(Stream data, Func<System.Windows.Media.Imaging.BitmapFrame, T> use)
        {
            try
            {
                return use(System.Windows.Media.Imaging.BitmapDecoder.Create(data,
                    System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile | System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0]);
            }
            catch (NotSupportedException)
            {
                throw new InvalidDataException(App.T(
                    "Windows could not read this WebP. If others fail too, install \"WebP Image Extensions\" from the Microsoft Store.",
                    "o Windows não conseguiu ler este WebP. Se outros também falharem, instale \"Extensões de Imagem do WebP\" pela Microsoft Store."));
            }
            catch (Exception)
            {
                throw new InvalidDataException(App.T("not a valid WebP image", "não é um WebP válido"));
            }
        }
        // ---------------------------------------------------------------- SVG

        /// <summary>
        /// Desenha com o Edge (ou Chrome) invisível: mesma fidelidade do navegador, com CSS, texto e
        /// filtros. Dentro de um img o SVG não executa scripts nem acessa a rede, e o
        /// object-fit: contain encaixa no quadrado sem esticar.
        /// </summary>
        static Bitmap RenderSvg(byte[] svg)
        {
            string browser = FindBrowser();
            if (browser == null)
                throw new InvalidDataException(App.T("SVG needs Microsoft Edge or Google Chrome installed", "SVG precisa do Microsoft Edge ou do Google Chrome instalado"));

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), App.Name + "-" + Guid.NewGuid().ToString("N"));
            string shot = System.IO.Path.Combine(dir, "o.png"), html = System.IO.Path.Combine(dir, "i.html");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(System.IO.Path.Combine(dir, "i.svg"), svg);
                File.WriteAllText(html, "<!doctype html><style>*{margin:0}img{position:absolute;left:0;top:0;width:" + MaxSide +
                    "px;height:" + MaxSide + "px;object-fit:contain}</style><img src=\"i.svg\" onerror=\"this.remove()\">");
                // A janela é maior que a imagem porque parte da altura pode ser descontada; depois recorta.
                string args = "--headless=new --disable-gpu --hide-scrollbars --no-first-run --no-default-browser-check --disable-extensions" +
                    " --force-device-scale-factor=1 --default-background-color=00000000 --window-size=" + (MaxSide + 200) + "," + (MaxSide + 400) +
                    " --user-data-dir=\"" + System.IO.Path.Combine(dir, "p") + "\" --screenshot=\"" + shot + "\" \"" + new Uri(html).AbsoluteUri + "\"";
                using (Process p = Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false, CreateNoWindow = true }))
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch (InvalidOperationException) { } }

                if (!File.Exists(shot))
                    throw new InvalidDataException(App.T("the browser could not draw this SVG", "o navegador não conseguiu desenhar este SVG"));
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(shot)))
                using (Bitmap full = new Bitmap(ms))
                {
                    Bitmap b = full.Clone(new Rectangle(0, 0, MaxSide, MaxSide), PixelFormat.Format32bppArgb);
                    if (IsBlank(b)) { b.Dispose(); throw new InvalidDataException(App.T("the SVG is empty or invalid", "o SVG está vazio ou é inválido")); }
                    return b;
                }
            }
            finally { DeleteTemp(dir); }
        }

        static string FindBrowser()
        {
            foreach (string exe in new[] { "msedge.exe", "chrome.exe" })
                foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                    using (RegistryKey k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        string p = k == null ? null : k.GetValue("") as string;
                        if (p != null && File.Exists(p = p.Trim('"'))) return p;
                    }
            return null;
        }

        static bool IsBlank(Bitmap b)
        {
            BitmapData bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] px = new byte[bd.Stride * b.Height];
                Marshal.Copy(bd.Scan0, px, 0, px.Length);
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) return false;
                return true;
            }
            finally { b.UnlockBits(bd); }
        }

        /// <summary>O navegador pode segurar arquivos do perfil por um instante depois de fechar.</summary>
        static void DeleteTemp(string dir)
        {
            for (int i = 0; i < 20; i++)
            {
                try { Directory.Delete(dir, true); return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(150);
            }
        }
    }
}
