# Requirements traceability

| Requirement | Code | Tests |
|---|---|---|
| SRS-IU-01 | `SoftwareIdentity`, `MainWindow.ShowAbout`, first-run, F1, `ReportService.GeneratePreviewText` | `Fda510kControlTests.SoftwareIdentity_*`, `Reports_IncludeIntendedUseVersionAndTraceLine` |
| SRS-IU-02 | `SoftwareIdentity.Version`, About / reports | `SoftwareIdentity_ExposesAssemblyVersionAndIntendedUse` |
| SRS-SEED-01 | `MainViewModel` constructor (no seeder calls) | `FreshDatabase_HasNoSeededSpecimens` |
| SRS-SEED-02 | `App.xaml.cs --seed-clinical`, `MainWindow.LoadDemoData` | Existing seeder tests |
| SRS-AUD-01 / 02 | `DatabaseService.AppendAudit` on confirm/clear/reaction/panel/rule/purge/restore; settings dialog | `ConfirmAndClear_WriteAuditEvents_AndRequireReason`, `SettingsChange_CanBeAudited` |
| SRS-AUD-03 | `AuditLogDialog` | Manual; export uses `GetAuditEvents` |
| SRS-LOCK-01 | `EnsureSpecimenUnlocked`, analyzer `updateDb` guard | `ConfirmedSpecimen_LocksReactionsAndAnalysisWrites` |
| SRS-LOCK-02 | `ClearSpecimenFinalCall(reason)` + `ReasonDialog` | `ConfirmAndClear_WriteAuditEvents_AndRequireReason` |
| SRS-SNAP-01 | `SaveAnalysisSnapshot` / `PersistSnapshot` | `AnalyzeSpecimen_PersistsTraceabilitySnapshot` |
| SRS-SNAP-02 | `ReportService.AnalysisTraceLine` | `Reports_IncludeIntendedUseVersionAndTraceLine` |
| SRS-LOG-01 | `AppLog`, `App.OnStartup` | `AppLog_WritesVersionedLineWithoutThrowing` |
| SRS-CYB-01 | `Environment.UserName` on audit; NTFS ACL tests | `HipaaSecurityTests`, confirm audit operator assertion |

Algorithm / clinical performance (pre-existing): `AntibodyAnalysisTests`, `AcsEvaluationTests`, `ScenarioTests`, `ValidationStressTests`, `ReportAccuracyTests`.
