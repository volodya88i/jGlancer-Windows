# jGlancer for Windows

A fast, light media browser, viewer and player for photos, video and music. Native Windows app (C# / .NET 10,
Avalonia, libmpv, SQLite, NativeAOT).

## Features

- A library of your folders with a fast, virtualized grid of thumbnails; albums, flags, search, duplicates, split view.
- Photo viewer with zoom, filmstrip and side-by-side compare; image sequences fold into one stack.
- Video and audio players: waveform, segment selection and export without re-encoding, audio tracks, subtitles, HDR.
- Quick Preview with Space, also from Explorer; a mini player for music with media keys.
- Split songs into stems on your own computer (Demucs, ONNX Runtime with DirectML; the model is downloaded only when
  you ask for it).
- Dark, Black and Light themes with an accent color of your choice; English and Russian.

## Install

Download `jGlancer-Setup-<version>.exe` from [Releases](https://github.com/volodya88i/jGlancerWindows/releases) and
run it. Windows 10 or 11, x64. The installer is not code-signed with a paid certificate, so SmartScreen may warn about
it: choose "More info", then "Run anyway".

## Your data and the network

jGlancer has no telemetry and never changes your media except through the file actions you choose. It uses the
network in two cases only:

- **Update check.** At most once a day it asks the GitHub API for the number of the latest release; GitHub sees your IP
  address and the app's version, nothing about your files or library. Turn it off in Settings → General. An update
  runs only after its signature has been checked against a key built into the app.
- **Stem model.** Downloaded from GitHub when you press Download, once.

## Build from source

You need the .NET 10 SDK and, for release builds, the Visual Studio C++ build tools (NativeAOT linker). In PowerShell,
from the repository root:

```powershell
git config core.longpaths true   # one test file has a 255-character name
scripts\deps.ps1                 # libmpv (LGPL build), pinned by SHA-256
scripts\build.ps1                # debug build
scripts\test.ps1                 # all tests; they touch temporary folders only
scripts\installer.ps1            # release build and installer in build\installer
```

Generated files (`JGTokens.g.cs`, `JGIconData.g.cs`, `Strings/mac.*.json`) are committed. Code comments mention internal
decision numbers such as `W-85` and the macOS version's documents, which are not published.

Builds you make yourself cannot use the update check's download: it accepts only installers signed with the project's
key. To turn the check off, use the setting above.

## License

jGlancer is free software under the [GNU General Public License 3.0](LICENSE), with an additional permission for
Microsoft DirectML described in [NOTICE.md](NOTICE.md). Third-party components and their licences:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Security problems: see [SECURITY.md](SECURITY.md).
