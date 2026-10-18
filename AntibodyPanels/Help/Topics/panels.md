# Panels

The Panels tab stores panel lots and the antigen profile of each cell. Analysis uses this grid together with the grades you enter.

## List

- **Find** filters by name, lot, vendor, or expiration date. Find is unavailable while you are editing antigens.
- **Add** creates a lot. **Details** edits the name, lot, vendor, cell count, and expiration. **Copy** duplicates a lot so you can start a new lot from an existing antigram.
- **Edit** turns on antigen editing for the selected panel. **Delete** removes the lot after you confirm.
- **Show Inactive** includes lots that are not active. Imported lots stay inactive until you review them.
- Yellow rows are nearing expiration. Red rows are expired.

## Bring a lot in

- **Import** creates a panel from a CSV lot sheet. The file uses a header row such as Cell, D, C, c, and the rest of the antigens.
- **Download…** opens the vendor catalog, or lets you import a vendor PDF or CSV you already have.
- After an import, the review dialog lets you **Activate now** or **Keep inactive**. You can also select an inactive lot later and click **Activate**.
- **Artifact** opens the retained import file and its SHA-256 hash.
- **Export** saves the selected antigen grid as CSV. **Antigram** saves a bench-copy PDF.

## Antigen grid

The grid on the right is the antigram. Press **Edit** before changing it.

- Click a cell to cycle **NT → + → ++ → −**. Antigens without an antithetical pair skip ++.
- Green is + or ++. Light gray is −. NT means the antigen was not typed.
- Drag column headers to reorder while editing.
- **Save** writes the grid (Ctrl+S does the same on this tab). **Cancel** leaves edit mode without saving.

**Add antigen…** adds a non-standard antigen from the warehouse catalog to this panel only. **Remove antigen…** drops one of those extra columns. **Compare…** diffs this antigram against another stored lot.

Antigen typing on the grid is the profile analysis uses. A blank or NT antigen is not treated as negative.
