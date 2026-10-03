# Antibody identification capability matrix

Decision-support software for trained blood-bank professionals. The engine
computes evidence; a qualified user must confirm identification. Scores and
rule-outs are analytical evidence, not a diagnosis.

Status: **Present** (usable), **Partial** (exists but incomplete or silent),
**Missing**.

Last updated: 2026-10-03 (iteration 16 — cold/warm/panreactive pattern evidence).

## Capability matrix

| Area | Capability | Status | Current evidence | Next action |
|---|---|---|---|---|
| Gap file | Living capability matrix | Present | This file | Update every iteration |
| Import | Provider adapter framework | Partial | `IVendorPanelSource` + Bio-Rad/Ortho/Quotient public catalogs; Immucor/Grifols/Medion file-only | Authenticated portals only with vendor permission |
| Import | CSV | Present | Lab CSV + vendor CSV parser; lab import now reviews before activation | — |
| Import | PDF | Partial | PdfPig positional grid; layout-fragile | Harden after schema work |
| Import | XLSX / XML / JSON panels | Present | JSON/XML structured schema; first-sheet XLSX via zip/shared strings; same inactive+artifact review | Multi-sheet XLSX later |
| Import | Vendor APIs / authenticated portals | Missing | Public HTML + file import only. Do not bypass auth, CAPTCHA, licensing, or terms | Secrets storage only if a vendor authorizes access |
| Common model | Manufacturer, lot, expiration, cell, ABO/Rh, standard antigens | Partial | `Panel` / `PanelCell`; antigens stored as `+`/`-` | Persist NT / unknown / zygosity |
| Common model | Homozygous / heterozygous / negative / unknown / NT | Partial | Import NT / missing columns stay untyped; zygosity still inferred from typed antitheticals | Persist explicit homozygous state later |
| Import validation | Required fields, duplicate lot | Partial | Parse fails without cells; `FindPanelByVendorLot` | Impossible-value checks later |
| Import validation | Impossible values, schema-vs-prior-lot, unknown antigens | Partial | NT/`?` not coerced to `-`; vendor and lab CSV compare antigen schema vs prior lot | Impossible-value catalog later |
| Import traceability | Original artifact retained | Present | Vendor and lab CSV store SHA-256 + file; Artifact viewer verifies and opens | — |
| Reaction entry | IS / 37°C / AHG / CC | Present | Keyboard 0–4, W/M/H, N, Enter | — |
| Reaction entry | w+, MF, hemolysis | Present | Combo + keys; treated as reactive evidence | — |
| Reaction entry | RT, PEG, gel, solid phase as phases | Partial | Extra phases stored as JSON; Gel/Solid/PEG count as IAT-like in pattern labels | Lab default ExtraPhases list |
| Analysis | Transparent candidate evidence | Partial | Explain Analysis lists suspected, in-progress, historical, and pattern candidates | Never-imported specificities; pattern classifiers |
| Analysis | Configurable visible rule-out | Partial | `Rule.MinRuleoutCount` is now applied; lab default + explanation sentences | Watch seeded anti-D/anti-k rules; ACS remains separate |
| Analysis | Single / multiple / dosage scoring | Partial | Fisher + pattern; pairwise combinations; dosage averages | Label as evidence, not diagnosis |
| Analysis | Panreactive / cold / warm / HTLA / auto / HFA / LFA models | Partial | Cold, warm/IAT, mixed-phase, panreactive sentences; Gel/Solid as IAT | HTLA / HFA / LFA later |
| Analysis | Selected-cell recommendations with “why” | Partial | Ranked unused inventory cells + “why this cell” on Analysis tab | Prefer unused selected-cell vials / phenotype later |
| Analysis | Patient phenotype / genotype / transfusion limits | Partial | Parses Weiner/Duffy-style text + previous Abs; transfusion notes mark typing uninterpretable | Structured genotype fields later |
| UI | Antigram freeze / slashes / run compare | Partial | Frozen cell; slashes; compare extras; Show dosage; filter/sort; panel lot compare | — |
| UI | Explain Analysis panel | Present | Explain tab + Summary + clinical report assemble rule-out, support, conflict, phenotype, additional testing | Keep wording as evidence, not diagnosis |
| Audit | Panel, reactions, rules, settings, final ID | Partial | `audit_events` + analysis snapshots; panel activate/deactivate logged; artifact hash visible | Rule-version detail |
| Tests | Synthetic cases with intermediate reasoning | Partial | Includes special-grade, extra-phase, and artifact-integrity cases | Expand case library |

## Iteration 1 (this change)

**Deficiency:** Labs can configure `MinRuleoutCount` and heterozygous exceptions,
but the analyzer treated any single qualifying homozygous cell as ruled out and
never explained the policy.

**Bounded improvement:** Apply `DefaultMinRuleoutCount` (lab default 1) and
per-antibody `Rule.MinRuleoutCount`. Separate observed qualifying cells from
“meets configured rule-out.” Show an explainable sentence. ACS continues to use
`AcsRuleoutCount` against **observed** qualifying counts.

**Not in this tick:** import NT semantics, selected cells, phenotype engine,
XLSX/portals.

## Iteration 2 (this change)

**Deficiency:** Vendor sheets that omit an antigen column, or mark a cell NT,
were persisted as typed `-`. That made E+ look homozygous when `e` was never
typed, and let untested antigens enter Ag− scoring.

**Bounded improvement:** Persist missing/NT as not-tested. Analysis only uses
antigens the panel actually types. Antithetical zygosity requires a typed
partner. Summary and suggestions list clinically significant antigens that
were not tested.

**Not in this tick:** selected cells, phenotype engine, XLSX/portals, import
review-before-activation.

## Iteration 3 (this change)

**Deficiency:** After remaining candidates were listed, the app only said
“add more Ag+ cells.” It did not rank unused inventory cells or explain why
a cell would discriminate competing antibodies.

**Bounded improvement:** Score unused active-panel cells for remaining
candidates. Prefer homozygous expression of the antigen of interest, cells
that lack competing antigens, and cells that resolve the most competing
pairs. Show explanations on the Selected Cells tab, Summary, and suggestions.

**Not in this tick:** phenotype/genotype limits, full per-candidate narrative
for non-suspected antibodies, XLSX/portals.

## Iteration 4 (this change)

**Deficiency:** Specimen phenotype and previous antibodies were display-only.
A patient who typed E+ could still be left with silent anti-E suspicion, and
recent transfusion was not treated as a limit on interpretation.

**Bounded improvement:** Parse Weiner and antigen text into support / against /
uninterpretable / historical considerations. Do not auto-remove candidates.
Notes mentioning transfusion make phenotype evidence uninterpretable.

**Not in this tick:** structured genotype columns, per-candidate narrative for
non-suspected antibodies, XLSX/portals.

## Iteration 5 (this change)

**Deficiency:** Supporting and conflicting cells existed only for antibodies
that already crossed the suspected threshold. Ruled-out, in-progress, and
historical specificities had no assembled narrative a reviewer could read
in one place.

**Bounded improvement:** `AnalysisExplainer` builds a per-candidate explanation
from existing rule-out evaluations, suspected evidence, dosage, phase scores,
patient typing, and selected-cell recommendations. The Analysis Explain tab,
Summary, suggestions, and clinical report show that text. Nothing is
auto-confirmed.

**Not in this tick:** import review-before-activation, genotype columns,
XLSX/portals, broader reaction grades/phases.

## Iteration 6 (this change)

**Deficiency:** Vendor lots became active as soon as parse succeeded. The
original file was not retained, and a new lot that dropped or added an
antigen column was not compared to the previous lot from that vendor.

**Bounded improvement:** Vendor imports stay inactive. SHA-256 of the source
bytes is stored and the file is copied beside the database. The review
sentence names added/removed antigens versus the prior lot. A qualified user
must activate the panel before it enters inventory.

**Not in this tick:** lab-CSV artifact retention, XLSX/portals, broader
reaction grades/phases, structured genotype fields.

## Iteration 7 (this change)

**Deficiency:** Lab CSV import typed missing antigen columns as `-`, kept
no source file, and activated from the details dialog without comparing the
sheet to the prior lot.

**Bounded improvement:** Missing CSV columns stay untyped. `LabPanelImportService`
stores SHA-256 + the original file, compares antigen headers to the prior
lot, and leaves the panel inactive until review. Same review dialog as vendor
import.

**Not in this tick:** artifact viewer, XLSX/portals, broader reaction
grades/phases, structured genotype fields.

## Iteration 8 (this change)

**Deficiency:** Reaction entry and scoring only knew 0–4+ and NT. A weak,
mixed-field, or hemolyzed result had no grade. `ReactionToNumeric` treated
those strings as 0, so they could disappear from “strongest phase” even when
`IsPositive` saw them as non-zero text.

**Bounded improvement:** Canonical grades `w+`, `MF`, and `H` are selectable
and typed (W/M/H). They count as reactive evidence, cannot support rule-out,
and produce a reviewable sentence. RT/PEG/gel remain later as extra phases.

**Not in this tick:** configurable extra phases, artifact viewer, XLSX/portals.

## Iteration 9 (this change)

**Deficiency:** Labs that record RT, PEG, or gel reactivity had no column.
Those results were either omitted or stuffed into AHG, so a room-temp-only
pattern could look like a valid AHG-negative rule-out.

**Bounded improvement:** Lab Preferences accepts extra phase names. Grades
are stored as JSON on the reaction. Extra-phase reactivity counts as
positive evidence and cannot support rule-out. Prewarm still suppresses RT
with IS. Classic IS / 37°C / AHG / CC is unchanged when the setting is blank.

**Not in this tick:** artifact viewer, XLSX/portals, compare-grid extra columns.

## Iteration 10 (this change)

**Deficiency:** Vendor and lab CSV imports stored a SHA-256 and a copy of
the source file, but the UI never showed whether that file still existed or
still matched.

**Bounded improvement:** The Panels **Artifact** button inspects the retained
file, reports present / missing / hash-mismatch, previews CSV text, and can
open the original. Panel details show the stored hash. Nothing is
auto-activated.

**Not in this tick:** XLSX/XML/JSON adapters, compare-grid extra columns.

## Iteration 11 (this change)

**Deficiency:** Labs and vendors could import PDF or CSV only. Spreadsheet
exports and structured panel files were rejected or misread as CSV.

**Bounded improvement:** Vendor and lab import accept JSON, XML, and first-sheet
XLSX. JSON/XML carry lot, catalog, and per-cell antigens without inventing
types for omitted antigens. XLSX uses the same grid rules as CSV (Cell column
plus recognized antigen headers). Imports stay inactive with a retained
artifact. No new packages; no authenticated vendor portals.

**Not in this tick:** multi-sheet workbooks, antigram grouping / dosage toggle,
compare-grid extra columns, authenticated APIs.

## Iteration 12 (this change)

**Deficiency:** Extra phases (RT, PEG, gel) were entered and scored, but the
run-vs-run compare grid only showed IS / 37°C / AHG / CC. A cell that was
AHG-negative on both runs and RT 2+ on one looked unchanged.

**Bounded improvement:** Compare rows include configured extra phases (and any
extras stored on either run). A difference at RT/PEG/gel marks the row changed
and produces a sentence such as `Cell 1 differs: RT (this 2+ vs other 0).`
Missing extra on the other run is NT. Classic phases still compare as before.

**Not in this tick:** antigram grouping / dosage toggle, solid-phase defaults,
authenticated APIs.

## Iteration 13 (this change)

**Deficiency:** The reaction antigram showed only +/−. Homozygous and
heterozygous cells looked the same, and columns followed sheet order with no
blood-group grouping. Dosage lived only on a later Analysis tab.

**Bounded improvement:** Reactions **Show dosage** groups visible columns by
system (Rh, Kell, Duffy, …) and marks homozygous antigen-positive cells as
`++`, heterozygous as `+`, and `+/?` when the antithetical partner was never
typed. Tooltips name the cell and zygosity. Off restores the panel column
order and plain +/−. Rule-out is unchanged.

**Not in this tick:** filter/sort, panel compare, solid-phase defaults,
authenticated APIs.

## Iteration 14 (this change)

**Deficiency:** The reaction antigram always showed every cell in vial order.
A reviewer looking for homozygous E+ or reactive cells had to scan the whole
sheet, and there was no way to hide Ag− rows without losing them from save.

**Bounded improvement:** Filter the visible grid by antigen and zygosity, and
sort by cell number, reactive first, or selected-antigen first. Autocontrol
stays visible. Hidden cells remain in the working set for save, compare, and
analysis. A sentence states how many cells match.

**Not in this tick:** panel compare, solid-phase defaults, authenticated APIs.

## Iteration 15 (this change)

**Deficiency:** Import review compared antigen *columns* to the prior lot, but
labs could not open two stored panels and see which *cells* changed typing.
A new lot that flipped cell 1 from E+ to E− looked the same if the header
list matched.

**Bounded improvement:** Panels **Compare…** diffs the selected lot against
another stored panel (defaults to the prior vendor/product lot). It reports
added/removed antigens, cells present on only one lot, and per-cell typing
changes. Missing types stay NT, not invented negatives.

**Not in this tick:** solid-phase defaults, authenticated APIs.

## Iteration 16 (this change)

**Deficiency:** Phase scores existed, but a sheet that was all IS 2+ / AHG 0
looked like ordinary negative AHG rule-outs. Gel and Solid reactivity had no
IAT-like meaning in the narrative.

**Bounded improvement:** Classify cold (IS/RT+ AHG/IAT−), warm (IAT+ IS/RT−),
mixed-phase, and panreactive patterns when at least three evaluated cells
match. Gel, Solid, and PEG count as IAT-like. Prewarm still ignores IS/RT.
Sentences appear on Explain, Summary, and suggestions. Nothing is
auto-confirmed.

**Not in this tick:** HTLA / HFA / LFA models, ExtraPhases lab default list,
authenticated APIs.

## Likely next tick

HTLA / high-prevalence pattern notes, or a lab default ExtraPhases list.
Authenticated vendor portals stay later and must not bypass auth, CAPTCHA,
licensing, or terms.

