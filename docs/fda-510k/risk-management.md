# Risk management (ISO 14971 starter)

This file is a starter hazard analysis for in-repo 510(k) readiness. It is not a complete ISO 14971 risk-management file and does not replace a manufacturer QMS.

Software safety classification for planning: IEC 62304 Class C (incorrect antibody identification can contribute to incompatible transfusion).

| ID | Hazard | Foreseeable sequence | Harm | Control | Verification |
|---|---|---|---|---|---|
| R-01 | Wrong antibody identification | Engine mis-scores reactions or user accepts suggestion without review | Incompatible transfusion | Professional confirmation required; lock after confirm; analysis snapshot | `Fda510kControlTests`, analysis golden tests |
| R-02 | Missed clinically significant antibody | Threshold / ACS settings too permissive | Delayed or missed ID | Settings clamp; ACS tests; settings change audit | `AcsEvaluationTests`, settings audit |
| R-03 | Seed / demo data treated as patient work | Empty production DB auto-seeded | Wrong patient result | No auto-seed on launch; explicit Tools > Load Demo Data or `--seed-clinical` | `Fda510kControlTests.FreshDatabase_HasNoSeededSpecimens` |
| R-04 | Post-confirmation edit | Reactions changed after final call | Result no longer matches signed ID | Record lock; clear requires reason + audit | `Fda510kControlTests.ConfirmedSpecimen_LocksReactionsAndAnalysisWrites` |
| R-05 | Settings drift | Probability / ID rule / ACS count changed silently | Different engine output | Clinical settings snapshot on analysis; settings audit | Snapshot + `update_settings` audit |
| R-06 | Untraceable calculation | Cannot reconstruct why a result was produced | Unable to investigate incidents | Snapshot: version, settings, input fingerprint, ACS/rule-out/suspect JSON | `Fda510kControlTests.AnalyzeSpecimen_PersistsTraceabilitySnapshot` |
| R-07 | Unlabeled software | User cannot see version or intended use | Misuse / untraceable reports | About, first-run, F1, report preamble | `Fda510kControlTests.Reports_IncludeIntendedUseVersionAndTraceLine` |
| R-08 | Lost forensic trail | Exception or data change with no record | Unable to reconstruct event | File log (no PHI grades); append-only `audit_events` | `AppLog` tests, audit tests |
| R-09 | World-readable PHI file | Weak NTFS ACL on database | Unauthorized disclosure | Local file + ACL tests | `HipaaSecurityTests` |
| R-10 | Vendor download to unexpected host | SSRF / unexpected network | Data leakage | HTTPS + URL allow-list | `HipaaSecurityTests` vendor URL tests |

Residual risk: professional confirmation is the primary clinical control. Residual submission items (predicate, QMS, clinical study) are listed in [gap-register.md](gap-register.md).
