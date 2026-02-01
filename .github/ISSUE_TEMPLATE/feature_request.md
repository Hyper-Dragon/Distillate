---
name: Feature request
about: Something the service should do, or should do differently
title: ''
labels: enhancement
assignees: ''
---

**What are you trying to do that you cannot do today?**

Describe the job, not the implementation. "Archive a site that requires a login"
is a job; "add a `login` field" is one possible answer to it.

**What happens now**

The workaround you are using, if any, and where it stops working.

**Would this change the API?**

The request fields and the response shape are documented in `SPEC.md`. A change
that adds a field is easy; one that renames or removes one breaks every existing
caller, and needs to be worth that.

**Have you checked Known Issues?**

The README lists what the service deliberately does not do — no internal queue,
no authentication, one browser at a time. If this request is one of those, say
why it should stop being one.
