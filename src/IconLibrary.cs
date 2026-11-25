using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Iconzinho
{
    /// <summary>
    /// Junta vários .ico numa DLL só de recursos, do tipo que a janela "Alterar Ícone" do Windows abre.
    /// As imagens são copiadas byte a byte. O arquivo é montado à mão, sem código executável.
    /// </summary>
    static class IconLibrary
    {
        const int RtIcon = 3, RtGroupIcon = 14;
        const int SectionRva = 0x1000, HeaderSize = 0x200, FileAlign = 0x200;

        class Frame
        {
            public byte Width, Height, Colors;
            public ushort Planes, Bits;
            public byte[] Data;
        }

        /// <summary>Um ícone por arquivo, na ordem recebida.</summary>
        public static byte[] Build(IEnumerable<byte[]> icos)
        {
            List<byte[]> icons = new List<byte[]>(), groups = new List<byte[]>();
            foreach (byte[] ico in icos)
            {
                List<Frame> frames = Parse(ico);
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)frames.Count);
                    foreach (Frame f in frames)
                    {
                        icons.Add(f.Data);
                        if (icons.Count > ushort.MaxValue)
                            throw new InvalidDataException(App.T("too many icons for one library", "ícones demais para uma biblioteca"));
                        w.Write(f.Width); w.Write(f.Height); w.Write(f.Colors); w.Write((byte)0);
                        w.Write(f.Planes); w.Write(f.Bits);
                        w.Write(f.Data.Length); w.Write((ushort)icons.Count); // número do ícone, a partir de 1
                    }
                    w.Flush();
                    groups.Add(ms.ToArray());
                }
            }
            return Pe(Resources(icons, groups));
        }

        /// <summary>
        /// Os .ico de uma biblioteca feita por Build, na ordem; null se for qualquer outro arquivo. A
        /// prova é remontar e comparar byte a byte: assim nunca se regrava a DLL de outro programa.
        /// </summary>
        public static List<byte[]> Read(byte[] dll)
        {
            try
            {
                if (dll.Length < HeaderSize + 48 || Encoding.ASCII.GetString(dll, 0x188, 5) != ".rsrc") return null;
                int icons = BitConverter.ToUInt16(dll, HeaderSize + 32 + 14);
                int groupDir = 32 + 16 + 8 * icons;
                int groups = BitConverter.ToUInt16(dll, HeaderSize + groupDir + 14);
                int entries = groupDir + 16 + 8 * groups + 24 * (icons + groups);
                Func<int, byte[]> resource = i =>
                {
                    int e = HeaderSize + entries + 16 * i;
                    byte[] d = new byte[BitConverter.ToInt32(dll, e + 4)];
                    Array.Copy(dll, BitConverter.ToInt32(dll, e) - SectionRva + HeaderSize, d, 0, d.Length);
                    return d;
                };

                List<byte[]> icos = new List<byte[]>();
                for (int g = 0; g < groups; g++)
                {
                    byte[] d = resource(icons + g);
                    List<Frame> frames = new List<Frame>();
                    for (int i = 0, e = 6; i < BitConverter.ToUInt16(d, 4); i++, e += 14)
                        frames.Add(new Frame
                        {
                            Width = d[e], Height = d[e + 1], Colors = d[e + 2],
                            Planes = BitConverter.ToUInt16(d, e + 4), Bits = BitConverter.ToUInt16(d, e + 6),
                            Data = resource(BitConverter.ToUInt16(d, e + 12) - 1)
                        });
                    icos.Add(ToIco(frames));
                }
                return icos.Count > 0 && Build(icos).SequenceEqual(dll) ? icos : null;
            }
            catch (Exception) { return null; } // não tem o nosso formato
        }

        /// <summary>O mesmo ícone sempre nos mesmos bytes, para reconhecer repetidos.</summary>
        public static byte[] Normalize(byte[] ico) { return ToIco(Parse(ico)); }

        static byte[] ToIco(List<Frame> frames)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)frames.Count);
                int offset = 6 + 16 * frames.Count;
                foreach (Frame f in frames)
                {
                    w.Write(f.Width); w.Write(f.Height); w.Write(f.Colors); w.Write((byte)0);
                    w.Write(f.Planes); w.Write(f.Bits); w.Write(f.Data.Length); w.Write(offset);
                    offset += f.Data.Length;
                }
                foreach (Frame f in frames) w.Write(f.Data);
                w.Flush();
                return ms.ToArray();
            }
        }

        static Exception Bad() { return new InvalidDataException(App.T("not a valid .ico", "não é um .ico válido")); }

        static List<Frame> Parse(byte[] b)
        {
            if (b.Length < 6 || BitConverter.ToUInt16(b, 0) != 0 || BitConverter.ToUInt16(b, 2) != 1) throw Bad();
            int n = BitConverter.ToUInt16(b, 4);
            if (n == 0 || b.Length < 6 + 16 * n) throw Bad();
            List<Frame> frames = new List<Frame>();
            for (int i = 0; i < n; i++)
            {
                int e = 6 + 16 * i;
                long size = BitConverter.ToUInt32(b, e + 8), offset = BitConverter.ToUInt32(b, e + 12);
                if (size < 40 || offset + size > b.Length) throw Bad();
                Frame f = new Frame
                {
                    Width = b[e], Height = b[e + 1], Colors = b[e + 2],
                    Planes = BitConverter.ToUInt16(b, e + 4), Bits = BitConverter.ToUInt16(b, e + 6),
                    Data = new byte[size]
                };
                Array.Copy(b, offset, f.Data, 0, size);
                // Alguns programas deixam isto zerado no .ico, mas o Windows usa para escolher o frame.
                bool png = f.Data[0] == 0x89 && f.Data[1] == 'P' && f.Data[2] == 'N' && f.Data[3] == 'G';
                if (f.Bits == 0) f.Bits = png ? (ushort)32 : BitConverter.ToUInt16(f.Data, 14);
                if (f.Planes == 0) f.Planes = 1;
                frames.Add(f);
            }
            return frames;
        }

        /// <summary>Árvore de recursos: tipo > número > idioma > dados.</summary>
        static byte[] Resources(List<byte[]> icons, List<byte[]> groups)
        {
            List<byte[]> all = icons.Concat(groups).ToList();
            int iconDir = 32, groupDir = iconDir + 16 + 8 * icons.Count;
            int leaves = groupDir + 16 + 8 * groups.Count;   // um diretório de idioma (24 bytes) por recurso
            int entries = leaves + 24 * all.Count;           // uma descrição (16 bytes) por recurso
            int data = Align(entries + 16 * all.Count, 8);
            const uint Dir = 0x80000000;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                Directory(w, 2);
                w.Write(RtIcon); w.Write(Dir | (uint)iconDir);
                w.Write(RtGroupIcon); w.Write(Dir | (uint)groupDir);

                Directory(w, icons.Count);
                for (int i = 0; i < icons.Count; i++) { w.Write(i + 1); w.Write(Dir | (uint)(leaves + 24 * i)); }
                Directory(w, groups.Count);
                for (int i = 0; i < groups.Count; i++) { w.Write(i + 1); w.Write(Dir | (uint)(leaves + 24 * (icons.Count + i))); }

                for (int i = 0; i < all.Count; i++) { Directory(w, 1); w.Write(0); w.Write(entries + 16 * i); } // idioma neutro

                long at = data;
                foreach (byte[] d in all)
                {
                    w.Write(checked((int)(SectionRva + at))); w.Write(d.Length); w.Write(0); w.Write(0);
                    at = Align(checked((int)(at + d.Length)), 8);
                }
                foreach (byte[] d in all)
                {
                    w.Write(new byte[Align((int)ms.Position, 8) - (int)ms.Position]);
                    w.Write(d);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        static void Directory(BinaryWriter w, int ids)
        {
            w.Write(0); w.Write(0); w.Write(0); // características, data, versão
            w.Write((ushort)0); w.Write(checked((ushort)ids));
        }

        static int Align(int v, int a) { return checked((v + a - 1) / a * a); }

        /// <summary>DLL x64 mínima: cabeçalhos e uma seção .rsrc, sem ponto de entrada.</summary>
        static byte[] Pe(byte[] rsrc)
        {
            int raw = Align(rsrc.Length, FileAlign);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((ushort)0x5A4D);                    // MZ
                w.Write(new byte[0x3A]);
                w.Write(0x80);                              // onde começa o cabeçalho PE
                w.Write(new byte[0x40]);

                w.Write(0x00004550);                        // PE\0\0
                w.Write((ushort)0x8664); w.Write((ushort)1); // x64, uma seção
                w.Write(0); w.Write(0); w.Write(0);
                w.Write((ushort)240); w.Write((ushort)0x2022); // cabeçalho opcional; EXECUTABLE | LARGE_ADDRESS | DLL

                w.Write((ushort)0x20B); w.Write((byte)14); w.Write((byte)0);
                w.Write(0); w.Write(raw); w.Write(0);       // código, dados, não inicializado
                w.Write(0); w.Write(SectionRva);            // sem ponto de entrada
                w.Write(0x180000000L);
                w.Write(0x1000); w.Write(FileAlign);
                w.Write((ushort)6); w.Write((ushort)0);     // versão do sistema
                w.Write((ushort)0); w.Write((ushort)0);
                w.Write((ushort)6); w.Write((ushort)0);     // versão do subsistema
                w.Write(0);
                w.Write(Align(SectionRva + rsrc.Length, 0x1000)); w.Write(HeaderSize); w.Write(0);
                w.Write((ushort)2); w.Write((ushort)0x0560); // GUI; ASLR | NX | sem SEH
                w.Write(0x100000L); w.Write(0x1000L); w.Write(0x100000L); w.Write(0x1000L);
                w.Write(0); w.Write(16);
                for (int i = 0; i < 16; i++)
                {
                    w.Write(i == 2 ? SectionRva : 0);       // diretório 2: recursos
                    w.Write(i == 2 ? rsrc.Length : 0);
                }

                w.Write(Encoding.ASCII.GetBytes(".rsrc\0\0\0"));
                w.Write(rsrc.Length); w.Write(SectionRva); w.Write(raw); w.Write(HeaderSize);
                w.Write(0); w.Write(0); w.Write(0);
                w.Write(0x40000040);                        // dados, só leitura

                w.Write(new byte[HeaderSize - (int)ms.Position]);
                w.Write(rsrc);
                w.Write(new byte[raw - rsrc.Length]);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
