# DYNAMIC_REPORT: режим → интегратор → доказательство (D4)

Generated from `dynamic.csv`; no hand edits.
Branch R1 = WH-BM14A through VesselStep (flags on in the test rig only),
R1_out = must stay DOP853, R3 = Sun-coast report only (flag false in the game).

Reference for R1 error: the certified default path (DOP853, flags off) on the
same VesselStep scenario; gate is the §6 absolute target 1 m. The stand's
certified reference is a different study-frame model and is not used here.

| branch | scenario | metric | value | unit | reference | verdict | note |
|---|---|---|---:|---|---:|---|---|
| R1 | S01 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S01 200 km | wh_chunk_share | 4.0277777777777779E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S01 200 km | error_max_m | 2.3707135538767379E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S01 200 km | fallbacks | 2.0500000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S01 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 200 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S01 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S01 200 km | wall_ms_wh | 2.1679999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 200 km | wall_ms_dop853 | 2.1210000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 200 km | wall_ratio | 1.0221593587930220E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S01 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S01 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S01 400 km | error_max_m | 1.5857406673037391E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S01 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S01 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S01 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S01 400 km | wall_ms_wh | 2.2359999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 400 km | wall_ms_dop853 | 2.0960000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 400 km | wall_ratio | 1.0667938931297709E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S01 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S01 2000 km | wh_chunk_share | 5.7638888888888884E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S01 2000 km | error_max_m | 2.9194276598524894E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S01 2000 km | fallbacks | 1.7900000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S01 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 2000 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S01 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S01 2000 km | wall_ms_wh | 2.0990000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 2000 km | wall_ms_dop853 | 2.0880000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 2000 km | wall_ratio | 1.0052681992337165E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 200 km | wh_chunk_share | 3.5416666666666669E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 200 km | error_max_m | 4.6495974053156065E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 200 km | fallbacks | 1.8600000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 200 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 200 km | wall_ms_wh | 2.0849999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 200 km | wall_ms_dop853 | 2.0840000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 200 km | wall_ratio | 1.0004798464491362E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 400 km | error_max_m | 1.2777292242611338E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 400 km | wall_ms_wh | 2.2370000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 400 km | wall_ms_dop853 | 2.0880000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 400 km | wall_ratio | 1.0713601532567050E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 2000 km | wh_chunk_share | 6.3194444444444442E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 2000 km | error_max_m | 4.1793823690845153E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 2000 km | fallbacks | 1.7700000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 2000 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 2000 km | wall_ms_wh | 2.1170000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 2000 km | wall_ms_dop853 | 2.1210000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 2000 km | wall_ratio | 9.9811409712399801E-001 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 200 km | wh_chunk_share | 6.9444444444444441E-003 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 200 km | error_max_m | 7.6931721086691838E-003 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 200 km | fallbacks | 1.9000000000000000E+001 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 200 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 200 km | wall_ms_wh | 2.4460000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 200 km | wall_ms_dop853 | 2.4220000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 200 km | wall_ratio | 1.0099091659785302E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 400 km | error_max_m | 3.1953397139973611E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 400 km | wall_ms_wh | 2.5540000000000002E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 400 km | wall_ms_dop853 | 2.2960000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 400 km | wall_ratio | 1.1123693379790942E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 2000 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 2000 km | error_max_m | 5.2848432556229695E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 2000 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 2000 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 2000 km | wall_ms_wh | 2.3980000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 2000 km | wall_ms_dop853 | 2.2550000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 2000 km | wall_ratio | 1.0634146341463415E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 200 km | wh_chunk_share | 6.9444444444444441E-003 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 200 km | error_max_m | 2.5754105812683767E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 200 km | fallbacks | 1.9000000000000000E+001 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 200 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 200 km | wall_ms_wh | 2.2780000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 200 km | wall_ms_dop853 | 2.2800000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 200 km | wall_ratio | 9.9912280701754386E-001 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 400 km | error_max_m | 2.5691179519147962E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 400 km | wall_ms_wh | 2.4470000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 400 km | wall_ms_dop853 | 2.3230000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 400 km | wall_ratio | 1.0533792509685751E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 2000 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 2000 km | error_max_m | 1.9700868216513776E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 2000 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 2000 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 2000 km | wall_ms_wh | 2.5869999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 2000 km | wall_ms_dop853 | 2.4879999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 2000 km | wall_ratio | 1.0397909967845660E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S01 event | event_crossing_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | altitude crossing 420 km; WH vs DOP853 |
| R1 | S01 event | event_timed_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | timed event 1234.5 s; WH vs DOP853 |
| R1_out | e=0.2 | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | e=0.2 | bitwise_vs_off | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | alt 3000 km | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | alt 3000 km | bitwise_vs_off | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | chunk 300 s | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | chunk 300 s | bitwise_vs_off | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | thrust | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | thrust | bitwise_vs_off | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | sun-dominant | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1_out | sun-dominant | bitwise_vs_off | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R3 | 1 AU 30 s | selected_dopri5 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R3 | 1 AU 30 s | error_max_m | 1.5258800983424298E-003 | m | 1.0000000000000000E+000 | PASS | 1 day vs DOP853 |
| R3 | 1 AU 30 s | wall_ms | 2.2000000000000001E-003 | ms |  | INFO | median of 5 |
| R3 | 1 AU 30 s | wall_ms_dop853 | 3.3000000000000000E-003 | ms |  | INFO | median of 5 |
| R3 | 1 AU 30 s | rhs_eq_est | 1.7280000000000000E+004 | calls |  | INFO | attempts x stages (VesselStep has no RHS counter) |
| R3 | 1 AU 30 s | event_timed_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | timed event 3600 s; DOPRI5 vs DOP853 |
| R3 | 1 AU 600 s | selected_dopri5 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R3 | 1 AU 600 s | error_max_m | 1.4953622594472847E-003 | m | 1.0000000000000000E+000 | PASS | 1 day vs DOP853 |
| R3 | 1 AU 600 s | wall_ms | 4.2599999999999999E-002 | ms |  | INFO | median of 5 |
| R3 | 1 AU 600 s | wall_ms_dop853 | 6.0800000000000000E-002 | ms |  | INFO | median of 5 |
| R3 | 1 AU 600 s | rhs_eq_est | 8.6400000000000000E+002 | calls |  | INFO | attempts x stages (VesselStep has no RHS counter) |
| R3 | 1 AU 600 s | event_timed_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | timed event 3600 s; DOPRI5 vs DOP853 |
| R3 | 5 AU | selected_dopri5 | 1.0000000000000000E+000 | 1/0 | 0.0000000000000000E+000 | FAIL | TASK_D expects 5 AU to stay on DOP853 |
| R3 | cislunar-like | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | near another body keeps DOP853 |

## Gate summary

* PASS: 79
* INFO: 66
* FAIL: 13
