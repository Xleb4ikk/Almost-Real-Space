# Work-precision crossovers

Replayed from the existing `Test552/553/554` CSVs; no integrator run was performed here.
A crossover is the error where the cheapest method changes. `EDGE` means one side is an upper bound (the cheapest tested point is already below the target); treat that crossing as provisional.

| scenario | error, m | from | to | note |
|---|---|---|---|---|
|Custom 6-body, 10 y|1.558E+001|GJ8|RKN12(10)|interpolated|
|Earth 400 km + J2|2.913E+001|WH-Y6|WH-BM14A|interpolated|
|Earth GTO 400 x 40000 km|6.802E+002|RKN12(10)|WH-Y6|interpolated|
|Earth GTO 400 x 40000 km|1.481E+001|WH-BM14A|WH-Y6|interpolated|
|Earth HEO 300 x 120000 km|2.007E+003|DOP853-adaptive|WH-Y6|interpolated|
|Earth HEO 300 x 120000 km|9.713E+002|WH-Y6|DOP853-adaptive|interpolated|
|Earth HEO 300 x 120000 km|8.379E+002|DOP853-adaptive|RKN12(10)|interpolated|
|Earth LEO 400 km polar|2.251E+001|WH-Y6|WH-BM14A|interpolated|
|Earth LEO 400 km sun-synchronous|1.287E+003|RKN12(10)|WH-BM14A|interpolated|
|Earth LEO 400 km sun-synchronous|8.627E+000|WH-BM14A|RKN12(10)|interpolated|
|Earth LEO 400 km, 1000 orbits (~64 d)|1.012E+003|WH-Y6|WH-BM14A|interpolated|
|Earth LEO 400 km, 1000 orbits (~64 d)|9.470E-001|WH-BM14A|WH-Y6|interpolated|
|Earth LEO 400 km, 1000 orbits (~64 d)|3.280E-002|WH-BM14A|RKN12(10)|interpolated|
|Earth LEO 400 km, 1000 orbits (~64 d)|2.103E-002|RKN12(10)|WH-BM14A|interpolated|
|Earth Molniya 500 x 40000 km|3.097E+002|WH-Y6|WH-BM14A|interpolated|
|Earth Molniya 500 x 40000 km|8.670E+001|WH-Y6|WH-BM14A|interpolated|
|Earth flyby 1.1 R vinf 1000|1.760E+001|WH-Y6|DOP853-adaptive|interpolated|
|Earth flyby 1.1 R vinf 1000|3.885E-001|DOP853-adaptive|RKN12(10)|interpolated|
|Earth flyby 1.1 R vinf 10000|1.295E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Earth flyby 10 R vinf 1000|2.997E+002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Earth flyby 10 R vinf 10000|1.398E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Earth flyby 10 R vinf 10000|2.472E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Earth flyby 3 R vinf 1000|8.867E+001|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Earth flyby 3 R vinf 1000|8.932E-001|DOP853-adaptive|RKN12(10)|interpolated|
|Earth flyby 3 R vinf 10000|1.038E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Earth flyby 3 R vinf 10000|1.808E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Earth hyperbolic flyby e=1.5|8.658E-001|DOP853-adaptive|WH-BM14A|interpolated|
|Earth hyperbolic flyby e=1.5|2.333E-001|WH-BM14A|RKN12(10)|interpolated|
|Earth-Moon flyby inside lunar SOI (Moon-centered)|5.751E+001|DOPRI5-adaptive|WH-Y6|interpolated|
|Far flight 30 AU e 0.5 (physical)|2.613E+001|Leapfrog|DOPRI5-adaptive|interpolated|
|Far flight 30 AU e 0.5 (physical)|4.468E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Far flight 30 AU e 0.5 (physical) (normalized)|2.608E+001|Leapfrog|DOPRI5-adaptive|interpolated|
|Heliocentric 0.3 AU e 0|4.718E+003|DOP853-adaptive|SRKN14A|interpolated|
|Heliocentric 0.3 AU e 0|1.434E+000|DOP853-adaptive|GJ8|interpolated|
|Heliocentric 0.3 AU e 0|1.615E-001|GJ8|WH-Y6|interpolated|
|Heliocentric 0.3 AU e 0|1.066E-002|WH-Y6|RKN12(10)|interpolated|
|Heliocentric 0.3 AU e 0.5|3.076E+002|RKN12(10)|WH-Y6|interpolated|
|Heliocentric 0.3 AU e 0.99|1.086E+003|DOP853-adaptive|RKN12(10)|interpolated|
|Heliocentric 30 AU e 0.5|2.612E+001|Leapfrog|DOPRI5-adaptive|interpolated|
|Heliocentric 30 AU e 0.9|3.181E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Heliocentric 5 AU e 0|8.226E+002|Leapfrog|DOPRI5-adaptive|interpolated|
|Heliocentric 5 AU e 0|3.069E-002|DOP853-adaptive|RKN12(10)|EDGE|
|Heliocentric Sun-Jupiter probe, 10 y|3.057E+003|WH-Y6|RKN12(10)|interpolated|
|Moon flyby vinf 1000 alt 1|3.616E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 1000 alt 100|1.326E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 1000 alt 1000|3.533E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 10000 alt 1|8.266E+003|DOPRI5-fixed|DOPRI5-adaptive|interpolated|
|Moon flyby vinf 10000 alt 1|2.129E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 10000 alt 10|8.302E+003|DOPRI5-fixed|DOPRI5-adaptive|interpolated|
|Moon flyby vinf 10000 alt 10|3.187E-004|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 10000 alt 100|8.659E+003|DOPRI5-fixed|DOPRI5-adaptive|interpolated|
|Moon flyby vinf 10000 alt 1000|3.204E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 200 alt 1|9.297E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 200 alt 1|9.742E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 200 alt 10|9.789E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 3000 alt 1|1.106E-001|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 3000 alt 1|1.565E-003|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 3000 alt 10|1.118E-001|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 3000 alt 10|1.576E-003|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 3000 alt 1000|2.243E-001|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 50 alt 1|9.916E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 50 alt 1|1.210E-001|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 50 alt 10|9.966E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 50 alt 100|1.277E-001|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 500 alt 10|6.583E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 500 alt 100|5.651E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 500 alt 100|6.614E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon flyby vinf 500 alt 1000|6.577E+000|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Moon flyby vinf 500 alt 1000|9.257E-002|DOP853-adaptive|RKN12(10)|interpolated|
|Moon hop 1000 m/s|1.709E+002|Leapfrog|RK4|interpolated|
|Moon hop 1000 m/s|2.790E-004|SRKN14A|DOP853-adaptive|interpolated|
|Moon hop 1200 m/s|6.782E-006|DOP853-adaptive|WH-Y6|EDGE|
|Moon hop 1400 m/s|8.114E+001|PEFRL|RK4|interpolated|
|Moon hop 1400 m/s|1.018E+001|RK4|DOPRI5-fixed|interpolated|
|Moon hop 1400 m/s|4.938E-003|DOPRI5-fixed|SRKN14A|interpolated|
|Moon hop 1400 m/s|4.310E-004|SRKN14A|DOP853-adaptive|interpolated|
|Moon hop 1400 m/s|3.105E-004|DOP853-adaptive|SRKN14A|interpolated|
|Moon hop 1500 m/s|1.529E+002|Leapfrog|PEFRL|interpolated|
|Moon hop 1500 m/s|1.263E+001|RK4|DOPRI5-fixed|interpolated|
|Moon hop 1500 m/s|7.042E-004|DOPRI5-fixed|DOP853-adaptive|interpolated|
|Moon hop 1500 m/s|1.143E-005|SRKN14A|DOP853-adaptive|EDGE|
|Moon hop 1600 m/s|1.407E+002|RK4|PEFRL|interpolated|
|Moon hop 1600 m/s|2.181E-004|SRKN14A|DOP853-adaptive|EDGE|
|Moon hop 1600 m/s|2.569E-006|DOP853-adaptive|WH-Y6|EDGE|
|Moon hop 200 m/s|2.003E-003|Leapfrog|RK4|interpolated|
|Moon hop 200 m/s|4.862E-004|RK4|PEFRL|interpolated|
|Moon hop 200 m/s|3.043E-005|PEFRL|RK4|EDGE|
|Moon hop 200 m/s|1.304E-005|RK4|DOPRI5-fixed|interpolated|
|Moon hop 200 m/s|3.382E-006|DOPRI5-fixed|RK4|EDGE|
|Moon hop 200 m/s|1.285E-006|Y6C|RK4|EDGE|
|Moon hop 500 m/s|7.590E-001|Leapfrog|RK4|interpolated|
|Moon orbit 1 km|1.141E+001|DOPRI5-fixed|SRKN14A|interpolated|
|Moon orbit 10 km|1.147E+001|DOPRI5-fixed|SRKN14A|interpolated|
|Moon orbit 10 km|1.609E-001|SRKN14A|DOP853-adaptive|interpolated|
|Moon orbit 10 km|1.142E-006|WH-Y6|RKN12(10)|EDGE|
|Moon orbit 100 km|1.692E-001|SRKN14A|DOP853-adaptive|interpolated|
|Moon orbit 100 km|1.147E-006|WH-Y6|RKN12(10)|EDGE|
|Moon orbit 1000 km|4.113E+002|PEFRL|DOPRI5-fixed|interpolated|
|Moon orbit 1000 km|1.796E+001|DOPRI5-fixed|SRKN14A|interpolated|
|Moon orbit 1000 km|2.520E-001|SRKN14A|DOP853-adaptive|interpolated|
|Moon orbit 1000 km|1.814E-006|WH-BM14A|RKN12(10)|EDGE|
|Real solar 7-body, 10 y|6.590E+001|GJ8|RKN12(10)|interpolated|
|Real solar 7-body, 10 y|4.701E+000|GJ8|RKN12(10)|interpolated|
|Real solar 7-body, 100 y|3.282E+002|GJ8|RKN12(10)|interpolated|
|Small-body flyby vinf 10|1.151E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Small-body flyby vinf 100|1.047E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Small-body flyby vinf 50|4.753E+003|Leapfrog|PEFRL|interpolated|
|Small-body flyby vinf 50|1.124E-002|DOPRI5-adaptive|DOP853-adaptive|interpolated|
|Sun 1 AU circular|6.044E+000|DOP853-adaptive|RKN12(10)|interpolated|

## Where the 6th-order advantage ends

Rows where a WH method leaves the lead (or takes it) are the practical boundary of the Kepler-split regime.

| scenario | error, m | from | to |
|---|---|---|---|
|Earth GTO 400 x 40000 km|6.802E+002|RKN12(10)|WH-Y6|
|Earth HEO 300 x 120000 km|2.007E+003|DOP853-adaptive|WH-Y6|
|Earth HEO 300 x 120000 km|9.713E+002|WH-Y6|DOP853-adaptive|
|Earth LEO 400 km sun-synchronous|1.287E+003|RKN12(10)|WH-BM14A|
|Earth LEO 400 km sun-synchronous|8.627E+000|WH-BM14A|RKN12(10)|
|Earth LEO 400 km, 1000 orbits (~64 d)|3.280E-002|WH-BM14A|RKN12(10)|
|Earth LEO 400 km, 1000 orbits (~64 d)|2.103E-002|RKN12(10)|WH-BM14A|
|Earth flyby 1.1 R vinf 1000|1.760E+001|WH-Y6|DOP853-adaptive|
|Earth hyperbolic flyby e=1.5|8.658E-001|DOP853-adaptive|WH-BM14A|
|Earth hyperbolic flyby e=1.5|2.333E-001|WH-BM14A|RKN12(10)|
|Earth-Moon flyby inside lunar SOI (Moon-centered)|5.751E+001|DOPRI5-adaptive|WH-Y6|
|Heliocentric 0.3 AU e 0|1.615E-001|GJ8|WH-Y6|
|Heliocentric 0.3 AU e 0|1.066E-002|WH-Y6|RKN12(10)|
|Heliocentric 0.3 AU e 0.5|3.076E+002|RKN12(10)|WH-Y6|
|Heliocentric Sun-Jupiter probe, 10 y|3.057E+003|WH-Y6|RKN12(10)|
|Moon hop 1200 m/s|6.782E-006|DOP853-adaptive|WH-Y6|
|Moon hop 1600 m/s|2.569E-006|DOP853-adaptive|WH-Y6|
|Moon orbit 10 km|1.142E-006|WH-Y6|RKN12(10)|
|Moon orbit 100 km|1.147E-006|WH-Y6|RKN12(10)|
|Moon orbit 1000 km|1.814E-006|WH-BM14A|RKN12(10)|
