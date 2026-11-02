# Builds the whole solution (Debug by default). Usage: scripts\build.ps1 [-Configuration Release]
param([string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build jGlancer.slnx -c $Configuration -nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
