# Changelog

All notable changes to this project. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- **Logging should move to source-generated `LoggerMessage` delegates** (CA1848,
  CA1873). The analysers flag it and the rules are suppressed in
  `Directory.Build.props` with a written reason rather than fixed here: it is a
  sweep across every call site in `DistillateService` and `ImageProcessor`, and a
  poor thing to combine with a first release.

## [0.1.0] — 2026-11-04

The first public release. Not a rewrite: the service has been running as a
private tool, and this is the point at which it became something a stranger could
build, run and report against.

### Added

- **`POST /process`**, the reason the project exists: a URL in, reader-view
  content out. Structured metadata (title, byline, site name, language, reading
  time), plain text, and markdown with four different image policies — embedded as
  data URIs, linked to local filenames, stripped, or left in place.
- **Honest backpressure.** No internal queue: a request that arrives at capacity is
  refused immediately with `503`, a `Retry-After` header and a body naming the
  reason, so a caller can tell "busy, come back" from "unreachable, do something
  else".
- **Live mode** (`MODE=live`) — a browser-based UI with a streaming request log and
  a curl builder covering every request field, for the times a URL is easier to
  paste than to script.
- **`GET /health`** reporting version, build commit, mode and current capacity.
- **`--healthcheck`**, so the image can report its own health with no shell, no
  `curl` and no `wget` in it.
- **`examples/distil.sh`** — a worked client that writes markdown, text, images and
  links out to separate files.

### Fixed in this release

- **The published image had no health probe at all.** The compose files probed with
  `curl` and `wget`, neither of which the runtime image installed; a second,
  test-only Dockerfile installed `curl`, which is why it appeared to work. The
  probe is now the application checking itself.
- **The container started a virtual X server for a browser that runs headless.**
  `DistillateService` launches Chromium with `Headless = true`, so `Xvfb` was a
  process nobody waited for, started by a `sh` that sat between PID 1 and the signal
  handler. `dotnet` is now PID 1's direct child under `tini`.
- **`Retry-After` and reading time were formatted with the server's locale.** Both
  are API surface: the header must be an invariant integer and `timeToRead` is
  documented as `hh:mm:ss`. Both are now invariant.
- **An unknown `MODE` silently ran the REST API.** `MODE=Livee` was accepted and
  ignored. Unknown values now stop the process and name the accepted ones; so does
  an unknown `LOG_LEVEL`, which previously died inside `Enum.Parse`.

### Security

- **AngleSharp 1.4.0 → 1.5.0**, the first version patched for
  [GHSA-pgww-w46g-26qg](https://github.com/advisories/GHSA-pgww-w46g-26qg)
  (CVE-2026-54570), an mXSS in HTML serialisation. This service parses untrusted
  HTML, so the parser being right matters more here than in most projects.
- NuGet advisories now fail the build (`TreatWarningsAsErrors` turns NU1902 into an
  error) rather than appearing as a warning in a log nobody reads.

### Changed

- **One version, one file.** The version lives in `Directory.Build.props`; the
  assembly, the startup log, `/health`, `/` and the image all read it. The previous
  hardcoded `"v1.1"` string in the root endpoint is gone.
- **The build commit is compiled into the binary** (`DISTILLATE_BUILD` build
  argument), so a bug report can name the commit that produced the binary.
- **One compose file** — `docker-compose.example.yml` — replacing four that each
  duplicated the whole service block. It publishes the port on `127.0.0.1` by
  default and sets `stop_grace_period: 30s`, because an extraction may legitimately
  run for `MAX_TIME_MS`.
- **One solution file.** Two solution formats in one repository made `dotnet list`
  and friends refuse to run.
- Configuration is validated at startup and every variable is documented in
  `.env.example` and in the README table.

### Known limitations

These are properties of the design, not open bugs. See the README's
[Known Issues](README.md#known-issues) and [SECURITY.md](SECURITY.md).

- No authentication, and it fetches any URL it is given. Bind it to loopback or put
  an authenticated proxy in front of it.
- One extraction at a time by default. That is the point; raise `CONCURRENCY` if the
  machine can take it.
- Live mode writes the fetched article into the page it returns. Untrusted HTML, by
  design; not for shared, internet-facing use.
- The `arm64` image is built on native arm64 runners, which are free only for public
  repositories. Until this repository is public, releases publish `linux/amd64`;
  re-running the release workflow on the same tag afterwards adds `linux/arm64` to
  the same manifest.
