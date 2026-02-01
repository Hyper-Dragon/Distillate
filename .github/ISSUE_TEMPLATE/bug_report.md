---
name: Bug report
about: Something extracted wrongly, or the service did not do what you asked
title: ''
labels: bug
assignees: ''
---

**Which build is this?**

`curl -s http://localhost:8080/health` prints the version and the commit it was
built from. Paste it:

```json
{
  "status": "healthy",
  "version": "...",
  "build": "..."
}
```

If `/health` does not answer, say so — that is a different bug from a wrong answer,
and it is the more serious one.

**How is it running?**

- [ ] The published image (`ghcr.io/hyper-dragon/distillate`)
- [ ] Built from source with `docker build`
- [ ] `dotnet run` / `dotnet test`
- [ ] Something else:

**What happened, and what did you expect instead**

Actual and expected behaviour. "The markdown contains the cookie banner" is
useful. "Doesn't work" has nothing to reproduce.

**The request that produced it**

```json
{ "url": "https://example.com/article", "...": "the full body you sent" }
```

If the page is not public, a saved copy of the HTML attached to the issue is
better than a description of it.

**Which part of the output is wrong?**

- [ ] Title, byline or other metadata
- [ ] The article body (missing content, or content that is not part of the article)
- [ ] Images (missing, wrong names, or junk images kept)
- [ ] Links / assets
- [ ] Markdown formatting
- [ ] Plain text
- [ ] The HTTP response itself (status code, headers, `completionFlags`)
- [ ] The live mode web UI
- [ ] The container will not start
- [ ] Something else

**Logs**

`docker logs <container>` around the request, with the trace id from the response
if you have it.

**Does it happen every time?**

Say so plainly. A failure that needs a specific page, a specific concurrency or a
cold start is a different bug from one that happens on every request, even when
the symptom looks the same.

**Anything else**

Raspberry Pi? A proxy? A page behind a login? `MAX_TIME_MS` raised? This is where
that goes.
