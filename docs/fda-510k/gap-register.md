# FDA 510(k) gap register

Status values: `OPEN`, `CLOSED`, `RESIDUAL`.

In-scope items are product controls and starter DHF artifacts that can be completed in this repository. Residual items cannot be closed by software changes.

## In-scope

| ID | Gap | Status | Resolution |
|---|---|---|---|
| G-01 | Intended use and version labeling | CLOSED | `SoftwareIdentity`; About, first-run, F1, report preamble |
| G-02 | Production auto-seed | CLOSED | Removed seeder calls from `MainViewModel`; keep `--seed-clinical` and Tools menu |
| G-03 | Append-only audit trail | CLOSED | `audit_events` + Audit Log dialog/export |
| G-04 | Confirmed-record lock | CLOSED | `RecordLockedException`; clear requires reason |
| G-05 | Analysis traceability snapshot | CLOSED | `analysis_snapshots` + report trace line |
| G-06 | Structured logging | CLOSED | `AppLog` under `%AppData%\AntibodyPanels\logs` |
| G-07 | Cybersecurity / SOUP evidence | CLOSED | [soup.md](soup.md); Windows identity on audit; existing HIPAA ACL/URL tests |
| G-08 | Starter DHF | CLOSED | This folder (intended use, SRS, risk, architecture, trace, SOUP, V&V, anomalies) |
| G-09 | Control tests | CLOSED | `Fda510kControlTests.cs` |

## Residual (out of scope)

| ID | Gap | Status | Why it remains |
|---|---|---|---|
| X-01 | FDA 510(k) submission / predicate / SE | RESIDUAL | Regulatory filing, not a code change |
| X-02 | 21 CFR 820 / QMSR quality system | RESIDUAL | Organizational process |
| X-03 | Establishment registration and listing | RESIDUAL | FDA business process |
| X-04 | Clinical performance study | RESIDUAL | Laboratory / clinical evidence |
| X-05 | Part 11 electronic signatures | RESIDUAL | User selected 510(k), not Part 11 |
| X-06 | Independent IEC 62304 lifecycle org | RESIDUAL | Process maturity beyond this repo |
