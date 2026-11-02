# Benchmarks of the published app (scripts\publish.ps1 first). Informational, as on macOS (D-42): reports
# ok / near / over against ARCHITECTURE section 19. Every run keeps its library under build\bench (never the real one).
#   launch: time to first frame with an empty library, idle memory and CPU, app size.
#   library: for each synthetic library (tools: `synth 3000 --media`, `synth 100000`), a cold run that indexes it and
#   makes the thumbnails, then a warm run: index on screen, first thumbnail, first screen complete, 8 s of scrolling
#   at 4000 px/s (late frames), then the photo viewer on the first photo (time to show it, 20 pages forward: time per
#   page, budget 50 ms; memory while viewing), memory. Libraries that do not exist are skipped
#   (`synth-photos 60` makes 12-MP JPEGs for the viewer).
param(
    [int]$Runs = 5,
    [string[]]$Libraries = @("$env:TEMP\jGlancer-synthetic-3000-media", "$env:TEMP\jGlancer-synthetic-100000", "$env:TEMP\jGlancer-synthetic-photos-60")
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = "build\publish\jGlancer.exe"
if (-not (Test-Path $exe)) { throw "run scripts\publish.ps1 first" }
$bench = Join-Path (Get-Location) "build\bench"
if (Test-Path $bench) { Remove-Item -Recurse -Force $bench }
New-Item -ItemType Directory -Force $bench | Out-Null
$stamp = Get-Date -Format yyyyMMdd-HHmmss

function Verdict([double]$value, [double]$budget) {
    if ($value -le $budget) { "ok" } elseif ($value -le 1.5 * $budget) { "near" } else { "over" }
}

function Run([string[]] $arguments, [int] $timeoutSeconds = 120) {
    $p = Start-Process $exe -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit($timeoutSeconds * 1000)) { Stop-Process $p; throw "run timed out: $arguments" }
}

function Results([string] $file) {
    $values = @{}
    foreach ($line in Get-Content $file) {
        $parts = $line -split ' '
        $values[$parts[0]] = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
    }
    $values
}

# Launch: one warm-up run (the first launch of a new binary is slowed down by the antivirus scan).
$empty = Join-Path $bench "data-empty"
$log = Join-Path $bench "launch-$stamp.txt"
foreach ($i in 0..$Runs) {
    Run @("--data-dir", $empty, "--launch-log", $log, "--snapshot", (Join-Path $bench "launch.png")) 30
}
$times = Get-Content $log | ForEach-Object { [double](($_ -split ' ')[1]) } | Select-Object -Skip 1
$median = ($times | Sort-Object)[[int][math]::Floor($times.Count / 2)]
"launch -> first frame: median {0} ms of {1} runs (budget 400) {2}" -f $median, $times.Count, (Verdict $median 400)

$p = Start-Process $exe -ArgumentList "--data-dir", $empty -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$memory = $p.WorkingSet64 / 1MB
$cpuStart = $p.TotalProcessorTime
Start-Sleep -Seconds 5
$p.Refresh()
$cpu = ($p.TotalProcessorTime - $cpuStart).TotalMilliseconds / 5000 / [Environment]::ProcessorCount * 100
Stop-Process $p
"idle memory (working set): {0:N0} MB (budget 250) {1}" -f $memory, (Verdict $memory 250)
"idle CPU: {0:N2} % (budget 1) {1}" -f $cpu, (Verdict $cpu 1)
$size = (Get-ChildItem build\publish -File | Measure-Object Length -Sum).Sum / 1MB
"app folder: {0:N1} MB (budget 15) {1}" -f $size, (Verdict $size 15)

foreach ($library in $Libraries) {
    if (-not (Test-Path $library)) { "skipped (missing): $library"; continue }
    $name = Split-Path $library -Leaf
    $data = Join-Path $bench "data-$name"
    ""
    "library $name"
    foreach ($pass in "cold", "warm") {
        $file = Join-Path $bench "$name-$pass.txt"
        Run @("--data-dir", $data, "--add-folder", $library, "--benchmark", $file, "--launch-log", $file) 600
        $r = Results $file
        "  {0}: {1:N0} items | first frame {2:N0} ms | index on screen {3:N0} ms | first thumbnail {4:N0} ms | first screen complete {5:N0} ms" -f `
            $pass, $r["library.items"], $r["launch.firstFrame"], $r["library.indexShown"], $r["library.firstThumbnail"], $r["library.firstScreenComplete"]
        "  {0}: scroll {1:N0} frames, {2:N0} late ({3:N1} %, budget 0) {4} | worst frame {5:N0} ms | working set {6:N0} MB (budget 250) {7}" -f `
            $pass, $r["scroll.frames"], $r["scroll.lateFrames"], $r["scroll.lateShare"], (Verdict $r["scroll.lateShare"] 1), `
            $r["scroll.worstFrame"], $r["memory.workingSet"], (Verdict $r["memory.workingSet"] 250)
        if ($r.ContainsKey("viewer.pages")) {
            "  {0}: viewer first photo {1:N0} ms | next photo median {2:N1} ms, worst {3:N1} ms (budget 50) {4} | viewing {5:N0} MB (budget 500) {6}" -f `
                $pass, $r["viewer.firstPhoto"], $r["viewer.nextMedian"], $r["viewer.nextWorst"], (Verdict $r["viewer.nextWorst"] 50), `
                $r["memory.viewerWorkingSet"], (Verdict $r["memory.viewerWorkingSet"] 500)
        }
        if ($r.ContainsKey("media.open")) {
            "  {0}: video opens in {1:N0} ms | playing {2:N1} % CPU, hardware decoding {3} | {4:N0} MB | closed cleanly {5}" -f `
                $pass, $r["media.open"], $r["media.playCpu"], ($(if ($r["media.hardwareDecoding"] -eq 1) { "on" } else { "off" })), `
                $r["memory.playingWorkingSet"], ($(if ($r["media.closed"] -eq 1) { "yes" } else { "NO" }))
        }
    }
}