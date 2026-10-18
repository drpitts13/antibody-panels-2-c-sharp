# Ruled Out

This tab shows the rule-out count for each antibody and the cells that produced it.

## The count grid

- **Antibody** is the specificity.
- **Count** is how many qualifying cells ruled it out.
- **Needed** is the minimum required. That number comes from a rule on the Rules tab when one exists for the antibody, and otherwise from **Default rule-outs** in Preferences.
- **Status** says whether the needed count was met.
- **Explanation** states why the row passed or failed, including zygosity limits.

Green rows met the required count. Yellow rows did not. Hover a row to read the same explanation.

## Cells that counted

Select a row. The lower grid lists the cells that counted toward that rule-out, with the panel and the grades. A cell counts when it is antigen-positive, nonreactive in the phases that count, and allowed by the zygosity rule. Homozygous cells are the usual standard. **Heterozygous OK** on a custom rule allows a heterozygous cell to count for that antibody.

Antigen-positive cells that reacted do not rule the antibody out. w+, mixed field, and hemolysis are reactive, so they do not count as rule-outs. Cells whose antigen was destroyed by ficin, papain, or DTT are listed on **Special Panels** as gated rule-outs instead of counting here.

The specimen detail pane on the Specimens tab repeats the ruled-out list as a summary. This tab is the one that shows the cells behind each count.
