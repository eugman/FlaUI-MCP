# Capture recipes

| Recipe | State | Engine |
|---|---|---|
| model-open | Loaded model and Sales | No |
| model-calculation-group-menu | Model menu, Add Calculation Group highlighted; Escape without executing | No |
| preferences-map | Open Preferences, map visible controls, capture, Cancel (discovery only) | No |
| preferences-language-choices | Verified User Interface pane and open six-language list; English unchanged; Escape/Cancel. Excluded from default batch | No |
| preferences-save-to-folder | Default/mode map, temporary customizable settings, original checked tree and selected Data Sources; Cancel. Larger than the original to avoid clipping | No |
| preferences-auto-formatting | Original checkbox/numeric/casing states, unfiltered selected section, 1155x714 | No |
| preferences-code-actions | Original toggle/prefix/disabled-casing states, 1155x714; extra tree categories differ | No |
| preferences-dax-general | Verified DAX Editor > General transition, pane identity and original values, 1155x714; current layout differs | No |
| preferences-file-formats | File Formats parent and serialization pane, 13 toggles, Always ask and disabled current compatibility 1600; guarded arrow anchors | No |
| measure-edit | UI Revenue expression and result 61 | Yes |
| dax-query | UI Revenue result grid | Yes |
| expression-recovery | Diagnostic and corrected result 62 | Yes |
| qualified-navigation | Duplicate Amount names in distinct tables | No |
| bpa-correction | Pinned violation, description fix, refreshed absence | Yes |
| properties | Format string and nested display folder | Yes |
| csharp-preview | Cancel/accept description change | Yes |
| csharp-preview-page | Editor and expanded preview components | No |
| csharp-output-scalar | Scalar output dialog | No |
| csharp-scalar-scene | Original source strip and compact output dialog in one real scene | No |
| csharp-output-object | Object output properties | No |
| csharp-output-values | Multiple output values | No |
| file-load-three-cycles | Exact source/output/close repeated in one window | No |
| csharp-auto-rollback-source | Original 21-line source and enabled Auto-rollback; never executed | No |

See VERIFICATION.md for live results. Seventeen offline capture recipes are verified individually;
the default batch includes fifteen, omitting the discovery-only preferences-map.
Save-to-folder passed its final visibility checks with identical pre/post-reboot PNGs.
Auto-rollback has an original-sized, outlined candidate; it is not approved/promoted.

## Repeatability and recovery

To reproduce the language-choice candidate (not approved for replacement):

```powershell
./src/FlaUI.Automation/bin/Debug/net8.0-windows/FlaUI.Automation.exe run automation/offline.config.json --scenario preferences-language-choices
```

Targets `content/assets/images/user-interface/chaning-language-preferences.png`
on the application-language page. Saves a scoped pane map and popup map; requires
the six observed TE3 3.26.3 labels, English selected and a visible popup adjacent to its
combo. Uses actual screen pixels for the dialog/popup union, not a background
dialog-only capture. The 1155x714 dialog differs from the historical 1917x1275
scene; inspect readability, all options and clipping before accepting a candidate.
Failures attempt Escape; runner recovery still owns restoring settings/closing TE3.
The first live map confirmed all six choices; current TE3 capitalizes Español
and Français, unlike the historical screenshot. That failed attempt restored
settings and produced no candidate PNG; exact current captions are now asserted.
Follow-up `fla_20260913_064543_7a6b4d92` passed labels, selection and adjacency
but rejected the popup's 10-pixel shadow (the menu guard expected 7). Language
capture now explicitly permits the observed 10-pixel same-process, untitled
shadow geometry; other obstructions remain rejected. Unit checks pass, but the
updated capture passed in `fla_20260913_064954_369bac03`. The PNG was inspected:
all six choices are readable, English selected, popup fully included. Model
unchanged and settings restored. Current categories, size and typography differ
from the original; this is a candidate, not an approved replacement.

Do not launch more live tests while memory is exhausted. Check Windows memory
headroom and confirm TE3 is closed; do not terminate unrelated processes to make
room. Desktop permission must still be in force. Keep using offline.config.json:
the server-backed recipes and new infrastructure remain deferred.

To repeat the checked-tree recipe:

```powershell
./src/FlaUI.Automation/bin/Debug/net8.0-windows/FlaUI.Automation.exe run automation/offline.config.json --scenario preferences-save-to-folder --repeat 2
```

For each run, inspect the final manifest for success, settingsRestored, no pending
recovery and the unchanged-model check. Then inspect preferences-save-to-folder-custom.png:
three serialization toggles checked, prefix off, Data Sources selected, Tables
expanded, and the six original checked rows visible. Compare the two PNG hashes.
Do not use the clipped 809x437 experiment as an update candidate. The readable
1155x714 capture still differs in mode selector, size and left-tree viewport.

Use preferences-file-formats as a Preferences regression check and compose its
arrows as below. After an interruption, inspect the latest manifest and run
`recover MANIFEST` if settings were not restored, even if needsRecovery is false:
an interrupted manifest may still contain its initial flags. Next targets are
the language-dropdown scene and context/cascade menus; retain exact original
content requirements in screenshot-backlog.json instead of substituting generic views.

## Annotated File Formats image

File Formats keeps the raw image and adds the original-style arrows separately:

```powershell
./src/FlaUI.Automation/bin/Debug/net8.0-windows/FlaUI.Automation.exe compose RUN/manifest.json automation/file-formats.composition.json RUN/file-formats-annotated.png
```

Use a run with the annotation-anchor checks; older raw runs did not verify them.
The annotated image still differs from the historical version's layout and 1500
compatibility label. It is not an approved docs replacement.

C# recipes load run-local .csx files through Ctrl+O and assert exact source before
execution. Do not fall back to typing on mismatch: autocomplete previously changed
Output() into Aggregate(). Close the output before closing the script tab.
Preview recipes assert old/new values before accept/revert; persisted checks are
independent of the UI.

The backlog retains all 596 sources, docs pages, priorities, blockers and reviewed
matching requirements. A linked recipe means related capability, not completion.
No original is approved/promoted. First reviewed targets are L044 scalar output,
L042 auto-rollback, L043 enumerable output and L010 Internet Sales Create > Measure.
Match original code, model names, selection and composition; same-feature images
are not replacements. Keep external infrastructure and missing-source blockers explicit.

Original-image inspection adds these constraints:

- L002: `run automation/offline.config.json --scenario model-calculation-group-menu`
  captures the complete menu with Calculation Group highlighted and Alt+7 visible.
  Current TE3 says Add rather than New; styling and offline model title differ.
  No group is created. The run also saves a scoped `.uia.json` menu map. This is
  an attempted offline counterpart, not a replica of the original Power BI connection.
- L044: `run automation/offline.config.json --scenario csharp-scalar-scene` loads
  exactly `"Hello World".Output();` and captures the compact dialog with the source
  behind it. Actual screen pixels, no stretching; current theme and saved `.csx`
  tab differ from the original. The checkbox label clips in this TE3/font layout,
  even with a wider dialog; do not treat it as ready to replace the original.
  Foreground, geometry and occlusion checks surround
  capture. The observed TE3 shadow window is allowed only with matching process,
  empty title and exact 10-pixel surround. Human visual approval remains required.
- L042: `replica-inputs/Columns to Measures.csx` transcribes all 21 visible lines.
  Load/display only for this screenshot; running the script is not required.
  Need the named document, all lines visible and the highlighted Auto-rollback control.
- L045: despite its output-to-string filename, the original shows the same
  Columns to Measures / Auto-rollback view, not a scalar output dialog. Treat it
  as a source-content discrepancy rather than inventing a different replacement.
- L046: the original is an annotated composite with a Fix All BPA Violations*
  source strip and a Budget-model preview. It shows Budget (EUR), Customer Key,
  isAvailableIn... changing to False, and deletion of Measure Selection perspective.
  Our Sales description-change preview proves the control path only; it is not
  an attempted replica. Exact model/script preparation and annotation layout remain.

Capability order from the inspected originals:

1. Compact dialog placement and source/dialog framing: L044.
2. Display-only script fit, document naming and control annotation: L042; L045
  needs an editorial decision because the source image contradicts its prose.
3. Model-menu capture now works for L002. Next extend to scoped context/cascade
   menus for L003, L005, L010. L003 additionally needs an offline calculation group.
4. Exact partial-expression caret and completion state: L004. A complete measure
   screenshot is not equivalent to the original IntelliSense demonstration.
5. Exact multi-object preview fixture and composite annotation: L046.

Original Power BI connection titles are not available from offline fixtures.
Record that difference rather than fabricating a connection or creating new databases.

Preferences navigation now works for Auto Formatting: Tools > Preferences,
search, select the TreeItem by Value, clear search, bounded visible-page navigation,
reselect and assert SelectionItem. The recipe checks every original toggle/value,
requires exact image dimensions, and Cancels without saving. Current categories
and search box differ from the historical UI. Code Actions reuses this path and
also passes its original control-state assertions. Its top row is verified as
Pivot Grid and Model Deployment must be visible; the original bottom-row framing
is not matched because the current layout shows additional categories below it.

| Target | Additional capability after opening Preferences | Constraint |
|---|---|---|
| L026 Auto Formatting | Implemented and live-verified | 14 checkbox states, 120/60 limits, Default casings; Cancel |
| L049 Code Actions | Implemented and live-verified, partial tree-frame fidelity | Underscore and @, Default casing disabled; extra lower tree rows remain |
| L455 Language | User Interface pane, scrolling, open combo and popup scene capture | Do not choose another language or restart; normal Markdown link maps to application-language.md |
| L323 Historical Features | Compare old Features pane with current category split | No mapped page; do not recreate server traces or activate tracking |
| L187 Built-in BPA Rules | Separate Manage Best Practice Rules dialog and selected rule grid | Not Preferences; do not click Disable All; versioned built-in inventory |

L323 and L187 are not substitutes for the simpler Preferences captures. Original
dimensions and exact visible states are recorded in the backlog. Language menus
may expose different installed options now; retain that version difference rather
than fabricating a historical dropdown.

The next inspected targets refine the capability order:

| Capability | Targets | What must be added |
|---|---|---|
| Logical parent/child Preferences navigation | L155 DAX General; L064 File Formats General | Implemented and tested for DAX General; apply to File Formats next. Verifies the keyboard transition and adjacent selected row, not a global General name match |
| Checked serialization tree | L078 Save-to-folder | Verify checked nodes, expanded Tables and exact viewport; no save operation needed |
| Annotated settings capture | L064 | Two arrows on the actual General/Save-to-folder rows; compatibility label may be version-dependent |
| Edit-menu context | L176 | Establish active editable measure/document and command states; do not execute clipboard/delete actions |
| Transient document switcher | L119 | Six named tabs, populated diagram preview, held-modifier lifetime with guaranteed release; investigate offline Pivot Grid availability |
| Historical compiler pane | L120 | First verify whether the old blank configuration UI exists; no compiler installation or execution |

Keep L120 behind currently linked pages: no reference to its filename was found in
the current docs content. L119 is not a blank-shell target and must not be counted
as reproduced by opening an empty Ctrl+Tab switcher. The backlog records all exact
visible states and original dimensions for these six targets.

Inventory audit: 483 local files were available for SHA-256 comparison. Two exact
file pairs share captures: L221 duplicates L315 (pivot menu), and L305 duplicates
L183 (Excel/ODBC image). `duplicateOf` records this without removing page mappings.
Similarity alone does not establish duplication: L042/L045 look alike but have
different file hashes. The basic/active-document shell images L034/L001 require
populated AdventureWorks states, not an empty startup window. L026 preferences
is the smaller offline shell target: navigate, assert settings, capture, Cancel.
