# Software requirements specification (starter)

| ID | Requirement |
|---|---|
| SRS-IU-01 | The software shall display the intended-use statement in About, first-run acknowledgment, Help, and report headers. |
| SRS-IU-02 | Reports and About shall display the assembly / informational version, not a hardcoded marketing string. |
| SRS-SEED-01 | Normal application launch shall not insert clinical or demo seed specimens. |
| SRS-SEED-02 | Seed data may be loaded only by Tools > Load Demo Data or the `--seed-clinical` switch. |
| SRS-AUD-01 | Clinical actions shall write an append-only audit event with UTC time, Windows operator, action, entity, optional reason, and before/after JSON. |
| SRS-AUD-02 | Confirm ID, clear ID, reaction save, panel/rule edits, settings changes, purge, and restore shall be audited. |
| SRS-AUD-03 | Operators shall be able to view and export the audit log. |
| SRS-LOCK-01 | A specimen with a confirmed identification shall reject reaction, panel-link, run, and analysis-write mutations. |
| SRS-LOCK-02 | Clearing a confirmed identification shall require a non-empty reason and shall be audited. |
| SRS-SNAP-01 | Each persisted analysis shall store software version, clinical settings JSON, input fingerprint, rule-out, suspected, and ACS summaries. |
| SRS-SNAP-02 | Analysis and clinical reports shall print version, settings, and input fingerprint. |
| SRS-LOG-01 | Unhandled exceptions shall be written to `%AppData%\AntibodyPanels\logs` with software version and without reaction grades. |
| SRS-CYB-01 | Operator identity on audit events shall be the Windows user name. Access control relies on the Windows login and NTFS ACLs. |
