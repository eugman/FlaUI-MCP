# Current verification status

Updated after 2026-09-13 UTC offline runs. Passing tests are not visual approval.

| Area | Current evidence |
|---|---|
| Generic MCP unit tests | 103 passed in Foundation; includes pending-batch stop, empty keyboard-fill, snapshot lifetime, complete key prevalidation, screenshot cancellation/strict-input and pending-process identity tests. New fixes not yet live-verified/published |
| Runner unit tests | 77 passed in Foundation; includes language-list, agent-handoff, display metadata and interrupted fixture-removal checks |
| Generic live integration | 30 passed; artifacts/live-refactor/live-refactor.trx |
| Earlier engine smoke | 7/8 passed; engine C# preview not rerun after absence fix |
| Offline capabilities | 17 capture recipes verified individually; default batch has 15 and excludes preferences-map and preferences-language-choices. Diagnostics are not counted |
| Original eight-recipe offline batch | All passed, 20 checkpoints; runs from fla_20260913_013720_5d1c54b8 through fla_20260913_014205_b1f2881e |
| Replica attempts | Auto-rollback, scalar scene, Calculation Group menu, Auto Formatting, Code Actions, DAX General, annotated File Formats, readable Save-to-folder |
| Approval/promotion | 0 approved or promoted; original docs untouched |
| Backlog | 596 entries, 27 detailed targets, one source-content discrepancy |
| Server effects | These follow-ups used no database and made no server changes |
| Cleanup | Successful recipes verified unchanged models. Latest failed runs restored preferences with no recovery pending, but did not complete the model check; TE3 closed |
| Deferred | Source-backed/live infrastructure, new databases and providers |
| Language dropdown | fla_20260913_064954_369bac03 passed after narrow 10px shadow allowance; six choices/English/adjacency checked and PNG inspected. Still excluded from default batch |
| Preferences blank-pane diagnosis | fla_20260913_064346_eb12da68: actual screen and background captures agree; both Value-pattern and keyboard clearing blank content, physical reselection restores it. Model unchanged/settings restored |
| Live-run resource pause | After the last UIA timeouts, Windows reported 1,924 KB free physical memory and 1,094,308 KB free virtual memory. Two Python processes (26824/38084) held about 48.75/47.87 GB private memory; WindowsTerminal about 10.28 GB. TE3/runner were closed. No unrelated process was terminated. Recheck memory headroom before resuming live tests |

## Latest reproducible image evidence

All paths below are under `artifacts/te3/`; each run has its manifest and HTML index.

| Target | Successful run(s) | Fidelity / repeatability |
|---|---|---|
| Language choices L455 | fla_20260913_064954_369bac03; fla_20260913_065111_e54792ca | Two passed runs with byte-identical 1155x714 PNGs; all six options readable, English selected. Original 1917x1275 has different typography/category layout; not approved |
| Auto-rollback L042 | fla_20260913_014630_602c9aa0 | auto-rollback-replica.png: 1063x537, 21 exact source lines, red control outline; current style and .csx suffix differ |
| Scalar output L044 | fla_20260913_020318_7ae1b72f; fla_20260913_020407_ae210591 | 704x361, actual source/dialog scene; PNGs byte-identical; checkbox label still clips |
| Scalar regression | fla_20260913_022550_aeb7e306 | Passed after sharing popup capture guard |
| Calculation Group menu L002 | fla_20260913_022259_3dcb8d89; repeats fla_20260913_022425_34bd94d3 and fla_20260913_022446_8d78bdd1 | Exact highlighted action and Alt+7; menu-region pixels identical; full images differ in run title; Add wording/offline title differ from original |
| Preferences entry map | fla_20260913_023447_78303c04; fla_20260913_023621_86575dce | Open, inspect, capture, Cancel; discovery only, not an original replica |
| Save-to-folder L078 staging | fla_20260913_034019_10913ee4 | Customizable mode, three checked options, expanded Tables, six checked rows through legacy accessibility; 1155x714 capture inspected. Settings restored/model unchanged; not in default batch |
| Save-to-folder original size | fla_20260913_034155_c652f773 | 809x437 accepted but visually clips almost all depth rows; not a usable replica |
| Save-to-folder final recheck | fla_20260913_044828_a3153147; fla_20260913_052910_55bf12c9 | Passed final visibility/state assertions; PNGs byte-identical across reboot. Settings restored/model unchanged. Earlier memory-pressure timeouts remain failed evidence |
| Interrupted-run recovery | fla_20260913_045030_34d9f908; fla_20260913_052024_4d4d674e | Explicit recovery restored durable settings backups after reboot/session crash. No recovery pending; interrupted runs are not passes |
| Auto Formatting L026 | fla_20260913_024845_33860fa4; fla_20260913_025117_c44e7b1b | 1155x714, 14 toggles plus 120/60 and Default casings asserted; PNGs byte-identical; current search box/categories/style differ |
| Code Actions L049 | fla_20260913_025738_9a36d843; fla_20260913_025806_8c95c246 | 1155x714, toggles/prefixes/disabled casing asserted; PNGs byte-identical; extra lower tree categories mean original bottom framing is not matched |
| DAX General L155 | fla_20260913_031309_b13c5644; fla_20260913_031352_caba7491 | 1155x714, logical parent/child selection, pane identity and original values asserted; PNGs byte-identical; newer layout/text size, extra option and disabled timeout differ |
| File Formats L064 | fla_20260913_032630_72ad56e0; fla_20260913_032701_96615c55 | 1389x855, state and pre/post-capture annotation anchors asserted; separate composed green arrows; compatibility1600 vs1500 and new layout remain differences |

Auto Formatting PNG SHA-256:
`4E3A8E3D2CB73273B8DD821E15490675273A851AD16C15C801C2AFEF1C581606`.
Scalar PNG SHA-256:
`8537A8058EFE4C8542B68C5DD6DF59435E5A53AF97E9E28261A68C56D5253673`.
Code Actions PNG SHA-256:
`F9BB8617A83D1AD8A5F9FE9475A02A67F46D7E850A87373AEBB246932C27A528`.
DAX General PNG SHA-256:
`48A8B6E7F5ACCDB53F0E941E70FA9CB28229C3E3D17A9EB6B5B709ABF7AABF6E`.
File Formats PNG SHA-256:
`24263BB78CE74952C741581DA7F89A3ED82F3BB019146412E71D25A862ADF5A5`.
File Formats annotated PNG SHA-256 (both latest runs):
`1F1D31D464DA5D89B7519551FCD8CCAD8EDD310C15A505E31150A63188E1D920`.
Menu region (272,52,405,507) PNG SHA-256:
`3D01CF4000F4CE1378B9FC9A48381F44BB531233BEC6F6114B1C7C2BFA4F1C9A`.

## Agent-efficiency baseline

Saved run fla_20260913_052910_55bf12c9 logged 23 internal MCP calls with 1,663
characters across text response blocks: click273, fill44, placement682,
screenshot477 and keys187. Both screenshots were artifact-only responses.
These are characters, not tokenizer counts. Direct C# selector/state checks and
image-inspection costs are excluded; this is not an end-to-end token benchmark or
a before/after savings claim. The runner returns a short final result rather than
requiring an agent to read the internal action ledger.

Next measure matched tasks through interactive MCP and side-automation: success,
agent-visible response size, calls, screenshots and retries. Consult the compact
AGENT-QUICKSTART.md before loading the 596-image backlog or full UI map.

Per-agent rollout telemetry was verified against the Luna-low metadata probe;
see ABLATION.md. No scored UI ablation has run. Current source was published to
the separate `artifacts/mcp-ablation-20260913` directory; existing MCP processes
were not replaced or restarted.

## Boundaries and next work

Terra reviewed the dialog absence fix, popup capture safety, final menu selection,
and Preferences selection/state/geometry checks. Live failures informed native
readiness waits and scoped tree mapping; they were not counted as successful captures.
Whole-Preferences exploration was removed in favor of required tree/pane scopes.
Details and locators belong in UI-MAP.md; reproduction commands belong in RECIPES.md.

Next: language-dropdown scene and context/cascade menus;
resolve the scalar label defect. No original is approved for replacement.
Broad offline desktop permission remains in force until revoked or the handoff ends.
No publishing over a running MCP server. Removed pre-refactor work remains recoverable
in `artifacts/refactor-backup-20260912-172012`; older run manifests remain in artifacts.
