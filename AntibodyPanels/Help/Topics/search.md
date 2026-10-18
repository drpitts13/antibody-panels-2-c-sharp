# Search

Search finds panel cells whose antigen profile matches a pattern you build. Use it to locate cells for selected-cell testing or to see which lots carry a profile.

## Build the pattern

Each antigen is a button. Click it to cycle **Any → + → −**.

- **Any** ignores that antigen.
- **+** (green) means the cell must have the antigen.
- **−** (gray) means the cell must lack the antigen.

For antigens you marked +, **Positive cells** chooses zygosity:

- **Both** accepts any antigen-positive cell.
- **Homozygous** accepts an explicit ++ or an antigen-positive cell whose antithetical antigen is negative.
- **Heterozygous** accepts cells where both of the antithetical antigens are positive.

An untyped partner is not treated as negative. Antigens without an antithetical pair, such as D, stay a simple antigen-positive match.

## Run it

**Search** fills the results grid. **Clear** sets every antigen back to Any. The **Results** count is the number of matching cells.

Each row is a panel, lot, and cell, followed by the antigen typings. Matching looks across the lots stored in the database, including extra warehouse antigens when a panel types them.

Search does not change grades or confirm an identification. To test a cell you found, enter the grades on the Reactions tab for the specimen you are working up.
