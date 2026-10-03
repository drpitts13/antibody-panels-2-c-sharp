# Antibody identification capability matrix

Decision-support software for trained blood-bank professionals. The engine
computes evidence; a qualified user must confirm identification. Scores and
rule-outs are analytical evidence, not a diagnosis.

Status: **Present** (usable), **Partial** (exists but incomplete or silent),
**Missing**.

Last updated: 2026-10-03 (iteration 7 — lab CSV artifact, schema compare, no invented negatives).

## Capability matrix

| Area | Capability | Status | Current evidence | Next action |
|---|---|---|---|---|
| Gap file | Living capability matrix | Present | This file | Update every iteration |
| Import | Provider adapter framework | Partial | `IVendorPanelSource` + Bio-Rad/Ortho/Quotient public catalogs; Immucor/Grifols/Medion file-only | XLSX/XML/JSON adapters later |
| Import | CSV | Present | Lab CSV + vendor CSV parser; lab import now reviews before activation | — |
| Import | PDF | Partial | PdfPig positional grid; layout-fragile | Harden after schema work |
| Import | XLSX / XML / JSON panels | Missing | Settings/snapshots only | Later tick |
| Import | Vendor APIs / authenticated portals | Missing | Public HTML + file import only. Do not bypass auth, CAPTCHA, licensing, or terms | Secrets storage only if a vendor authorizes access |
| Common model | Manufacturer, lot, expiration, cell, ABO/Rh, standard antigens | Partial | `Panel` / `PanelCell`; antigens stored as `+`/`-` | Persist NT / unknown / zygosity |
| Common model | Homozygous / heterozygous / negative / unknown / NT | Partial | Import NT / missing columns stay untyped; zygosity still inferred from typed antitheticals | Persist explicit homozygous state later |
| Import validation | Required fields, duplicate lot | Partial | Parse fails without cells; `FindPanelByVendorLot` | Impossible-value checks later |
| Import validation | Impossible values, schema-vs-prior-lot, unknown antigens | Partial | NT/`?` not coerced to `-`; vendor and lab CSV compare antigen schema vs prior lot | Impossible-value catalog later |
| Import traceability | Original artifact retained | Partial | Vendor and lab CSV store SHA-256 + file beside the database | Artifact viewer |
| Reaction entry | IS / 37°C / AHG / CC | Present | Keyboard 0–4, N, Enter | — |
| Reaction entry | RT, PEG, gel, solid phase, enzyme-as-phase, w+, MF, hemolysis | Missing | Enzyme is a treated **run**, not a phase column | Configurable phases later |
| Analysis | Transparent candidate evidence | Partial | Explain Analysis lists suspected, in-progress, historical, and pattern candidates | Broader grades/phases; never-imported specificities |
| Analysis | Configurable visible rule-out | Partial | `Rule.MinRuleoutCount` is now applied; lab default + explanation sentences | Watch seeded anti-D/anti-k rules; ACS remains separate |
| Analysis | Single / multiple / dosage scoring | Partial | Fisher + pattern; pairwise combinations; dosage averages | Label as evidence, not diagnosis |
| Analysis | Panreactive / cold / warm / HTLA / auto / HFA / LFA models | Missing | Phase scores computed but not shown | Pattern classifiers later |
| Analysis | Selected-cell recommendations with “why” | Partial | Ranked unused inventory cells + “why this cell” on Analysis tab | Prefer unused selected-cell vials / phenotype later |
| Analysis | Patient phenotype / genotype / transfusion limits | Partial | Parses Weiner/Duffy-style text + previous Abs; transfusion notes mark typing uninterpretable | Structured genotype fields later |
| UI | Antigram freeze / slashes / run compare | Partial | Frozen cell column; rule-out slashes; run-vs-run compare | Antigen grouping, dosage toggle, filter/sort, panel compare |
| UI | Explain Analysis panel | Present | Explain tab + Summary + clinical report assemble rule-out, support, conflict, phenotype, additional testing | Keep wording as evidence, not diagnosis |
| Audit | Panel, reactions, rules, settings, final ID | Partial | `audit_events` + analysis snapshots; panel activate/deactivate logged | Rule-version detail; artifact viewer |
| Tests | Synthetic cases with intermediate reasoning | Partial | Rule-out, untyped, selected-cell, phenotype, Explain, vendor and lab CSV import-review tests | Expand case library |

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

## Likely next tick

Broader grades/phases (`w+`, MF, hemolysis; RT/PEG/gel as configured
phases), or an artifact viewer on the panel details. Authenticated vendor
portals stay later and must not bypass auth, CAPTCHA, licensing, or terms.

