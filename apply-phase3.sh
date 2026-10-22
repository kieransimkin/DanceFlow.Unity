#!/usr/bin/env bash
set -euo pipefail
repo="${1:-.}"
checks="${2:-}"
repo="$(git -C "$repo" rev-parse --show-toplevel)"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ ! -f "$repo/package.json" ]] || ! grep -q 'com.kieransimkin.danceflow.unity' "$repo/package.json"; then
  echo 'This is not the DanceFlow.Unity UPM repository.' >&2; exit 2
fi
if git -C "$repo" apply --reverse --check "$script_dir/DanceFlow.Unity-Phase3-Cumulative.patch" 2>/dev/null; then
  echo 'Full Phase 3 already applied. Nothing changed.'; exit 0
fi
if [[ -n "$(git -C "$repo" status --porcelain)" ]]; then
  echo 'Refusing changes in a dirty Git working tree; commit or stash first.' >&2
  git -C "$repo" status --short >&2; exit 2
fi
selected=''
for name in DanceFlow.Unity-Phase3-Cumulative.patch Phase3-From-Showcase.patch Phase3-From-Foundation.patch; do
  if git -C "$repo" apply --check "$script_dir/$name" 2>/dev/null; then selected="$name"; break; fi
done
if [[ -z "$selected" ]]; then
  echo 'No variant applies cleanly; no files changed. Verify the published Phase 2 baseline or prior Phase 3 slice.' >&2
  exit 3
fi
git -C "$repo" apply --whitespace=error "$script_dir/$selected"
echo "Applied $selected; changes are uncommitted for your review."
if [[ "$checks" == '--checks' ]]; then
  cd "$repo"
  python -m unittest discover -s 'tools~/showcase-checks' -p 'test_*.py' -v
  python 'tools~/recording-checks/verify_fixture.py'
  python 'tools/shader-checks/check_shader_contract.py'
  python -m unittest discover -s 'tools/benchmarks/tests' -v
  if command -v dotnet >/dev/null; then
    dotnet run --project tools/clock-checks/ClockChecks.csproj --configuration Release
    dotnet run --project 'tools~/recording-checks/RecordingChecks.csproj' --configuration Release
  else
    echo '.NET CLI missing: C# compilation and parity remain outstanding.' >&2
  fi
fi
echo 'Run licensed Unity 6 Editor/URP GPU verification before a Phase 3 release.'
