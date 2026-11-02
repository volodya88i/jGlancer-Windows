# Fetches the native libraries the build copies next to the app, pinned by URL and SHA-256 (approved,
# 2026-10-13). Builds never download anything; run this once (and after a pin changes). Files land in build\deps.
#   libmpv (LGPL build, DECISIONS W-32): zhongfly/mpv-winbuild, mpv-dev-lgpl-x86_64.
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$mpvRelease = "2026-10-13-c3ac7f3951"
$mpvArchive = "mpv-dev-lgpl-x86_64-20261013-git-c3ac7f3951.7z"
$mpvSha256 = "1ab49aa9ea347e0e1fae943900dd092642f63739fd49b43b46e1b41d5084756f"
$mpvTarget = "build\deps\mpv-lgpl"

if (Test-Path "$mpvTarget\libmpv-2.dll") {
    "libmpv: present ($mpvTarget)"
    return
}
$download = "build\deps\mpv-download"
New-Item -ItemType Directory -Force $download, $mpvTarget | Out-Null
$archive = Join-Path $download $mpvArchive
if (-not (Test-Path $archive)) {
    curl.exe -sSfL -o $archive "https://github.com/zhongfly/mpv-winbuild/releases/download/$mpvRelease/$mpvArchive"
}
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $mpvSha256) { throw "libmpv archive hash mismatch: $actual" }
# Windows' tar (libarchive) reads 7z.
& "$env:SystemRoot\System32\tar.exe" -xf $archive -C $mpvTarget
"libmpv: $mpvTarget\libmpv-2.dll"
