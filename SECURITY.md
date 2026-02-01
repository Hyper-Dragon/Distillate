# Security policy

## Reporting a vulnerability

Use [GitHub's private vulnerability reporting](https://github.com/Hyper-Dragon/Distillate/security/advisories/new).
Please do not open a public issue for something exploitable.

Include the version and build from `GET /health`, the request you sent, and what
came back. If a container reproduces it, give the image tag.

One maintainer reads these. Expect an acknowledgement within a week. How long a
fix takes depends on what it is.

## Supported versions

The published image tags. Fixes go to `latest` and to the most recent minor
version. Older tags are not patched.

## In scope

- Escaping the container, or running code in it, through a crafted URL or a
  crafted page.
- A parser or converter defect that lets a fetched page run script in the
  service's own context rather than being extracted as text.
- Reading files from the container that a request should not reach.
- One request interfering with another that is in flight.

## Out of scope

These are how the service is meant to work:

1. **No authentication.** Every endpoint is open to anyone who can reach the port.
   The documented deployment publishes it on `127.0.0.1` for that reason.
2. **It fetches any URL it is given.** `POST /process` will request an internal
   address, a cloud metadata endpoint or a device on your LAN. That is what a URL
   fetcher does. If someone who should not have that reach can also reach this
   service, the exposure is the network, not the fetch.
3. **Live mode serves fetched pages from your origin.** `MODE=live` writes the
   extracted article into the page it returns. That content comes from the
   internet. It is meant for one person on loopback.
4. **Chromium runs without its sandbox, and the container runs as root.** The
   service passes `--no-sandbox` and the container is the boundary. A page that
   escapes Chromium has not escaped the container.
5. **What you are allowed to fetch is your problem.** Robots.txt, terms of service
   and copyright are not security questions and this project cannot answer them.

## Dependencies

Dependabot watches NuGet packages, GitHub Actions and the container base image.
NuGet advisories fail the build rather than appearing as a warning: package audit
warnings are errors via `TreatWarningsAsErrors`.
