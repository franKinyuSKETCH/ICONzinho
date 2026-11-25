// Ferramenta de compilação: junta os arquivos num .msix usando a API do próprio Windows, para não
// depender do makeappx do SDK.  Uso: pack.exe saida.msix AppxManifest.xml arquivo1 arquivo2 ...
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[StructLayout(LayoutKind.Sequential)]
struct AppxPackageSettings { public int ForceZip32; public IntPtr HashMethod; }

[ComImport, Guid("beb94909-e451-438b-b5a7-d79e767b75d8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAppxFactory
{
    IAppxPackageWriter CreatePackageWriter(IStream output, ref AppxPackageSettings settings);
}

[ComImport, Guid("9099e33b-246f-41e4-881a-008eb613f858"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAppxPackageWriter
{
    void AddPayloadFile([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string contentType, int compression, IStream input);
    void Close(IStream manifest);
}

static class Pack
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern IStream SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, IStream template);
    [DllImport("urlmon.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern IntPtr CreateUri(string uri, uint flags, IntPtr reserved);

    static IStream Read(string f) { return SHCreateStreamOnFileEx(f, 0x20, 0, false, null); } // SHARE_DENY_WRITE

    static int Main(string[] a)
    {
        try
        {
            IStream o = SHCreateStreamOnFileEx(a[0], 0x1001, 0x80, true, null); // CREATE | WRITE
            AppxPackageSettings s = new AppxPackageSettings { ForceZip32 = 1, HashMethod = CreateUri("http://www.w3.org/2001/04/xmlenc#sha256", 0, IntPtr.Zero) };
            IAppxFactory f = (IAppxFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781")));
            IAppxPackageWriter w = f.CreatePackageWriter(o, ref s);
            for (int i = 2; i < a.Length; i++)
                w.AddPayloadFile(Path.GetFileName(a[i]), a[i].EndsWith(".png") ? "image/png" : "application/x-msdownload", 1, Read(a[i]));
            w.Close(Read(a[1]));
            o.Commit(0);
            Marshal.ReleaseComObject(w);
            Marshal.ReleaseComObject(o);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("pack: " + ex.Message); return 1; }
    }
}
