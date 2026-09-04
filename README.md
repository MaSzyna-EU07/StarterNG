# StarterNG

Launcher for the MaSzyna train simulator, written in C# with Avalonia.

## Building a native (NativeAOT) build

The project publishes as a self-contained NativeAOT binary — no .NET runtime is
needed on the target machine. The commands below are the same ones CI runs, see
[`.github/workflows/dotnet.yml`](.github/workflows/dotnet.yml).

Requirements:

- .NET SDK 9
- Linux: a native toolchain and zlib headers — `sudo apt install clang zlib1g-dev`
  (Debian/Ubuntu) or `sudo dnf install clang zlib-devel` (Fedora)
- Windows: the Visual Studio C++ build tools (Desktop development with C++)

Restore for the target runtime first, then publish:

```sh
dotnet restore StarterNG/StarterNG.csproj -r linux-x64

dotnet publish StarterNG/StarterNG.csproj \
    -c Release -r linux-x64 --self-contained true --no-restore \
    -p:PublishAot=true -p:PublishTrimmed=true \
    -o publish/linux-x64
```

For a Windows build swap both `linux-x64` occurrences for `win-x64` (and the
line continuations for PowerShell backticks). NativeAOT does not cross-compile,
so the Windows package has to be built on Windows and the Linux one on Linux —
that is why CI uses a `windows-2022` and an `ubuntu-22.04` runner.

The output directory is the whole distributable: the `Starter` executable, the
Skia/HarfBuzz native libraries and `startercfg/` with the translations and the
default key bindings. The `.dbg` (or `.pdb`) file next to it is debug symbols and
can be dropped from a release. Trim/AOT analysis warnings during the publish are
expected and do not fail the build.

CI builds both runtimes on every push and pull request to `main` and `staging`,
and on manual dispatch. Pushes additionally replace a rolling pre-release with
the packaged artifacts — tagged `latest` for `main` and `staging` for `staging`.

## Development build

```sh
dotnet build StarterNG/StarterNG.csproj
```
