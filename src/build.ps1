# Gera ICONzinho.exe, iconz.exe e ICONzinho-Setup.exe na pasta dist.
# Usa o csc que vem com o Windows e, para a DLL do menu novo, um clang++ com MinGW: o que vem com o
# Windhawk serve, ou o llvm-mingw no PATH.
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot) 'dist'), [switch]$Test)
$ErrorActionPreference = 'Stop'
$s = $PSScriptRoot
$net = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$net\csc.exe"
$obj = Join-Path $OutDir 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
function Check($what) { if ($LASTEXITCODE) { Write-Host "falhou: $what"; exit 1 } }

$md = "$env:WINDIR\System32\WinMetadata"
$common = @('/nologo', '/codepage:65001', '/target:winexe', '/optimize+', '/nowarn:0649', "/win32icon:$s\app.ico", "/win32manifest:$s\app.manifest",
    "/lib:$net\WPF", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll', '/r:System.Runtime.dll',
    '/r:System.Runtime.WindowsRuntime.dll', '/r:System.Runtime.InteropServices.WindowsRuntime.dll',
    "/r:$md\Windows.Foundation.winmd", "/r:$md\Windows.Management.winmd", "/r:$md\Windows.ApplicationModel.winmd", "/r:$md\Windows.System.winmd")
$shared = @("$s\Common.cs", "$s\AssemblyInfo.cs", "$s\Uninstaller.cs", "$s\MenuPackage.cs", "$s\UserPath.cs")
if ($Test) { $common += '/define:TEST' }

$engine = @("$s\Converter.cs", "$s\Loader.cs", "$s\Resampler.cs", "$s\IconLibrary.cs")

# 1. programa e comando de terminal
& $csc @common "/out:$OutDir\ICONzinho.exe" /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll "$s\Program.cs" @engine @shared
Check 'ICONzinho.exe'
& $csc @common /target:exe "/out:$OutDir\iconz.exe" /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll "$s\Cli.cs" "$s\Common.cs" "$s\AssemblyInfo.cs" @engine
Check 'iconz.exe'

# 2. DLL do menu novo
$clang = @("$env:ProgramFiles\Windhawk\Compiler\bin\clang++.exe") + @(Get-Command clang++ -ErrorAction SilentlyContinue | ForEach-Object Source) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $clang) { throw 'clang++ com MinGW nao encontrado (Windhawk ou llvm-mingw)' }
$def = @('-DUNICODE', '-D_UNICODE', '-D_WIN32_WINNT=0x0A00'); if ($Test) { $def += '-DTEST' }
& $clang -target x86_64-w64-mingw32 -std=c++17 -O2 -s -shared -static -Wno-dll-attribute-on-redeclaration @def -o "$obj\IconzinhoMenu.dll" "$s\menu\IconzinhoMenu.cpp" -lole32 -lshell32 -lshlwapi -ladvapi32 -luuid
Check 'IconzinhoMenu.dll'

# 3. pacote MSIX, com a versao propria dele (MenuVersion)
$version = [regex]::Match([IO.File]::ReadAllText("$s\Common.cs"), 'MenuVersion = "([\d.]+)"').Groups[1].Value
$xml = [IO.File]::ReadAllText("$s\menu\AppxManifest.xml").Replace('{VERSION}', "$version.0")
if ($Test) { $xml = $xml.Replace('frankinyuSKETCH.ICONzinho', 'frankinyuSKETCH.ICONzinhoTest').Replace('5d2e8f60a71c', '5d2e8f60a71d').Replace('5d2e8f60a72c', '5d2e8f60a72d').Replace('<DisplayName>ICONzinho<', '<DisplayName>ICONzinhoTest<').Replace('Id="ICONzinho', 'Id="ICONzinhoTest') }
[IO.File]::WriteAllText("$obj\AppxManifest.xml", $xml)
& $csc /nologo /codepage:65001 "/out:$obj\pack.exe" "$s\menu\Pack.cs"
Check 'pack.exe'
& "$obj\pack.exe" "$obj\ICONzinho.msix" "$obj\AppxManifest.xml" "$obj\IconzinhoMenu.dll" "$OutDir\ICONzinho.exe" "$s\menu\logo44.png" "$s\menu\logo150.png"
Check 'ICONzinho.msix'

# 4. instalador, com o programa e o pacote dentro
& $csc @common "/out:$OutDir\ICONzinho-Setup.exe" "/resource:$OutDir\ICONzinho.exe,ICONzinho.exe" "/resource:$OutDir\iconz.exe,iconz.exe" "/resource:$obj\ICONzinho.msix,ICONzinho.msix" "/resource:$s\app.ico,app.ico" "$s\Setup.cs" @shared
Check 'ICONzinho-Setup.exe'
exit 0
