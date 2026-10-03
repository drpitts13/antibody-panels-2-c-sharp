# Antibody identification capability matrix

Decision-support software for trained blood-bank professionals. The engine
computes evidence; a qualified user must confirm identification. Scores and
rule-outs are analytical evidence, not a diagnosis.

Status: **Present** (usable), **Partial** (exists but incomplete or silent),
**Missing**.

Last updated: 2026-10-03 (iteration 3 — selected-cell recommendations with explanations).

## Capability matrix

| Area | Capability | Status | Current evidence | Next action |
|---|---|---|---|---|
| Gap file | Living capability matrix | Present | This file | Update every iteration |
| Import | Provider adapter framework | Partial | `IVendorPanelSource` + Bio-Rad/Ortho/Quotient public catalogs; Immucor/Grifols/Medion file-only | XLSX/XML/JSON adapters later |
| Import | CSV | Present | Lab CSV + vendor CSV parser | — |
| Import | PDF | Partial | PdfPig positional grid; layout-fragile | Harden after schema work |
| Import | XLSX / XML / JSON panels | Missing | Settings/snapshots only | Later tick |
| Import | Vendor APIs / authenticated portals | Missing | Public HTML + file import only. Do not bypass auth, CAPTCHA, licensing, or terms | Secrets storage only if a vendor authorizes access |
| Common model | Manufacturer, lot, expiration, cell, ABO/Rh, standard antigens | Partial | `Panel` / `PanelCell`; antigens stored as `+`/`-` | Persist NT / unknown / zygosity |
| Common model | Homozygous / heterozygous / negative / unknown / NT | Partial | Import NT / missing columns stay untyped; zygosity still inferred from typed antitheticals | Persist explicit homozygous state later |
| Import validation | Required fields, duplicate lot | Partial | Parse fails without cells; `FindPanelByVendorLot` | Staging + review before activation |
| Import validation | Impossible values, schema-vs-prior-lot, unknown antigens | Partial | NT/`?` no longer coerced to `-`; unknown antigens listed on analysis | Schema-vs-prior-lot and review-before-activation |
| Import traceability | Original artifact retained | Missing | `SourceUrl` / `SourceFormat` / `ImportedAt` only | Store hash + artifact |
| Reaction entry | IS / 37°C / AHG / CC | Present | Keyboard 0–4, N, Enter | — |
| Reaction entry | RT, PEG, gel, solid phase, enzyme-as-phase, w+, MF, hemolysis | Missing | Enzyme is a treated **run**, not a phase column | Configurable phases later |
| Analysis | Transparent candidate evidence | Partial | Supporting/conflicting cells for **suspected** antibodies only | Per-candidate narrative |
| Analysis | Configurable visible rule-out | Partial | `Rule.MinRuleoutCount` is now applied; lab default + explanation sentences | Watch seeded anti-D/anti-k rules; ACS remains separate |
| Analysis | Single / multiple / dosage scoring | Partial | Fisher + pattern; pairwise combinations; dosage averages | Label as evidence, not diagnosis |
| Analysis | Panreactive / cold / warm / HTLA / auto / HFA / LFA models | Missing | Phase scores computed but not shown | Pattern classifiers later |
| Analysis | Selected-cell recommendations with “why” | Partial | Ranked unused inventory cells + “why this cell” on Analysis tab | Prefer unused selected-cell vials / phenotype later |
| Analysis | Patient phenotype / genotype / transfusion limits | Missing | Free-text phenotype on specimen/report only | Structured phenotype later |
| UI | Antigram freeze / slashes / run compare | Partial | Frozen cell column; rule-out slashes; run-vs-run compare | Antigen grouping, dosage toggle, filter/sort, panel compare |
| UI | Explain Analysis panel | Partial | Ruled Out + Summary now show reviewable rule-out sentences | Dedicated Explain panel |
| Audit | Panel, reactions, rules, settings, final ID | Partial | `audit_events` + analysis snapshots | Import artifact + rule-version detail |
| Tests | Synthetic cases with intermediate reasoning | Partial | Rule-out, untyped-antigen, and selected-cell intermediate tests | Expand case library |

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

## Likely next tick

Per-candidate evidence (supporting / conflicting / additional testing) for
antibodies that are not yet “suspected,” or structured patient phenotype
with “cannot interpret if recently transfused.”
