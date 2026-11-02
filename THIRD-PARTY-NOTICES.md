# Third-party notices

jGlancer for Windows includes the following third-party components. Their licence texts ship with the app (installer
and About box, W12).

| Component | Use | Licence |
|---|---|---|
| [Avalonia](https://avaloniaui.net) 12 (incl. Fluent theme, Skia/HarfBuzz/ANGLE natives) | UI framework | MIT (natives under their own permissive licences) |
| [Inter](https://rsms.me/inter/) via Avalonia.Fonts.Inter | Body text | SIL Open Font License 1.1 |
| [Unbounded](https://github.com/googlefonts/unbounded) | Display and heading text | SIL Open Font License 1.1 — `src/JGlancer.Design/Assets/Fonts/Unbounded-OFL.txt` |
| [JetBrains Mono](https://www.jetbrains.com/lp/mono/) 2.304 | Numbers, sizes, time codes | SIL Open Font License 1.1 — `src/JGlancer.Design/Assets/Fonts/JetBrainsMono-OFL.txt` |
| [Lucide](https://lucide.dev) 1.55 | Icons (path data in `JGIconData.g.cs`) | ISC, some icons MIT (Feather) — `src/JGlancer.Design/Icons/Lucide-LICENSE.txt` |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) with SQLitePCLRaw | Library database | MIT / Apache-2.0 |
| [SQLite](https://sqlite.org) 3.53 (e_sqlite3.dll) | Library database engine | Public domain |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | View-models | MIT |
| [mpv](https://mpv.io) (libmpv-2.dll, LGPL build by [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild), 2026-10-13, with [FFmpeg](https://ffmpeg.org) and its LGPL libraries) | Video and audio playback | LGPL-2.1-or-later; used as a separate, replaceable DLL. Sources: the projects' repositories at the build's commits (`scripts/deps.ps1`) |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | Stem splitting | MIT |
| [DirectML](https://github.com/microsoft/DirectML) | GPU for stem splitting | Microsoft Software License Terms (`DirectML-LICENSE.txt`) |
| [Demucs](https://github.com/facebookresearch/demucs) models | Downloaded on request for stem splitting | MIT |
| [xunit.v3](https://xunit.net) | Tests, not shipped | Apache-2.0 |
