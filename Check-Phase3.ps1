[CmdletBinding()]
param(
    [string]$RepositoryPath = '.',
    [string]$OutputDirectory,
    [switch]$PythonOnly
)
$ErrorActionPreference = 'Stop'
$scriptFile = Join-Path $PSScriptRoot 'Check-Phase3.py'
if (-not (Test-Path -LiteralPath $scriptFile)) {
    throw "Missing Python checker: $scriptFile"
}
$py = Get-Command 'py' -ErrorAction SilentlyContinue
if ($null -ne $py) {
    $executable = $py.Source
    $arguments = @('-3', $scriptFile, '--repo', $RepositoryPath)
} else {
    $py = Get-Command 'python' -ErrorAction SilentlyContinue
    if ($null -eq $py) { throw 'Python 3 is required. Run the Python checker with a Python 3 installation.' }
    $executable = $py.Source
    $arguments = @($scriptFile, '--repo', $RepositoryPath)
}
if ($PythonOnly) { $arguments += '--python-only' }
if ($OutputDirectory) { $arguments += @('--output', $OutputDirectory) }
& $executable @arguments
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Warning "Some Phase 3 checks failed (exit $code). See the full logs printed above. No patches or resets were performed."
}
