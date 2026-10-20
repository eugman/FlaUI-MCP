# Operation and protocol recovery

The stdio server supports MCP 2024-11-05 and 2025-03-26 initialization. Unknown requested revisions receive the supported 2024-11-05 revision, which a client must accept explicitly or disconnect. This does not introduce the newer stateless MCP lifecycle.

Requests retain operation IDs in structured results. `completed` describes tool execution; `verification: not-established` does not assert the target changed. Existing tool-specific verification is preserved. The operation-status tool can inspect delayed provider completion.

Cancellation notifications cancel only the matching in-flight request ID. Notifications never dispatch tools or produce responses. The input reader remains available while serialized tools run, with at most 32 admitted requests. Cancellation suppresses the eventual RPC response and requests cooperative cancellation; it does not undo input already dispatched or terminate an unresponsive UIA provider. Never replay on timeout. Observe the target and pending operation first.

No-focus checks use fake tools, signals, and timers. Foreground, UIPI, docking behavior and real screenshots still require a fresh machine grant.

For a locally merged, unpublished Core commit, set `CORE_REVISION` to that exact commit when running product gates. The ownership gate requires that commit to be an ancestor and compares shared paths with its tree. The default remains `core/main`; no remote-tracking ref is rewritten to impersonate a published commit.

The optional `flaui/operations/status` JSON-RPC extension is advertised as `capabilities.experimental.flauiOperationStatus.version = 1`. Supply `requestKey`, the JSON representation of the original request ID (for numeric ID 7, use string "7"). The response echoes that key and returns matching operations plus `notDispatched` for a cancelled queued request. `finished` and provider status describe execution lifetime, not verified product state; `resultOutcome` preserves tool-specific evidence. Unknown or evicted requests never imply safe retry.
