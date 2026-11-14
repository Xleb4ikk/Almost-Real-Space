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
| R1 | S01 200 km | wall_ms_wh | 2.2559999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 200 km | wall_ms_dop853 | 2.1049999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 200 km | wall_ratio | 1.0717339667458432E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S01 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S01 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S01 400 km | error_max_m | 1.5857406673037391E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S01 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S01 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S01 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S01 400 km | wall_ms_wh | 2.2630000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 400 km | wall_ms_dop853 | 2.1540000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 400 km | wall_ratio | 1.0506035283194057E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S01 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S01 2000 km | wh_chunk_share | 5.7638888888888884E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S01 2000 km | error_max_m | 2.9194276598524894E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S01 2000 km | fallbacks | 1.7900000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S01 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S01 2000 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S01 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S01 2000 km | wall_ms_wh | 2.0640000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 2000 km | wall_ms_dop853 | 2.0569999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S01 2000 km | wall_ratio | 1.0034030140982013E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 200 km | wh_chunk_share | 3.5416666666666669E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 200 km | error_max_m | 4.6495974053156065E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 200 km | fallbacks | 1.8600000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 200 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 200 km | wall_ms_wh | 2.1010000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 200 km | wall_ms_dop853 | 2.0899999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 200 km | wall_ratio | 1.0052631578947369E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 400 km | error_max_m | 1.2777292242611338E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 400 km | wall_ms_wh | 2.2300000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 400 km | wall_ms_dop853 | 2.0920000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 400 km | wall_ratio | 1.0659655831739963E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S02 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S02 2000 km | wh_chunk_share | 6.3194444444444442E-001 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S02 2000 km | error_max_m | 4.1793823690845153E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S02 2000 km | fallbacks | 1.7700000000000000E+002 | count |  | INFO | guard fallbacks (D3) |
| R1 | S02 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S02 2000 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S02 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S02 2000 km | wall_ms_wh | 2.0880000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 2000 km | wall_ms_dop853 | 2.0760000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S02 2000 km | wall_ratio | 1.0057803468208093E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 200 km | wh_chunk_share | 6.9444444444444441E-003 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 200 km | error_max_m | 7.6931721086691838E-003 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 200 km | fallbacks | 1.9000000000000000E+001 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 200 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 200 km | wall_ms_wh | 2.3540000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 200 km | wall_ms_dop853 | 2.3530000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 200 km | wall_ratio | 1.0004249893752655E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 400 km | error_max_m | 3.1953397139973611E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 400 km | wall_ms_wh | 2.4670000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 400 km | wall_ms_dop853 | 2.3740000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 400 km | wall_ratio | 1.0391743892165122E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S03 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S03 2000 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S03 2000 km | error_max_m | 5.2848432556229695E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S03 2000 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S03 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S03 2000 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S03 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S03 2000 km | wall_ms_wh | 2.3449999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 2000 km | wall_ms_dop853 | 2.2689999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S03 2000 km | wall_ratio | 1.0334949316879682E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 200 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 200 km | wh_chunk_share | 6.9444444444444441E-003 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 200 km | error_max_m | 2.5754105812683767E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 200 km | fallbacks | 1.9000000000000000E+001 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 200 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 200 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 200 km | alloc_bytes_wh | 7.4400000000000000E+004 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 200 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 200 km | wall_ms_wh | 2.2259999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 200 km | wall_ms_dop853 | 2.2209999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 200 km | wall_ratio | 1.0022512381809996E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 400 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 400 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 400 km | error_max_m | 2.5691179519147962E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 400 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 400 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 400 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 400 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 400 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 400 km | wall_ms_wh | 2.3449999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 400 km | wall_ms_dop853 | 2.2380000000000000E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 400 km | wall_ratio | 1.0478105451295798E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
| R1 | S09 2000 km | selected_first_is_wh | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | selection of the first 600 s chunk |
| R1 | S09 2000 km | wh_chunk_share | 1.0000000000000000E+000 | fraction |  | INFO | calls whose policy selection was WH |
| R1 | S09 2000 km | error_max_m | 1.9700868216513776E-002 | m | 1.0000000000000000E+000 | PASS | max abs(r_WH - r_DOP853) over 1 day |
| R1 | S09 2000 km | fallbacks | 0.0000000000000000E+000 | count |  | INFO | guard fallbacks (D3) |
| R1 | S09 2000 km | wh_state_finite | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 2000 km | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R1 | S09 2000 km | alloc_bytes_wh | 6.8800000000000000E+003 | B | 7.4400000000000000E+004 | PASS | one 600 s chunk |
| R1 | S09 2000 km | alloc_bytes_dop853 | 7.4400000000000000E+004 | B |  | INFO | one 600 s chunk |
| R1 | S09 2000 km | wall_ms_wh | 2.3499999999999999E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 2000 km | wall_ms_dop853 | 2.2520000000000001E-001 | ms |  | INFO | median of 5; one 600 s call |
| R1 | S09 2000 km | wall_ratio | 1.0435168738898755E+000 | x | 5.0000000000000000E-001 | FAIL | policy + WH <= 0.5 x DOP853 (option A) |
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
| R3 | 1 AU 30 s | wall_ms_dop853 | 3.0000000000000001E-003 | ms |  | INFO | median of 5 |
| R3 | 1 AU 30 s | rhs_eq_est | 1.7280000000000000E+004 | calls |  | INFO | attempts x stages (VesselStep has no RHS counter) |
| R3 | 1 AU 30 s | event_timed_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | timed event 3600 s; DOPRI5 vs DOP853 |
| R3 | 1 AU 600 s | selected_dopri5 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| R3 | 1 AU 600 s | error_max_m | 1.4953622594472847E-003 | m | 1.0000000000000000E+000 | PASS | 1 day vs DOP853 |
| R3 | 1 AU 600 s | wall_ms | 4.1000000000000002E-002 | ms |  | INFO | median of 5 |
| R3 | 1 AU 600 s | wall_ms_dop853 | 5.9499999999999997E-002 | ms |  | INFO | median of 5 |
| R3 | 1 AU 600 s | rhs_eq_est | 8.6400000000000000E+002 | calls |  | INFO | attempts x stages (VesselStep has no RHS counter) |
| R3 | 1 AU 600 s | event_timed_dt_s | 0.0000000000000000E+000 | s | 1.0000000000000000E-003 | PASS | timed event 3600 s; DOPRI5 vs DOP853 |
| R3 | 5 AU | selected_dopri5 | 1.0000000000000000E+000 | 1/0 | 0.0000000000000000E+000 | FAIL | TASK_D expects 5 AU to stay on DOP853 |
| R3 | cislunar-like | selected_dop853 | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | near another body keeps DOP853 |
| D5 | surface liftoff 0.05 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | surface liftoff 0.05 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | ascent 5 km 0.05 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | ascent 5 km 0.05 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | LEO live 1/60 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | LEO live 1/60 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | LEO long-warp 600 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | LEO long-warp 600 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | thrust maneuver 600 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | thrust maneuver 600 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | LEO long-warp again 600 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | LEO long-warp again 600 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | touchdown 0.05 s | integrator | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS | reason=Default |
| D5 | touchdown 0.05 s | reason | 0.0000000000000000E+000 | enum | 0.0000000000000000E+000 | PASS |  |
| D5 | touchdown | event_touchdown_s | 1.8005681549495312E+003 | s |  | PASS |  |
| D5 | touchdown | regime_landed | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS |  |
| D5 | sequence | determinism_bitwise | 1.0000000000000000E+000 | 1/0 | 1.0000000000000000E+000 | PASS | two identical scripted runs |

## Gate summary

* PASS: 96
* INFO: 66
* FAIL: 13

## Decision table statuses

| row | status | why |
|---|---|---|
| R0 | DEFAULT | DOP853, the certified default |
| R1 WH-BM14A | REJECTED | D4 wall gate: 0.998-1.112x > 0.5x through VesselStep |
| R2 Surface | OFF-BY-OWNER | Q2 = NO; D1.c NO-EVIDENCE |
| R3 Sun-coast | OFF-BY-OWNER | Q3 = NO; D4 report only |
| R4 GTO/Molniya | OFF-BY-OWNER | Q4 = NO (BACKLOG B14) |
| R5 HEO | DEFAULT | no port |
| R6 GEO | DEFAULT | no port; wall fails |
| R7 flyby | OFF-BY-OWNER | Q8 = NO |
| R8 J2 near-circular | DEFAULT | WH-Y6 breaks on J2 |
| R9 J2 == 0 | OFF-BY-OWNER | Q6 = NO |
| R10 chaos/encounters | OFF-BY-OWNER | Q7 = NO |

## Flags

* `EnableRegimeIntegratorPolicy` = false (default; serialized 0 in OutdoorsScene).
* `EnableSunCoastPolicy` = false (default; serialized 0 in OutdoorsScene).
* `EnablePortedIntegratorPolicy` = false (default; serialized 0 in OutdoorsScene).
* `SpacecraftIntegrator.Dop853` remains the default. Nothing is enabled.

## Confirmed gaps and fixes

* G1 (mode drift): confirmed in D2 (11 of 24 cases left the Ported band during one
  30-day warp while WH kept integrating); fixed in D3 by the per-chunk invariant
  guard (in-band 0 fallbacks; out-of-band 1 fallback, bitwise equal to DOP853).
* F3: the VesselStep long-warp/flight path is not bitwise equal to a single direct
  Step (round-off/float-frame); live 1/60 s and 30 s chunks are bitwise. Measured,
  not fixed (events are outside D).
* F4: the game's propagator uses a 30 s event grain, so WH-BM14A has no wall gain
  through VesselStep (0.998-1.112x); this is the D4 REJECTED reason.
* F5: with the Sun-coast flag on, 5 AU selects DOPRI5 (TASK_D expected DOP853);
  the flag stays false, so the game is unaffected.

## Limits

* R1 error is measured against the game's certified default path (DOP853, flags off),
  not the qualification stand's study-frame reference; the gate is the 1 m target.
* R2 was not executed: no CSV evidence (owner decision A).
* R3 is report-only: the flag remains false (Q3 = NO).
* `dynamic.csv` is ignored by the repository's .gitignore; the file is kept locally
  in `results/<UTC>_<hash>/`, the report is committed.
