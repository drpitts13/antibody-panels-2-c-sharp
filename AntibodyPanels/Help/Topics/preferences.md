# Preferences

Open **Tools → Preferences** to set laboratory defaults. **Save** writes them to this computer. **Cancel** discards the dialog. **Help** opens this chapter.

## Identity and thresholds

- **Lab name** and **Department** identify the laboratory on this workstation.
- **Score threshold** is the minimum combined score for the suspected list. The allowed range is 0.30 to 0.95.
- **ID cell rule** is how many antigen-positive reactive cells and antigen-negative nonreactive cells are required before a row is marked as meeting the identification rule. AABB 3 + 3 is the usual standard. The Suspected Antibodies tab turns those rows green.
- **ACS rule-outs** is how many times each clinically significant antibody must be ruled out before the application can suggest that all clinically significant antibodies are ruled out. The range is 1 to 5.
- **Default rule-outs** is the minimum number of qualifying antigen-positive nonreactive cells required to rule out an antibody when no custom rule exists. The range is 1 to 5. A rule on the Rules tab overrides this for that antibody.

## Specimens and the database

- **Default specimen type** pre-selects serum, plasma, or eluate on a new specimen.
- **Specimen dating (days)** pre-fills expiration when you add a specimen. 0 leaves the date blank. Three days is a typical antibody workup dating interval.
- **Expiration warning (days)** is how far ahead the Worklist and the lists mark a specimen or panel as expiring.
- **Max database size (MB)** warns when the database file reaches 80 percent of this size. The allowed range is 50 to 10240 MB. The warning can offer to open Purge Database.
- **Default initials** pre-fills **Initials** on Confirm ID. Confirming an identification updates this value to the initials you used.
- **Show inactive specimens and panels by default** turns on **Show Inactive** when those tabs open.

## Phases and the reaction grid

- **Hide ruled-out antigen columns on the Reactions tab** starts Reactions with **Hide ruled-out** checked.
- **Extra phases** adds optional columns after the classic IS / 37°C / AHG / CC grid. Leave the box blank to keep the classic grid.
- **RT**, **PEG**, **Gel**, and **Solid** are suggested columns. Gel, Solid, and PEG count as IAT-like evidence. You can also type custom names.
- **Titer grid (Dil1–Dil128)** adds serial-dilution columns. The last reactive tube is the titer endpoint. Dilution grades are not IAT, and they do not rule out or identify an antibody.

Changes apply to the next analysis and to new specimens. They do not rewrite identifications you have already confirmed.
