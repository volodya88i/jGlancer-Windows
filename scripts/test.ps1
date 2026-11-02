# Runs every test project. Tests only touch temp folders, never real media (CLAUDE.md).
# No -nologo here: under Microsoft.Testing.Platform `dotnet test` forwards it to the test apps, which then run nothing.
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet test --solution jGlancer.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
