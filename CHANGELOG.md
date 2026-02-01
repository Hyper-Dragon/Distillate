# Changelog

Notable changes. The format follows [Keep a Changelog](https://keepachangelog.com/)
and the version numbers follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Removed

- **`DistillateOptions.LogLevel`.** It was set at startup and never read; the
  logger is configured from the parsed `LOG_LEVEL` directly. Removed with its test
  assertion.

### Changed

- **Logging calls should use source-generated `LoggerMessage` delegates** (CA1848,
  CA1873). Both rules are suppressed in `Directory.Build.props` with a reason
  instead of being fixed here, because it touches every call site in
  `DistillateService` and `ImageProcessor`.

## [0.1.0] — 2026-11-04

First public release. The service was running privately before this; the work here
is what made it something another person can build, run and report against.

### Added

- **`POST /process`** — send a URL, get reader-view content back. Metadata, plain
  text, and markdown with four image policies: embedded as data URIs, linked to
  local filenames, stripped, or untouched.
- **Capacity limits at request time.** A request that arrives while the service is
  busy gets `503` with a `Retry-After` header and a body explaining why. Nothing is
  queued.
- **Live mode** (`MODE=live`) — a browser UI with a streaming log and a curl
  builder covering every request field.
- **`GET /health`** — status, version, build commit, mode and free capacity.
- **`--healthcheck`** — the application checks itself, so the image needs no shell
  or `curl` to report health.
- **`examples/distil.sh`** — a client script that writes markdown, text, images and
  links to separate files.

### Fixed

- **The published image had no working health probe.** The compose files probed
  with `curl` and `wget`, and the runtime image had neither. It looked fine because
  a second, test-only Dockerfile installed `curl`. The probe is now part of the
  application.
- **The container started a virtual X server for a headless browser.**
  `DistillateService` launches Chromium with `Headless = true`, so `Xvfb` was an
  unused process, started by a `sh` that sat between PID 1 and the signal handler.
  `dotnet` now runs as the direct child of `tini`.
- **`Retry-After` and reading time used the server's locale.** Both are part of the
  API: the header has to be an invariant integer and `timeToRead` is documented as
  `hh:mm:ss`. Both are invariant now.
- **An unknown `MODE` ran the REST API anyway.** `MODE=Livee` was accepted and
  ignored. It now stops the process and names the accepted values, as does an
  unknown `LOG_LEVEL`, which previously failed inside `Enum.Parse`.

### Security

- **AngleSharp 1.4.0 → 1.5.0**, the first version without
  [GHSA-pgww-w46g-26qg](https://github.com/advisories/GHSA-pgww-w46g-26qg)
  (CVE-2026-54570), an mXSS in HTML serialisation. This service parses untrusted
  HTML, so the parser matters more here than in most projects.
- **NuGet advisories now fail the build.** `TreatWarningsAsErrors` turns NU1902
  into an error instead of a warning in a log.

### Changed

- **The version lives in one file.** `Directory.Build.props` holds it; the
  assembly, the startup log, `/health`, `/` and the image all read it. The
  hardcoded `"v1.1"` in the root endpoint is gone.
- **The build commit is compiled into the binary** via the `DISTILLATE_BUILD`
  build argument, so a bug report can name the commit that produced it.
- **One compose file.** `docker-compose.example.yml` replaces four that each
  duplicated the whole service block. It publishes the port on `127.0.0.1` and sets
  `stop_grace_period: 30s`, because an extraction can run for `MAX_TIME_MS`.
- **One solution file.** Two solution formats in one repository made `dotnet list`
  and similar commands refuse to run.
- **Configuration is validated at startup**, and every variable is documented in
  `.env.example` and in the README table.

### Known limitations

These are design decisions and unresolved limits, not open bugs. See the README's
[Known issues](README.md#known-issues) and [SECURITY.md](SECURITY.md).

- No authentication, and it fetches any URL it is given. Bind it to loopback or put
  an authenticated proxy in front.
- One extraction at a time by default. That is the intended behaviour; raise
  `CONCURRENCY` if the machine can afford it.
- Live mode puts fetched HTML into a page on your origin. Do not expose it to the
  internet.
- The `arm64` image is built on native arm64 runners, which are free only for
  public repositories. Until this repository is public, releases publish
  `linux/amd64`; re-running the release workflow on the same tag afterwards adds
  `linux/arm64` to the same manifest.
