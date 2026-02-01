# Security policy

## Reporting a vulnerability

Report privately, through GitHub's [private vulnerability reporting](https://github.com/Hyper-Dragon/Distillate/security/advisories/new).
Please do not open a public issue for something exploitable — a public issue is a
working exploit with an audience.

Include the version and build from `GET /health`, the request you sent and what
you got back. If you have a container that reproduces it, say which image tag.

There is one maintainer. Expect an acknowledgement within a week; a fix depends
on what it is.

## Supported versions

The published image tags. Fixes go to `latest` and to the most recent minor
version. Older tags are not patched — pull a newer one.

## What is in scope

- Escaping the container, or gaining code execution in it, through a crafted URL
  or a crafted page.
- A defect in the parser or converter chain that lets a fetched page execute
  script in the *service's* context, rather than being extracted as text.
- Reading files from the container filesystem that the request should not reach.
- Anything that makes `POST /process` interfere with another in-flight request.

## What is not in scope, by design

These are properties of the service, stated here so nobody spends an afternoon
writing them up:

1. **There is no authentication.** Every endpoint is open to whoever can reach the
   port. The documented deployment publishes the port on `127.0.0.1` for that
   reason. If you expose it to a network, you own what that network can do with it.
2. **It fetches whatever URL it is given.** `POST /process` will request an
   internal address, a metadata endpoint or a device on your LAN, because that is
   what a URL fetcher does. It is only an SSRF *finding* if this service is
   reachable by someone who should not have that reach; on a network where they
   already have it, this changes nothing. Treat the port as a capability.
3. **Live mode renders pages it fetched from the internet.** `MODE=live` writes the
   extracted article into the page it returns. That content is untrusted HTML by
   definition; the mode exists to look at it in a browser. Do not run live mode as
   a shared, internet-facing service.
4. **The browser is not hardened against the pages it visits.** Chromium runs
   inside the container with its sandbox disabled, because it runs as root and
   container runtimes do not reliably provide user namespaces. The container is the
   boundary; a page that escapes Chromium's own sandbox has escaped nothing else
   unless the container itself is broken.
5. **Robots, terms of service and copyright are the operator's problem.** The
   service will fetch whatever it is pointed at. Whether you were allowed to fetch
   it is not a security question and not one this project can answer.

## Dependencies

NuGet and container base images are watched by Dependabot. `dotnet list package
--vulnerable --include-transitive` is part of the release checklist, and
`TreatWarningsAsErrors` means a package advisory reported by NuGet (NU1902) fails
the build rather than scrolling past in a log.
