# Building

## From source

You need the .NET 10 SDK. `global.json` pins the minimum and rolls forward within
10.0, so any 10.0.x SDK works.

```bash
dotnet build Distillate.sln -c Release
dotnet test  Distillate.sln -c Release
dotnet run --project src/Distillate.Web
```

Warnings are errors. `dotnet format Distillate.sln --verify-no-changes` is the
formatting gate CI runs; `dotnet format Distillate.sln` fixes it.

### The browser

The service drives Chromium. There are two ways to provide one:

1. **A system browser.** Set `CHROMIUM_PATH`. The image sets `/usr/bin/chromium`.
2. **Playwright's browser.** Leave `CHROMIUM_PATH` unset, or point it at a path
   that does not exist, and the service uses the browser Playwright manages.

```bash
pwsh tests/Distillate.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

The second is what the tests use on a machine with no system browser, and what CI
uses.

## The container image

```bash
docker build -t distillate:local .
docker run --rm -p 127.0.0.1:8080:8080 distillate:local
curl -s http://127.0.0.1:8080/health
```

The image has its own `HEALTHCHECK`, which runs the application against itself. It
contains no `curl` and no `wget`, so a compose healthcheck that calls either will
report unhealthy forever:

```bash
docker run --rm --entrypoint dotnet distillate:local Distillate.Web.dll --healthcheck
```

### Multiple architectures

The Dockerfile cross-compiles the application; publishing a RID-specific build
does not execute it. The image itself should be built on a runner of its own
architecture, which is what the release workflow does: one native runner per
architecture, then a manifest list.

On a single machine:

```bash
docker buildx build --platform linux/amd64,linux/arm64 -t distillate:multi .
```

The second platform runs under QEMU: slow, and never executed before it is pushed.
Useful for a one-off check, not for a release.

## Build identity

The commit is compiled into the assembly. A local build reports `unknown`; the
image build passes it in:

```bash
docker build --build-arg DISTILLATE_BUILD=$(git rev-parse --short HEAD) -t distillate:local .
```

`GET /health` returns it as `build`, and the startup log line starts with it.

## Releasing

1. Move the `[Unreleased]` section of `CHANGELOG.md` into a new `## [x.y.z]` heading.
2. Set `Version` in `Directory.Build.props` to the same number.
3. Merge to `main`, then tag that commit `vx.y.z` and push the tag.

The `release` workflow publishes an image on every push to `main` (`latest` and
`edge`) and a versioned image plus a release page on every `v*` tag. The release
page body is the changelog section for that version, so the notes are written once.

`linux/arm64` is built on GitHub's arm64 runners, which are free for public
repositories and billed for private ones. While this repository is private, the
workflow publishes `linux/amd64` only. Re-running it on the same tag after the
repository becomes public adds `linux/arm64` to the same manifest list.
