# Distillate Command Line Script

> *NOTE* Example script for demonstration purposes. Adjust as needed for your environment and use case.

This command line script is a bash script that interacts with the Distillate API to process a given article URL. It sends a POST request with specific parameters, handles the response, and organizes the output into a structured directory. The script also includes error handling and checks for required dependencies.

```bash
#!/usr/bin/env bash
set -u
# Exit on error only where we explicitly want to; capture failures from curl separately

DEBUG_FLAG=false
URL=""

for arg in "$@"; do
  case "$arg" in
    --debug) DEBUG_FLAG=true ;;
    *)       URL="$arg" ;;
  esac
done

if [ -z "$URL" ]; then
  echo "Usage: $0 [--debug] <article-url>"
  exit 2
fi

API="http://localhost:8888/process"

# Dependencies check
for cmd in curl jq base64 sed tr awk mkdir date; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $cmd"
    exit 3
  fi
done

# Build payload (exact flags from your example)
read -r -d '' PAYLOAD <<JSON || true
{
  "url": "${URL}",
  "includeSourceHtml": false,
  "includeCleanHtmlEmbeddedImages": true,
  "includeCleanHtmlLinkedImages": true,
  "includeCleanHtmlNoImages": true,
  "includeMarkdownLinkedImages": true,
  "includeMarkdownNoImages": true,
  "includePlainText": true,
  "includeAllLinksInPage": true,
  "includeAllLinksMedia": true,
  "includeAllLinksAssets": true,
  "includeAllInPageImages": true,
  "includeAllOutOfPageImages": true,
  "imageLinkRewrite": true,
  "pngConversionRequired": false,
  "includeDebugInfo": ${DEBUG_FLAG}
}
JSON

tmp_resp="$(mktemp --suffix=.json)"
http_code=$(curl -s --max-time 300 -w "%{http_code}" -o "$tmp_resp" -X POST "$API" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD")

if [[ "$http_code" -lt 200 || "$http_code" -ge 300 ]]; then
  echo "ERROR: POST failed with HTTP $http_code"
  echo "Response saved to $tmp_resp"
  cat "$tmp_resp"
  exit 4
fi

# Try to obtain a title for slug; if none, fall back to host + timestamp
title=$(jq -r '.title // empty' "$tmp_resp")
if [ -n "$title" ]; then
  # slugify: ascii, lower, non-alnum -> -, trim hyphens
  slug=$(printf '%s' "$title" | iconv -f utf-8 -t ascii//TRANSLIT 2>/dev/null || cat) \
    | tr '[:upper:]' '[:lower:]' \
    | sed -E 's/[^a-z0-9]+/-/g' \
    | sed -E 's/^-+|-+$//g'
fi

if [ -z "${slug:-}" ]; then
  host=$(echo "$URL" | awk -F/ '{print $3}' | sed -E 's/[:]+/-/g')
  ts=$(date -u +%Y%m%dT%H%M%SZ)
  slug="${host:-article}-$ts"
fi

dir="./${slug}"
mkdir -p "$dir"

# Move response JSON into dir for reference
mv "$tmp_resp" "$dir/response.json"
resp="$dir/response.json"

# Fields to extract into metadata file (best-effort)
jq -r '{
  requestedUrl, processedAt, processingTimeMs,
  title, byline, author, excerpt, siteName, language, publicationDate,
  featuredImage, timeToRead, completionFlags, errors, traceId
}' "$resp" > "$dir/metadata.json"

# Text/html/markdown artifacts -> files if present
declare -A outputs=(
  ["plainText"]="$dir/plain_text.txt"
  ["sourceHtml"]="$dir/source.html"
  ["cleanHtmlEmbeddedImages"]="$dir/clean_html_embedded_images.html"
  ["cleanHtmlLinkedImages"]="$dir/clean_html_linked_images.html"
  ["cleanHtmlNoImages"]="$dir/clean_html_no_images.html"
  ["markdownLinkedImages"]="$dir/article.md"
  ["markdownNoImages"]="$dir/article_no_images.md"
)

for key in "${!outputs[@]}"; do
  out=${outputs[$key]}
  if jq -e --arg k "$key" 'has($k) and .[$k] != null' "$resp" >/dev/null; then
    jq -r --arg k "$key" '.[$k] // ""' "$resp" > "$out"
    echo "WROTE: $out"
  else
    echo "ABSENT: $key" >> "$dir/missing_fields.txt"
  fi
done

# Links arrays
for arr in allLinksInPage allLinksMedia allLinksAssets; do
  out="$dir/${arr}.txt"
  if jq -e --arg a "$arr" 'has($a) and .[$a] != null' "$resp" >/dev/null; then
    jq -r --arg a "$arr" '.[$a][]' "$resp" 2>/dev/null > "$out" || true
    echo "WROTE: $out"
  else
    echo "ABSENT: $arr" >> "$dir/missing_fields.txt"
  fi
done

# Featured image: if present attempt to download (best-effort)
featured_url=$(jq -r '.featuredImage // empty' "$resp")
if [ -n "$featured_url" ]; then
  # derive ext from URL if possible
  ext=$(basename "${featured_url}" | sed -n 's/.*\.\([a-zA-Z0-9]\{1,6\}\)$/\1/p' || true)
  if [ -z "$ext" ]; then ext="img"; fi
  feat_path="$dir/featured_image.${ext}"
  if curl -s -L -f "$featured_url" -o "$feat_path"; then
    echo "WROTE: $feat_path"
  else
    echo "WARNING: could not download featured image from $featured_url" >> "$dir/missing_fields.txt"
    # still save the URL for reference
    echo "$featured_url" > "$dir/featured_image.url"
  fi
else
  echo "ABSENT: featuredImage" >> "$dir/missing_fields.txt"
fi

# Helper: extract and write ImageEntry arrays (allInPageImages, allOutOfPageImages)
process_image_array() {
  local arr="$1"
  local manifest="$dir/${arr}_manifest.csv"
  # header
  printf 'index,localName,originalUrl,originalMimeType,alt,sizeBytes,written\n' > "$manifest"
  if jq -e --arg a "$arr" 'has($a) and .[$a] != null' "$resp" >/dev/null; then
    # iterate entries; decode base64 data if present
    jq -c --arg a "$arr" '.[$a][]?' "$resp" | while read -r obj; do
      [ -z "$obj" ] && continue
      localName=$(printf '%s' "$obj" | jq -r '.localName // "image-unknown"')
      originalUrl=$(printf '%s' "$obj" | jq -r '.originalUrl // empty')
      originalMimeType=$(printf '%s' "$obj" | jq -r '.originalMimeType // empty')
      alt=$(printf '%s' "$obj" | jq -r '.alt // empty' | sed 's/,/ /g')
      sizeBytes=$(printf '%s' "$obj" | jq -r '.sizeBytes // empty')
      data=$(printf '%s' "$obj" | jq -r '.data // empty')
      written="no"
      if [ -n "$data" ]; then
        # ensure directory exists
        # protect against directory traversal by removing path components from localName
        safe_localName=$(basename "$localName")
        outpath="$dir/$safe_localName"
        # base64 decode; tolerate newlines
        printf '%s' "$data" | base64 --decode > "$outpath" 2>/dev/null && written="yes" || written="no"
      else
        written="no-data"
      fi
      printf '%s,%s,%s,%s,%s,%s,%s\n' \
        "$(printf '%s' "$obj" | jq -r '.index // empty')" \
        "$localName" "$originalUrl" "$originalMimeType" "$alt" "$sizeBytes" "$written" >> "$manifest"
    done
    echo "WROTE: $manifest"
  else
    echo "ABSENT: $arr" >> "$dir/missing_fields.txt"
  fi
}

process_image_array "allInPageImages"
process_image_array "allOutOfPageImages"

# Debug info: write to file if present
if jq -e '.debugInfo != null' "$resp" >/dev/null 2>&1; then
  jq '.debugInfo' "$resp" > "$dir/debug_info.json"
  echo "WROTE: $dir/debug_info.json"
fi

# Build a concise missing-fields report (unique)
if [ -f "$dir/missing_fields.txt" ]; then
  sort -u "$dir/missing_fields.txt" -o "$dir/missing_fields.txt"
  echo
  echo "Missing / absent fields (best-effort):"
  sed -n '1,200p' "$dir/missing_fields.txt" || true
else
  echo
  echo "No missing fields detected (according to returned JSON keys)."
fi

# Final summary file
cat > "$dir/summary.txt" <<EOF
Saved response JSON: response.json
Metadata: metadata.json
Text/HTML/Markdown artifacts: $(for f in "${outputs[@]}"; do printf '%s ' "$f"; done)
Links: allLinksInPage.txt allLinksMedia.txt allLinksAssets.txt
Image manifests and binaries: ${dir}/allInPageImages_manifest.csv ${dir}/allOutOfPageImages_manifest.csv
Featured image: ${feat_path:-none}
Missing fields file: ${dir}/missing_fields.txt
EOF

echo
echo "DONE. Article directory: $dir"
```
