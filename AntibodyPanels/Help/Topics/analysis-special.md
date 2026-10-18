# Special Panels

Special Panels collects the evidence that comes from treated cells and adsorbed serum. Create those runs with **+ Run** on the Reactions tab.

## Gated rule-outs

These rule-outs were suppressed because the antigen is destroyed by the cell treatment on that run. A ficin-treated or papain-treated panel cannot rule out an antibody to an enzyme-destroyed antigen. A DTT-treated panel cannot rule out an antibody to a DTT-destroyed antigen, including Kell-system antigens that DTT denatures.

**Antibody**, **Treatment**, and **Reason** name what was withheld and why. Those cells still appear on the reaction grid with a strikethrough antigen header. They do not add to the rule-out count on the Ruled Out tab.

## Treatment inferences

This grid interprets treated runs next to the untreated run. **Run**, **Antibody**, and **Observation** describe what changed, such as reactivity that disappeared after enzyme treatment. The observations are clinical interpretations for you to review.

## Absorption comparison

When a run used an adsorbed aliquot, this grid compares untreated serum with that aliquot on the same panel.

- **Adsorption** names the aliquot, such as R1R1, R2R2, rr, or autoadsorption.
- **Finding** and **Explanation** describe the analytical result.
- **Supporting cells** and **Conflicting cells** are the cell lists behind it.
- **Score** is an evidence score for review.

The text above the grid states an important limit: ordinary probability and pattern scores omit specificities that the aliquot is expected to remove. The comparison is analytical evidence. It is not an identification. Confirm any specificity you accept on **Suspected Antibodies**.
