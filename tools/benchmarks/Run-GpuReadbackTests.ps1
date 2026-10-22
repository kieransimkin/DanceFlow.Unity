# Run the actual Unity PlayMode GPU readback acceptance test. Skips/inconclusive are FAILED acceptance.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$UnityEditorPath,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [string]$OutputDirectory = './bench-results'
)
$ErrorActionPreference = 'Stop'
$unity = (Resolve-Path $UnityEditorPath).Path
$project = (Resolve-Path $ProjectPath).Path
$dest = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$testResults = Join-Path $dest 'unity-gpu-readback-results.xml'
$logFile = Join-Path $dest 'unity-gpu-readback.log'
& $unity -batchmode -quit -projectPath $project `
    -runTests -testPlatform PlayMode -testFilter 'GpuShaderReadbackTests' `
    -testResults $testResults -logFile $logFile
if ($LASTEXITCODE -ne 0) { throw "Unity GPU test process failed: exit $LASTEXITCODE; read $logFile" }
if (!(Test-Path $testResults -PathType Leaf)) { throw "Unity did not write Test Runner XML: $testResults" }
[xml]$report = Get-Content -Raw $testResults
$cases = $report.SelectNodes("//test-case[contains(@fullname,'GpuShaderReadbackTests')]")
if ($null -eq $cases -or $cases.Count -lt 1) { throw 'GPU readback acceptance test was not discovered (enable package testables and import URP shader sample).' }
foreach ($case in $cases) {
    if ($case.GetAttribute('result') -ne 'Passed') {
        throw "GPU readback acceptance not passed: $($case.GetAttribute('fullname')) => $($case.GetAttribute('result'))"
    }
}
Write-Host "Real Unity GPU readback test passed. XML: $testResults"
