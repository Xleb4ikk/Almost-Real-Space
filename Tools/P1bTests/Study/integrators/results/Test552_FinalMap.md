# Test552: final regime map

One row per mode, generated from the same CSV as the mode summary. A decisive winner is named only when margin > 2 and the reference is a different family. Otherwise the union of the equivalent sets is shown. Floor-limited and uncertified cells are listed separately and never promoted.

| mode | scenarios | cells | decisive winner / equivalent set | failed-validity | floor-limited | uncertified-ref | trivial | N | W | median margin | bad share | confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
|drag|1|3|eq: DOP853-adaptive, IAS15|-|0|1|0|1|0|1.39|0.00|low|
|eccentric|3|9|eq: RKN12(10), WH-BM14A, WH-Y6, DOP853-adaptive|-|2|0|0|3|0|1.23|0.22|low|
|flyby|2|6|-|-|6|0|0|2|0|-|1.00|low|
|heliocentric|1|3|eq: RKN12(10)|-|2|1|0|1|0|2.65|0.67|low|
|heliocentric-eccentric|1|3|eq: RKN12(10)|-|0|3|0|1|0|1.74|0.00|low|
|j2|2|6|WH-BM14A (1)|-|1|0|0|2|1|1.08|0.17|medium|
|pure-circular|3|9|eq: WH-BM14A, WH-Y6, GJ8, RKN12(10)|-|2|0|0|3|0|1.31|0.22|low|
|pure-circular-long|2|6|eq: WH-BM14A, WH-Y6|-|0|0|0|2|0|1.21|0.33|low|
|thrust|2|6|eq: DOP853-adaptive, DOPRI5-adaptive, IAS15|-|0|2|0|2|0|1.46|0.00|low|
