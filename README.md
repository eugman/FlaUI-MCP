# FlaUI-MCP

An MCP server that lets AI agents drive Windows desktop applications through UI Automation, the accessibility API screen readers use. It works the way Playwright's MCP server works for browsers: the agent reads an accessibility tree with element refs and acts on those refs, instead of guessing coordinates from screenshots.

This is the shared core. Product-specific automation builds on it in separate repositories, such as [FlaUI-MCP for Tabular Editor 3](https://github.com/eugman/FlaUI-MCP). It started as a fork of [shanselman/FlaUI-MCP](https://github.com/shanselman/FlaUI-MCP).

## Example

```
(abbreviated output)
windows_launch   {"app": "calc.exe"}                      → handle w1
windows_snapshot {"handle": "w1"}
  - window "Calculator" [ref=w1]
    - button "Three" [ref=w1e43]
    - button "Multiply by" [ref=w1e35]
    - button "Equals" [ref=w1e38]
    - text "Display is 0" [ref=w1e15]
windows_batch    {"actions": [{"action": "click", "ref": "w1e43"}, {"action": "click", "ref": "w1e35"},
                              {"action": "click", "ref": "w1e43"}, {"action": "click", "ref": "w1e38"}]}
```

## Install

You need Windows 10 or 11, the .NET 9 SDK (9.0.300 or later; see `global.json`) and the .NET 8 Desktop Runtime.

```powershell
git clone https://github.com/eugman/FlaUI-MCP-Core.git
cd FlaUI-MCP-Core
dotnet build FlaUI.Mcp.slnx -c Release
```

Then add the server to your MCP client:

```json
{
  "mcpServers": {
    "windows": {
      "command": "C:/path/to/FlaUI-MCP-Core/src/FlaUI.Mcp/bin/Release/net8.0-windows/FlaUI.Mcp.exe"
    }
  }
}
```

## Tools

| Tool | Does |
|---|---|
| `windows_launch` | Start an application and attach to its window. |
| `windows_list_windows` | List top-level windows and their handles. |
| `windows_focus` | Bring a window to the foreground. |
| `windows_close` | Ask a window to close. |
| `windows_place_window` | Move or resize a window. |
| `windows_set_window_state` | Maximize, restore or minimize a window, and report the state reached. |
| `windows_snapshot` | Read a window's accessibility tree with element refs. |
| `windows_find` | Find elements by selector without reading the whole tree. `nameContains` matches part of a name. |
| `windows_wait` | Wait until a selector matches, or stops matching. |
| `windows_get_text` | Read an element's text. |
| `windows_click` | Click or invoke an element. `action` picks invoke, select, expand, collapse, toggle or a mouse click; toggle reports the state it reached. |
| `windows_fill` | Replace a field's value. |
| `windows_type` | Type text into an element or the focused control. |
| `windows_paste` | Paste text through the clipboard, which autocomplete can't rewrite. The pasted text stays on the clipboard. |
| `windows_send_keys` | Send keys or chords such as `Ctrl+A`. |
| `windows_get_clipboard` | Read the clipboard, for editors that hide their text from UI Automation. |
| `windows_screenshot` | Capture a window, element or screen region as PNG. `parkPointer` moves the mouse off the window first. |
| `windows_mouse` | Park, move, hover, scroll, click with the left or right button, or drag the pointer; drags can start on an element's edge and resize it to a given size. |
| `windows_batch` | Run several actions in one call. |
| `windows_operation_status` | Check on an action that is still running (for example, one blocked by a modal dialog). |

### Tool annotations

Every tool in `tools/list` carries MCP `annotations`, and `flaui/operations/status` echoes them on each operation, so a client can tell a failed observation (safe to repeat) from a failed mutation (inspect before repeating):

<!-- tool-facts:begin -->
| Tool | readOnly | destructive | idempotent | observation | recovery | mutating when |
|---|---|---|---|---|---|---|
| `windows_batch` |  | yes |  |  |  |  |
| `windows_click` |  | yes |  |  |  |  |
| `windows_close` |  | yes |  |  | yes |  |
| `windows_fill` |  | yes |  |  |  |  |
| `windows_find` | yes |  | yes | yes |  |  |
| `windows_focus` |  |  | yes |  | yes |  |
| `windows_get_clipboard` | yes |  | yes | yes |  |  |
| `windows_get_text` | yes |  | yes | yes |  |  |
| `windows_launch` |  |  |  |  |  |  |
| `windows_list_windows` | yes |  | yes | yes | yes |  |
| `windows_mouse` |  | yes |  |  |  |  |
| `windows_operation_status` | yes |  | yes |  | yes |  |
| `windows_paste` |  | yes |  |  |  |  |
| `windows_place_window` |  |  | yes |  |  |  |
| `windows_screenshot` |  |  | yes | yes | yes | `parkPointer` |
| `windows_send_keys` |  | yes |  |  | yes |  |
| `windows_set_window_state` |  |  | yes |  |  |  |
| `windows_snapshot` | yes |  | yes | yes |  |  |
| `windows_type` |  |  |  |  |  |  |
| `windows_wait` | yes |  | yes | yes |  |  |
<!-- tool-facts:end -->

The table is generated from each tool's `ToolFacts` (run the offline tests with `FLAUI_APPROVE_TOOL_SCHEMAS=1` to rewrite it). `observation` marks a call whose success is evidence of what is on screen, `recovery` one usable while an app's UI Automation provider is blocked, and `mutating when` the arguments that make an observing call change the desktop; `tools/list` carries them as `observationHint`, `recoveryHint` and `mutatingWhenArgument`. `openWorldHint` is false throughout. A product tool sets its own hints by overriding `ToolBase.Annotations` with `McpToolAnnotations.ReadOnly` or `McpToolAnnotations.Input(destructive: ..., idempotent: ...)`; a tool that does not is listed without annotations, which a client must treat as a possible mutation.

Product hosts embed the core and may expose only some of these. `launch`, `list_windows`, `focus`, `close`, `get_text` and `batch` are the desktop-management tools that a host can leave out.

## Behaviour worth knowing

**Finding things.** `windows_find` and `windows_wait` accept `text:`, which matches an element's name or its Value-pattern value, except that password-field values are excluded. Grid, tree and list rows often have positional names like `Node0` with the visible label in `value`, so prefer `text:` for anything you can see on screen.

**Terminal windows.** The same window tools can list, focus, place, minimize, and close terminal windows when their owning process is allowed. If `FLAUI_MCP_ALLOWED_APPS` is set, include `WindowsTerminal` in it; an unset or empty variable allows all apps. Use `windows_list_windows` to attach to the actual window; `wt.exe` may exit after handing the request to an existing Terminal process, so its launch PID is not a stable window identity. Use `windows_snapshot` or `windows_find` to locate a terminal text area and `windows_get_text` to read its UI Automation Text pattern. Tabs and panes are ordinary accessible controls where the provider exposes them. Keyboard input is guarded by the same foreground and process checks as in other apps. Terminal UI text is an observation of the displayed buffer, not a reliable command result or exit status; for builds and scripts, run the command through a shell process API and use the Terminal window only when its visible UI matters. A host that only exposes a product-specific subset of Core's tools must opt into the needed generic window and text tools.

**Modal dialogs.** A click that opens a modal dialog blocks the app's UI Automation provider until the dialog closes. `windows_click` notices this and returns with the dialog's title. While the click is pending, tools that need that app's provider fail fast with guidance; keyboard input by handle, window listing, focus and close still work. Don't replay the pending click: check it with `windows_operation_status`. An Invoke, Toggle, Select or Expand that returned is watched for 300 ms more: a dialog that opens a moment later is reported in the reply (`then dialog "Title" opened`, `modalTitle`), and a toggle, expand or collapse state read back before it is `unsettled` (unverified), not verified.

**Screenshots.**
- A normal capture copies screen pixels, so it is refused when another application covers the target. Native capture (`strictNative: true`) renders the window's own surface and isn't affected by what's in front of it.
- `background: true` captures by handle without bringing the window forward; it leaves out owned dialogs and open menus.
- `savePath` must be an absolute `.png` path. Existing files are kept unless you pass `overwrite: true`.
- `locate: ["w1e5"]` returns each element's rectangle when the capture has a known origin and one-to-one pixel geometry; it refuses the location request if a DPI mismatch or changed capture size makes the coordinates unreliable.
- The result reports window and monitor DPI and flags a mismatch.

**Drags.** `windows_mouse` `drag` resolves and hit-tests both ends under the input lease just before the press. A drop on a `toRef` edge (`toEdge` left, right, top or bottom) is placed 2 px inside that edge, symmetric on every side, so it never lands on the neighbour's border; `center` is the centre of the bounds, and a drag start on `fromEdge` keeps the edge's own first or last pixel, where a column divider is. Before the button is released the drop point is checked again: it must still be on the target window, and the `toRef`'s bounds, re-read then, must still hold it with the hit test still passing. A drop on a `toRef` within one row of the top or bottom of a list or pane whose content scrolls vertically now (the Scroll pattern says so; a list that holds all its rows has no band), where a resting drag auto-scrolls the view, is refused before input (`drop-point-in-scroll-band`): scroll the view first. When something moved (a tree auto-scrolled under the drag, an overlay came up, the geometry could not be re-read) Escape is sent while the button is still down, then the button is released, and the call is `unverified` with `failure.code: drop-target-moved` (`actionDispatched: true`, `inputsSent` counting the moves, the Escape and the release) and `Under the pointer now: <element>` in its text. Only an OLE drag ends without a drop on Escape; an app that drags by mouse capture (a tree's "Move objects") may still drop on the release, so observe the window before going on. Every other failure with the button down (a step refused because the target lost the foreground, a provider error, a cancellation) takes the same way out: Escape only while the target still holds the foreground, then the release, then a refusal (`target-changed`, `drag-interrupted`) saying what went out. A `resizeTo` drag reads the ref's bounds back after the release and reports the size reached against the one requested as the outcome's `proof` (`element-width` or `element-height`, independent): `ok` when equal, `failed` with `resized: {requested, reached}` when not, `unverified` when the bounds could not be read. A drag with a ref reports `droppedOn`, the element UI Automation reports under the pointer after the release (`TreeItem 'Name'`, `nothing UI Automation can see`, or `unreadable (reason)`), so the reply says where the drop went. The reply's `changed` says when the tool scrolled a ref into view first (`the view was scrolled`), so an element's bounds may differ from your last snapshot.

**Fills.** `windows_fill` sets a control through its Value pattern when it has one, else by keyboard (focus, Ctrl+A, type). A Value-pattern fill reads the value back through the same pattern, so its proof is self-reported (`verdict: self-reported`, `independent: false`) and the call is `unverified`, never `ok`: a provider can accept the value without the app taking it (a file dialog's name field does). Observe the control, or the effect, before relying on it. A field in a common file dialog (under a `FileNameControlHost`, or in a `#32770` dialog that hosts the shell view (`DirectUIHWND`, `ShellView`) or whose field is the file name itself, AutomationId `1148` or `1152`) is filled by keyboard so the dialog sees the change; a MessageBox, Find, Font or Print dialog is also `#32770` and keeps the Value pattern. The keyboard route reads the text back independently and reports `Filled` (ok) or a `mismatch` (failed).

**Batches.** `windows_batch` stops at a step whose action is still pending, even with `stopOnError: false`. Inspect the application before sending dependent input.

**Timeouts.** Every tool call times out after 30 seconds, so a hung provider returns an error instead of hanging the server. The action may still complete afterwards: observe before retrying.

**Snapshots** are bounded by `maxNodes` (default 3000) and `maxCharacters` (default 20000). A partial snapshot says so; a control missing from it is not proven absent.

## Configuration

| Environment variable | Effect |
|---|---|
| `FLAUI_MCP_ALLOWED_APPS` | Semicolon- or comma-separated process names (e.g. `notepad;calc`). Only these apps can be launched, listed, read, captured or sent input. Unset means any app. |
| `FLAUI_MCP_KEEP_AWAKE_SECONDS` | While tools are being called, the server keeps the display on and the lock screen away. It releases that after this many idle seconds (default 300). `0` turns it off. |

The allowlist guards against an agent being steered into the wrong app through this server. It matches by process name and is not a sandbox. Dialogs the allowed process owns keep working; separate processes it starts (such as a browser for sign-in) are blocked unless listed. While the allowlist is set, the Windows key is rejected.

## Develop

```powershell
pwsh scripts/setup.ps1      # once: formatter and git hooks
pwsh scripts/check.ps1      # formatting, build with analyzers, unit tests
```

`tests/FlaUI.Mcp.IntegrationTests` launches WinForms and WPF test apps and needs an interactive desktop. See [docs/code-quality.md](docs/code-quality.md) for the formatting and size rules, and [CONTRIBUTING.md](CONTRIBUTING.md) for how product repos take changes from this one.

## License

MIT. See [LICENSE](LICENSE).
