# DanceFlow.Unity — permanent Git byte-integrity and step-test fix

This small source fix targets the current Phase 3 repository (main commit `34ca68c`).

It marks two byte-exact recording fixtures `-text diff` in the tracked `.gitattributes` file, and keeps `.gitattributes` itself LF on all platforms. Git will no longer convert their LF/CRLF endings on checkout, even if a developer has `core.autocrlf=true`. The `diff` attribute keeps reviewable textual diffs. It also fixes a mistaken .NET step-sampling assertion: pip -31.5 is still inside native pip window -32 and yields 0.2, not 0.3.

## Apply on Windows

Extract `Apply-Git-ByteIntegrity.py` into your `DanceFlow.Unity` checkout (or run it from anywhere with `--repo`). From the repository root:

```powershell
python .\Apply-Git-ByteIntegrity.py --repo .              # read-only preview
python .\Apply-Git-ByteIntegrity.py --repo . --apply      # guarded apply
python tools~/recording-checks/verify_fixture.py
dotnet run --project tools~/recording-checks/RecordingChecks.csproj -c Release
git diff --check
```

After validation, commit only `.gitattributes` and `tools~/recording-checks/Program.cs`. The Python installer itself need not be committed.

The utility checks the original `.gitattributes` and exact assertion before editing; refuses dirty target files or invalid fixture SHA; never resets files, edits fixture data or changes a hash receipt. It **does not change** `core.autocrlf` or other Git configuration; changing it on an existing checkout can make unrelated CRLF working files appear modified.

## Why this is permanent

`*.json -text diff` explicitly disables Git text conversion for these individual byte-exact files and retains diffs. This is versioned in `.gitattributes`, so it works on every clone. No local or global Git configuration change is necessary: committed attributes take precedence over `core.autocrlf` for these fixtures. Editing tools can still change bytes, and the existing SHA-256 guard correctly catches that.

No Unity runtime or motion recording file is modified.

## Reviewable diff

`DanceFlow.Unity-ByteExact-Fixture-and-StepTest.patch` contains the same two tracked-file changes. Prefer the guarded Python installer on Windows because its edit is safe even when C# source has CRLF working-tree endings. The `.patch` file is included for human review and clean LF checkouts.
