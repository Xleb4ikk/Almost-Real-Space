# Test555: mode summary

Generated from the sweep CSV by `StudyRegimeAnalysis`; the same rows produce the 2D maps and the final regime table. `decisive` = margin > 2, `marginal` = 1.5-2, `equivalent` = < 1.5. `floor-limited` = a cheaper grid point already met the target (EDGE) or the cheapest point is the grid minimum; such a cell is never called equivalent. `trivial` = the cheapest bracketed cost is below 200 RHS calls. `uncertified-reference` = the winner belongs to the same broad family as the reference, so it is not a decisive accuracy claim. Confidence uses the rule in `ORBIT_INTEGRATOR_TEST_SCHEME.md`.

| mode | scenarios | cells | decisive | marginal | equivalent | trivial | floor-limited | same-family ref | unresolved | no winner | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
|altitude-ladder|6|0|0|0|0|0|0|0|0|0|-|1.00|none|
|ascent|1|0|0|0|0|0|0|0|0|0|-|1.00|none|
|atmosphere-entry|1|0|0|0|0|0|0|0|0|0|-|1.00|none|
|ballistic-hop|3|0|0|0|0|0|0|0|0|0|-|1.00|none|
|parachute|3|0|0|0|0|0|0|0|0|0|-|1.00|none|
|suicide-burn|1|0|0|0|0|0|0|0|0|0|-|1.00|none|

Confidence rule: `high` = N>=3, W>=3, median margin > 1.5, bad share (trivial+floor-limited+unresolved)/cells < 0.25; `medium` = N>=2, W>=1, median margin > 1.0, bad share < 0.60; `low` otherwise; `none` = no certified cells. `N` excludes near-uniform-field scenarios; `W` is decisive cells only.
