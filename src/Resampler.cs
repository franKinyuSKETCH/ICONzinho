using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Iconzinho
{
    /// <summary>Imagem RGBA em float, alfa premultiplicado, 0..1, ordem B,G,R,A.</summary>
    public class FloatImage
    {
        public readonly int W, H;
        public readonly float[] D;
        public FloatImage(int w, int h) { W = w; H = h; D = new float[w * h * 4]; }

        /// <summary>Se passar de maxSide, reduz durante a leitura, linha a linha, mantendo a proporção.</summary>
        public static FloatImage FromBitmap(Bitmap src, int maxSide)
        {
            int w = src.Width, h = src.Height;
            double f = Math.Min(1.0, (double)maxSide / Math.Max(w, h));
            int dw = Math.Max(1, (int)Math.Round(w * f)), dh = Math.Max(1, (int)Math.Round(h * f));

            // LockBits converte qualquer formato (paleta, 24 bits) sem passar pelo DrawImage, que
            // arredondaria as cores dos pixels semitransparentes. Em 32bppRgb os bytes vêm crus.
            bool raw = src.PixelFormat == PixelFormat.Format32bppRgb;
            BitmapData bd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, raw ? PixelFormat.Format32bppRgb : PixelFormat.Format32bppArgb);
            try
            {
                byte[] px = new byte[w * 4];
                Action<int> read = y => Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, 0, px.Length);

                // BMP de 32 bits: o GDI+ ignora o alfa, mas o Photoshop o grava no 4º byte.
                // Tudo zero significa que o byte não é usado.
                bool opaque = raw;
                for (int y = 0; opaque && y < h; y++)
                {
                    read(y);
                    for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { opaque = false; break; }
                }

                return Resampler.Resize(w, h, delegate(int y, float[] row)
                {
                    read(y);
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        float a = opaque ? 1f : px[i + 3] / 255f;
                        row[i] = px[i] / 255f * a;
                        row[i + 1] = px[i + 1] / 255f * a;
                        row[i + 2] = px[i + 2] / 255f * a;
                        row[i + 3] = a;
                    }
                }, dw, dh, Kernel.Lanczos3);
            }
            finally { src.UnlockBits(bd); }
        }
        /// <summary>BGRA de 8 bits, alfa normal (não premultiplicado).</summary>
        public byte[] ToBgra()
        {
            byte[] o = new byte[D.Length];
            for (int i = 0; i < D.Length; i += 4)
            {
                float a = Math.Max(0f, Math.Min(1f, D[i + 3]));
                byte ab = (byte)(a * 255f + 0.5f);
                if (ab == 0) continue;
                for (int c = 0; c < 3; c++)
                    o[i + c] = (byte)(Math.Max(0f, Math.Min(1f, D[i + c] / a)) * 255f + 0.5f);
                o[i + 3] = ab;
            }
            return o;
        }

        /// <summary>Centraliza numa tela quadrada transparente.</summary>
        public FloatImage PadToSquare()
        {
            if (W == H) return this;
            int s = Math.Max(W, H), ox = (s - W) / 2, oy = (s - H) / 2;
            FloatImage o = new FloatImage(s, s);
            for (int y = 0; y < H; y++)
                Array.Copy(D, y * W * 4, o.D, ((y + oy) * s + ox) * 4, W * 4);
            return o;
        }
    }

    public enum Kernel { Box, Triangle, Mitchell, CatmullRom, Lanczos3 }

    public static class Resampler
    {
        static double Radius(Kernel k)
        {
            switch (k)
            {
                case Kernel.Box: return 0.5;
                case Kernel.Triangle: return 1;
                case Kernel.Lanczos3: return 3;
                default: return 2;
            }
        }

        static double Cubic(double x, double b, double c)
        {
            if (x < 1) return ((12 - 9 * b - 6 * c) * x * x * x + (-18 + 12 * b + 6 * c) * x * x + (6 - 2 * b)) / 6;
            if (x < 2) return ((-b - 6 * c) * x * x * x + (6 * b + 30 * c) * x * x + (-12 * b - 48 * c) * x + (8 * b + 24 * c)) / 6;
            return 0;
        }

        static double Sinc(double x)
        {
            if (x < 1e-9) return 1;
            x *= Math.PI;
            return Math.Sin(x) / x;
        }

        static double Eval(Kernel k, double x)
        {
            x = Math.Abs(x);
            switch (k)
            {
                case Kernel.Triangle: return x < 1 ? 1 - x : 0;
                case Kernel.Mitchell: return Cubic(x, 1 / 3.0, 1 / 3.0);
                case Kernel.CatmullRom: return Cubic(x, 0, 0.5);
                default: return x < 3 ? Sinc(x) * Sinc(x / 3) : 0;
            }
        }

        /// <summary>
        /// Redimensiona com o kernel esticado pela razão de redução: cada pixel do destino leva em
        /// conta TODOS os pixels de origem que cobre. É a falta disso que deixa serrilhado.
        /// </summary>
        public static FloatImage Resize(FloatImage src, int size, Kernel k)
        {
            if (src.W == size && src.H == size) return src;
            return Resize(src.W, src.H, (y, row) => Array.Copy(src.D, y * src.W * 4, row, 0, row.Length), size, size, k);
        }

        /// <summary>readRow entrega uma linha da origem por vez, para não precisar dela inteira na memória.</summary>
        public static FloatImage Resize(int sw, int sh, Action<int, float[]> readRow, int dw, int dh, Kernel k)
        {
            FloatImage mid = new FloatImage(dw, sh);
            float[] row = new float[sw * 4];
            int[] st = null; double[][] w = null;
            if (dw != sw) Weights(sw, dw, k, out st, out w);
            for (int y = 0; y < sh; y++)
            {
                readRow(y, row);
                if (dw == sw) { Array.Copy(row, 0, mid.D, y * sw * 4, row.Length); continue; }
                for (int x = 0; x < dw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        double v = 0;
                        double[] ws = w[x];
                        int p = st[x] * 4 + c;
                        for (int j = 0; j < ws.Length; j++, p += 4) v += row[p] * ws[j];
                        mid.D[(y * dw + x) * 4 + c] = (float)v;
                    }
            }
            return dh == sh ? mid : Vertical(mid, dh, k);
        }
        static void Weights(int sn, int dn, Kernel k, out int[] start, out double[][] w)
        {
            double ratio = (double)sn / dn, scale = Math.Max(1.0, ratio), r = Radius(k) * scale;
            start = new int[dn];
            w = new double[dn][];
            for (int i = 0; i < dn; i++)
            {
                double c = (i + 0.5) * ratio;
                int a = Math.Max(0, (int)Math.Floor(c - r)), b = Math.Min(sn - 1, (int)Math.Ceiling(c + r));
                double[] ws = new double[b - a + 1];
                double sum = 0;
                for (int j = a; j <= b; j++)
                {
                    double v = k == Kernel.Box
                        ? Math.Max(0, Math.Min(j + 1, c + r) - Math.Max(j, c - r)) // cobertura exata
                        : Eval(k, (j + 0.5 - c) / scale);
                    ws[j - a] = v;
                    sum += v;
                }
                for (int j = 0; j < ws.Length; j++) ws[j] /= sum;
                start[i] = a;
                w[i] = ws;
            }
        }

        static FloatImage Vertical(FloatImage s, int dh, Kernel k)
        {
            int[] st; double[][] w;
            Weights(s.H, dh, k, out st, out w);
            FloatImage o = new FloatImage(s.W, dh);
            for (int y = 0; y < dh; y++)
                for (int x = 0; x < s.W; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        double v = 0;
                        double[] ws = w[y];
                        int p = (st[y] * s.W + x) * 4 + c;
                        for (int j = 0; j < ws.Length; j++, p += s.W * 4) v += s.D[p] * ws[j];
                        o.D[(y * s.W + x) * 4 + c] = (float)v;
                    }
            return o;
        }
    }
}
