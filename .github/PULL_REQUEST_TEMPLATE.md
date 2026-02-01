## What this changes

What was wrong, or what is new, and why this is the right shape for it.

## How it was checked

- [ ] `dotnet build Distillate.sln -c Release` (warnings are errors)
- [ ] `dotnet format Distillate.sln --verify-no-changes`
- [ ] `dotnet test Distillate.sln -c Release`
- [ ] Built the image and ran it with no environment variables
- [ ] Made a real `POST /process` request and read the output

Tick what you ran. An unticked box is a question, not a failure.

## Documentation

- [ ] README configuration table matches the code
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]`
- [ ] `docs/SPEC.md` updated if the API changed

## Notes for the reviewer

Limitations of this change, cases it does not handle, or a dependency that had
to move.
