---
name: flaui-mcp
description: Use FlaUI MCP keyboard and dialog tools with the correct input format, window scope, and screenshot review.
---

Use `windows_send_keys` for supported key/chord tokens, for example
`chord:"Ctrl+O"` or `keys:["Right"]`. Do not put literal text or file paths
in `keys`, or pass it a scalar string. Use `windows_type` or `windows_fill`
for literal text.

After opening a dialog, refresh `windows_list_windows` and identify its current
handle before querying its children. Do not assume the dialog is the owner's
root or a descendant of it. `rootOnly:true` does not search descendants.

Before claiming screenshot success, inspect the final saved image against the
requested content. Tool success does not establish visual completeness. Display
returned image content (not just its text response), or load the saved PNG.
