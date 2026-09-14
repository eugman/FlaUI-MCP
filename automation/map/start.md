# TE3 map: start here

Locators observed on TE3 3.26.3. `te3_catalog` reports the map revision, a hash of these topics. Passing navigation or capture checks does not make a screenshot complete; look at the image.

Read only the topic matching the task: `preferences-auto-formatting`, `preferences-code-actions`, `objects`, `scripts`, or `capture`. Topic Markdown is the source of truth for local readers, `te3_catalog(topic:...)`, and `te3://map/...` resources.

Generic runs use `windows_list_windows` to obtain current handles, then narrow `windows_find` queries. A window handle (`w1`) is not an element ref (`w1e5`); neither is a PID. Companion calls use an explicit running TabularEditor3 `processId`, not another server's handle. `te3_inspect` defaults to native window/blocker information without focusing.

Observe before acting. A control's presence, Offscreen=false, or a selected tree row alone does not prove the correct page is displayed. Require the task's identity and pane checks. Null toggle/focus state means unknown. Root-only queries inspect roots, not descendant tabs. Truncated or unreadable searches cannot prove absence or uniqueness; narrow the scope.

Use `windows_wait` for settled absence and exact value/selection/toggle checks. It observes provider-realized nodes, not virtual rows that have never been realized. Unsupported state and provider failures are not success.

Input and navigation require desktop permission. Timeout means uncertain outcome: inspect, do not replay. Never dismiss an unrelated application's dialog or change file associations to recover. Refocus/retry only after a new observation establishes a safe target.

The companion attaches only; it does not launch TE3, normalize preferences, execute scripts, save models, or close dialogs. Supported navigation/capture destinations: `tom-explorer`, `expression-editor`, `object`, `preferences/auto-formatting`, `preferences/code-actions`. Capture expects an already prepared scene and does not resize it.

Topic IDs and destination IDs differ: read topic `objects` for destination `object`, and topic `preferences-auto-formatting` for destination `preferences/auto-formatting`. For either tab destination, call `te3_navigate(processId:PID, destination:"tom-explorer")` (or `expression-editor`), check `assessment.sceneIdentity`, then `te3_capture(processId:PID, scene:"tom-explorer", savePath:ABSOLUTE_PNG)` with the same destination. Capture never performs the navigation for you.

Scripted runner success, agent success, and approved docs replacement are separate claims. An image still needs visual review. No companion capability implies permission to edit the original docs checkout.
