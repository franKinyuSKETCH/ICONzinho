// Item do menu de contexto novo do Windows 11 (IExplorerCommand).
// Fica num pacote MSIX e roda num dllhost, fora do Explorer. Só mostra o item e repassa os arquivos
// para o ICONzinho.exe instalado; toda a conversão está lá.

#ifndef UNICODE
#define UNICODE
#endif
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <shobjidl.h>
#include <new>
#include <string>

#ifdef TEST
#define APP_NAME L"ICONzinhoTest"
#else
#define APP_NAME L"ICONzinho"
#endif

// Precisa ser igual à do AppxManifest.xml. A compilação de teste usa outra.
#ifdef TEST
static const CLSID CLSID_Menu = { 0x7c1f9b52, 0x3e0d, 0x4a86, { 0x9b, 0x41, 0x5d, 0x2e, 0x8f, 0x60, 0xa7, 0x1d } };
static const CLSID CLSID_Library = { 0x7c1f9b52, 0x3e0d, 0x4a86, { 0x9b, 0x41, 0x5d, 0x2e, 0x8f, 0x60, 0xa7, 0x2d } };
#else
static const CLSID CLSID_Menu = { 0x7c1f9b52, 0x3e0d, 0x4a86, { 0x9b, 0x41, 0x5d, 0x2e, 0x8f, 0x60, 0xa7, 0x1c } };
static const CLSID CLSID_Library = { 0x7c1f9b52, 0x3e0d, 0x4a86, { 0x9b, 0x41, 0x5d, 0x2e, 0x8f, 0x60, 0xa7, 0x2c } };
#endif

static long g_refs = 0;

static std::wstring ExePath()
{
    std::wstring path;
    PWSTR local = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)))
        path = std::wstring(local) + L"\\Programs\\" APP_NAME L"\\" APP_NAME L".exe";
    CoTaskMemFree(local);
    return path;
}

static bool Portuguese()
{
#ifdef TEST
    const wchar_t* key = L"Software\\" APP_NAME L"\\HKCU\\Software\\" APP_NAME;
#else
    const wchar_t* key = L"Software\\" APP_NAME;
#endif
    wchar_t v[8] = {};
    DWORD size = sizeof(v);
    if (RegGetValueW(HKEY_CURRENT_USER, key, L"Language", RRF_RT_REG_SZ, nullptr, v, &size) == ERROR_SUCCESS)
        return lstrcmpiW(v, L"pt") == 0;
    return PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_PORTUGUESE;
}

// Abre o exe fora do contêiner do pacote (senão o AppData dele seria redirecionado) e entrega os
// caminhos pela entrada padrão, um por linha, para não esbarrar no limite da linha de comando.
static HRESULT Launch(const std::string& paths, const wchar_t* option)
{
    std::wstring exe = ExePath();
    if (GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);

    SECURITY_ATTRIBUTES sa = { sizeof(sa), nullptr, TRUE };
    HANDLE rd = nullptr, wr = nullptr;
    if (!CreatePipe(&rd, &wr, &sa, 0)) return HRESULT_FROM_WIN32(GetLastError());
    SetHandleInformation(wr, HANDLE_FLAG_INHERIT, 0);

    const DWORD_PTR ATTR_HANDLE_LIST = 0x00020002, ATTR_DESKTOP_APP_POLICY = 0x00020012;
    DWORD breakaway = 1; // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE
    SIZE_T size = 0;
    InitializeProcThreadAttributeList(nullptr, 2, 0, &size);
    auto list = static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(HeapAlloc(GetProcessHeap(), 0, size));
    HRESULT hr = E_OUTOFMEMORY;
    if (list && InitializeProcThreadAttributeList(list, 2, 0, &size))
    {
        UpdateProcThreadAttribute(list, 0, ATTR_HANDLE_LIST, &rd, sizeof(rd), nullptr, nullptr);
        UpdateProcThreadAttribute(list, 0, ATTR_DESKTOP_APP_POLICY, &breakaway, sizeof(breakaway), nullptr, nullptr);

        STARTUPINFOEXW si = {};
        si.StartupInfo.cb = sizeof(si);
        si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        si.StartupInfo.hStdInput = rd;
        si.lpAttributeList = list;
        PROCESS_INFORMATION pi = {};
        std::wstring cmd = L"\"" + exe + L"\" " + option;
        if (CreateProcessW(exe.c_str(), &cmd[0], nullptr, nullptr, TRUE, EXTENDED_STARTUPINFO_PRESENT, nullptr, nullptr, &si.StartupInfo, &pi))
        {
            CloseHandle(rd);
            rd = nullptr;
            hr = S_OK;
            DWORD done = 0;
            for (size_t i = 0; i < paths.size(); i += done)
                if (!WriteFile(wr, paths.data() + i, static_cast<DWORD>(paths.size() - i), &done, nullptr) || !done)
                {
                    hr = HRESULT_FROM_WIN32(GetLastError());
                    break;
                }
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
        else hr = HRESULT_FROM_WIN32(GetLastError());
        DeleteProcThreadAttributeList(list);
    }
    if (list) HeapFree(GetProcessHeap(), 0, list);
    if (rd) CloseHandle(rd);
    CloseHandle(wr);
    return hr;
}

class Command : public IExplorerCommand
{
    long refs = 1;
    const bool library; // .ico -> biblioteca .dll
    ~Command() { InterlockedDecrement(&g_refs); }

public:
    explicit Command(bool lib) : library(lib) { InterlockedIncrement(&g_refs); }

    IFACEMETHODIMP QueryInterface(REFIID iid, void** out)
    {
        if (!out) return E_POINTER;
        if (iid == IID_IUnknown || iid == __uuidof(IExplorerCommand)) { *out = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        *out = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() { long r = InterlockedDecrement(&refs); if (!r) delete this; return r; }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* out) 
    {
        if (library) return SHStrDupW(Portuguese() ? L"Criar biblioteca de ícones (.dll)" : L"Create icon library (.dll)", out);
        return SHStrDupW(Portuguese() ? L"Criar ícone (.ico)" : L"Create icon (.ico)", out);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* out) { return SHStrDupW((ExePath() + L",0").c_str(), out); }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* out) { *out = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* out) { *out = library ? CLSID_Library : CLSID_Menu; return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* out) { *out = ECS_ENABLED; return S_OK; }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* out) { *out = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** out) { *out = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*)
    {
        if (!items) return E_INVALIDARG;
        DWORD n = 0;
        HRESULT hr = items->GetCount(&n);
        if (FAILED(hr)) return hr;
        std::string paths;
        for (DWORD i = 0; i < n; i++)
        {
            IShellItem* item = nullptr;
            PWSTR p = nullptr;
            if (SUCCEEDED(items->GetItemAt(i, &item)) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &p)))
            {
                int len = WideCharToMultiByte(CP_UTF8, 0, p, -1, nullptr, 0, nullptr, nullptr);
                std::string u(len, '\0');
                WideCharToMultiByte(CP_UTF8, 0, p, -1, &u[0], len, nullptr, nullptr);
                u.back() = '\n';
                paths += u;
            }
            CoTaskMemFree(p);
            if (item) item->Release();
        }
        return paths.empty() ? E_FAIL : Launch(paths, library ? L"--stdin-lib" : L"--stdin");
    }
};

class Factory : public IClassFactory
{
    const bool library;

public:
    explicit Factory(bool lib) : library(lib) {}
    IFACEMETHODIMP QueryInterface(REFIID iid, void** out)
    {
        if (!out) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IClassFactory) { *out = static_cast<IClassFactory*>(this); return S_OK; }
        *out = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return 2; }
    IFACEMETHODIMP_(ULONG) Release() { return 1; }
    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** out)
    {
        *out = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        Command* c = new (std::nothrow) Command(library);
        if (!c) return E_OUTOFMEMORY;
        HRESULT hr = c->QueryInterface(iid, out);
        c->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock) { lock ? InterlockedIncrement(&g_refs) : InterlockedDecrement(&g_refs); return S_OK; }
};

static Factory g_menu(false), g_library(true);

extern "C" __declspec(dllexport) HRESULT STDAPICALLTYPE DllGetClassObject(REFCLSID clsid, REFIID iid, void** out)
{
    if (clsid == CLSID_Menu) return g_menu.QueryInterface(iid, out);
    if (clsid == CLSID_Library) return g_library.QueryInterface(iid, out);
    *out = nullptr;
    return CLASS_E_CLASSNOTAVAILABLE;
}

extern "C" __declspec(dllexport) HRESULT STDAPICALLTYPE DllCanUnloadNow() { return g_refs == 0 ? S_OK : S_FALSE; }
