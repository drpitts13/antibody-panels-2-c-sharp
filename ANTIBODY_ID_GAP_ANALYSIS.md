# Antibody identification capability matrix

Decision-support software for trained blood-bank professionals. The engine
computes evidence; a qualified user must confirm identification. Scores and
rule-outs are analytical evidence, not a diagnosis.

Status: **Present** (usable), **Partial** (exists but incomplete or silent),
**Missing**.

Last updated: 2026-10-04 (iteration 36 — RHCE variant C / CeRN).

## Capability matrix

| Area | Capability | Status | Current evidence | Next action |
|---|---|---|---|---|
| Gap file | Living capability matrix | Present | This file | Update every iteration |
| Import | Provider adapter framework | Partial | `IVendorPanelSource` + Bio-Rad/Ortho/Quotient public catalogs; Immucor/Grifols/Medion file-only | Authenticated portals only with vendor permission |
| Import | CSV | Present | Lab CSV + vendor CSV parser; lab import now reviews before activation | — |
| Import | PDF | Partial | PdfPig positional grid; layout-fragile | Harden after schema work |
| Import | XLSX / XML / JSON panels | Present | JSON/XML structured schema; first-sheet XLSX via zip/shared strings; same inactive+artifact review | Multi-sheet XLSX later |
| Import | Vendor APIs / authenticated portals | Missing | Public HTML + file import only. Do not bypass auth, CAPTCHA, licensing, or terms | Secrets storage only if a vendor authorizes access |
| Common model | Manufacturer, lot, expiration, cell, ABO/Rh, standard antigens | Partial | `Panel` / `PanelCell`; ++ vs partner+ and rare-null (K0, Jk(a−b−), Rhnull-like) on import review | More ISBT alleles later |
| Common model | Homozygous / heterozygous / negative / unknown / NT | Present | Import and panel editor persist `++` / NT; rule-out and search use explicit ++ or typed partner − | Combo/list editors later |
| Import validation | Required fields, duplicate lot | Partial | Parse fails without cells; `FindPanelByVendorLot` | — |
| Import validation | Impossible values, schema-vs-prior-lot, unknown antigens | Partial | Schema vs prior lot; ++ vs partner+; unknown columns; rare-null notes; Weiner label vs typed D/C/c/E/e | More ISBT alleles later |
| Import traceability | Original artifact retained | Present | Vendor and lab CSV store SHA-256 + file; Artifact viewer verifies and opens | — |
| Reaction entry | IS / 37°C / AHG / CC | Present | Keyboard 0–4, W/M/H, N, Enter | — |
| Reaction entry | w+, MF, hemolysis | Present | Combo + keys; treated as reactive evidence | — |
| Reaction entry | RT, PEG, gel, solid phase as phases | Partial | Extra JSON plus Preferences RT/PEG/Gel/Solid and titer-grid checkboxes; empty stays tube-only; Gel/Solid/PEG are IAT-like; Dil1–Dil128 are titer only | Custom method names later |
| Analysis | Transparent candidate evidence | Partial | Explain Analysis lists suspected, in-progress, historical, and pattern candidates | Never-imported specificities; pattern classifiers |
| Analysis | Configurable visible rule-out | Partial | `Rule.MinRuleoutCount` is now applied; lab default + explanation sentences | Watch seeded anti-D/anti-k rules; ACS remains separate |
| Analysis | Single / multiple / dosage scoring | Partial | Fisher + pattern; pairwise combinations; dosage averages | Label as evidence, not diagnosis |
| Analysis | Panreactive / cold / warm / HTLA / auto / HFA / LFA models | Partial | HTLA titer plus Dil/1:n grid endpoint; Preferences can add Dil1–Dil128; Neut/Inhib vs IAT | More ISBT alleles later |
| Analysis | Selected-cell recommendations with “why” | Partial | Unused in-date vials preferred; P1−/Ch−/Sd(a−) selected cells ranked when neutralization favors soluble substance | More ISBT alleles later |
| Analysis | Patient phenotype / genotype / transfusion limits | Partial | GYPA null/Mk En(a); RHCE*Ce vs ce is case-sensitive; CeRN/HAR/C^w variant-C notes; GATA still does not support anti-Fya | More RHD/RHCE alleles later |
| UI | Antigram freeze / slashes / run compare | Partial | Frozen cell; slashes; compare extras; Show dosage; filter/sort; panel lot compare | — |
| UI | Explain Analysis panel | Present | Explain tab + Summary + clinical report assemble rule-out, support, conflict, phenotype, additional testing | Keep wording as evidence, not diagnosis |
| Audit | Panel, reactions, rules, settings, final ID | Partial | `audit_events` + snapshots with rule-engine version and per-antibody rule JSON; Explain/Summary/report show the policy | User/operator on snapshot later |
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

## Iteration 17 (this change)

**Deficiency:** Weak pan-AHG (w+/1+) looked like an ordinary warm pattern.
Panagglutination did not use the autocontrol to separate autoantibody from
high-prevalence alloantibody, and a single unexplained reactive cell had no
low-frequency note.

**Bounded improvement:** Weak IAT without IS/RT (4+ cells) is labeled HTLA-like.
Panreactive plus a nonreactive AC favors high-prevalence; a reactive AC favors
autoantibody. One or two reactive cells among six or more is labeled
low-frequency / extra antibody / mistype. Still evidence, not a diagnosis.

**Not in this tick:** ExtraPhases lab default list, titration workflow,
authenticated APIs.

## Iteration 18 (this change)

**Deficiency:** Genotype lived only as free-text phenotype. After
transfusion the entire type was uninterpretable, so a recorded *RHCE*ce/ce*
or *FY*02/FY*02* result could not argue for or against an alloantibody.

**Bounded improvement:** Specimen `genotype` is stored and parsed into
predicted antigens (ISBT-style *RHCE* / *RHD* / *FY* / *JK* / *KEL* pairs,
or the same Weiner/Fy(a−b+) marks). Serology still becomes
uninterpretable after transfusion; genotype remains Predicted evidence.
Conflicts are explained. Nothing is auto-ruled-out or auto-confirmed.

**Not in this tick:** ExtraPhases lab default list, variant-allele risk
catalog, authenticated APIs.

## Iteration 19 (this change)

**Deficiency:** Extra phases existed only as a comma-separated string. Labs
did not have a visible RT / PEG / Gel / Solid default, and empty settings
gave no hint that Solid is an IAT-like Capture column.

**Bounded improvement:** Preferences now toggles the suggested extras.
`FromSuggested` / `Toggle` keep custom names, refuse reserved IS/AHG
columns, and leave a blank list blank so tube-only labs stay on IS / 37°C /
AHG / CC. Solid-phase 0 with AHG 0 can still rule out; Solid+ remains
IAT-like evidence, not a diagnosis.

**Not in this tick:** Variant-allele catalog, titration workflow,
authenticated APIs.

## Iteration 20 (this change)

**Deficiency:** Selected-cell suggestions treated every unused inventory cell
the same. A leftover ID-panel cell could outrank an unused Selectogen /
0.8% vial, and patient E− (or a post-transfusion genotype prediction)
did not raise an E+ cell.

**Bounded improvement:** Unused vials whose name looks like selected cells
get a ranking boost and an explanation. Patient Ag− (including Predicted
genotype after transfusion) boosts matching Ag+ cells; patient Ag+ only
softens the score and never drops a distinguishing cell. Still
recommendations, not a diagnosis.

**Not in this tick:** HTLA titration workflow, expired-vial filtering,
authenticated APIs.

## Iteration 21 (this change)

**Deficiency:** HTLA notes only said “consider titration.” A recorded titer
in Notes or a Titer extra-phase was ignored, and unused Yt(a−) / Vel−
cells were never ranked when the sheet looked HTLA or high-prevalence.

**Bounded improvement:** Parse a recorded titer (notes or Titer/Dilution
column). Titer ≥16 strengthens the HTLA sentence; a low titer warns that
it may not be HTLA. When HTLA or high-prevalence is present, unused cells
typed or annotated as high-prevalence Ag− are recommended. Nothing is
auto-identified.

**Not in this tick:** Neutralization workflow, expired-vial filtering,
authenticated APIs.

## Iteration 22 (this change)

**Deficiency:** Selected-cell ranking could name an expired Selectogen vial
ahead of an in-date ID-panel cell. Expiration was not explained.

**Bounded improvement:** In-date unused cells are preferred. Expired vials
are omitted when any in-date recommendation exists. If only expired
inventory remains, at most two cells are listed with a “do not use”
warning. Expiring lots stay recommended and ask the user to confirm the
date. Still not a diagnosis.

**Not in this tick:** Allele variant catalog, neutralization workflow,
authenticated APIs.

## Iteration 23 (this change)

**Deficiency:** Any `N` in an *RHD* allele was treated as a deletion, so
*RHD*DNB* became D−. *FY*01N (GATA) predicted Fy(a−) and treated that as
support for alloanti-Fya, which is usually wrong.

**Bounded improvement:** Null *RHD* alleles are *01N* / deletion / DEL only.
Weak D, partial D, DEL, GATA *FY*01N, FyX, and *RHCE*ceAR-like haplotypes
add Variant notes. GATA suppresses “predicted Fya− supports anti-Fya.”
Nothing is auto-identified.

**Not in this tick:** Full ISBT catalog, neutralization workflow,
authenticated APIs.

## Iteration 24 (this change)

**Deficiency:** HTLA notes said “consider neutralization,” but a Neut/Inhib
column or “neutralized with plasma” note was never compared to IAT.

**Bounded improvement:** Pair IAT with Neut/Neutral/Inhib grades. Loss of
reactivity favors a soluble-substance / HTLA-like pattern (plasma, urine,
saliva, P1). Persistence argues against readily neutralized Ch/Rg, Sd(a),
or Lewis. Notes without paired grades stay qualitative. Nothing is
auto-identified.

**Not in this tick:** Full titer grid, broader ISBT catalog, authenticated
APIs.

## Iteration 25 (this change)

**Deficiency:** Vendor `++` / `+/+` / HOMO was flattened to `+`. Homozygous
rule-out then required a typed antithetical partner, so a marked homozygous
cell with `e` NT could not rule out anti-E. A lone `+` with an untyped
partner still had to stay unknown.

**Bounded improvement:** Persist explicit `++`. Rule-out, selected-cell
ranking, dosage homo/het, antigram dosage, and inventory search treat `++`
as homozygous without inventing a partner-negative. A typed partner `+`
overrides `++` (conflict, not homozygous). A single `+` with an untyped
partner is still not homozygous. Nothing is auto-identified.

**Not in this tick:** Panel-editor `++` toggle, broader ISBT catalog,
authenticated APIs.

## Iteration 26 (this change)

**Deficiency:** *JK**, *KEL* null, and *GYPB* tokens were parsed for
predicted Jk/K/S types, but there were no review notes. *JK*01N/JK*02N*
looked like ordinary Jk(a−b−) support and never mentioned anti-Jk3.
Weak *JK*01W was treated as a normal Jk(a+).

**Bounded improvement:** True JK nulls stay predicted antigen-negative
(unlike GATA-FY). Two JK null alleles add a Jk3 review note. Weak JK
warns that predicted Jk+ does not rule out alloanti-Jk. KEL-null / K0
and GYPB null / U-var add Variant notes. Nothing is auto-identified.

**Not in this tick:** Full ISBT catalog, panel-editor zygosity marks,
authenticated APIs.

## Iteration 27 (this change)

**Deficiency:** The panel antigen editor toggled only `+`/`−` and displayed
`++` as `+`. A lab could not mark homozygous or NT without inventing a
partner-negative, and an imported `++` was lost on the first click.

**Bounded improvement:** Click cycles NT → + → ++ → − (unpaired antigens
such as D skip ++). The editor shows and saves `++` and NT. Saved `++`
with an untyped partner still rules out; saved NT is not treated as
antigen-negative. Nothing is auto-identified.

**Not in this tick:** Full titer grid, more ISBT alleles, authenticated
APIs.

## Iteration 28 (this change)

**Deficiency:** A titer was only a notes word or a single Titer/Dilution
number. Serial tubes (Dil1–Dil128 or 1:2 / 1:4 …) were ignored, and a
grade on a dilution column could look like a panel phase.

**Bounded improvement:** Last reactive serial-dilution extra phase is the
endpoint (prozone uses the highest reactive tube). `1+` is still a grade,
not titer 1. Dilution columns are not IAT and do not change rule-out.
Nothing is auto-identified.

**Not in this tick:** Preferences titer-grid checkbox, more ISBT alleles,
authenticated APIs.

## Iteration 29 (this change)

**Deficiency:** Serial-dilution columns parsed as a titer only if the lab
typed Dil1–Dil128 (or 1:n) into Extra phases. Tube-only Preferences had
no control to add that grid, and the extra-phase list capped at 12 names
so a titer grid plus Solid/Neut could be truncated.

**Bounded improvement:** Preferences **Titer grid (Dil1–Dil128)** adds
those columns. Unchecking removes them and keeps RT/PEG/Gel/Solid and
custom names such as Neut. Dilution grades remain titer evidence, not
IAT, and cannot prevent AHG-negative rule-out. Extra-phase cap raised
to 24. Nothing is auto-identified.

**Not in this tick:** More ISBT alleles, impossible-value import catalog,
authenticated APIs.

## Iteration 30 (this change)

**Deficiency:** Import review compared antigen *headers* to the prior lot,
but a cell marked E++ with e+ still looked like a clean homozygous type,
and columns the catalog does not know (Fy3, vendor extras) disappeared
without a review sentence.

**Bounded improvement:** `PanelTypingInspector` flags homozygous ++ with
a positive antithetical partner, unrecognized type tokens, and unknown
antigen columns. Issues appear on the inactive-lot review. Heterozygous
+/+ and ++ with an untyped or negative partner stay silent. Unknown
columns are not imported. Nothing is auto-activated or identified.

**Not in this tick:** Null-phenotype catalogs (K0, Rhnull), more ISBT
alleles, authenticated APIs.

## Iteration 31 (this change)

**Deficiency:** Both-negative antithetical types on a reagent cell looked
like ordinary negatives. K−k−, Jk(a−b−), or C−c− plus E−e− could be a
true null cell or a mistype, and Lewis Lea−Leb− (common) would have been
noisy if every pair were flagged.

**Bounded improvement:** Import review notes rare-null patterns (K0-like,
Jk(a−b−), Fy(a−b−), S−s−, M−N−, Lu(a−b−), Rhnull/D−−-like). Lewis both
negative is not flagged. An untyped partner is not treated as negative.
Lots stay inactive. Nothing is auto-identified.

**Not in this tick:** More ISBT alleles, phenotype-string vs typed-antigen
conflicts, authenticated APIs.

## Iteration 32 (this change)

**Deficiency:** A stored Weiner label (R1R1) was never compared with typed
D/C/c/E/e. An R1R1 cell that was c+ looked like a normal heterozygous
C+c+ type and could be used for homozygous anti-c rule-out by mistake.

**Bounded improvement:** Import review flags Weiner/Rh labels that
disagree with typed Rh antigens. C++ still matches an expected C+.
Untyped antigens and matching rr / R1R1 cells stay silent. Lots stay
inactive. Nothing is auto-identified.

**Not in this tick:** More ISBT alleles, ABO vs reverse-cell notes,
authenticated APIs.

## Iteration 33 (this change)

**Deficiency:** Neutralization notes explained IAT vs Neut, but unused
inventory was never ranked for the matching antigen-negative cell
(P1− after P1 substance, Ch− after plasma). Ordinary ID-panel P1−
rows would have drowned those vials.

**Bounded improvement:** When neutralization favors a soluble
substance (lost IAT reactivity or a qualitative note), unused
selected-cell vials or Special Types annotations that lack P1, Ch/Rg,
Sd(a), or Lewis are recommended with a why sentence. Persistence after
neutralization does not boost those cells. Ordinary panel P1− rows are
not ranked. Nothing is auto-identified.

**Not in this tick:** More ISBT alleles, saliva Le(a−b−) panel-wide
ranking, authenticated APIs.

## Iteration 34 (this change)

**Deficiency:** Analysis snapshots stored software version and lab
settings, but not the rule-engine version or per-antibody MinRuleoutCount
/ heterozygous exceptions. A later rule edit left no trace of the policy
that produced a prior analysis.

**Bounded improvement:** Snapshots record `rule-engine-v1` plus the
active antibody rules. Explain Analysis, Summary, and the report trace
line state lab default vs overrides (for example anti-D requires 3,
heterozygous C allowed). Changing a rule writes a new snapshot. Nothing
is auto-identified.

**Not in this tick:** More ISBT alleles, operator identity on the
snapshot row, authenticated APIs.

## Iteration 35 (this change)

**Deficiency:** GYPA tokens were recognized but GYPA*01N was treated as
ordinary GYPA*01 (predicted M+). Mk / En(a−) genotypes never produced a
review note, so M−N− looked like a typing gap rather than a GYPA null.

**Bounded improvement:** GYPA nulls persist as antigen-negative (M0/N0),
not as M+ or N+. GYPA*01N/GYPA*02 stays M−N+ without an En(a) note.
Two GYPA nulls or Mk alleles add an anti-En(a) review note. Ordinary
GYPA*01/GYPA*02 stays silent. Nothing is auto-identified.

**Not in this tick:** RHCE*CeRN / more RHD alleles, operator on snapshots,
authenticated APIs.

## Iteration 36 (this change)

**Deficiency:** RHCE haplotype parsing used case-insensitive "ce", so
RHCE*Ce and RHCE*CeRN were treated as little-c haplotypes. Variant C
(CeRN / HAR / C^w) had no review note, so predicted C+ looked like it
could rule out anti-C.

**Bounded improvement:** C vs c in RHCE alleles is case-sensitive. CeRN
still predicts C+e+ (with c+ if the partner is ce) and adds a variant-C
note that predicted C+ does not rule out anti-C. Ordinary RHCE*Ce/ce
stays silent. ceAR remains a variant-e note. Nothing is auto-identified.

**Not in this tick:** More RHD DAR/DVI catalog rows, never-imported
specificity classifiers, authenticated APIs.

## Likely next tick

Never-imported specificity classifiers, or operator identity on snapshots.
Authenticated vendor portals stay later and must not bypass auth,
CAPTCHA, licensing, or terms.

