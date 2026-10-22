# Build with a preinstalled, activated Unity 6 editor and a dedicated URP fixture project.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$UnityEditorPath,
    [Parameter(Mandatory=$true)][string]$ProjectPath
)
$ErrorActionPreference = 'Stop'
$unity = (Resolve-Path $UnityEditorPath).Path
$project = (Resolve-Path $ProjectPath).Path
if (!(Test-Path (Join-Path $project 'Assets'))) { throw 'ProjectPath must be a complete Unity URP project.' }
& $unity -batchmode -quit -projectPath $project `
    -executeMethod DanceFlow.Unity.Editor.DanceFlowBenchmarkBuilder.BuildPlayer `
    -logFile (Join-Path $project 'danceflow-benchmark-build.log')
if ($LASTEXITCODE -ne 0) { throw "Unity Editor benchmark build failed: $LASTEXITCODE" }
Write-Host "Built benchmark Player under $project/Build/DanceFlowBenchmark"
