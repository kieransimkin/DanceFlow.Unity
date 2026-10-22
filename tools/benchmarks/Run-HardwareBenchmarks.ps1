# Runs genuine Unity Player GPU/CPU benchmarks on the LOCAL graphics device.
# No Unity Editor, licence, or GPU is provided by this script.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PlayerPath,
    [string]$OutputDirectory = './bench-results',
    [ValidateRange(1,20)][int]$Repeats = 3,
    [ValidateRange(32,16384)][int]$Objects = 1024,
    [ValidateRange(30,3600)][int]$WarmupFrames = 600,
    [ValidateRange(60,12000)][int]$MeasuredFrames = 1800,
    [string]$PythonExe = 'python',
    [switch]$RequireGpu
)
$ErrorActionPreference = 'Stop'
$player = (Resolve-Path $PlayerPath).Path
if (!(Test-Path $player -PathType Leaf)) { throw "Unity benchmark Player not found: $PlayerPath" }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$gitRevision = 'unrecorded'
try { $gitRevision = (& git rev-parse HEAD).Trim() } catch { }
$session = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$modes = @('Baseline','Clock','Globals','Shader','GlobalsMonitored','ShaderMonitored')
$files = New-Object 'System.Collections.Generic.List[string]'
for ($i = 0; $i -lt $Repeats; $i++) {
    $runId = "${session}-repeat${i}"
    # Deterministic rotation reduces fixed-order thermal bias; compare within each repeat.
    for ($j = 0; $j -lt $modes.Length; $j++) {
        $mode = $modes[($i + $j) % $modes.Length]
        $output = Join-Path $destination "$runId-$mode.json"
        Write-Host "Benchmark $runId / $mode"
        & $player -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 `
            --dfu-mode $mode --dfu-output $output --dfu-run $runId `
            --dfu-revision $gitRevision --dfu-objects $Objects `
            --dfu-warmup $WarmupFrames --dfu-frames $MeasuredFrames
        if ($LASTEXITCODE -ne 0) { throw "Unity Player failed in $mode (exit $LASTEXITCODE). See Player.log." }
        if (!(Test-Path $output -PathType Leaf)) { throw "Missing output: $output" }
        $files.Add($output)
    }
}
$summary = Join-Path $destination "${session}-analysis.json"
$analysisArgs = @('tools/benchmarks/analyse.py') + @($files.ToArray()) + @('--output', $summary, '--required-repeats', $Repeats)
if ($RequireGpu) { $analysisArgs += '--require-gpu' }
& $PythonExe @analysisArgs
if ($LASTEXITCODE -ne 0) { throw "Benchmark analyser reported inconclusive/error result. Inspect $summary" }
Write-Host "Performance-only report: $summary"
Write-Host 'Release acceptance ALSO requires Unity shader GPU-readback parity, lifecycle, and approved device baselines.'
