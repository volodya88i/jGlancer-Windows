# NativeAOT release build into build\publish (win-x64). Needs the VS Build Tools C++ workload (MSVC linker).
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
# The ILCompiler's linker lookup calls vswhere.exe by name.
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path $installer) { $env:Path = "$installer;$env:Path" }
dotnet publish src\JGlancer.App -c Release -o build\publish -nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$total = (Get-ChildItem build\publish -File | Measure-Object Length -Sum).Sum / 1MB
Get-ChildItem build\publish -File | ForEach-Object { "{0,8:N2} MB  {1}" -f ($_.Length / 1MB), $_.Name }
"{0,8:N2} MB  total" -f $total
