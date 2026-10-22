# DanceFlow.Unity Phase 3 installer repair. Windows PowerShell 5.1 and PowerShell 7.
# This file is intended to sit beside the three Phase 3 .patch files, or be invoked
# with -RepositoryPath pointing to a checkout containing them.
# It never uses --force, git reset, git clean, or a 3-way merge.
param(
    [string]$RepositoryPath = '.',
    [switch]$RunChecks,
    [switch]$PreserveReadme
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryPath).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $root 'package.json'))) {
    throw "No package.json at $root. Run inside DanceFlow.Unity or supply -RepositoryPath."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not available on PATH.'
}

# Expected failures from git apply --check (especially --reverse) must NOT be
# interpreted as terminating PowerShell errors. Capture the native exit code.
function GitProbe {
    param([string[]]$GitArguments)
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $messages = @(& git -C $root @GitArguments 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prior
    }
    return [pscustomobject]@{
        Success = ($code -eq 0)
        Code = $code
        Output = ($messages | Out-String).Trim()
    }
}

$inside = GitProbe @('rev-parse', '--show-toplevel')
if (-not $inside.Success) { throw "Not a Git checkout: $root" }
$package = (Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw | ConvertFrom-Json)
if ($package.name -ne 'com.kieransimkin.danceflow.unity') {
    throw "Wrong package: expected com.kieransimkin.danceflow.unity, got $($package.name)."
}

# Protect tracked or staged work. Untracked installer and patch files are OK.
$unstaged = GitProbe @('diff', '--quiet', '--')
$staged = GitProbe @('diff', '--cached', '--quiet', '--')
if (-not $unstaged.Success -or -not $staged.Success) {
    throw 'There are tracked/staged local edits. Nothing applied. Commit or reconcile them first.'
}

# Most specific patch first. We do not manually stack patch variants.
$names = @(
    'Phase3-From-Showcase.patch',
    'Phase3-From-Foundation.patch',
    'DanceFlow.Unity-Phase3-Cumulative.patch'
)
$candidates = @()
foreach ($name in $names) {
    $path = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $path)) {
        $path = Join-Path $PSScriptRoot $name
    }
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $fullPath = (Resolve-Path -LiteralPath $path).ProviderPath
    $reverse = GitProbe @('apply', '--reverse', '--check', '--', $fullPath)
    if ($reverse.Success) {
        $expected = @(
            'Samples~/SongReactiveLandscape/SongShowcaseProfile.cs',
            'Samples~/SongReactiveLandscape/RecordedMotionWarp.shader',
            'tools~/showcase-checks/prepare_song_assets.py'
        )
        $allPresent = @($expected | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $root $_))
        }).Count -eq 0
        if ($allPresent) {
            Write-Host "Already applied: $name; all expected files exist." -ForegroundColor Yellow
            return
        }
        # A smaller earlier delta can be present while the final additions are
        # still missing. Do not wrongly declare the entire Phase 3 installed.
        Write-Host "Earlier delta present: $name; searching for the remaining additions." -ForegroundColor DarkGray
        continue
    }
    $check = GitProbe @('apply', '--check', '--', $fullPath)
    $mode = 'normal'
    if (-not $check.Success -and $PreserveReadme) {
        $readmeCheck = GitProbe @('apply', '--check', '--exclude=README.md', '--', $fullPath)
        if ($readmeCheck.Success) {
            $check = $readmeCheck
            $mode = 'preserve-readme'
        }
    }
    $candidates += [pscustomobject]@{
        Name = $name
        Path = $fullPath
        Mode = $mode
        Check = $check
    }
    if ($check.Success) { Write-Host "Compatible: $name ($mode)" -ForegroundColor Green }
    else { Write-Host "Not applicable: $name" -ForegroundColor DarkGray }
}

$matches = @($candidates | Where-Object { $_.Check.Success })
if ($matches.Count -ne 1) {
    if ($matches.Count -eq 0) {
        Write-Host 'No patch variant applies cleanly. No files changed.' -ForegroundColor Yellow
        foreach ($item in $candidates) {
            Write-Host ("`n--- {0} ---`n{1}" -f $item.Name, $item.Check.Output)
        }
        if (-not $PreserveReadme) {
            Write-Host 'If the ONLY conflict is README.md, rerun with -PreserveReadme to leave it untouched.'
        }
    } else {
        Write-Host 'More than one variant passes. No files changed; select a single intended variant.' -ForegroundColor Yellow
    }
    throw 'Refusing an ambiguous or conflicting Phase 3 patch.'
}

$selected = $matches[0]
Write-Host "Applying $($selected.Name) ($($selected.Mode))..."
$applyArgs = @('apply')
if ($selected.Mode -eq 'preserve-readme') {
    $applyArgs += '--exclude=README.md'
    Write-Warning 'README.md conflicts and will be kept unchanged. Restore the package README in a separate reviewed change.'
}
$applyArgs += @('--', $selected.Path)
$applied = GitProbe $applyArgs
if (-not $applied.Success) {
    throw "Git apply failed (exit $($applied.Code)):`n$($applied.Output)"
}

$required = @(
    'Samples~/SongReactiveLandscape/SongShowcaseProfile.cs',
    'Samples~/SongReactiveLandscape/RecordedMotionWarp.shader',
    'tools~/showcase-checks/prepare_song_assets.py'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_)) })
if ($missing.Count -gt 0) {
    throw "Patch applied, but these expected files are still absent: $($missing -join ', '). Inspect before committing."
}
Write-Host 'All three previously missing Phase 3 files now exist.' -ForegroundColor Green

$whitespace = GitProbe @('diff', '--check', '--')
if (-not $whitespace.Success) { throw "git diff --check reported an issue:`n$($whitespace.Output)" }

if ($RunChecks) {
    Push-Location -LiteralPath $root
    try {
        if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
            throw 'Python not found; Phase 3 files are applied, but checks are incomplete.'
        }
        foreach ($check in @(
            'tools/shader-checks/check_shader_contract.py',
            'tools~/showcase-checks/test_showcase_contract.py',
            'tools~/showcase-checks/test_song_profile_contract.py',
            'tools~/recording-checks/verify_fixture.py'
        )) {
            if (-not (Test-Path -LiteralPath $check)) { throw "Missing test script after apply: $check" }
            Write-Host "Testing: $check"
            & python $check
            if ($LASTEXITCODE -ne 0) { throw "Python check failed: $check" }
        }
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            & dotnet run --project 'tools~/recording-checks/RecordingChecks.csproj' -c Release
            if ($LASTEXITCODE -ne 0) { throw 'Managed recording checks failed.' }
        } else {
            Write-Warning '.NET SDK unavailable; managed recording checks remain outstanding.'
        }
    } finally {
        Pop-Location
    }
}

Write-Host "Phase 3 patch applied. Review 'git status --short' and commit only the intended source files." -ForegroundColor Green
