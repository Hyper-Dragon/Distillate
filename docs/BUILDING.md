# Building

## From source

Requires the .NET 10 SDK. `global.json` pins the minimum and rolls forward within
10.0, so any 10.0.x SDK works.

```bash
dotnet build Distillate.sln -c Release
dotnet test  Distillate.sln -c Release
dotnet run --project src/Distillate.Web
```

Warnings are errors. `dotnet format Distillate.sln --verify-no-changes` is the
formatting gate CI runs; `dotnet format Distillate.sln` fixes it locally.

### The browser

The service drives a real Chromium. Two ways to give it one:

1. **System Chromium** — set `CHROMIUM_PATH` (the image sets `/usr/bin/chromium`).
2. **Playwright's bundled build** — leave `CHROMIUM_PATH` unset or point it at a
   path that does not exist, and the service falls back to the browser Playwright
   manages:

```bash
pwsh tests/Distillate.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

The second path is what the test suite uses on a machine with no system browser,
and what CI uses.

## The container image

```bash
docker build -t distillate:local .
docker run --rm -p 127.0.0.1:8080:8080 distillate:local
curl -s http://127.0.0.1:8080/health
```

The image carries its own `HEALTHCHECK`, which is the application checking itself.
There is no `curl` and no `wget` inside it, so a compose healthcheck that shells out
to either will always report unhealthy:

```bash
docker run --rm --entrypoint dotnet distillate:local Distillate.Web.dll --healthcheck
```

### Multi-architecture

`Dockerfile` cross-compiles the .NET application (build platform ≠ target platform
is fine: publishing a RID-specific app does not execute it), but the *image* is
meant to be built on a runner of its own architecture. The release workflow does
exactly that — one native runner per architecture, then a manifest list.

Ad-hoc, on one machine:

```bash
docker buildx build --platform linux/amd64,linux/arm64 -t distillate:multi .
```

That second platform is emulated under QEMU: slow, and the arm64 half is never
executed before it is pushed. Fine for a one-off check, not for a release.

## What the build produces

```bash
docker run --rm --entrypoint dotnet distillate:local Distillate.Web.dll --help
```

Not a real flag — the point is that `dotnet Distillate.Web.dll --healthcheck` is the
probe, and that the image needs nothing from the OS to run it.

The build commit is compiled in. Locally it reads `unknown`; the image build passes
`--build-arg DISTILLATE_BUILD=<sha>` and the value shows up in `/health` and in the
first line of the log:

```bash
docker build --build-arg DISTILLATE_BUILD=$(git rev-parse --short HEAD) -t distillate:local .
docker run --rm --entrypoint dotnet distillate:local Distillate.Web.dll --healthcheck
```

## Releasing

1. Move the `[Unreleased]` section of `CHANGELOG.md` into a new `## [x.y.z]` heading.
2. Set `Version` in `Directory.Build.props` to the same `x.y.z`.
3. Merge to `main`, then tag that commit `vx.y.z` and push the tag.

The `release` workflow publishes the image on every push to `main` (tags `latest`
and `edge`) and publishes a stable version plus a release page on every `v*` tag.
The release page body is the changelog section for that version, so the notes are
written once.

`linux/arm64` is built on GitHub's native arm64 runners, which are free for public
repositories and billable for private ones. While this repository is private the
workflow publishes `linux/amd64` only; re-running the release workflow on the same
tag after the repository becomes public adds `linux/arm64` to the same manifest
list, with the same tag.
