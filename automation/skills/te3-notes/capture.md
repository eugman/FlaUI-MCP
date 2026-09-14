# Capture and check

1. Save the dialog or main window once by handle with `background: true`, an absolute `savePath` and `includeImage: true`. Plain capture can omit the title bar.
2. Look at the returned image. A successful tool result doesn't mean the screenshot is complete.
3. Check the task's required content is visible and unclipped. For Auto Formatting: title and OK/Cancel readable, Auto Formatting selected, search empty, and every control visible including `Use default formatting settings`.
4. If clipped, resize the window, capture to a new path, and look again. Don't change settings or display scaling to fix framing.
