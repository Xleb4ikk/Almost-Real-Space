# Test559: final regime map

One row per mode, generated from the same CSV as the mode summary. A decisive winner is named only when margin > 2 and the reference is a different family. Otherwise the union of the equivalent sets is shown. Floor-limited and uncertified cells are listed separately and never promoted.

| mode | scenarios | cells | decisive winner / equivalent set | failed-validity | floor-limited | uncertified-ref | trivial | N | W | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
|far-flight|1|6|-|-|3|0|0|1|0|-|1.00|low|
|far-normalized|1|6|-|-|4|0|0|1|0|-|1.00|low|
|heliocentric-orbit|16|96|RKN12(10) (2)|-|23|18|1|16|2|1.69|0.62|low|
|moon-flyby|24|144|eq: DOP853-adaptive, RKN12(10), DOPRI5-adaptive|-|22|3|6|24|0|1.16|0.19|low|
|moon-hop|8|42|-|-|38|0|4|7|0|-|1.00|low|
|moon-orbit|7|42|eq: WH-BM14A, RKN12(10), GJ8, WH-Y6|-|24|0|0|7|0|1.07|0.62|low|
|planet-flyby|6|36|eq: RKN12(10), DOP853-adaptive, DOPRI5-adaptive|-|9|5|0|6|0|1.20|0.25|low|
|small-body-flyby|3|18|-|-|12|0|6|3|0|-|1.00|low|
