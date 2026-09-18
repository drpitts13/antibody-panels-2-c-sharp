# Intended use

**Product:** Antibody Panel Management System  
**Software version:** assembly informational version (currently 2.0.0)

## Intended use statement

Decision-support software for licensed immunohematology professionals to document antibody-panel reactions and compute rule-out and probability statistics. A qualified professional must confirm identification. This software does not issue or release blood units and is not a substitute for licensed clinical judgment.

## Intended users

Licensed blood-bank / immunohematology technologists and supervisors working in a clinical laboratory.

## Intended use environment

A local Windows workstation. The database and settings files reside on the workstation (or a path set by `ANTIBODY_PANELS_DB`). The application does not expose a network server.

## Indications

Documentation of panel reactions and decision support for antibody identification, including rule-outs, Fisher-based probabilities, ACS evaluation, and treatment inferences.

## Contraindications / limitations

- Not a blood-unit issue or compatibility-release system.
- Not a standalone diagnostic device; confirmation by a qualified professional is required.
- Configurable clinical thresholds (probability, identification cell rule, ACS rule-out count) change output and must be controlled by the laboratory.
- Demo / clinical seed data must be loaded only by explicit operator action or the `--seed-clinical` switch.
