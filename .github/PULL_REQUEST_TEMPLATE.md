## What this changes

One paragraph. What was wrong, or what is new, and why the change is the right
shape for it.

## How it was checked

- [ ] `dotnet build Distillate.sln -c Release` (warnings are errors)
- [ ] `dotnet format Distillate.sln --verify-no-changes`
- [ ] `dotnet test Distillate.sln -c Release`
- [ ] Built the image and ran it with **no environment variables**
- [ ] Made a real `POST /process` request against a page and read the output

Tick only what you actually ran. An unticked box is a question, not a failure.

## Does the documentation still tell the truth?

- [ ] `README.md` configuration table matches the defaults in the code
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]`
- [ ] `SPEC.md` updated if the API changed

## Anything the reviewer should not have to guess

Known limitations of this change, a case that is deliberately not handled, or a
dependency that had to move.
