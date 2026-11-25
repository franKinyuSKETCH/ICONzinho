# ICONzinho

Turn images into Windows `.ico` files from the right-click menu. By **frankinyu_SKETCH**.

**[English](#english)** · **[Português](#português)**

---

## English

Select one or more images, right-click, choose **Create icon (.ico)**. The icon appears next to the images. There is no window to fill in and nothing stays running in the background.

### What it does

- Reads **PNG, JPG/JPEG, WebP, SVG, BMP and GIF**.
- Generates every size Windows asks for: 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256.
- Scales down smoothly, without jagged edges.
- Keeps transparency.
- Joins several `.ico` files into an icon library (`.dll`).
- Never stretches: a non-square image is fitted and centered on a transparent square.
- Never enlarges: an icon made from a 100×100 image has no 128 or 256.
- Never overwrites: if `logo.ico` exists, it creates `logo (2).ico`.

### Install

Run `ICONzinho-Setup.exe` and pick English or Portuguese. It installs for your user only, in `%LOCALAPPDATA%\Programs\ICONzinho`.

You choose where the item appears:

| Option | What it needs |
|---|---|
| Classic menu only | Nothing. On Windows 11 the item is under *Show more options*, or Shift + right-click |
| New Windows 11 menu only | Windows 11, 64-bit, and administrator permission once, when installing |
| Both menus | Same as above |

The new menu only accepts commands from an MSIX package. Ours is **not digitally signed**, and Windows only installs an unsigned package with administrator permission. No certificate is added to your computer. If that step is refused, the classic menu still works. To change the menus later, run the installer again.

- **SmartScreen:** the files are not digitally signed, so Windows may warn you. Choose *More info > Run anyway*.
- **Uninstall:** *Settings > Apps > Installed apps*. Or run `ICONzinho-Setup.exe` again: it detects the installation and offers to reinstall or uninstall. Your icons are kept.
- **Silent install:** `ICONzinho-Setup.exe /S /lang=en` (or `/lang=pt`), with `/menu=classic`, `/menu=new` or `/menu=both`; classic is the default. To remove: `/S /uninstall`.
- **Without installing:** drop images onto `ICONzinho.exe`.

### Tips and special cases

#### Your own art at each size

Name each file with its size, select them all and convert:

```
512.png  256.png  128.png  64.png  48.png  32.png  16.png   →  one icon, named after the folder
```

- Your images go in **untouched**, pixel by pixel. Useful when you drew or exported each size by hand.
- Sizes you did not provide are generated from the largest image.
- Images above 256 are only used as that source, because 256 is the largest size in an `.ico`.
- You do not need all of them: `512.png` and `16.png` are enough.

#### Names that are grouped

Files in the same folder with the same name, apart from the size, become one icon:

| Selected files | Result |
|---|---|
| `logo-256.png`, `logo-32.png`, `logo-16.png` | `logo.ico` |
| `logo_32x32.png`, `logo 16px.png`, `logo(48).png` | `logo.ico` |
| `logo.svg`, `logo-32.png`, `logo-16.png` | `logo.ico`: your PNGs at 32 and 16, the SVG for the rest |
| `16.png`, `32.png`, `256.png` in the folder `app` | `app.ico` |
| `cat.png`, `car.jpg`, `house.svg` | three icons |
| `logo.png`, `logo.svg`, `logo.jpg` | three icons, because there is no way to tell which one to use |
| two sets at once: `app-16`, `app-32`, `doc-16`, `doc-32` | `app.ico` and `doc.ico` |

**The number must be the real size of the image.** `photo2.png` stays `photo2`, and a `256.png` that is actually 300×300 becomes a separate icon.

#### Icon library

Select several `.ico` files, right-click and choose **Create icon library (.dll)**. The result opens in the *Change Icon* window of shortcuts and folders, with all the icons in one place.

- The library is named after the folder, and the icons follow the order of the file names (`2` before `10`).
- Icons are copied byte by byte, with all their sizes.
- The DLL holds only resources, no code.
- It is not updated later: to add icons, create it again. Windows points to an icon by its position, so a different order changes the icons of existing shortcuts.
- If any file is not a valid `.ico`, nothing is created and the message says which one.

### Formats in detail

| Format | Notes |
|---|---|
| PNG | Transparency supported |
| JPG/JPEG | Follows the EXIF rotation of phone photos |
| WebP | Transparency supported. Needs *WebP Image Extensions* (Microsoft Store) if Windows does not have it yet. Animated: first frame |
| SVG | CSS, text and filters supported. Needs Microsoft Edge or Google Chrome, and takes about 2 seconds. Scripts are not executed |
| BMP | 32-bit BMP keeps its transparency |
| GIF | Transparency supported. Animated: first frame |

### How it scales

- **Photos and bitmaps:** Lanczos3, with premultiplied alpha. Among the filters tested, it came closest to the result of Photoshop.
- **SVG:** drawn at 1024×1024 by the browser engine, then reduced by exact pixel coverage. On flat artwork, Lanczos adds a light ring around edges and shifts colors.
- **Inside the `.ico`:** 256 is stored as PNG, the smaller sizes as 32-bit bitmaps, for compatibility.

### Build

Needs Windows 11, for the C# compiler (.NET Framework 4) and the Windows metadata that ship with it, and a `clang++` with MinGW for the new menu DLL: the one inside [Windhawk](https://windhawk.net) works, or [llvm-mingw](https://github.com/mstorsjo/llvm-mingw) in the PATH. The Windows SDK is not needed.

```powershell
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

This creates `ICONzinho.exe` and `ICONzinho-Setup.exe` in the `dist` folder.

| File | Contents |
|---|---|
| `Program.cs` | Entry point, Explorer queue, grouping, `.ico` writing |
| `Uninstaller.cs` | Removal |
| `Loader.cs` | Reading of each format |
| `Resampler.cs` | Scaling |
| `IconLibrary.cs` | Icon library (`.dll`) writing |
| `Common.cs` | Names, paths and language |
| `Setup.cs` | Installer |
| `MenuPackage.cs` | Installing and removing the new menu package |
| `menu/` | New menu: the DLL (`IExplorerCommand`), the MSIX manifest and the packaging tool |

In the classic menu, Explorer starts one process per selected file. Each one puts its path in a queue, and the first one waits for the others and converts everything together. In the new menu, the DLL receives all the files at once and hands them over.

### Limits

- Tested on Windows 11 x64. Other versions and ARM64 have not been tested.
- WebP files with EXIF rotation are not rotated.
- Embedded color profiles are ignored: export in sRGB.

### License

[MIT](LICENSE)

---

## Português

Selecione uma ou mais imagens, clique com o botão direito e escolha **Criar ícone (.ico)**. O ícone aparece ao lado das imagens. Não há janela para preencher, e nada fica rodando em segundo plano.

### O que faz

- Lê **PNG, JPG/JPEG, WebP, SVG, BMP e GIF**.
- Gera todos os tamanhos que o Windows pede: 16, 20, 24, 32, 40, 48, 64, 96, 128 e 256.
- Reduz com suavidade, sem serrilhado.
- Mantém a transparência.
- Junta vários `.ico` numa biblioteca de ícones (`.dll`).
- Nunca estica: imagem não quadrada é encaixada e centralizada num quadrado transparente.
- Nunca amplia: um ícone feito de uma imagem 100×100 não tem 128 nem 256.
- Nunca sobrescreve: se `logo.ico` já existe, cria `logo (2).ico`.

### Instalação

Abra o `ICONzinho-Setup.exe` e escolha inglês ou português. Instala só para o seu usuário, em `%LOCALAPPDATA%\Programs\ICONzinho`.

Você escolhe onde o item aparece:

| Opção | O que precisa |
|---|---|
| Só no menu clássico | Nada. No Windows 11 o item fica em *Mostrar mais opções*, ou Shift + clique direito |
| Só no menu novo do Windows 11 | Windows 11 de 64 bits e permissão de administrador uma vez, ao instalar |
| Nos dois menus | O mesmo do anterior |

O menu novo só aceita comandos de um pacote MSIX. O nosso **não tem assinatura digital**, e o Windows só instala um pacote assim com permissão de administrador. Nenhum certificado é adicionado ao seu computador. Se essa etapa for recusada, o menu clássico continua disponível. Para trocar os menus depois, abra o instalador de novo.

- **SmartScreen:** os arquivos não têm assinatura digital, então o Windows pode avisar. Escolha *Mais informações > Executar assim mesmo*.
- **Desinstalar:** *Configurações > Aplicativos > Aplicativos instalados*. Ou abra o `ICONzinho-Setup.exe` de novo: ele detecta a instalação e oferece reinstalar ou desinstalar. Os seus ícones são mantidos.
- **Instalação silenciosa:** `ICONzinho-Setup.exe /S /lang=pt` (ou `/lang=en`), com `/menu=classic`, `/menu=new` ou `/menu=both`; o padrão é o clássico. Para remover: `/S /uninstall`.
- **Sem instalar:** arraste imagens para cima do `ICONzinho.exe`.

### Segredinhos e casos especiais

#### A sua arte em cada tamanho

Dê a cada arquivo o nome do seu tamanho, selecione todos e converta:

```
512.png  256.png  128.png  64.png  48.png  32.png  16.png   →  um ícone só, com o nome da pasta
```

- As suas imagens entram **sem alteração**, pixel por pixel. Útil quando você desenhou ou exportou cada tamanho à mão.
- Os tamanhos que faltarem são gerados a partir da maior imagem.
- Imagens acima de 256 servem só de base para isso, porque 256 é o maior tamanho de um `.ico`.
- Não precisa ter todos: `512.png` e `16.png` bastam.

#### Nomes que são agrupados

Arquivos da mesma pasta com o mesmo nome, fora o tamanho, viram um ícone só:

| Arquivos selecionados | Resultado |
|---|---|
| `logo-256.png`, `logo-32.png`, `logo-16.png` | `logo.ico` |
| `logo_32x32.png`, `logo 16px.png`, `logo(48).png` | `logo.ico` |
| `logo.svg`, `logo-32.png`, `logo-16.png` | `logo.ico`: os seus PNGs em 32 e 16, o SVG no resto |
| `16.png`, `32.png`, `256.png` na pasta `app` | `app.ico` |
| `gato.png`, `carro.jpg`, `casa.svg` | três ícones |
| `logo.png`, `logo.svg`, `logo.jpg` | três ícones, porque não há como saber qual usar |
| dois conjuntos de uma vez: `app-16`, `app-32`, `doc-16`, `doc-32` | `app.ico` e `doc.ico` |

**O número precisa ser o tamanho real da imagem.** `foto2.png` continua `foto2`, e um `256.png` que na verdade tem 300×300 vira um ícone à parte.

#### Biblioteca de ícones

Selecione vários `.ico`, clique com o botão direito e escolha **Criar biblioteca de ícones (.dll)**. O resultado abre na janela *Alterar Ícone* de atalhos e pastas, com todos os ícones num lugar só.

- A biblioteca leva o nome da pasta, e os ícones seguem a ordem dos nomes dos arquivos (`2` antes de `10`).
- Os ícones são copiados byte a byte, com todos os tamanhos.
- A DLL só tem recursos, sem código.
- Ela não é atualizada depois: para acrescentar ícones, crie de novo. O Windows aponta para um ícone pela posição, então mudar a ordem troca os ícones dos atalhos existentes.
- Se algum arquivo não for um `.ico` válido, nada é criado e a mensagem diz qual é.

### Formatos em detalhe

| Formato | Observações |
|---|---|
| PNG | Aceita transparência |
| JPG/JPEG | Respeita a rotação EXIF das fotos de celular |
| WebP | Aceita transparência. Precisa das *Extensões de Imagem do WebP* (Microsoft Store), se o Windows ainda não as tiver. Animado: primeiro quadro |
| SVG | Aceita CSS, texto e filtros. Precisa do Microsoft Edge ou do Google Chrome e leva cerca de 2 segundos. Scripts não são executados |
| BMP | O BMP de 32 bits mantém a transparência |
| GIF | Aceita transparência. Animado: primeiro quadro |

### Como reduz

- **Fotos e bitmaps:** Lanczos3, com alfa premultiplicado. Entre os filtros testados, foi o que mais se aproximou do Photoshop.
- **SVG:** desenhado em 1024×1024 pelo motor do navegador e reduzido por cobertura exata de pixel. Em desenho de cor chapada, o Lanczos cria um anel claro nas bordas e altera as cores.
- **Dentro do `.ico`:** o 256 é guardado como PNG e os menores como bitmap de 32 bits, por compatibilidade.

### Compilar

Precisa do Windows 11, pelo compilador de C# (.NET Framework 4) e pelos metadados que vêm com ele, e de um `clang++` com MinGW para a DLL do menu novo: serve o que vem com o [Windhawk](https://windhawk.net), ou o [llvm-mingw](https://github.com/mstorsjo/llvm-mingw) no PATH. Não precisa do SDK do Windows.

```powershell
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

Gera `ICONzinho.exe` e `ICONzinho-Setup.exe` na pasta `dist`.

| Arquivo | Conteúdo |
|---|---|
| `Program.cs` | Entrada, fila do Explorer, agrupamento, gravação do `.ico` |
| `Uninstaller.cs` | Remoção |
| `Loader.cs` | Leitura de cada formato |
| `Resampler.cs` | Redimensionamento |
| `IconLibrary.cs` | Gravação da biblioteca (`.dll`) |
| `Common.cs` | Nomes, caminhos e idioma |
| `Setup.cs` | Instalador |
| `MenuPackage.cs` | Instalação e remoção do pacote do menu novo |
| `menu/` | Menu novo: a DLL (`IExplorerCommand`), o manifesto MSIX e a ferramenta que empacota |

No menu clássico, o Explorer abre um processo por arquivo selecionado. Cada um põe o seu caminho numa fila, e o primeiro espera os outros e converte tudo junto. No menu novo, a DLL recebe todos os arquivos de uma vez e os repassa.

### Limites

- Testado no Windows 11 x64. Outras versões e ARM64 não foram testadas.
- WebP com rotação EXIF não é girado.
- Perfis de cor embutidos são ignorados: exporte em sRGB.

### Licença

[MIT](LICENSE)
