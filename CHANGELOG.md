# Changelog

All notable changes to FlaUI-MCP will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- The server now keeps the display awake while tools are actively being called, so Windows does not turn off the screen or show the lock screen in the middle of a long automation run. Implemented with a Windows power availability request (`PowerCreateRequest`/`PowerSetRequest` with `PowerRequestDisplayRequired` + `PowerRequestSystemRequired`) — the same mechanism video players and conferencing apps use, visible in `powercfg /requests`. The request is released after 5 minutes without a tool call; configure the idle period (or disable with `0`) via the `FLAUI_MCP_KEEP_AWAKE_SECONDS` environment variable.

### Fixed
- `windows_click` no longer hangs (and then times out) when the clicked element's handler opens a modal dialog. UIA pattern calls (Invoke/Toggle/Select) are synchronous cross-process calls: a WinForms/DevExpress handler that calls `ShowDialog()` does not return until the dialog closes, blocking the target app's entire UIA provider. The click now runs on a background thread while non-blocking Win32 APIs watch for the modal signature (new top-level window, or owner window disabled) and returns immediately with the dialog's title and interaction guidance.
- While an app's UIA provider is blocked by such a pending call, `windows_snapshot`, `windows_get_text`, `windows_click`, `windows_type`, `windows_fill`, `windows_send_keys` (ref-based) and `windows_batch` actions targeting that app now fail fast with guidance (use `windows_screenshot` / `windows_send_keys` without ref) instead of hanging until the 30s global timeout.
- `windows_list_windows` now enumerates windows via Win32 instead of walking the UIA desktop tree, so it keeps working even while some app's UIA provider is blocked. Window handles are also stable across calls now (previously every call registered new handles for the same windows).
- `windows_focus` and `windows_close` (by handle) now use Win32 (`SetForegroundWindow` / `WM_CLOSE`) and work while a provider is blocked.
- `windows_screenshot` with a window handle falls back to a Win32 window-bounds capture while the app's provider is blocked.

## [0.2.0] - 2026-07-08

### Fixed
- Screenshots are now correct on scaled displays (DPI > 100%). Added `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` as the first call in the process entry point so UIA coordinates match physical pixels.
- Tool execution now times out after 30 seconds instead of hanging indefinitely when UI Automation blocks.

### Added
- Solution file (`FlaUI.Mcp.slnx`)
- xUnit test project (`tests/FlaUI.Mcp.Tests`) with DPI regression test
- `windows_send_keys` MCP tool for sending key presses and key chords.
- Opt-in `background` mode for `windows_screenshot` handle captures, with blank-frame fallback to the normal capture path.
- `savePath` and `overwrite` options for `windows_screenshot`, with local PNG path validation and atomic writes.
- Desktop integration test project with WinForms and WPF test applications.

## [0.1.0] - 2024-02-02

### Added
- Initial release
- **Core MCP Tools:**
  - `windows_launch` - Launch Windows applications
  - `windows_snapshot` - Capture accessibility tree with element refs
  - `windows_click` - Click elements by ref (uses Invoke pattern when available)
  - `windows_type` - Type text into elements
  - `windows_fill` - Clear and fill text fields
  - `windows_get_text` - Get element text content
  - `windows_screenshot` - Capture window/element screenshots
  - `windows_list_windows` - List all open windows
  - `windows_focus` - Bring window to foreground
  - `windows_close` - Close windows
  - `windows_batch` - Execute multiple actions in a single call

- **Architecture:**
  - MCP protocol handler (JSON-RPC over stdio)
  - Element registry for ref ↔ AutomationElement mapping
  - Snapshot builder for agent-friendly accessibility tree format
  - Session manager for tracking launched applications

- **Documentation:**
  - README with installation and usage instructions
  - GitHub Actions for CI/CD
  - MIT License

### Technical Details
- Built on [FlaUI](https://github.com/FlaUI/FlaUI) for Windows UI Automation
- Uses UIA3 for modern app support (WPF, UWP, Win32)
- Targets .NET 8.0-windows
- Prefers control patterns (Invoke, Value, Toggle) over mouse simulation
