# Docs owner report

Screenshots in the docs that we can't regenerate as they are, because the UI they show no longer exists or
the image and its page disagree. Current state is from a static review of the original images against
Tabular Editor 3.26. Rows marked **to confirm** are checked in the next approved TE3 discovery run.

Source of truth for every item: `automation/screenshot-backlog.json`.

## UI no longer exists

| Item | Image | Used on | What it shows | Current TE3 | Suggestion |
|---|---|---|---|---|---|
| L120 | `content/assets/images/custom-compiler-te3.png` | No page | Preferences > C# Scripts and Macros with an empty custom compiler path and options | Confirmed 2026-09-15 (3.26.3): searching Preferences for "Compiler" finds no section | Delete the image if no page needs it |
| L210 | `content/assets/images/features/dax-optimizer-preview.png` | No page | View menu with "DAX Optimizer (Preview)" | The label no longer says "(Preview)"; otherwise the same as L212 (`dax-optimizer-view-menu.png`) | Delete; L212 covers it |
| L276 | `content/assets/images/import-tables-wizard.png` | `content/features/import-tables.partial.md` | Early 3.0 Import Tables wizard first page (legacy/structured choices, blue sidebar) | The wizard has been redesigned | Replace with a capture of the current first page (L084 shows the new data source page) |
| L322 | `content/assets/images/pref-general-features.png` | `content/references/preferences.md`, `content/getting-started/personalizing-te3.md` | Old Preferences > Tabular Editor > Features pane with trace count and cleanup button | Confirmed 2026-09-15 (3.26.3): searching Preferences for "Features" finds no section; its settings live in newer sections | Replace with the current sections and update the page text that walks through the old pane |
| L323 | `content/assets/images/preferences-features.png` | No page | Same old Features pane, different checkbox states | As L322 | Delete if unused |
| R032 | GitHub user image `170603450-8232ad55-...png` | `content/how-tos/incremental-refresh2-h.md` | Tabular Editor 2 property grid with the Refresh Policy SourceExpression editor open | TE3 shows refresh policy properties in its own grid | Replace with a TE3 capture of the same properties, or state that the image is TE2 |

## Image and page disagree

| Item | Image | Used on | Issue | Suggestion |
|---|---|---|---|---|
| L045 | `content/assets/images/c-sharp-script-output-to-string-function.png` | `content/features/csharp-scripts.md` | The image shows the Columns to Measures script with Auto-rollback highlighted, but the surrounding text explains IEnumerable output | Decide whether the text or the image is right |

## Source missing

| Item | Source | Used on | Issue |
|---|---|---|---|
| R013 | `https://docs.tabulareditor.com/images/tmdl-options.png` | `content/references/release-notes/3_14_0.md` | Returns 404 |
| R017 | `https://github.com/TabularEditor3/PublicPreview/blob/master/update%20schema.gif?raw=true` | `content/references/release-notes/beta-18_2.md`, `content/references/release-notes/beta-18_1.md` | Returns 404 |

## Images no page uses

46 backlog images are not referenced by any docs page. They may be safe to delete, or they may be
referenced in ways the scan missed (for example from HTML or includes):

L053 L056 L057 L070 L120 L132 L137 L140 L173 L181 L207 L210 L221 L242 L269 L284 L286 L287 L288 L323
L338 L342 L345 L353 L355 L356 L367 L368 L369 L372 L373 L406 L409 L410 L413 L414 L416 L419 L423 L427
L428 L430 L444 L456 L457 L464
