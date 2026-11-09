# Test559: mode summary

Generated from the sweep CSV by `StudyRegimeAnalysis`; the same rows produce the 2D maps and the final regime table. `decisive` = margin > 2, `marginal` = 1.5-2, `equivalent` = < 1.5. `floor-limited` = a cheaper grid point already met the target (EDGE) or the cheapest point is the grid minimum; such a cell is never called equivalent. `trivial` = the cheapest bracketed cost is below 200 RHS calls. `uncertified-reference` = the winner belongs to the same broad family as the reference, so it is not a decisive accuracy claim. Confidence uses the rule in `ORBIT_INTEGRATOR_TEST_SCHEME.md`.

| mode | scenarios | cells | decisive | marginal | equivalent | trivial | floor-limited | same-family ref | unresolved | no winner | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
|far-flight|1|6|0|0|0|0|3|0|0|3|-|1.00|low|
|far-normalized|1|6|0|0|0|0|4|0|0|2|-|1.00|low|
|heliocentric-orbit|16|96|2|1|15|1|23|18|0|36|1.69|0.62|low|
|moon-flyby|24|144|0|0|113|6|22|3|0|0|1.16|0.19|low|
|moon-hop|8|42|0|0|0|4|38|0|0|0|-|1.00|low|
|moon-orbit|7|42|0|1|15|0|24|0|0|2|1.07|0.62|low|
|planet-flyby|6|36|0|0|22|0|9|5|0|0|1.20|0.25|low|
|small-body-flyby|3|18|0|0|0|6|12|0|0|0|-|1.00|low|

Confidence rule: `high` = N>=3, W>=3, median margin > 1.5, bad share (trivial+floor-limited+unresolved)/cells < 0.25; `medium` = N>=2, W>=1, median margin > 1.0, bad share < 0.60; `low` otherwise; `none` = no certified cells. `N` excludes near-uniform-field scenarios; `W` is decisive cells only.
