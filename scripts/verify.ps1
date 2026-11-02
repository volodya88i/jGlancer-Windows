# Everything before saying "done": build (warnings are errors), tests, generated sources up to date (tests check
# tokens and icons), and an offscreen launch of the app in every theme and language.
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
& "$here\build.ps1"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$here\test.ps1"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$here\snapshots.ps1"
"verify: ok"
