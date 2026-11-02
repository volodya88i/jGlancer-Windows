# Renders the main window offscreen in each theme and language into build\snapshots (design review, DESIGN.md section 12).
# Every run uses its own data folder under build\ (never the real library): the first-run screen with an empty one,
# the library with a copy of TestMedia\fixtures.
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = "src\JGlancer.App\bin\Debug\net10.0-windows\jGlancer.exe"
if (-not (Test-Path $exe)) { throw "run scripts\build.ps1 first" }
$work = "build\snapshot-data"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force build\snapshots, $work | Out-Null
$media = Join-Path (Resolve-Path $work) "media"
Copy-Item TestMedia\fixtures $media -Recurse

function Snapshot([string] $file, [string[]] $arguments) {
    $p = Start-Process $exe -ArgumentList ($arguments + @("--snapshot", $file)) -PassThru
    if (-not $p.WaitForExit(60000)) { Stop-Process $p; throw "snapshot $file timed out" }
    if ($p.ExitCode -ne 0) { throw "snapshot $file failed with exit code $($p.ExitCode)" }
    $file
}

foreach ($theme in "dark", "light", "dark-hc", "light-hc") {
    foreach ($lang in "en", "ru") {
        $empty = Join-Path $work "empty-$theme-$lang"
        Snapshot "build\snapshots\first-run-$theme-$lang.png" @("--data-dir", $empty, "--theme", $theme, "--lang", $lang)
        # One library per language, reused across themes, so only the first shot indexes.
        $data = Join-Path $work "library-$lang"
        Snapshot "build\snapshots\library-$theme-$lang.png" @(
            "--data-dir", $data, "--add-folder", $media, "--select-first", "--theme", $theme, "--lang", $lang)
        # The photo viewer on a readable photo of the same library.
        Snapshot "build\snapshots\viewer-$theme-$lang.png" @(
            "--data-dir", $data, "--open-first", "--viewer-step", "3", "--theme", $theme, "--lang", $lang)
        # The players (screen captures: video is drawn by the GPU; needs scripts\deps.ps1 for libmpv).
        if (Test-Path "build\deps\mpv-lgpl\libmpv-2.dll") {
            Snapshot "build\snapshots\video-$theme-$lang.png" @(
                "--data-dir", $data, "--open-first", "--open-kind", "video", "--theme", $theme, "--lang", $lang)
            Snapshot "build\snapshots\audio-$theme-$lang.png" @(
                "--data-dir", $data, "--open-first", "--open-kind", "audio", "--viewer-step", "2", "--theme", $theme, "--lang", $lang)
            # The preview pane with a song, and Quick Preview playing a video.
            Snapshot "build\snapshots\pane-audio-$theme-$lang.png" @(
                "--data-dir", $data, "--select-first", "--select-kind", "audio", "--theme", $theme, "--lang", $lang)
            Snapshot "build\snapshots\quick-preview-$theme-$lang.png" @(
                "--data-dir", $data, "--select-first", "--select-kind", "video", "--quick-preview", "--theme", $theme, "--lang", $lang)
            # The context menu with Open With, and Get Info (screen captures: they are windows of their own).
            Snapshot "build\snapshots\context-menu-$theme-$lang.png" @(
                "--data-dir", $data, "--select-first", "--select-kind", "audio", "--context-menu", "--theme", $theme, "--lang", $lang)
            Snapshot "build\snapshots\get-info-$theme-$lang.png" @(
                "--data-dir", $data, "--select-first", "--select-kind", "video", "--get-info", "--theme", $theme, "--lang", $lang)
        }
    }
}
