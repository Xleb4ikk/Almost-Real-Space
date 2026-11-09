# Test552: mode summary

Generated from the sweep CSV by `StudyRegimeAnalysis`; the same rows produce the 2D maps and the final regime table. `decisive` = margin > 2, `marginal` = 1.5-2, `equivalent` = < 1.5. `floor-limited` = a cheaper grid point already met the target (EDGE) or the cheapest point is the grid minimum; such a cell is never called equivalent. `trivial` = the cheapest bracketed cost is below 200 RHS calls. `uncertified-reference` = the winner belongs to the same broad family as the reference, so it is not a decisive accuracy claim. Confidence uses the rule in `ORBIT_INTEGRATOR_TEST_SCHEME.md`.

| mode | scenarios | cells | decisive | marginal | equivalent | trivial | floor-limited | same-family ref | unresolved | no winner | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
|drag|1|3|0|0|2|0|0|1|0|0|1.39|0.00|low|
|eccentric|3|9|0|0|7|0|2|0|0|0|1.23|0.22|low|
|flyby|2|6|0|0|0|0|6|0|0|0|-|1.00|low|
|heliocentric|1|3|0|0|0|0|2|1|0|0|2.65|0.67|low|
|heliocentric-eccentric|1|3|0|0|0|0|0|3|0|0|1.74|0.00|low|
|j2|2|6|1|1|3|0|1|0|0|0|1.08|0.17|medium|
|pure-circular|3|9|0|0|7|0|2|0|0|0|1.31|0.22|low|
|pure-circular-long|2|6|0|0|4|0|0|0|0|2|1.21|0.33|low|
|thrust|2|6|0|0|4|0|0|2|0|0|1.46|0.00|low|

Confidence rule: `high` = N>=3, W>=3, median margin > 1.5, bad share (trivial+floor-limited+unresolved)/cells < 0.25; `medium` = N>=2, W>=1, median margin > 1.0, bad share < 0.60; `low` otherwise; `none` = no certified cells. `N` excludes near-uniform-field scenarios; `W` is decisive cells only.
