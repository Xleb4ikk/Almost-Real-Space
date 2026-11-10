# Test560b - decision details

Per-row detail for `Test560b_DecisionTable.csv`: acceptable percentages,
selected_best_all_total, selected_production_total, edge policy, reference
certificate, fixed-step h_req/h_avg and the heuristic bound label.

## Cislunar Moon flyby, SOI entry-exit, 10 d

- regime/config: `cislunar` / `production`
- target: 100 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.001326778003403687, floor_v 8.845186689357915E-06
- best_production: `DOPRI5 [production] [kernel switch]` at 33408 (kind: kernel switch)
- best_ported: `DOPRI5-adaptive (requires port)` at 30096
- best_stand_raw: `DOPRI5-adaptive (raw, requires port, not comparable)` at 10992
- best_all: `tie: DOPRI5-adaptive (requires port)@30,096/1.18E-001, DOPRI5 [production]@33,408/2.25E-001` at 30096 (tie: production+stand-only)
- selected_best_all_total: 30096
- selected_production: `DOPRI5 [production] [kernel switch]` at 33408
- selected_production_vs_best_all: +11%; best_all_vs_production: -10%
- cheapest_edge: `Y6 (requires port)` at 92160
- fixed_step_h: RK4 h_req=89.19053362520408 s, h_avg=85.71428571428571 s; DOPRI5-fixed h_req=300 s, h_avg=300 s; PEFRL h_req=126.13446228805718 s, h_avg=120 s; Y6 h_req=150 s, h_avg=150 s; Y6C h_req=150 s, h_avg=150 s; WH-Y6 h_req=300 s, h_avg=300 s; Leapfrog h_req=1.171875 s, h_avg=1.171875 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=91.69562395312947 s, h_avg=85.71428571428571 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Y6 92,160 is 206% more expensive than selected_best_all 30,096 and 176% more expensive than selected_production 33,408 (DOPRI5 [production] [kernel switch])
- acceptable: DOPRI5 [production] (+0% vs best_production, +11% vs best_all, +204% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+72% vs best_production, +91% vs best_all, +424% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+124% vs best_production, +149% vs best_all, +581% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (+150% vs best_production, +178% vs best_all, +660% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+208% vs best_production, +242% vs best_all, +836% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+240% vs best_production, +277% vs best_all, +932% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+241% vs best_production, +279% vs best_all, +938% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+253% vs best_production, +292% vs best_all, +974% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+266% vs best_production, +307% vs best_all, +1014% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+266% vs best_production, +307% vs best_all, +1014% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+1916% vs best_production, +2137% vs best_all, +6026% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2606% vs best_production, +2903% vs best_all, +8124% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+6525% vs best_production, +7254% vs best_all, +20035% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: DOPRI5-adaptive is selected as cheapest comparable hit at 30,096 (rhs 10,992 + local 0 + aux 0; cost_if_ported 30,096); selected stand option requires port: cost_if_ported = cost_total + 2*(raw - chunks); the production option in the tie needs no port; status ok; next DOPRI5 [production] 33,408; err/target 0.118; floor 1.33E-003; selected_production = DOPRI5 [production] [kernel switch] cost_total 33,408; selected_best_all = 30,096 (best_all base = cost_if_ported (stand port estimate); tie includes production); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Y6 92,160 is 206% more expensive than selected_best_all 30,096 and 176% more expensive than selected_production 33,408 (DOPRI5 [production] [kernel switch]); bound chunk (heuristic); tie: selected DOPRI5-adaptive err/target 1.177E-001 (cost 30,096); lowest err in tie DOPRI5-adaptive 1.177E-001; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## Far flight 30 AU e 0.5, 10 y

- regime/config: `far-flight` / `production`
- target: 100 km
- scope/bound: `config` / `max-step` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.009865273896297811, floor_v 1.1418141083678022E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 9475456 (kind: kernel switch)
- best_ported: `Leapfrog (requires port)` at 1580464
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 529264
- best_all: `Leapfrog (requires port)` at 1580464 (stand-only)
- selected_best_all_total: 1580464
- selected_production: `DOPRI5 [production] [kernel switch]` at 9475456
- selected_production_vs_best_all: +500%; best_all_vs_production: -83%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=600 s, h_avg=600 s; DOPRI5-fixed h_req=600 s, h_avg=600 s; PEFRL h_req=600 s, h_avg=600 s; Y6 h_req=600 s, h_avg=600 s; Y6C h_req=600 s, h_avg=600 s; WH-Y6 h_req=600 s, h_avg=600 s; Leapfrog h_req=600 s, h_avg=600 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=600 s, h_avg=600 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: GJ8 [requires port] (-54% vs best_production, +178% vs best_all, +731% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-34% vs best_production, +299% vs best_all, +1090% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (-34% vs best_production, +299% vs best_all, +1090% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-0% vs best_production, +499% vs best_all, +1688% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-0% vs best_production, +499% vs best_all, +1688% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +500% vs best_all, +1690% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+17% vs best_production, +599% vs best_all, +1986% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+17% vs best_production, +599% vs best_all, +1986% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+60% vs best_production, +859% vs best_all, +2763% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+100% vs best_production, +1097% vs best_all, +3476% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+100% vs best_production, +1097% vs best_all, +3476% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+133% vs best_production, +1296% vs best_all, +4070% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+150% vs best_production, +1396% vs best_all, +4367% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+183% vs best_production, +1596% vs best_all, +4965% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+226% vs best_production, +1854% vs best_all, +5734% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2001% vs best_production, +12498% vs best_all, +37519% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Leapfrog is selected as cheapest comparable hit at 1,580,464 (rhs 529,264 + local 0 + aux 0; cost_if_ported 1,580,464); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next GJ8 4,399,360; err/target 1.34E-05; floor 9.87E-003; selected_production = DOPRI5 [production] [kernel switch] cost_total 9,475,456; selected_best_all = 1,580,464 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; max-step-bound (600 s) (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Far flight 30 AU e 0.5, 10 y [regime86400]

- regime/config: `far-flight` / `regime86400`
- target: 100 km
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.009865273896297811, floor_v 1.1418141083678022E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 80608 (kind: kernel switch)
- best_ported: `Leapfrog (requires port)` at 14656
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 7328
- best_all: `Leapfrog (requires port)` at 14656 (stand-only)
- selected_best_all_total: 14656
- selected_production: `DOPRI5 [production] [kernel switch]` at 80608
- selected_production_vs_best_all: +450%; best_all_vs_production: -82%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=86400 s, h_avg=86069.8689956332 s; DOPRI5-fixed h_req=86400 s, h_avg=86069.8689956332 s; PEFRL h_req=86400 s, h_avg=86069.8689956332 s; Y6 h_req=86400 s, h_avg=86069.8689956332 s; Y6C h_req=86400 s, h_avg=86069.8689956332 s; WH-Y6 h_req=86400 s, h_avg=86069.8689956332 s; Leapfrog h_req=86400 s, h_avg=86069.8689956332 s; SRKN14A h_req=86400 s, h_avg=86069.8689956332 s; WH-BM14A h_req=86400 s, h_avg=86069.8689956332 s; GJ8 h_req=86400 s, h_avg=86069.8689956332 s; Encke-IAS15 h_req=86400 s, h_avg=86069.8689956332 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: RK4 [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+36% vs best_production, +646% vs best_all, +1391% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +900% vs best_all, +1900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +1200% vs best_all, +2500% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+158% vs best_production, +1319% vs best_all, +2738% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+190% vs best_production, +1498% vs best_all, +3095% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+231% vs best_production, +1723% vs best_all, +3545% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1763% vs best_production, +10144% vs best_all, +20389% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: Leapfrog is selected as cheapest comparable hit at 14,656 (rhs 7,328 + local 0 + aux 0; cost_if_ported 14,656); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 36,640; err/target 0.283; floor 9.87E-003; selected_production = DOPRI5 [production] [kernel switch] cost_total 80,608; selected_best_all = 14,656 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 0.3 AU e 0.5, 1 y

- regime/config: `heliocentric` / `production`
- target: 10 km
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.000731101932860252, floor_v 1.0154193511947945E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 1156320 (kind: kernel switch)
- best_ported: `PEFRL (requires port)` at 525600
- best_stand_raw: `PEFRL (raw, requires port, not comparable)` at 210240
- best_all: `tie: PEFRL (requires port)@525,600/6.00E-005, RK4 (requires port)@525,600/1.22E-004` at 525600 (tie: stand-only)
- selected_best_all_total: 525600
- selected_production: `DOPRI5 [production] [kernel switch]` at 1156320
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 3679200
- fixed_step_h: RK4 h_req=600 s, h_avg=600 s; DOPRI5-fixed h_req=600 s, h_avg=600 s; PEFRL h_req=600 s, h_avg=600 s; Y6 h_req=600 s, h_avg=600 s; Y6C h_req=600 s, h_avg=600 s; WH-Y6 h_req=600 s, h_avg=600 s; Leapfrog h_req=22.053430523056218 s, h_avg=21.428571428571427 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=600 s, h_avg=600 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 3,679,200 is 600% more expensive than selected_best_all 525,600 and 218% more expensive than selected_production 1,156,320 (DOPRI5 [production] [kernel switch])
- acceptable: RK4 [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+36% vs best_production, +199% vs best_all, +646% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+158% vs best_production, +468% vs best_all, +1321% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+191% vs best_production, +540% vs best_all, +1501% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+232% vs best_production, +630% vs best_all, +1726% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+286% vs best_production, +750% vs best_all, +2025% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2217% vs best_production, +4998% vs best_all, +12646% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: PEFRL is selected as cheapest comparable hit at 525,600 (rhs 210,240 + local 0 + aux 0; cost_if_ported 525,600); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 525,600; err/target 6E-05; floor 7.31E-004; selected_production = DOPRI5 [production] [kernel switch] cost_total 1,156,320; selected_best_all = 525,600 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 3,679,200 is 600% more expensive than selected_best_all 525,600 and 218% more expensive than selected_production 1,156,320 (DOPRI5 [production] [kernel switch]); bound chunk (heuristic); tie: selected PEFRL err/target 6.000E-005 (cost 525,600); lowest err in tie PEFRL 6.000E-005; equal-cost group 2 -> tie-break by err/target applied; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 0.3 AU e 0.5, 1 y [regime86400]

- regime/config: `heliocentric` / `regime86400`
- target: 10 km
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.000731101932860252, floor_v 1.0154193511947945E-07
- best_production: `DOP853 [production] [current default]` at 16583 (kind: current default)
- best_ported: `WH-Y6 (requires port)` at 10987.769021676357
- best_stand_raw: `SRKN14A (raw, requires port, not comparable)` at 5152
- best_all: `WH-Y6 (requires port)` at 10987.769021676357 (stand-only)
- selected_best_all_total: 10987.769021676357
- selected_production: `DOP853 [production] [current default]` at 16583
- selected_production_vs_best_all: +51%; best_all_vs_production: -34%
- cheapest_edge: `DOPRI5 [production]` at 30350
- fixed_step_h: RK4 h_req=4540.840642370058 s, h_avg=4322.368421052632 s; DOPRI5-fixed h_req=12843.436842029387 s, h_avg=12318.75 s; PEFRL h_req=5400 s, h_avg=5400 s; Y6 h_req=15273.506473629426 s, h_avg=14386.861313868612 s; Y6C h_req=15273.506473629426 s, h_avg=14386.861313868612 s; WH-Y6 h_req=86400 s, h_avg=85695.65217391304 s; SRKN14A h_req=86400 s, h_avg=85695.65217391304 s; WH-BM14A h_req=86400 s, h_avg=85695.65217391304 s; GJ8 h_req=36667.310284499064 s, h_avg=28985.29411764706 s; Encke-IAS15 h_req=86400 s, h_avg=85695.65217391304 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5 [production] 30,350 is 176% more expensive than selected_best_all 10,988 and 83% more expensive than selected_production 16,583 (DOP853 [production] [current default])
- acceptable: SRKN14A [requires port] (-11% vs best_production, +34% vs best_all, +186% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +51% vs best_all, +222% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+8% vs best_production, +63% vs best_all, +247% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+15% vs best_production, +74% vs best_all, +271% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+26% vs best_production, +90% vs best_all, +306% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+74% vs best_production, +163% vs best_all, +460% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+109% vs best_production, +215% vs best_all, +572% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+156% vs best_production, +286% vs best_all, +723% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+180% vs best_production, +322% vs best_all, +801% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+180% vs best_production, +322% vs best_all, +801% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+180% vs best_production, +323% vs best_all, +802% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (+318% vs best_production, +531% vs best_all, +1246% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+424% vs best_production, +690% vs best_all, +1585% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+833% vs best_production, +1308% vs best_all, +2903% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1398% vs best_production, +2161% vs best_all, +4721% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: Leapfrog:no_hit
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: WH-Y6 is selected as cheapest comparable hit at 10,988 (rhs 2,944 + local 0 + aux 2,892; cost_if_ported 10,988); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next SRKN14A 14,720; err/target 1.32E-05; floor 7.31E-004; selected_production = DOP853 [production] [current default] cost_total 16,583; selected_best_all = 10,988 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5 [production] 30,350 is 176% more expensive than selected_best_all 10,988 and 83% more expensive than selected_production 16,583 (DOP853 [production] [current default]); bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 16 ok, 4 edge-run row(s), 0 n/a, 1 unresolved

## Heliocentric 5 AU e 0.3, 10 y [forced Sun]

- regime/config: `heliocentric` / `production`
- target: 100 km
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.01274898079054503, floor_v 3.5413835529291753E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 11563200 (kind: kernel switch)
- best_ported: `Leapfrog (requires port)` at 2102400
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 1051200
- best_all: `Leapfrog (requires port)` at 2102400 (stand-only)
- selected_best_all_total: 2102400
- selected_production: `DOPRI5 [production] [kernel switch]` at 11563200
- selected_production_vs_best_all: +450%; best_all_vs_production: -82%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=600 s, h_avg=600 s; DOPRI5-fixed h_req=600 s, h_avg=600 s; PEFRL h_req=600 s, h_avg=600 s; Y6 h_req=600 s, h_avg=600 s; Y6C h_req=600 s, h_avg=600 s; WH-Y6 h_req=600 s, h_avg=600 s; Leapfrog h_req=600 s, h_avg=600 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=600 s, h_avg=600 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: RK4 [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+35% vs best_production, +644% vs best_all, +1388% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +900% vs best_all, +1900% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+97% vs best_production, +981% vs best_all, +2062% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +1200% vs best_all, +2500% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+137% vs best_production, +1206% vs best_all, +2512% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+157% vs best_production, +1316% vs best_all, +2732% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1610% vs best_production, +9306% vs best_all, +18711% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Leapfrog is selected as cheapest comparable hit at 2,102,400 (rhs 1,051,200 + local 0 + aux 0; cost_if_ported 2,102,400); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 5,256,000; err/target 0.0182; floor 1.27E-002; selected_production = DOPRI5 [production] [kernel switch] cost_total 11,563,200; selected_best_all = 2,102,400 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]

- regime/config: `heliocentric` / `regime86400`
- target: 100 km
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 0.01274898079054503, floor_v 3.5413835529291753E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 80608 (kind: kernel switch)
- best_ported: `PEFRL (requires port)` at 36640
- best_stand_raw: `PEFRL (raw, requires port, not comparable)` at 14656
- best_all: `tie: PEFRL (requires port)@36,640/4.26E-003, RK4 (requires port)@36,640/2.53E-002` at 36640 (tie: stand-only)
- selected_best_all_total: 36640
- selected_production: `DOPRI5 [production] [kernel switch]` at 80608
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 233632
- fixed_step_h: RK4 h_req=86400 s, h_avg=86069.8689956332 s; DOPRI5-fixed h_req=86400 s, h_avg=86069.8689956332 s; PEFRL h_req=86400 s, h_avg=86069.8689956332 s; Y6 h_req=86400 s, h_avg=86069.8689956332 s; Y6C h_req=86400 s, h_avg=86069.8689956332 s; WH-Y6 h_req=86400 s, h_avg=86069.8689956332 s; Leapfrog h_req=3768.1583105087434 s, h_avg=3756.432246998285 s; SRKN14A h_req=86400 s, h_avg=86069.8689956332 s; WH-BM14A h_req=86400 s, h_avg=86069.8689956332 s; GJ8 h_req=86400 s, h_avg=86069.8689956332 s; Encke-IAS15 h_req=86400 s, h_avg=86069.8689956332 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 233,632 is 538% more expensive than selected_best_all 36,640 and 190% more expensive than selected_production 80,608 (DOPRI5 [production] [kernel switch])
- acceptable: RK4 [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+35% vs best_production, +198% vs best_all, +644% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+157% vs best_production, +466% vs best_all, +1316% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+195% vs best_production, +550% vs best_all, +1524% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+217% vs best_production, +597% vs best_all, +1643% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+236% vs best_production, +640% vs best_all, +1749% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1858% vs best_production, +4208% vs best_all, +10671% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: PEFRL is selected as cheapest comparable hit at 36,640 (rhs 14,656 + local 0 + aux 0; cost_if_ported 36,640); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 36,640; err/target 0.00426; floor 1.27E-002; selected_production = DOPRI5 [production] [kernel switch] cost_total 80,608; selected_best_all = 36,640 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 233,632 is 538% more expensive than selected_best_all 36,640 and 190% more expensive than selected_production 80,608 (DOPRI5 [production] [kernel switch]); bound chunk (heuristic); tie: selected PEFRL err/target 4.260E-003 (cost 36,640); lowest err in tie PEFRL 4.260E-003; equal-cost group 2 -> tie-break by err/target applied; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## High orbit 200 000 km, 30 d

- regime/config: `high-orbit` / `production`
- target: 100 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 8.808490763140521E-07, floor_v 1.4680817938567535E-09
- best_production: `DOPRI5 [production] [kernel switch]` at 95040 (kind: kernel switch)
- best_ported: `PEFRL (requires port)` at 43200
- best_stand_raw: `PEFRL (raw, requires port, not comparable)` at 17280
- best_all: `tie: PEFRL (requires port)@43,200/1.71E-004, RK4 (requires port)@43,200/2.28E-004` at 43200 (tie: stand-only)
- selected_best_all_total: 43200
- selected_production: `DOPRI5 [production] [kernel switch]` at 95040
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 211680
- fixed_step_h: RK4 h_req=600 s, h_avg=600 s; DOPRI5-fixed h_req=600 s, h_avg=600 s; PEFRL h_req=600 s, h_avg=600 s; Y6 h_req=600 s, h_avg=600 s; Y6C h_req=600 s, h_avg=600 s; WH-Y6 h_req=600 s, h_avg=600 s; Leapfrog h_req=35.58850273433909 s, h_avg=35.294117647058826 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=600 s, h_avg=600 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 211,680 is 390% more expensive than selected_best_all 43,200 and 123% more expensive than selected_production 95,040 (DOPRI5 [production] [kernel switch])
- acceptable: RK4 [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+21% vs best_production, +165% vs best_all, +563% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+126% vs best_production, +397% vs best_all, +1143% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+203% vs best_production, +567% vs best_all, +1569% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+244% vs best_production, +657% vs best_all, +1794% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1812% vs best_production, +4107% vs best_all, +10418% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: PEFRL is selected as cheapest comparable hit at 43,200 (rhs 17,280 + local 0 + aux 0; cost_if_ported 43,200); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 43,200; err/target 0.000171; floor 8.81E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 95,040; selected_best_all = 43,200 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 211,680 is 390% more expensive than selected_best_all 43,200 and 123% more expensive than selected_production 95,040 (DOPRI5 [production] [kernel switch]); bound chunk (heuristic); tie: selected PEFRL err/target 1.706E-004 (cost 43,200); lowest err in tie PEFRL 1.706E-004; equal-cost group 2 -> tie-break by err/target applied; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## High orbit 35 786 km (GEO), 30 d

- regime/config: `high-orbit` / `production`
- target: 100 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 9.64171414807208E-06, floor_v 1.6069523580120135E-08
- best_production: `DOP853 [production] [current default]` at 159840 (kind: current default)
- best_ported: `Y6 (requires port)` at 95040
- best_stand_raw: `Y6 (raw, requires port, not comparable)` at 34560
- best_all: `tie: Y6 (requires port)@95,040/4.20E-002, Y6C (requires port)@95,040/4.20E-002, PEFRL (requires port)@95,040/3.11E-001` at 95040 (tie: stand-only)
- selected_best_all_total: 95040
- selected_production: `DOP853 [production] [current default]` at 159840
- selected_production_vs_best_all: +68%; best_all_vs_production: -41%
- cheapest_edge: `DOPRI5-fixed (requires port)` at 82080
- fixed_step_h: RK4 h_req=218.6924072023403 s, h_avg=200 s; DOPRI5-fixed h_req=156.21684288230003 s, h_avg=150 s; PEFRL h_req=306.15372891634036 s, h_avg=300 s; Y6 h_req=600 s, h_avg=600 s; Y6C h_req=600 s, h_avg=600 s; WH-Y6 h_req=600 s, h_avg=600 s; Leapfrog h_req=2.3302619873873702 s, h_avg=2.3255813953488373 s; SRKN14A h_req=600 s, h_avg=600 s; WH-BM14A h_req=600 s, h_avg=600 s; GJ8 h_req=600 s, h_avg=600 s; Encke-IAS15 h_req=600 s, h_avg=600 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 82,080 is 14% cheaper than selected_best_all 95,040 and 49% cheaper than selected_production 159,840 (DOP853 [production] [current default])
- acceptable: Y6C [requires port] (-41% vs best_production, +0% vs best_all, +175% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-41% vs best_production, +0% vs best_all, +175% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (-28% vs best_production, +22% vs best_all, +235% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (-8% vs best_production, +55% vs best_all, +325% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+0% vs best_production, +68% vs best_all, +362% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +68% vs best_all, +362% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+8% vs best_production, +82% vs best_all, +400% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+36% vs best_production, +129% vs best_all, +528% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+41% vs best_production, +136% vs best_all, +550% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+65% vs best_production, +177% vs best_all, +662% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+97% vs best_production, +232% vs best_all, +812% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+97% vs best_production, +232% vs best_all, +812% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+170% vs best_production, +353% vs best_all, +1147% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+396% vs best_production, +733% vs best_all, +2192% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1301% vs best_production, +2255% vs best_all, +6378% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+1995% vs best_production, +3423% vs best_all, +9588% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Y6 is selected as cheapest comparable hit at 95,040 (rhs 34,560 + local 0 + aux 0; cost_if_ported 95,040); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next Y6C 95,040; err/target 0.042; floor 9.64E-006; selected_production = DOP853 [production] [current default] cost_total 159,840; selected_best_all = 95,040 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 82,080 is 14% cheaper than selected_best_all 95,040 and 49% cheaper than selected_production 159,840 (DOP853 [production] [current default]); bound chunk (heuristic); tie: selected Y6 err/target 4.204E-002 (cost 95,040); lowest err in tie Y6 4.204E-002; equal-cost group 3 -> tie-break by err/target applied; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 17 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## LEO 200 km, 1 d

- regime/config: `leo` / `production`
- target: 10 m
- scope/bound: `config` / `max-step` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 1.9805465827965774E-05, floor_v 1.6504554856638145E-07
- best_production: `DOP853 [production] [current default]` at 59775 (kind: current default)
- best_ported: `DOPRI5-fixed (requires port)` at 59287
- best_stand_raw: `DOPRI5-fixed (raw, requires port, not comparable)` at 20659
- best_all: `tie: DOPRI5-fixed (requires port)@59,287/7.80E-001, DOP853-adaptive (requires port)@59,701/6.03E-002, DOP853 [production]@59,775/4.57E-002` at 59287 (tie: production+stand-only)
- selected_best_all_total: 59287
- selected_production: `DOP853 [production] [current default]` at 59775
- selected_production_vs_best_all: +1%; best_all_vs_production: -1%
- cheapest_edge: `DOPRI5-adaptive (requires port)` at 63589
- fixed_step_h: RK4 h_req=7.883403893003574 s, h_avg=7.033539563660046 s; DOPRI5-fixed h_req=44.59526681260204 s, h_avg=26.84063373718546 s; Encke-IAS15 h_req=600 s, h_avg=64.09495548961425 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-adaptive 63,589 is 7% more expensive than selected_best_all 59,287 and 6% more expensive than selected_production 59,775 (DOP853 [production] [current default])
- acceptable: DOP853-adaptive [requires port] (-0% vs best_production, +1% vs best_all, +189% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +1% vs best_all, +189% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+22% vs best_production, +23% vs best_all, +253% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+22% vs best_production, +23% vs best_all, +254% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+73% vs best_production, +75% vs best_all, +402% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+142% vs best_production, +144% vs best_all, +601% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+774% vs best_production, +781% vs best_all, +2429% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: DOPRI5-fixed is selected as cheapest comparable hit at 59,287 (rhs 20,659 + local 0 + aux 0; cost_if_ported 59,287); selected stand option requires port: cost_if_ported = cost_total + 2*(raw - chunks); the production option in the tie needs no port; status ok; next DOP853-adaptive 59,701; err/target 0.78; floor 1.98E-005; selected_production = DOP853 [production] [current default] cost_total 59,775; selected_best_all = 59,287 (best_all base = cost_if_ported (stand port estimate); tie includes production); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-adaptive 63,589 is 7% more expensive than selected_best_all 59,287 and 6% more expensive than selected_production 59,775 (DOP853 [production] [current default]); bound max-step (heuristic); tie: selected DOPRI5-fixed err/target 7.804E-001 (cost 59,287); lowest err in tie DOP853 [production] 4.572E-002; production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## LEO 300 km, 30 d

- regime/config: `leo` / `production`
- target: 100 m
- scope/bound: `config` / `max-step` (bound is heuristic)
- reference: ratio_ok, floor_m 0.001541339771916362, floor_v 1.7378762014516959E-06
- best_production: `DOP853 [production] [current default]` at 956031 (kind: current default)
- best_ported: `DOP853-adaptive (requires port)` at 658093
- best_stand_raw: `DOP853-adaptive (raw, requires port, not comparable)` at 223045
- best_all: `DOP853-adaptive (requires port)` at 658093 (stand-only)
- selected_best_all_total: 658093
- selected_production: `DOP853 [production] [current default]` at 956031
- selected_production_vs_best_all: +45%; best_all_vs_production: -31%
- cheapest_edge: `DOPRI5-fixed (requires port)` at 2515459
- fixed_step_h: RK4 h_req=7.883403893003574 s, h_avg=7.764291448495668 s; DOPRI5-fixed h_req=9.375 s, h_avg=9.334552971427337 s; Encke-IAS15 h_req=600 s, h_avg=469.22519913106447 s
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 2,515,459 is 282% more expensive than selected_best_all 658,093 and 163% more expensive than selected_production 956,031 (DOP853 [production] [current default])
- acceptable: IAS15 [requires port] (-10% vs best_production, +31% vs best_all, +286% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +45% vs best_all, +329% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+216% vs best_production, +360% vs best_all, +1256% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+257% vs best_production, +418% vs best_all, +1429% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+294% vs best_production, +472% vs best_all, +1588% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+318% vs best_production, +507% vs best_all, +1691% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+423% vs best_production, +660% vs best_all, +2143% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: DOP853-adaptive is selected as cheapest comparable hit at 658,093 (rhs 223,045 + local 0 + aux 0; cost_if_ported 658,093); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next IAS15 859,990; err/target 0.259; floor 1.54E-003; selected_production = DOP853 [production] [current default] cost_total 956,031; selected_best_all = 658,093 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 2,515,459 is 282% more expensive than selected_best_all 658,093 and 163% more expensive than selected_production 956,031 (DOP853 [production] [current default]); bound max-step (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22

- regime/config: `surface` / `production`
- target: 0.1 m/s
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.751621147421441E-07, floor_v 6.879052868553602E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26422 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12024
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4808
- best_all: `RK4 (requires port)` at 12024 (stand-only)
- selected_best_all_total: 12024
- selected_production: `DOPRI5 [production] [kernel switch]` at 26422
- selected_production_vs_best_all: +120%; best_all_vs_production: -54%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.04991680532445923 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.04991680532445923 s; Encke-IAS15 h_req=0.05 s, h_avg=0.04991680532445923 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +374% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +269% vs best_all, +823% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +824% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+386% vs best_production, +967% vs best_all, +2569% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2529% vs best_production, +5677% vs best_all, +14348% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,024 (rhs 4,808 + local 0 + aux 0; cost_if_ported 12,024); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 1.77E-06; floor 6.88E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,422; selected_best_all = 12,024 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; prescribed thrust schedule; open-loop; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22

- regime/config: `surface` / `production`
- target: 0.1 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.751621147421441E-07, floor_v 6.879052868553602E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26422 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12024
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4808
- best_all: `RK4 (requires port)` at 12024 (stand-only)
- selected_best_all_total: 12024
- selected_production: `DOPRI5 [production] [kernel switch]` at 26422
- selected_production_vs_best_all: +120%; best_all_vs_production: -54%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.04991680532445923 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.04991680532445923 s; Encke-IAS15 h_req=0.05 s, h_avg=0.04991680532445923 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +374% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +269% vs best_all, +823% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +824% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+386% vs best_production, +967% vs best_all, +2569% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2529% vs best_production, +5677% vs best_all, +14348% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,024 (rhs 4,808 + local 0 + aux 0; cost_if_ported 12,024); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 6.7E-05; floor 2.75E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,422; selected_best_all = 12,024 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; prescribed thrust schedule; open-loop; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: ratio_ok, floor_m 1.2022966698118821E-07, floor_v 1.984131484954506E-08
- best_production: `DOPRI5 [production] [kernel switch]` at 13376 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 6080
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 2432
- best_all: `RK4 (requires port)` at 6080 (stand-only)
- selected_best_all_total: 6080
- selected_production: `DOPRI5 [production] [kernel switch]` at 13376
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.049342105263157895 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.049342105263157895 s; Encke-IAS15 h_req=0.05 s, h_avg=0.049342105263157895 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+401% vs best_production, +1002% vs best_all, +2655% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5792% vs best_all, +14629% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 6,080 (rhs 2,432 + local 0 + aux 0; cost_if_ported 6,080); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 11,552; err/target 5.36E-07; floor 1.98E-008; selected_production = DOPRI5 [production] [kernel switch] cost_total 13,376; selected_best_all = 6,080 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; prescribed thrust schedule; open-loop; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: ratio_ok, floor_m 1.2022966698118821E-07, floor_v 1.984131484954506E-08
- best_production: `DOPRI5 [production] [kernel switch]` at 13376 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 6080
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 2432
- best_all: `RK4 (requires port)` at 6080 (stand-only)
- selected_best_all_total: 6080
- selected_production: `DOPRI5 [production] [kernel switch]` at 13376
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.049342105263157895 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.049342105263157895 s; Encke-IAS15 h_req=0.05 s, h_avg=0.049342105263157895 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+401% vs best_production, +1002% vs best_all, +2655% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5792% vs best_all, +14629% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 6,080 (rhs 2,432 + local 0 + aux 0; cost_if_ported 6,080); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 11,552; err/target 1.7E-06; floor 1.20E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 13,376; selected_best_all = 6,080 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; prescribed thrust schedule; open-loop; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface hover 0.5 km, 0 m/s relative

- regime/config: `surface` / `production`
- target: 0.1 m/s
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.263609484195607E-08, floor_v 5.6590237104890166E-08
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+287% vs best_production, +751% vs best_all, +2028% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2073% vs best_production, +4681% vs best_all, +11853% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 3.65E-08; floor 5.66E-008; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface hover 0.5 km, 0 m/s relative

- regime/config: `surface` / `production`
- target: 0.1 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.263609484195607E-08, floor_v 5.6590237104890166E-08
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+287% vs best_production, +751% vs best_all, +2028% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2073% vs best_production, +4681% vs best_all, +11853% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 6.47E-05; floor 2.26E-008; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 0.5 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.8161806132913515E-07, floor_v 7.040451533228379E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2386% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5793% vs best_all, +14632% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 3.93E-07; floor 7.04E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 0.5 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 2.8161806132913515E-07, floor_v 7.040451533228379E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2386% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5793% vs best_all, +14632% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 7.37E-05; floor 2.82E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 1 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 3.27674407264988E-07, floor_v 8.191860181624699E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2385% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2565% vs best_production, +5763% vs best_all, +14558% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 7.83E-05; floor 3.28E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 1 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- scope/bound: `config` / `chunk` (bound is heuristic)
- reference: resolved_finest_delta, floor_m 3.27674407264988E-07, floor_v 8.191860181624699E-07
- best_production: `DOPRI5 [production] [kernel switch]` at 26400 (kind: kernel switch)
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production] [kernel switch]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- fixed_step_h: RK4 h_req=0.05 s, h_avg=0.05 s; DOPRI5-fixed h_req=0.05 s, h_avg=0.05 s; Encke-IAS15 h_req=0.05 s, h_avg=0.05 s
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2385% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2565% vs best_production, +5763% vs best_all, +14558% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 2.33E-07; floor 8.19E-007; selected_production = DOPRI5 [production] [kernel switch] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; bound chunk (heuristic); production atol 1e-4/1e-8/1e-4 vs stand Atol=0; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Max-step variants (informational, not mixed into best_all)

These are the same methods run with an explicit max step that differs from the
scenario's config max step. They are **not** candidates for `best_all` or
`best_production`; the production/regime86400 separation is unchanged.

| scenario | target | method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5-adaptive [cap=86400 s] | 86400 | 600 | 9.174E-003 | 25648 | 69616 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | DOP853-adaptive [cap=86400 s] | 86400 | 600 | 9.203E-003 | 47632 | 135568 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | RKN12(10) [cap=86400 s] | 86400 | 600 | 5.720E-003 | 65952 | 190528 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | IAS15 [cap=86400 s] | 86400 | 600 | 9.392E-003 | 80496 | 234160 | chunk | ok |

Far-flight DOPRI5-adaptive `[cap=86400 s]` is 69616 comparable / 0.00917 m at 100 km, 22.7x cheaper than best_all Leapfrog (requires port) (1,580,464). It is a max-step configuration, not the 600 s production default, and is not
selected in `best_all`.

