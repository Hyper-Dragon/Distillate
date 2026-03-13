# Markdown to PDF Example

## split_images.lua

The following Lua filter can be used with Pandoc to process inline images in a markdown document when converting to PDF. It moves inline images to their own centered paragraphs, adds vertical space before and after the image, enforces a maximum height while keeping the aspect ratio, and converts short paragraphs immediately following an image into centered, italic, smaller captions.

```lua
-- split_images.lua
-- Moves inline images to their own centered paragraph,
-- adds vertical space, enforces height and keepaspectratio,
-- and converts short paragraphs immediately after an image into
-- centered, italic, smaller captions.

local pandoc = require("pandoc")
local stringify = pandoc.utils and pandoc.utils.stringify or pandoc.utils.stringify

-- Configuration (tweak if you like)
local SPACE_BEFORE = "2mm"
local SPACE_AFTER  = "2mm"
local CAPTION_MAX_CHARS = 250   -- max length to be considered a caption
local CAPTION_SIZE_CMD = "\\small" -- latex size command for caption

-- helper to clone attributes and add/override keyvals
local function with_image_attrs(attr)
  local id, classes, kv = "", {}, {}
  if attr and type(attr) == "table" then
    id = attr[1] or ""
    classes = attr[2] or {}
    kv = attr[3] or {}
  end

  -- ensure height and keepaspectratio
  local found_height = false
  local new_kv = {}
  for _, pair in ipairs(kv) do
    if pair[1] == "height" then
      pair[2] = "0.2\\textheight"
      found_height = true
    else
      table.insert(new_kv, pair)
    end
  end
  if not found_height then
    table.insert(new_kv, {"height", "0.2\\textheight"})
  end

  local found_keep = false
  for _, pair in ipairs(new_kv) do
    if pair[1] == "keepaspectratio" then found_keep = true end
  end
  if not found_keep then
    table.insert(new_kv, {"keepaspectratio", ""})
  end

  return pandoc.Attr(id, classes, new_kv)
end

-- produce the blocks for an image (vspace, centered image, vspace)
local function vspace_para(img, space_before, space_after)
  space_before = space_before or SPACE_BEFORE
  space_after  = space_after  or SPACE_AFTER
  local blocks = {}
  table.insert(blocks, pandoc.RawBlock("latex", "\\vspace{" .. space_before .. "}"))
  table.insert(blocks, pandoc.RawBlock("latex", "\\begin{center}"))
  table.insert(blocks, pandoc.Para{ img })
  table.insert(blocks, pandoc.RawBlock("latex", "\\end{center}"))
  table.insert(blocks, pandoc.RawBlock("latex", "\\vspace{" .. space_after .. "}"))
  return blocks
end

-- state flag: set when the previous top-level block emitted was an image block
local last_was_image = false

-- helper: determine whether a paragraph node is a likely caption
local function is_likely_caption(para)
  -- must be a Para
  if para.t ~= "Para" then return false end
  -- stringify & length check
  local s = stringify(para)
  if s == nil or s == "" then return false end
  if #s > CAPTION_MAX_CHARS then return false end
  -- do not treat paragraphs containing images or block-level links as captions
  for _, inl in ipairs(para.content) do
    if inl.t == "Image" or inl.t == "Link" then
      return false
    end
  end
  -- passed heuristics — treat as caption
  return true
end

-- If the paragraph is a caption following an image, emit centered small italic caption
local function caption_blocks_from_para(para)
  local blocks = {}
  table.insert(blocks, pandoc.RawBlock("latex", "\\begin{center}" .. CAPTION_SIZE_CMD))
  -- wrap the paragraph's inlines in Emph to produce italic via pandoc
  table.insert(blocks, pandoc.Para{ pandoc.Emph(para.content) })
  table.insert(blocks, pandoc.RawBlock("latex", "\\end{center}"))
  return blocks
end

-- Main Para filter
function Para(el)
  -- If previous top-level output was an image, check whether this Para is a caption
  if last_was_image then
    if is_likely_caption(el) then
      -- reset flag and emit caption blocks
      last_was_image = false
      return caption_blocks_from_para(el)
    else
      -- not a caption: clear flag and continue to normal processing
      last_was_image = false
    end
  end

  -- Otherwise, do the usual splitting of inline images into their own blocks
  local inlines = el.content
  local out_blocks = {}
  local buffer = {}
  local pending_image = false  -- set true whenever we inserted an image block; cleared if trailing text exists

  local function flush_buffer()
    if #buffer > 0 then
      table.insert(out_blocks, pandoc.Para(buffer))
      buffer = {}
      -- if there is trailing text after an image in the same paragraph, it's not a caption
      pending_image = false
    end
  end

  for _, inline in ipairs(inlines) do
    if inline.t == "Image" then
      -- flush text before image
      flush_buffer()
      -- enforce attributes
      local img = inline
      img.attr = with_image_attrs(img.attr)
      -- insert centered image with spacing
      local img_blocks = vspace_para(img, SPACE_BEFORE, SPACE_AFTER)
      for _, b in ipairs(img_blocks) do
        table.insert(out_blocks, b)
      end
      -- mark that last emitted block(s) included an image (will be used for next Para)
      pending_image = true
    else
      table.insert(buffer, inline)
    end
  end

  -- flush any remaining text
  flush_buffer()

  -- set top-level flag: if the paragraph produced at least one out_block and the final thing emitted was an image (no trailing text),
  -- then last_was_image should be true so next Para call may be treated as a caption.
  if #out_blocks == 0 then
    return nil  -- nothing changed; leave original paragraph alone
  end

  -- If pending_image is true here, that means the original paragraph ended with an image and no trailing text,
  -- so the next top-level Para might be a caption.
  last_was_image = pending_image

  return out_blocks
end
```

## Usage

This command applies the `split_images.lua` filter to the input markdown file `article.md`, and produces a PDF with the specified formatting options. The filter will move inline images to their own centered paragraphs, add vertical space, enforce height and aspect ratio, and convert short paragraphs immediately following an image into centered, italic, smaller captions.

```bash
pandoc "article.md" -V geometry:"paperwidth=157mm, paperheight=209mm, margin=8mm, top=5mm, headsep=4mm, includehead" -V fontsize=10pt -V linestretch=1.1 -V fontfamily=libertinus -V colorlinks=true -V linkcolor=black -V urlcolor=black -V header-includes='\usepackage{microtype}\usepackage{fancyhdr,lastpage}\pagestyle{fancy}\fancyhf{}\renewcommand{\headrulewidth}{0pt}\fancyhead[L]{\fontsize{8}{10}\selectfont article page \thepage\ of \pageref{LastPage}}\fancypagestyle{plain}{\fancyhf{}\fancyhead[L]{\fontsize{8}{10}\selectfont article page \thepage\ of \pageref{LastPage}}}' --lua-filter=split_images.lua -o article.pdf
```
