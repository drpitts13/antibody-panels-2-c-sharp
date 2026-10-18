# Reactions

The Reactions tab is where you record IS, 37°C, AHG, and CC grades for each cell, plus any extra phases configured in Preferences.

## Choose the work

- **Find** filters the specimen list by accession, type, phenotype, previous antibodies, notes, or final ID.
- **Specimen** and **Panel** pick the accession and the lot. Grades load when you change specimen, panel, or run.
- **Run** selects which set of grades you are editing. The first run is the untreated panel. **+ Run** adds a treated or adsorbed run. **Delete Run** removes the run you are viewing.

## While you enter grades

- **Compare** shows another run beside this one. Pick that run in the second list. Rows that differ are highlighted, and extra phases are included.
- **Hide ruled-out** hides antigen columns already ruled out for this specimen. The setting can also be turned on by default in Preferences.
- **Show dosage** groups columns by blood-group system and marks homozygous ++, heterozygous +, or +/? when the antithetical antigen was not typed. This is display only. It does not change rule-out.
- **Load Reactions** reloads grades from the database.
- **Save** writes grades without running analysis. Ctrl+S does the same.
- **Save & Analyze** writes grades, runs analysis, and shows a green or red banner with the result.
- **Fill 0s** sets remaining NT phases to 0. Check cells become 2+ when AHG is 0. Grades you already entered are left alone.
- **Clear** removes the grades on this run.
- **Help** opens this chapter.

A blue banner names the cell or serum treatment when the run is not an ordinary untreated panel.

## The grid

Antigen columns come from the panel. Red header text means that antibody is ruled out for this specimen. A strikethrough header means the antigen is destroyed by the cell treatment, so that column cannot support a rule-out on this run.

Row colors: blue when no grades have been entered, green when the row contributed to a rule-out. Phase cells use yellow for w+ or 1+, orange for 2+, red for 3+, 4+, or hemolysis, and purple for mixed field. A pink CC cell is invalid when AHG is 0.

## Keyboard

Click a phase cell, then type:

- **0–4** writes 0, 1+, 2+, 3+, or 4+.
- **W** writes w+. **M** writes MF. **H** writes hemolysis. **N** writes NT.
- **Enter** moves to the next phase, then to the next cell.

## Limit the rows you see

**Filter** shows only cells positive for one antigen. The autocontrol stays visible. Hidden cells are still saved and still analyzed.

When an antigen is selected, the zygosity list can limit the rows to homozygous or heterozygous expression. The sort list reorders the visible rows and does not change stored cell numbers. The text beside the lists explains the filter that is in effect.

w+, MF, and hemolysis count as reactive. They cannot be used to rule an antibody out. Extra-phase reactivity is recorded and shown, and it does not by itself rule out an antibody.
