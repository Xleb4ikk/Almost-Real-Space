# Test_ALL: final regime map

Combined mechanically from Test559_FinalMap.md, Test552_FinalMap.md and Test558_FinalMap.md. Every row was produced by StudyRegimeAnalysis from its sweep CSV.

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
|drag|1|3|eq: DOP853-adaptive, IAS15|-|0|1|0|1|0|1.39|0.00|low|
|eccentric|3|9|eq: RKN12(10), WH-BM14A, WH-Y6, DOP853-adaptive|-|2|0|0|3|0|1.23|0.22|low|
|flyby|2|6|-|-|6|0|0|2|0|-|1.00|low|
|heliocentric|1|3|eq: RKN12(10)|-|2|1|0|1|0|2.65|0.67|low|
|heliocentric-eccentric|1|3|eq: RKN12(10)|-|0|3|0|1|0|1.74|0.00|low|
|j2|2|6|WH-BM14A (1)|-|1|0|0|2|1|1.08|0.17|medium|
|pure-circular|3|9|eq: WH-BM14A, WH-Y6, GJ8, RKN12(10)|-|2|0|0|3|0|1.31|0.22|low|
|pure-circular-long|2|6|eq: WH-BM14A, WH-Y6|-|0|0|0|2|0|1.21|0.33|low|
|thrust|2|6|eq: DOP853-adaptive, DOPRI5-adaptive, IAS15|-|0|2|0|2|0|1.46|0.00|low|
|j2-sensitivity|16|80|WH-BM14A (3)|-|6|2|0|16|3|1.23|0.07|medium|
|sun-normalized|1|5|-|-|5|0|0|1|0|-|1.00|low|
