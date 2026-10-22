[CmdletBinding()]
param(
    [string]$RepositoryPath = (Get-Location).Path,
    [switch]$RunChecks
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path -LiteralPath $RepositoryPath).Path
$root = (& git -C $repo rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
    throw 'RepositoryPath must be inside a valid DanceFlow.Unity Git checkout.'
}
$root = $root.Trim()
$manifest = Join-Path $root 'package.json'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'UPM package.json missing.' }
$package = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
if ($package.name -ne 'com.kieransimkin.danceflow.unity') {
    throw 'This patch can only be applied to com.kieransimkin.danceflow.unity.'
}

$patches = @(
    'DanceFlow.Unity-Phase3-Cumulative.patch',
    'Phase3-From-Showcase.patch',
    'Phase3-From-Foundation.patch'
)
$fullPatch = Join-Path $PSScriptRoot $patches[0]
& git -C $root apply --reverse --check -- $fullPatch 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host 'Phase 3 cumulative patch is already present; no files changed.'
    exit 0
}
$dirty = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the working tree.' }
if ($dirty.Count -gt 0) {
    Write-Host 'Local modifications exist; refusing to overwrite. Commit or stash your work first:'
    $dirty | ForEach-Object { Write-Host $_ }
    exit 2
}
$selected = $null
foreach ($name in $patches) {
    $path = Join-Path $PSScriptRoot $name
    & git -C $root apply --check -- $path 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $selected = $path; break }
}
if ($null -eq $selected) {
    Write-Host 'No Phase 3 patch variant applies cleanly to this checkout.'
    Write-Host 'The cumulative patch targets published Phase 2 commit 9e1a02f.'
    Write-Host 'The compatibility deltas target earlier Phase 3 Foundation or Phase 3B slices.'
    Write-Host 'No files were modified. Compare your local Git history; do not force apply.'
    exit 3
}
Write-Host ('Applying ' + [IO.Path]::GetFileName($selected) + ' to ' + $root)
& git -C $root apply --whitespace=error -- $selected
if ($LASTEXITCODE -ne 0) { throw 'git apply failed.' }
Write-Host 'Applied without overwriting existing tracked changes. Inspect git diff.'
if ($RunChecks) {
    Push-Location $root
    try {
        if (Get-Command python -ErrorAction SilentlyContinue) {
            & python -m unittest discover -s 'tools~/showcase-checks' -p 'test_*.py' -v
            if ($LASTEXITCODE -ne 0) { throw 'Song showcase checks failed.' }
            & python 'tools~/recording-checks/verify_fixture.py'
            if ($LASTEXITCODE -ne 0) { throw 'Recording fixture check failed.' }
            & python 'tools/shader-checks/check_shader_contract.py'
            if ($LASTEXITCODE -ne 0) { throw 'Shader contract checks failed.' }
            & python -m unittest discover -s 'tools/benchmarks/tests' -v
            if ($LASTEXITCODE -ne 0) { throw 'Benchmark analysis checks failed.' }
        } else { Write-Warning 'Python is unavailable: static/fixture checks still need to run.' }
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            & dotnet run --project 'tools/clock-checks/ClockChecks.csproj' --configuration Release
            if ($LASTEXITCODE -ne 0) { throw 'Phase 1/2 .NET checks failed.' }
            & dotnet run --project 'tools~/recording-checks/RecordingChecks.csproj' --configuration Release
            if ($LASTEXITCODE -ne 0) { throw 'Phase 3 .NET checks failed.' }
        } else { Write-Warning '.NET 8 CLI not present: C# compilation/parity still required.' }
    } finally { Pop-Location }
}
Write-Host 'Review changes and Unity 6 Editor/GPU results before committing or releasing.'
