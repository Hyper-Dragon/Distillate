# Distillate — Test URL List

A reference list of URLs used for manual quality testing of the Distillate extraction pipeline.

Use these with the `POST /process` endpoint (see [DistillateUsage.md](DistillateUsage.md)) to verify reader-view output quality, markdown cleanliness, and plain-text fidelity.

---

## Quick Test Command

Replace `<HOST_PORT>` and `<URL>` as appropriate:

```bash
curl -s -X POST http://localhost:<HOST_PORT>/process \
  -H "Content-Type: application/json" \
  -d '{
    "url": "<URL>",
    "includeMarkdownNoImages": true,
    "includePlainText": true
  }' | jq '{success, title, processingTimeMs, completionFlags, errors, markdownNoImages}'
```

---

## Test URLs

### News & Investigative Journalism

| URL | Notes |
|-----|-------|
| `https://theintercept.com/2026/03/09/israel-gaza-iran-war-transportation/` | Long-form investigative article with subscription prompts |
| `https://www.bellingcat.com/news/middle-east/2026/03/04/satellite-imagery-reveals-strikes-on-iranian-police-stations/` | Article with satellite imagery captions; expect image stripping |
| `https://www.polygon.com/steam-valve-gdc-2026-sales-growth-new-game-release-data/` | Gaming news with data/charts; expect clean extraction |

### Organisations & Blogs

| URL | Notes |
|-----|-------|
| `https://www.compassonline.org.uk/britannia-rule-ourselves/?utm_source=feedly&utm_medium=rss&utm_campaign=britannia-rule-ourselves` | Blog post with UTM tracking params; expect clean body |
| `https://www.openrightsgroup.org/blog/to-consent-or-pay/` | Policy/rights blog post |

### Reference / Encyclopedia

| URL | Notes |
|-----|-------|
| `https://en.wikipedia.org/wiki/Network_switch` | Wikipedia article; infoboxes and navboxes should be stripped |

---

## Previously Tested URLs

These URLs have been used in earlier quality-checking sessions:

| URL | Notes |
|-----|-------|
| `https://example.com` | Basic smoke test; minimal content |

---

## What to Check

For each URL, verify:

1. **`success`** is `true`
2. **`title`** is extracted correctly
3. **`markdownNoImages`** contains the article body, with:
   - No newsletter banners or subscription prompts
   - No social share blocks
   - No navigation menus or related-story sidebars
   - No tag clouds or author bios (unless editorially relevant)
   - Clean headings and paragraph structure
4. **`plainText`** is readable and well-separated by paragraphs
5. **`completionFlags`** is `2` (only `SourceHtmlOmitted` set, as expected for default requests)
6. **`errors`** is `null`

---

## Stability Comparison

To compare with a slower stabilization delay (for JS-heavy pages):

```bash
curl -s -X POST http://localhost:<HOST_PORT>/process \
  -H "Content-Type: application/json" \
  -d '{
    "url": "<URL>",
    "stabilizationMs": 20000,
    "includeMarkdownNoImages": true,
    "includePlainText": true
  }' | jq '{success, title, processingTimeMs, completionFlags}'
```
