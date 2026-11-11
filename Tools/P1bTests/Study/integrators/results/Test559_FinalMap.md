# Test559: final regime map

One row per mode, generated from the same CSV as the mode summary. A decisive winner is named only when margin > 2 and the reference is a different family. Otherwise the union of the equivalent sets is shown. Floor-limited and uncertified cells are listed separately and never promoted.

| mode | scenarios | cells | decisive winner / equivalent set | failed-validity | floor-limited | uncertified-ref | trivial | N | W | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
|far-flight|1|6|-|-|3|0|0|1|0|-|1.00|low|
|far-normalized|1|6|-|-|3|0|0|1|0|-|1.00|low|
|heliocentric-orbit|16|96|RKN12(10) (1)|-|23|18|1|16|1|1.61|0.66|low|
|moon-flyby|24|144|eq: DOP853-adaptive, RKN12(10), DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s]|-|18|5|7|24|0|1.17|0.17|low|
|moon-hop|8|42|-|-|39|0|3|7|0|-|1.00|low|
|moon-orbit|7|42|eq: WH-BM14A, RKN12(10), GJ8, WH-Y6|-|25|0|0|7|0|1.20|0.67|low|
|planet-flyby|6|36|eq: RKN12(10), DOP853-adaptive, DOPRI5-adaptive|-|2|5|0|6|0|1.16|0.06|low|
|small-body-flyby|3|18|eq: DOP853-adaptive, DOP853-adaptive [cap=600 s], DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], RKN12(10)|-|3|0|6|3|0|1.00|0.50|low|
