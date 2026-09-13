# Pilot results — in progress

All counts are native runtime reports, not dollar charges. Report failed and
excluded attempts as overhead; do not hide them in a success-only comparison.

| Attempt | Status | Input | Cached input (subset) | Output | Reasoning (subset) |
|---|---|---:|---:|---:|---:|
| A1 | Excluded: operator interrupted a permitted dialog close | 679306 | 630528 | 1282 | 560 |
| D1 | Agent reported failure; inspected PNG shows main editor, not Code Actions | 1138303 | 1094784 | 2055 | 596 |
| A2 | Budget failure: stopped on attempted 31st call; PNG shows wrong Preferences page | 1816677 | 1730560 | 2542 | 1067 |
| D+1 | Excluded: user reported possible focus interference and requested restart | 1154712 | 1103104 | 1770 | 467 |
| D+2 | Budget failure: stopped after attempted 31st call; saved blank Preferences pane | 1113385 | 1070080 | 2611 | 955 |
| D+ Terra-low | False success report: blank screenshot and independent grader failure | 868054 | 823040 | 2629 | 722 |

A1: Luna-low thread `01a0995b-2dc4-7e22-8e4e-f93b925d460c`, run
`fla_20260913_060046_a6a8ca6b`. Seventeen recorded responses reconcile;
turn interrupted, billing completeness unverified. Fresh input: 48778.
No successful task result or screenshot. Runner abort completed with no cleanup
error. The agent's close targeted Manage Best Practice Rules, not TE3; it was
not a demonstrated safety violation. Operator interruption invalidates its
success/time comparison, but the trace remains useful diagnostic evidence.

Observed before interruption: overly broad tool discovery; repeated whole-window
snapshots; guessed Alt+T/P/Ctrl+, shortcuts; blind Down-key sequence opened the
wrong dialog. No final attribution of root cause or comparison claim yet.

D1: Luna-low thread `01a0995e-2a03-70d1-97cf-bc351c9a20fa`, run
`fla_20260913_060346_3edd86a7`. Thirty-one responses, 28 FlaUI calls;
native counters reconcile and the agent turn completed. Fresh input: 43519.
The agent did not falsely claim success. The saved PNG was visually inspected
and shows the main editor. No prefix values were verified.

Candidate cause: invoking Preferences starts a pending modal pattern operation.
The tool response explicitly advises screenshots and handle-targeted keys instead
of UIA queries. The existing guide/map does not explain `physical:true`, used by
the typed runner to avoid this problem. Treat this as a hypothesis for a separate
guidance/tool intervention, not permission to change a scored arm mid-run.

A2: Luna-low thread `01a09962-c4a0-7f00-aded-24e23183cae2`, run
`fla_20260913_060841_a757e2d3`. Thirty-two recorded responses reconcile;
fresh input 86117. Thirty FlaUI calls completed; an attempted 31st triggered
operator interruption. One exec invocation contained five find calls, so counting
exec invocations alone would undercount usage. The PNG was inspected: Preferences
is open but still on the initial Power BI settings page, not Code Actions.
Earlier progress commentary mistook attempted queries for successful results;
this report uses the screenshot and final state instead. No verified prefix
values or successful completion. Interruption means billing completeness remains
unverified and the failed attempt's costs include the over-budget request.

Initial pilot conclusion: both A2 and D1 failed. D1 used fewer fresh input tokens
but this single failed pair does not establish an efficiency advantage. A2 made
guessed exact-name searches and broad snapshots; D1 hit modal-blocking guidance
and keyboard fallbacks. Test the explicit physical-open hint separately before
scaling repetitions. No server or original docs changes were made.

D+1: Luna-low thread `01a09968-cf31-7161-a9fb-d3ed689476c5`, run
`fla_20260913_061534_06ae1aa6`. Interrupted on user request, not scored as a
model failure. Twenty-seven responses reconcile; fresh input 51608. Physical
click returned success and a subsequent UIA query returned the Preferences
window, but the full task was not independently graded. Retain these costs as
excluded pilot overhead, not as evidence that D+ succeeded.

D+2: Luna-low thread `01a0996d-3017-7610-b9d4-7aac6bd12338`, run
`fla_20260913_062000_3ee74039`. Thirty-two recorded responses reconcile; fresh
input 43305. The physical opening succeeded, but repeated unscoped searches
were truncated. The agent cleared the filter without establishing the selected
Code Actions page. Its saved PNG confirms a blank content pane; preserved as
blank-after-clear.png for separate diagnosis. Stopped at the budget boundary;
over-budget request cost retained. No verified prefix values or task success.

Terra-low matched D+: thread `01a09972-6a6d-71e2-a61d-b176471ffceb`, run
`fla_20260913_062536_33199137`. Thirty FlaUI calls, 32 recorded responses;
native counters reconcile and the turn completed. Fresh input: 45014.
The agent reported success and prefixes `_` and `@`, but visual inspection of
agent-code-actions.png shows an empty search, selected Code Actions row and
blank content. Independent grading passed search/selection checks but could
not find the visible Variable prefix control. Do not count the reported values
as verified. Model-unchanged verification did not complete after the failure.
Settings were restored; no recovery or cleanup error remains.

This single matched comparison shows no successful task for either model.
Terra reached the selected category within the call budget, but its false
success report is a reliability failure. Fresh input was slightly higher than
Luna D+2 (45014 versus 43305); neither result establishes a model advantage.
The blank-pane interaction should be isolated before further scored trials.
