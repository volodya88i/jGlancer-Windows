# Builds the installer build\installer\jGlancer-Setup-<version>.exe (W-73): the release app (scripts\publish.ps1)
# zipped and appended to the setup stub (src\JGlancer.Setup), which unpacks it and starts `jGlancer.exe --setup`.
# -SkipPublish reuses build\publish as it is. Needs the VS Build Tools C++ workload (NativeAOT linker), like publish.
param([switch] $SkipPublish)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path $installer) { $env:Path = "$installer;$env:Path" }

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $SkipPublish) {
    & "$PSScriptRoot\publish.ps1" | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet publish src\JGlancer.Setup -c Release -o build\setup-stub -nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The app's own files only (build\publish may also hold stray shortcuts).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$source = (Resolve-Path build\publish).Path
$files = Get-ChildItem $source -Recurse -File | Where-Object { $_.Extension -in ".exe", ".dll", ".txt" }
New-Item -ItemType Directory -Force build\installer | Out-Null
$zip = Join-Path (Resolve-Path build\installer).Path "app.zip"
if (Test-Path $zip) { Remove-Item $zip }
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $name = $file.FullName.Substring($source.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

# stub + archive + [archive offset (8 bytes) + "JGSETUP1"]
$output = Join-Path (Resolve-Path build\installer).Path "jGlancer-Setup-$version.exe"
$stub = [System.IO.File]::ReadAllBytes((Resolve-Path build\setup-stub\jGlancer-Setup.exe).Path)
$stream = [System.IO.File]::Create($output)
try {
    $stream.Write($stub, 0, $stub.Length)
    $payload = [System.IO.File]::OpenRead($zip)
    try { $payload.CopyTo($stream) } finally { $payload.Dispose() }
    $offset = [BitConverter]::GetBytes([long]$stub.Length)
    $stream.Write($offset, 0, 8)
    $mark = [System.Text.Encoding]::ASCII.GetBytes("JGSETUP1")
    $stream.Write($mark, 0, 8)
} finally { $stream.Dispose() }
Remove-Item $zip
"{0}  {1:N1} MB  ({2} files)" -f $output, ((Get-Item $output).Length / 1MB), $files.Count
