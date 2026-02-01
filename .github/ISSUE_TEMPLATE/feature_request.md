---
name: Feature request
about: Something the service should do, or should do differently
title: ''
labels: enhancement
assignees: ''
---

**What are you trying to do?**

Describe the job rather than the implementation. "Archive a site that needs a
login" is a job. "Add a `login` field" is one possible answer to it.

**What happens now**

The workaround you use, if there is one, and where it stops working.

**Does it change the API?**

Request and response fields are documented in `docs/SPEC.md`. Adding a field is
straightforward. Renaming or removing one breaks every existing caller.

**Is it in Known issues?**

The README lists what the service does not do: no queue, no authentication, one
browser at a time. If this request is one of those, say why it should change.
