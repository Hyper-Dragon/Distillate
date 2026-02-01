#!/usr/bin/env bash
set -u

DEBUG_FLAG=false
PNG_FLAG=false
URL=""

for arg in "$@"; do
  case "$arg" in
    --debug) DEBUG_FLAG=true ;;
    --png)   PNG_FLAG=true ;;
    *)       URL="$arg" ;;
  esac
done

if [ -z "$URL" ]; then
  echo "Usage: $0 [--debug] [--png] <article-url>"
  exit 2
fi

API="http://localhost:8080/process"

for cmd in curl jq base64 sed tr awk mkdir date; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $cmd"
    exit 3
  fi
done

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
  "pngConversionRequired": ${PNG_FLAG},
  "includeDebugInfo": ${DEBUG_FLAG}
}
JSON

tmp_resp="$(mktemp --suffix=.json)"
http_code=$(curl -s --max-time 300 -w "%{http_code}" -o "$tmp_resp" -X POST "$API" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD")

if [[ "$http_code" -lt 200 || "$http_code" -ge 300 ]]; then
  echo "ERROR: POST failed with HTTP $http_code"
  cat "$tmp_resp"
  exit 4
fi

title=$(jq -r '.title // empty' "$tmp_resp")
if [ -n "$title" ]; then
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

mv "$tmp_resp" "$dir/response.json"
resp="$dir/response.json"

jq -r '{
  requestedUrl, processedAt, processingTimeMs,
  title, byline, author, excerpt, siteName, language, publicationDate,
  featuredImage, timeToRead, completionFlags, errors, traceId
}' "$resp" > "$dir/metadata.json"

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

for arr in allLinksInPage allLinksMedia allLinksAssets; do
  out="$dir/${arr}.txt"
  if jq -e --arg a "$arr" 'has($a) and .[$a] != null' "$resp" >/dev/null; then
    jq -r --arg a "$arr" '.[$a][]' "$resp" 2>/dev/null > "$out" || true
    echo "WROTE: $out"
  else
    echo "ABSENT: $arr" >> "$dir/missing_fields.txt"
  fi
done

featured_url=$(jq -r '.featuredImage // empty' "$resp")
if [ -n "$featured_url" ]; then
  if [ "$PNG_FLAG" = "true" ]; then
    # When PNG conversion is requested the API already converts and returns the
    # featured image inside allInPageImages (as image-0000.png) whenever the
    # article has no other in-page images.  Check whether it is present there;
    # if so, skip the direct curl download to avoid a duplicate JPEG-format file
    # under a misleading name.
    featured_in_array=$(jq -r --arg u "$featured_url" \
      '(.allInPageImages // [])[] | select(.originalUrl == $u) | .localName' \
      "$resp" 2>/dev/null | head -1)
    if [ -n "$featured_in_array" ]; then
      echo "INFO: featured image already saved as $featured_in_array (PNG, from allInPageImages)"
    else
      # Not in allInPageImages (article had other in-page images); download the
      # original and note that it has not been converted to PNG.
      ext=$(basename "${featured_url}" | sed -n 's/.*\.\([a-zA-Z0-9]\{1,6\}\)$/\1/p' || true)
      if [ -z "$ext" ]; then ext="img"; fi
      feat_path="$dir/featured_image.${ext}"
      if curl -s -L -f "$featured_url" -o "$feat_path"; then
        echo "WROTE: $feat_path (original format -- not PNG-converted)"
      else
        echo "WARNING: could not download featured image" >> "$dir/missing_fields.txt"
        echo "$featured_url" > "$dir/featured_image.url"
      fi
    fi
  else
    ext=$(basename "${featured_url}" | sed -n 's/.*\.\([a-zA-Z0-9]\{1,6\}\)$/\1/p' || true)
    if [ -z "$ext" ]; then ext="img"; fi
    feat_path="$dir/featured_image.${ext}"
    if curl -s -L -f "$featured_url" -o "$feat_path"; then
      echo "WROTE: $feat_path"
    else
      echo "WARNING: could not download featured image" >> "$dir/missing_fields.txt"
      echo "$featured_url" > "$dir/featured_image.url"
    fi
  fi
else
  echo "ABSENT: featuredImage" >> "$dir/missing_fields.txt"
fi

process_image_array() {
  local arr="$1"
  local manifest="$dir/${arr}_manifest.csv"
  printf 'index,localName,originalUrl,originalMimeType,alt,sizeBytes,written\n' > "$manifest"
  if jq -e --arg a "$arr" 'has($a) and .[$a] != null' "$resp" >/dev/null; then
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
        safe_localName=$(basename "$localName")
        outpath="$dir/$safe_localName"
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

if jq -e '.debugInfo != null' "$resp" >/dev/null 2>&1; then
  jq '.debugInfo' "$resp" > "$dir/debug_info.json"
  echo "WROTE: $dir/debug_info.json"
fi

if [ -f "$dir/missing_fields.txt" ]; then
  sort -u "$dir/missing_fields.txt" -o "$dir/missing_fields.txt"
fi

echo
echo "DONE. Article directory: $dir"
