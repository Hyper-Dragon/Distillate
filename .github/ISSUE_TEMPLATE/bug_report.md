---
name: Bug report
about: Something extracted wrongly, or the service did not do what you asked
title: ''
labels: bug
assignees: ''
---

**Version and build**

`curl -s http://localhost:8080/health` prints both. Paste it:

```json
{ "status": "healthy", "version": "...", "build": "..." }
```

If `/health` does not answer, say so. That is a different bug from a wrong answer.

**How it is running**

- [ ] Published image (`ghcr.io/hyper-dragon/distillate`)
- [ ] Built from source with `docker build`
- [ ] `dotnet run` or `dotnet test`
- [ ] Something else:

**What happened, and what you expected**

"The markdown contains the cookie banner" is something I can act on. "Doesn't
work" is not.

**The request**

```json
{ "url": "https://example.com/article", "...": "the full body you sent" }
```

If the page is not public, attach the HTML instead of describing it.

**Which output is wrong**

- [ ] Title, byline or other metadata
- [ ] The article body (missing content, or content that is not the article)
- [ ] Images (missing, wrong names, junk images kept)
- [ ] Links or assets
- [ ] Markdown formatting
- [ ] Plain text
- [ ] The HTTP response (status, headers, `completionFlags`)
- [ ] The live mode UI
- [ ] The container will not start
- [ ] Something else

**Logs**

`docker logs <container>` around the request. Include the `traceId` from the
response if you have it.

**Every time, or sometimes?**

A failure that needs a particular page, a particular concurrency or a cold start
is a different bug from one that happens on every request.

**Anything else**

Raspberry Pi, a proxy, a page behind a login, settings you changed.
