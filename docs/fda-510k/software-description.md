# Software description

Antibody Panel Management System is a .NET 8 WPF desktop application with SQLite persistence.

## Device software functions

| Function | Description |
|---|---|
| Specimen and panel records | Accession, type, phenotype, DAT, panel lots, antigen profiles |
| Reaction entry | IS / 37C / AHG / CC grades per panel run, including treated runs |
| Identification engine | Rule-outs, Fisher exact probabilities, ACS, combinations, treatment inferences |
| Confirmation | Operator confirms final ID with initials; record then locks |
| Reports | Text / PDF / CSV worksheets including software version and intended use |
| Analytics / search | Confirmed-ID summaries and cell search |
| Vendor import | Optional HTTPS download of vendor panel files (allow-listed URLs) |

## Programming language and runtime

C# / WPF on .NET 8, self-contained win-x64 publish for the installer.

## Off-the-shelf software

See [soup.md](soup.md).

## Hardware / OS

Windows 10/11 x64. No dedicated medical hardware.

## Networks

No inbound listener. Optional outbound HTTPS to vendor catalog URLs.

## Persistent data

- `%AppData%\AntibodyPanels\antibody_panels.db` (or `ANTIBODY_PANELS_DB`)
- `%AppData%\AntibodyPanels\settings.json`
- `%AppData%\AntibodyPanels\logs\app-yyyy-MM-dd.log`
