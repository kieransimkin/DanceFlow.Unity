[CmdletBinding()]
param(
    [string]$RepositoryPath = '.',
    [switch]$RunChecks,
    [switch]$DryRun,
    [switch]$ContextMerge
)
$ErrorActionPreference = 'Stop'
$pythonExe = Get-Command 'py' -ErrorAction SilentlyContinue
if ($null -ne $pythonExe) { $prefix = @('-3') ; $exe = $pythonExe.Source }
else {
    $pythonExe = Get-Command 'python' -ErrorAction SilentlyContinue
    if ($null -eq $pythonExe) { throw 'Python 3 is required; install it or run Recover-Phase3.py from a working Python environment.' }
    $prefix = @() ; $exe = $pythonExe.Source
}
$script = Join-Path $PSScriptRoot 'Recover-Phase3.py'
$argsList = @($prefix) + @($script, '--repo', $RepositoryPath)
if ($RunChecks) { $argsList += '--checks' }
if ($DryRun) { $argsList += '--dry-run' }
if ($ContextMerge) { $argsList += '--context-merge' }
& $exe @argsList
if ($LASTEXITCODE -ne 0) { throw "Targeted Phase 3 recovery returned exit code $LASTEXITCODE. See diagnostics above; no reset was performed." }
