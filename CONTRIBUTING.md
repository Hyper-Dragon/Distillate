# Contributing

Thanks for looking. This is a small project with one maintainer, so the fastest
route for a change is one that is easy to check.

## Getting it running

Requires the .NET 10 SDK. `global.json` pins the minimum and rolls forward within
10.0.

```bash
git clone https://github.com/Hyper-Dragon/Distillate.git
cd Distillate
dotnet build Distillate.sln -c Release
dotnet run --project src/Distillate.Web
```

`dotnet run` needs a browser. On Linux that is usually `chromium` at
`/usr/bin/chromium`; elsewhere leave `CHROMIUM_PATH` unset and the service uses the
Chromium that Playwright bundles, which you can install once with:

```bash
pwsh tests/Distillate.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

## Before you open a pull request

```bash
dotnet format Distillate.sln          # fix formatting
dotnet build Distillate.sln -c Release
dotnet test  Distillate.sln -c Release
```

The build treats warnings as errors, and CI additionally runs
`dotnet format --verify-no-changes`. A build failure is a real problem; a format
failure is a diff to apply.

## What a good change looks like

- **One concern per pull request.** A fix and a refactor in the same diff means
  neither can be reviewed independently.
- **A test that fails without the change.** The suite has 249 tests and a loopback
  static server for anything that needs a real page (`ExcludeImageUrlsTests` is the
  pattern). Tests must not depend on a third-party site being up, or on a page that
  will change next week.
- **Error messages name the fix.** "invalid config" is a support ticket;
  "MODE must be one of: rest, live — got 'full'" is a fix.
- **Documentation moves with the code.** If you add a configuration variable, it
  goes in `.env.example`, in the README table and in `docker-compose.example.yml` in
  the same pull request. If you change the API, `SPEC.md` changes with it.
- **`CHANGELOG.md` gets an entry** under `[Unreleased]`, in the category that fits.

## Things that will be pushed back on

- **A new dependency** where the base class library or an existing package would
  do. Every package is something a stranger has to audit.
- **A configuration option that changes a default.** Defaults are a contract; a new
  flag is cheap, a changed default is a silent behaviour change for every existing
  deployment.
- **Retry logic inside the service.** The refusal with `503` and `Retry-After` is
  deliberate: callers retry, the service does not queue. A pull request that adds a
  queue needs to argue against the design, not around it.
- **Bypassing the analysers.** `NoWarn` entries in `Directory.Build.props` each
  carry a written reason. An entry without one is not accepted.

## Commit messages

The repository keeps a short history: the initial commit, then one commit per
release or per substantial change. Write the message for someone reading the
history in a year — what changed and why, not which files moved.

## Reporting something you cannot fix

Bug reports are welcome and the template asks for the version from `GET /health`
for a reason: with it, the build is known. Without it, the first round trip is
always "which build is this?".

Security issues go through [SECURITY.md](SECURITY.md), not the issue tracker.
