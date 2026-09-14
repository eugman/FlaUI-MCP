# Capture a prepared TE3 scene

TE3 3.26.3. Prepare the destination first. This workflow requires desktop permission even when native capture avoids activation.

## Capture

For the title and complete window boundary, target the exact dialog/window handle or Window ref with `strictNative:true` and `includeMetadata:true`. This requests native whole-window capture without screen fallback; it may fail while an Invoke is pending. Ordinary UIA element capture may omit title chrome. Do not silently substitute it for a required whole-window image.

Use an absolute PNG `savePath`. `includeImage:false` avoids returning image tokens but requires loading the saved PNG for review. With `includeImage:true`, display the returned image content rather than merely printing its text/JSON wrapper.

Companion `te3_capture` uses native capture of the prepared scene's actual window. It does not navigate, resize, or overwrite. Read its `assessment`: `sceneIdentity: matched` and `artifact: saved` are separate facts; `visualCompleteness: not-assessed` is not approval. A post-capture failure can still report a saved PNG path. Keep that artifact as evidence, not an approved replacement.

## Inspect the final PNG

For Auto Formatting, check all of these in the image:

- Preferences title and bottom OK/Cancel buttons are readable.
- Auto Formatting is selected and search is empty.
- The correct pane and all its controls are visible, including **Use default formatting settings**, with no clipping, scrolling hiding required content, or unwanted overlays.

For other scenes, use the requested content and destination map's identity checks. UIA presence, Offscreen=false, and selected state do not prove visual completeness. If clipped, resize explicitly, inspect actual bounds, capture to a new path, and review again. Never click a setting to fix framing.

Record capture method, dimensions, crop and window DPI from metadata; do not infer DPI from PNG size. Compare like-for-like scales and never change per-monitor scaling as part of capture. A larger window is not a higher-DPI image.

Native owner capture excludes separate popups, menus and output windows. Multi-window scenes and optional composition remain runner recipes. Preserve raw PNGs. Manually approved replacements go only to the docs automation copy, not the original docs checkout.
