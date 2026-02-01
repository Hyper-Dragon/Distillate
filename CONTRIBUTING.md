# Contributing

## Build and run

You need the .NET 10 SDK. `global.json` pins the minimum and rolls forward within
10.0.

```bash
git clone https://github.com/Hyper-Dragon/Distillate.git
cd Distillate
dotnet build Distillate.sln -c Release
dotnet run --project src/Distillate.Web
```

`dotnet run` needs a browser. On Linux that is usually `chromium` at
`/usr/bin/chromium`. If it is somewhere else, or missing, leave `CHROMIUM_PATH`
unset and the service uses the browser Playwright manages:

```bash
pwsh tests/Distillate.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

## Before you open a pull request

```bash
dotnet format Distillate.sln
dotnet build Distillate.sln -c Release
dotnet test  Distillate.sln -c Release
```

The build treats warnings as errors, and CI also runs
`dotnet format --verify-no-changes`. A build failure is a real problem; a format
failure is a diff to apply.

## What a change should look like

- **One thing per pull request.** A fix and a refactor in the same diff are hard to
  review on their own and impossible together.
- **A test that fails without the change.** There are 249 tests and a loopback
  static server for anything that needs a real page — `ExcludeImageUrlsTests` is
  the pattern to copy. Tests must not depend on a third-party site or on a fixture
  that will change next week.
- **Error messages that say what to fix.** `MODE must be one of: rest, live — got
  'full'` is useful. `invalid config` is a support ticket.
- **Documentation in the same pull request.** A new variable goes in `.env.example`,
  the README table and `docker-compose.example.yml`. An API change goes in
  `docs/SPEC.md`. Either way, add a line to `CHANGELOG.md` under `[Unreleased]`.

## What will get pushed back on

- **New dependencies** where the base class library or an existing package would
  do. Every package is something a stranger has to audit.
- **Changing a default.** Add a variable if you need one; changing what an existing
  variable does is a silent behaviour change for every deployment.
- **Retry or queue logic inside the service.** It answers `503` with `Retry-After`
  and lets the caller decide. A pull request that adds a queue needs to argue
  against that design, not around it.
- **Adding to `NoWarn` without a reason.** Every entry in
  `Directory.Build.props` says why it is there. Yours has to as well.

## Commit messages

The history is short: an initial commit, then one per release or substantial
change. Write the message for someone reading it in a year. Say what changed and
why; the file list is already in the diff.

## Reporting a bug

The template asks for the version from `GET /health` because it names the exact
build. Without it, the first reply is always "which build is this?".

Security problems go through [SECURITY.md](SECURITY.md), not the issue tracker.
