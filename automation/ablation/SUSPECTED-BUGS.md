# Suspected bugs and automation issues

Do not send these upstream as confirmed defects without isolated reproduction.

## TE3: blank Preferences pane after clearing search

- Latest decisive check: `fla_20260913_064346_eb12da68`, paired foreground
  (`*-screen.png`) and PrintWindow captures. Code Actions is visibly populated
  before clearing; both capture paths show a blank pane after clearing and
  settling. This occurs with both Value-pattern clear and Ctrl+A/Backspace,
  using separate Preferences dialogs. Physical reselection restores the page
  (`09-recovered-screen.png`) and final prefix/selection checks pass.
  Thus this occurrence is not merely a PrintWindow artifact or specific to
  Value-pattern clearing. It remains an application interaction to report as a
  suspected defect, not proof of which TE3/DevExpress handler is responsible.
  Model unchanged, settings restored, no recovery pending, TE3 closed.
  Reproduce with `run automation/offline.config.json --scenario preferences-search-diagnostic`.
- Status: blank-pane symptom reproduced in isolated runner checks; upstream cause
  unconfirmed. TE3 3.26.3.12966.
- Supporting trace: restarted D+2, thread
  `01a0996d-3017-7610-b9d4-7aac6bd12338`, run
  `fla_20260913_062000_3ee74039`, shows a snapshot with an empty
  `pnlSectionContent` after a successful fill of search with an empty string.
  Earlier Code Actions lookup was truncated and no successful selection was
  established. This supports an empty-content state, not a confirmed rendering
  defect or a manual reproduction. Preserve that distinction.
- Report: Preferences appears blank when its search is empty.
- Visual evidence preserved: `artifacts/te3/fla_20260913_062000_3ee74039/blank-after-clear.png`.
  The dialog and unfiltered tree are visible, search is empty, content is blank,
  and no selected category is apparent. This confirms the symptom, not whether
  TE3 should retain a selected category or whether a UIA operation caused it.
- Initial context: agent trials selecting Code Actions through the Preferences
  filter; one excluded trial had possible user focus interference.
- Stronger evidence from the Terra D+ trial, run
  `fla_20260913_062536_33199137`: `agent-code-actions.png` shows empty search,
  Code Actions visibly selected, and a blank content pane. The independent
  grader passed the empty-search and selected-row checks, then failed finding
  the visible Variable prefix control. Thus an unselected category alone does
  not explain every occurrence. Settings restoration completed without errors.
  This still does not distinguish a TE3 defect from an automation interaction
  or capture/rendering issue; manual and foreground controls remain necessary.
- Controlled reproduction: `fla_20260913_063614_f7c3118a`, recipe
  `preferences-search-diagnostic`. `02-selected-filtered.png` shows working
  controls; `03-cleared.png` and `04-cleared-settled.png` show blank content
  after Value-pattern clearing, including a one-second settling interval.
  `05-reselected.png` shows restored controls after bounded tree navigation and
  physical reselection. Independent control assertions passed afterward; model
  unchanged, settings restored. Matching UI maps are retained at each stage.
- Follow-up `fla_20260913_063758_c3e0ee44` reproduced clear/recovery, but its
  keyboard comparison is invalid: `06-before-keyboard-clear.png` was already
  blank after refiltering the selected page. Do not attribute that transition to
  keyboard clearing. The diagnostic now closes/reopens Preferences between arms
  and requires a visible prefix control before keyboard clearing.
- Follow-up `fla_20260913_063945_e6663d7a`: reopening and the prefix assertion
  passed, but `06-before-keyboard-clear.png` is blank while its complete UI map
  contains onscreen prefix controls. Therefore even this keyboard comparison
  is not a visually established before/after transition. Recovery/model checks
  passed and settings were restored. Foreground/background paired capture is
  implemented and builds, but has not yet run. Diagnostic Passed means final
  recovery assertions passed, not that the cause was established.
- Reproduce separately from scored arms: open Preferences, filter Code Actions,
  select it, capture, clear search, then capture immediately and after settling.
  Record tree selection, pane identity, visible control count, geometry and focus.
  Compare an actual foreground screenshot with background window capture.
- Controls: repeat by manual typing/clearing versus Value-pattern fill; test
  clicking the selected category again and resizing only after preserving the
  failing state. Compare another category. Cancel and restore settings afterward.
- Upstream handoff needs: exact steps, TE3/Windows/DPI versions, original screen
  recording or before/after PNGs, whether manual reproduction works, frequency,
  and whether reselecting/resizing recovers it. No secrets or user models.

### Online research (2026-09-13)

Local TE3 ships DevExpress 25.2.5.0; our SessionManager uses UIA3.

- [DevExpress selection semantics](https://docs.devexpress.com/WindowsForms/206/controls-and-libraries/tree-list/feature-center/focus-selection-and-navigation/node-selection):
  selected and focused nodes differ. Selection alone need not prove page activation.
- [Microsoft IsOffscreen](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.automationelementinformation.isoffscreen):
  occlusion does not affect the property. Our Visible selector is its inverse,
  not a pixel-based visibility assertion.
- [Microsoft PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow):
  the target app paints into a supplied device context. Our background capture
  uses this path, not the desktop framebuffer. Its near-black-image check cannot
  detect an otherwise normal gray dialog with missing content.
- [FlaUI README](https://github.com/FlaUI/FlaUI): UIA3 can have WinForms issues
  absent with UIA2. A small isolated provider comparison is a candidate, not an
  established fix or reason to switch the production server now.
- [DevExpress accessibility, v25.2](https://docs.devexpress.com/WindowsForms/404293/build-an-application/accessibility?v=25.2):
  the application can enable UseUIAutomation and customize accessible metadata
  with QueryAccessibleInfo. Whether TE3 enables this is unverified; ask its
  maintainers rather than assuming the client can change that setting.
- [DevExpress testing guidance](https://docs.devexpress.com/WindowsForms/404045/build-an-application/ui-tests-ui-automation-appium-coded-ui?v=25.2):
  recommends targeted ID/name/pattern-based tests; Appium also relies on UIA.
  Changing test frameworks alone is not evidence of fixing a provider problem.
- No exact public matching blank-Preferences report found in the searches made.
  [TE3 3.26.3 notes](https://docs.tabulareditor.com/en/references/release-notes/3_26_3.html)
  do not establish a fix for this symptom. Search absence is not proof of novelty.

## MCP/TE3: Invoke-based modal opening blocks follow-up UIA

- Status: observed in guided pilot D1. Opening Preferences via default Invoke
  produces a pending pattern warning; the agent switches to keyboard fallbacks.
- The warning explicitly says UIA-based tools will fail until the dialog closes.
  This is a limitation of this automation path, not yet proven to be a TE3 bug.
- Candidate workaround: guarded physical click. D+1 returned a Preferences
  window through UIA after physical opening, but the run was interrupted for
  possible focus interference and is not a successful end-to-end verification.

## Harness: call-budget undercount with looped tool calls

- Status: observed in A2. Five FlaUI calls inside one exec were initially counted
  as one invocation. Operator stopped the attempted 31st actual FlaUI call.
- Count actual dispatched calls, not exec envelopes, before unattended trials.
  Current pilot monitoring is manual; there is no hard technical call cap yet.
