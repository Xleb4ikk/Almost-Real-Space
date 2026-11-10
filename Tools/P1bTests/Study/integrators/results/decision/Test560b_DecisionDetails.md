# Test560b - decision details

Per-row detail for `Test560b_DecisionTable.csv`: acceptable percentages,
selected_best_all_total, selected_production_total and edge policy.

## Cislunar Moon flyby, SOI entry-exit, 10 d

- regime/config: `cislunar` / `production`
- target: 100 m
- best_production: `DOPRI5 [production]` at 33408
- best_ported: `DOPRI5-adaptive (requires port)` at 30096
- best_stand_raw: `DOPRI5-adaptive (raw, requires port, not comparable)` at 10992
- best_all: `tie: DOPRI5-adaptive (requires port), DOPRI5 [production]` at 30096 (tie: production+stand-only)
- selected_best_all_total: 30096
- selected_production: `DOPRI5 [production]` at 33408
- selected_production_vs_best_all: +11%; best_all_vs_production: -10%
- cheapest_edge: `Y6 (requires port)` at 92160
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Y6 92,160 is 206% more expensive than selected_best_all 30,096 and 176% more expensive than selected_production 33,408 (DOPRI5 [production])
- acceptable: DOPRI5 [production] (+0% vs best_production, +11% vs best_all, +204% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+59% vs best_production, +77% vs best_all, +385% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+72% vs best_production, +91% vs best_all, +424% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+124% vs best_production, +149% vs best_all, +581% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (+150% vs best_production, +178% vs best_all, +660% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+208% vs best_production, +242% vs best_all, +836% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+240% vs best_production, +277% vs best_all, +932% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+241% vs best_production, +279% vs best_all, +938% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+253% vs best_production, +292% vs best_all, +974% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+266% vs best_production, +307% vs best_all, +1014% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+266% vs best_production, +307% vs best_all, +1014% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+1916% vs best_production, +2137% vs best_all, +6026% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2606% vs best_production, +2903% vs best_all, +8124% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+6525% vs best_production, +7254% vs best_all, +20035% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: DOPRI5-adaptive is selected as cheapest comparable hit at 30,096 (rhs 10,992 + local 0 + aux 0; cost_if_ported 30,096); selected stand option requires port: cost_if_ported = cost_total + 2*(raw - chunks); the production option in the tie needs no port; status ok; next DOPRI5 [production] 33,408; err/target 0.118; floor 1.33E-003; selected_production = DOPRI5 [production] cost_total 33,408; selected_best_all = 30,096 (best_all base = cost_if_ported (stand port estimate); tie includes production); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Y6 92,160 is 206% more expensive than selected_best_all 30,096 and 176% more expensive than selected_production 33,408 (DOPRI5 [production]); tie band 20%; 17 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## Far flight 30 AU e 0.5, 10 y

- regime/config: `far-flight` / `production`
- target: 100 km
- best_production: `DOPRI5 [production]` at 9475456
- best_ported: `Leapfrog (requires port)` at 1580464
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 529264
- best_all: `Leapfrog (requires port)` at 1580464 (stand-only)
- selected_best_all_total: 1580464
- selected_production: `DOPRI5 [production]` at 9475456
- selected_production_vs_best_all: +500%; best_all_vs_production: -83%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: GJ8 [requires port] (-54% vs best_production, +178% vs best_all, +731% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (-34% vs best_production, +299% vs best_all, +1090% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-34% vs best_production, +299% vs best_all, +1090% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-0% vs best_production, +499% vs best_all, +1688% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-0% vs best_production, +499% vs best_all, +1688% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +500% vs best_all, +1690% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+17% vs best_production, +599% vs best_all, +1986% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+17% vs best_production, +599% vs best_all, +1986% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+60% vs best_production, +859% vs best_all, +2763% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+100% vs best_production, +1097% vs best_all, +3476% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+100% vs best_production, +1097% vs best_all, +3476% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+133% vs best_production, +1296% vs best_all, +4070% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+150% vs best_production, +1396% vs best_all, +4367% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+183% vs best_production, +1596% vs best_all, +4965% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+226% vs best_production, +1854% vs best_all, +5734% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2001% vs best_production, +12498% vs best_all, +37519% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Leapfrog is selected as cheapest comparable hit at 1,580,464 (rhs 529,264 + local 0 + aux 0; cost_if_ported 1,580,464); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next GJ8 4,399,360; err/target 1.34E-05; floor 9.87E-003; selected_production = DOPRI5 [production] cost_total 9,475,456; selected_best_all = 1,580,464 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Far flight 30 AU e 0.5, 10 y [regime86400]

- regime/config: `far-flight` / `regime86400`
- target: 100 km
- best_production: `DOPRI5 [production]` at 80608
- best_ported: `Leapfrog (requires port)` at 14656
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 7328
- best_all: `Leapfrog (requires port)` at 14656 (stand-only)
- selected_best_all_total: 14656
- selected_production: `DOPRI5 [production]` at 80608
- selected_production_vs_best_all: +450%; best_all_vs_production: -82%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: RK4 [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+36% vs best_production, +646% vs best_all, +1391% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +900% vs best_all, +1900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +1200% vs best_all, +2500% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+158% vs best_production, +1319% vs best_all, +2738% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+190% vs best_production, +1498% vs best_all, +3095% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+231% vs best_production, +1723% vs best_all, +3545% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1763% vs best_production, +10144% vs best_all, +20389% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: Leapfrog is selected as cheapest comparable hit at 14,656 (rhs 7,328 + local 0 + aux 0; cost_if_ported 14,656); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 36,640; err/target 0.283; floor 9.87E-003; selected_production = DOPRI5 [production] cost_total 80,608; selected_best_all = 14,656 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 0.3 AU e 0.5, 1 y

- regime/config: `heliocentric` / `production`
- target: 10 km
- best_production: `DOPRI5 [production]` at 1156320
- best_ported: `RK4 (requires port)` at 525600
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 210240
- best_all: `tie: RK4 (requires port), PEFRL (requires port)` at 525600 (tie: stand-only)
- selected_best_all_total: 525600
- selected_production: `DOPRI5 [production]` at 1156320
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 3679200
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 3,679,200 is 600% more expensive than selected_best_all 525,600 and 218% more expensive than selected_production 1,156,320 (DOPRI5 [production])
- acceptable: PEFRL [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+36% vs best_production, +199% vs best_all, +646% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+158% vs best_production, +468% vs best_all, +1321% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+191% vs best_production, +540% vs best_all, +1501% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+232% vs best_production, +630% vs best_all, +1726% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+286% vs best_production, +750% vs best_all, +2025% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2217% vs best_production, +4998% vs best_all, +12646% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 525,600 (rhs 210,240 + local 0 + aux 0; cost_if_ported 525,600); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next PEFRL 525,600; err/target 0.000122; floor 7.31E-004; selected_production = DOPRI5 [production] cost_total 1,156,320; selected_best_all = 525,600 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 3,679,200 is 600% more expensive than selected_best_all 525,600 and 218% more expensive than selected_production 1,156,320 (DOPRI5 [production]); tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 0.3 AU e 0.5, 1 y [regime86400]

- regime/config: `heliocentric` / `regime86400`
- target: 10 km
- best_production: `DOP853 [production]` at 16583
- best_ported: `WH-Y6 (requires port)` at 10987.769021676357
- best_stand_raw: `SRKN14A (raw, requires port, not comparable)` at 5152
- best_all: `WH-Y6 (requires port)` at 10987.769021676357 (stand-only)
- selected_best_all_total: 10987.769021676357
- selected_production: `DOP853 [production]` at 16583
- selected_production_vs_best_all: +51%; best_all_vs_production: -34%
- cheapest_edge: `DOPRI5 [production]` at 30350
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5 [production] 30,350 is 176% more expensive than selected_best_all 10,988 and 83% more expensive than selected_production 16,583 (DOP853 [production])
- acceptable: SRKN14A [requires port] (-11% vs best_production, +34% vs best_all, +186% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +51% vs best_all, +222% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+8% vs best_production, +63% vs best_all, +247% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+15% vs best_production, +74% vs best_all, +271% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+26% vs best_production, +90% vs best_all, +306% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+74% vs best_production, +163% vs best_all, +460% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+109% vs best_production, +215% vs best_all, +572% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+156% vs best_production, +286% vs best_all, +723% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+180% vs best_production, +322% vs best_all, +801% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+180% vs best_production, +322% vs best_all, +801% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+180% vs best_production, +323% vs best_all, +802% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (+318% vs best_production, +531% vs best_all, +1246% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+424% vs best_production, +690% vs best_all, +1585% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+833% vs best_production, +1308% vs best_all, +2903% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1398% vs best_production, +2161% vs best_all, +4721% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: Leapfrog:no_hit
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: WH-Y6 is selected as cheapest comparable hit at 10,988 (rhs 2,944 + local 0 + aux 2,892; cost_if_ported 10,988); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next SRKN14A 14,720; err/target 1.32E-05; floor 7.31E-004; selected_production = DOP853 [production] cost_total 16,583; selected_best_all = 10,988 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5 [production] 30,350 is 176% more expensive than selected_best_all 10,988 and 83% more expensive than selected_production 16,583 (DOP853 [production]); tie band 20%; 16 ok, 4 edge-run row(s), 0 n/a, 1 unresolved

## Heliocentric 5 AU e 0.3, 10 y [forced Sun]

- regime/config: `heliocentric` / `production`
- target: 100 km
- best_production: `DOPRI5 [production]` at 11563200
- best_ported: `Leapfrog (requires port)` at 2102400
- best_stand_raw: `Leapfrog (raw, requires port, not comparable)` at 1051200
- best_all: `Leapfrog (requires port)` at 2102400 (stand-only)
- selected_best_all_total: 2102400
- selected_production: `DOPRI5 [production]` at 11563200
- selected_production_vs_best_all: +450%; best_all_vs_production: -82%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: RK4 [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-55% vs best_production, +150% vs best_all, +400% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +375% vs best_all, +850% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +450% vs best_all, +1000% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+35% vs best_production, +644% vs best_all, +1388% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +825% vs best_all, +1750% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +900% vs best_all, +1900% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+97% vs best_production, +981% vs best_all, +2062% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +1200% vs best_all, +2500% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+137% vs best_production, +1206% vs best_all, +2512% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+157% vs best_production, +1316% vs best_all, +2732% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1610% vs best_production, +9306% vs best_all, +18711% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Leapfrog is selected as cheapest comparable hit at 2,102,400 (rhs 1,051,200 + local 0 + aux 0; cost_if_ported 2,102,400); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next RK4 5,256,000; err/target 0.0182; floor 1.27E-002; selected_production = DOPRI5 [production] cost_total 11,563,200; selected_best_all = 2,102,400 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 17 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Heliocentric 5 AU e 0.3, 10 y [regime86400]

- regime/config: `heliocentric` / `regime86400`
- target: 100 km
- best_production: `DOPRI5 [production]` at 80608
- best_ported: `RK4 (requires port)` at 36640
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 14656
- best_all: `tie: RK4 (requires port), PEFRL (requires port)` at 36640 (tie: stand-only)
- selected_best_all_total: 36640
- selected_production: `DOPRI5 [production]` at 80608
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 233632
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 233,632 is 538% more expensive than selected_best_all 36,640 and 190% more expensive than selected_production 80,608 (DOPRI5 [production])
- acceptable: PEFRL [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+35% vs best_production, +198% vs best_all, +644% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+157% vs best_production, +466% vs best_all, +1316% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+195% vs best_production, +550% vs best_all, +1524% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+217% vs best_production, +597% vs best_all, +1643% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+236% vs best_production, +640% vs best_all, +1749% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1858% vs best_production, +4208% vs best_all, +10671% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]
- why: RK4 is selected as cheapest comparable hit at 36,640 (rhs 14,656 + local 0 + aux 0; cost_if_ported 36,640); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next PEFRL 36,640; err/target 0.0253; floor 1.27E-002; selected_production = DOPRI5 [production] cost_total 80,608; selected_best_all = 36,640 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 233,632 is 538% more expensive than selected_best_all 36,640 and 190% more expensive than selected_production 80,608 (DOPRI5 [production]); tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## High orbit 200 000 km, 30 d

- regime/config: `high-orbit` / `production`
- target: 100 m
- best_production: `DOPRI5 [production]` at 95040
- best_ported: `RK4 (requires port)` at 43200
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 17280
- best_all: `tie: RK4 (requires port), PEFRL (requires port)` at 43200 (tie: stand-only)
- selected_best_all_total: 43200
- selected_production: `DOPRI5 [production]` at 95040
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `Leapfrog (requires port)` at 211680
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 211,680 is 390% more expensive than selected_best_all 43,200 and 123% more expensive than selected_production 95,040 (DOPRI5 [production])
- acceptable: PEFRL [requires port] (-55% vs best_production, +0% vs best_all, +150% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), Y6C [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), Y6 [requires port] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (+21% vs best_production, +165% vs best_all, +563% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+82% vs best_production, +300% vs best_all, +900% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+126% vs best_production, +397% vs best_all, +1143% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+136% vs best_production, +420% vs best_all, +1200% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+203% vs best_production, +567% vs best_all, +1569% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+244% vs best_production, +657% vs best_all, +1794% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1812% vs best_production, +4107% vs best_all, +10418% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 43,200 (rhs 17,280 + local 0 + aux 0; cost_if_ported 43,200); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next PEFRL 43,200; err/target 0.000228; floor 8.81E-007; selected_production = DOPRI5 [production] cost_total 95,040; selected_best_all = 43,200 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge Leapfrog 211,680 is 390% more expensive than selected_best_all 43,200 and 123% more expensive than selected_production 95,040 (DOPRI5 [production]); tie band 20%; 17 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## High orbit 35 786 km (GEO), 30 d

- regime/config: `high-orbit` / `production`
- target: 100 m
- best_production: `DOP853 [production]` at 159840
- best_ported: `Y6C (requires port)` at 95040
- best_stand_raw: `Y6C (raw, requires port, not comparable)` at 34560
- best_all: `tie: Y6C (requires port), Y6 (requires port), PEFRL (requires port)` at 95040 (tie: stand-only)
- selected_best_all_total: 95040
- selected_production: `DOP853 [production]` at 159840
- selected_production_vs_best_all: +68%; best_all_vs_production: -41%
- cheapest_edge: `DOPRI5-fixed (requires port)` at 82080
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 82,080 is 14% cheaper than selected_best_all 95,040 and 49% cheaper than selected_production 159,840 (DOP853 [production])
- acceptable: Y6 [requires port] (-41% vs best_production, +0% vs best_all, +175% vs best_stand_raw (raw, not comparable)), PEFRL [requires port] (-41% vs best_production, +0% vs best_all, +175% vs best_stand_raw (raw, not comparable)), WH-Y6 [requires port] (-28% vs best_production, +22% vs best_all, +235% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (-8% vs best_production, +55% vs best_all, +325% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +68% vs best_all, +362% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+0% vs best_production, +68% vs best_all, +362% vs best_stand_raw (raw, not comparable)), SRKN14A [requires port] (+8% vs best_production, +82% vs best_all, +400% vs best_stand_raw (raw, not comparable)), WH-BM14A [requires port] (+36% vs best_production, +129% vs best_all, +528% vs best_stand_raw (raw, not comparable)), RKN12(10) [requires port] (+41% vs best_production, +136% vs best_all, +550% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+65% vs best_production, +177% vs best_all, +662% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+97% vs best_production, +232% vs best_all, +812% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+97% vs best_production, +232% vs best_all, +812% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+170% vs best_production, +353% vs best_all, +1147% vs best_stand_raw (raw, not comparable)), GJ8 [requires port] (+396% vs best_production, +733% vs best_all, +2192% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+1301% vs best_production, +2255% vs best_all, +6378% vs best_stand_raw (raw, not comparable)), Leapfrog [requires port] (+1995% vs best_production, +3423% vs best_all, +9588% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]
- why: Y6C is selected as cheapest comparable hit at 95,040 (rhs 34,560 + local 0 + aux 0; cost_if_ported 95,040); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next Y6 95,040; err/target 0.042; floor 9.64E-006; selected_production = DOP853 [production] cost_total 159,840; selected_best_all = 95,040 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 82,080 is 14% cheaper than selected_best_all 95,040 and 49% cheaper than selected_production 159,840 (DOP853 [production]); tie band 20%; 17 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## LEO 200 km, 1 d

- regime/config: `leo` / `production`
- target: 10 m
- best_production: `DOP853 [production]` at 59775
- best_ported: `DOPRI5-fixed (requires port)` at 59287
- best_stand_raw: `DOPRI5-fixed (raw, requires port, not comparable)` at 20659
- best_all: `tie: DOPRI5-fixed (requires port), DOP853-adaptive (requires port), DOP853 [production]` at 59287 (tie: production+stand-only)
- selected_best_all_total: 59287
- selected_production: `DOP853 [production]` at 59775
- selected_production_vs_best_all: +1%; best_all_vs_production: -1%
- cheapest_edge: `DOPRI5-adaptive (requires port)` at 63589
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-adaptive 63,589 is 7% more expensive than selected_best_all 59,287 and 6% more expensive than selected_production 59,775 (DOP853 [production])
- acceptable: DOP853-adaptive [requires port] (-0% vs best_production, +1% vs best_all, +189% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +1% vs best_all, +189% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+22% vs best_production, +23% vs best_all, +253% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+22% vs best_production, +23% vs best_all, +254% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+73% vs best_production, +75% vs best_all, +402% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+142% vs best_production, +144% vs best_all, +601% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+774% vs best_production, +781% vs best_all, +2429% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: DOPRI5-fixed is selected as cheapest comparable hit at 59,287 (rhs 20,659 + local 0 + aux 0; cost_if_ported 59,287); selected stand option requires port: cost_if_ported = cost_total + 2*(raw - chunks); the production option in the tie needs no port; status ok; next DOP853-adaptive 59,701; err/target 0.78; floor 1.98E-005; selected_production = DOP853 [production] cost_total 59,775; selected_best_all = 59,287 (best_all base = cost_if_ported (stand port estimate); tie includes production); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-adaptive 63,589 is 7% more expensive than selected_best_all 59,287 and 6% more expensive than selected_production 59,775 (DOP853 [production]); tie band 20%; 8 ok, 1 edge-run row(s), 0 n/a, 0 unresolved

## LEO 300 km, 30 d

- regime/config: `leo` / `production`
- target: 100 m
- best_production: `DOP853 [production]` at 956031
- best_ported: `DOP853-adaptive (requires port)` at 658093
- best_stand_raw: `DOP853-adaptive (raw, requires port, not comparable)` at 223045
- best_all: `DOP853-adaptive (requires port)` at 658093 (stand-only)
- selected_best_all_total: 658093
- selected_production: `DOP853 [production]` at 956031
- selected_production_vs_best_all: +45%; best_all_vs_production: -31%
- cheapest_edge: `DOPRI5-fixed (requires port)` at 2515459
- edge_policy: edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 2,515,459 is 282% more expensive than selected_best_all 658,093 and 163% more expensive than selected_production 956,031 (DOP853 [production])
- acceptable: IAS15 [requires port] (-10% vs best_production, +31% vs best_all, +286% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+0% vs best_production, +45% vs best_all, +329% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+216% vs best_production, +360% vs best_all, +1256% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (+257% vs best_production, +418% vs best_all, +1429% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+294% vs best_production, +472% vs best_all, +1588% vs best_stand_raw (raw, not comparable)), RK4 [requires port] (+318% vs best_production, +507% vs best_all, +1691% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (+423% vs best_production, +660% vs best_all, +2143% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: DOP853-adaptive is selected as cheapest comparable hit at 658,093 (rhs 223,045 + local 0 + aux 0; cost_if_ported 658,093); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next IAS15 859,990; err/target 0.259; floor 1.54E-003; selected_production = DOP853 [production] cost_total 956,031; selected_best_all = 658,093 (best_all base = cost_if_ported (stand port estimate)); edge policy: selected ok for margin; edge not selected because err/target > 0.8; cheapest edge DOPRI5-fixed 2,515,459 is 282% more expensive than selected_best_all 658,093 and 163% more expensive than selected_production 956,031 (DOP853 [production]); tie band 20%; 8 ok, 3 edge-run row(s), 0 n/a, 0 unresolved

## Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22

- regime/config: `surface` / `production`
- target: 0.1 m/s
- best_production: `DOPRI5 [production]` at 26422
- best_ported: `RK4 (requires port)` at 12024
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4808
- best_all: `RK4 (requires port)` at 12024 (stand-only)
- selected_best_all_total: 12024
- selected_production: `DOPRI5 [production]` at 26422
- selected_production_vs_best_all: +120%; best_all_vs_production: -54%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +374% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +269% vs best_all, +823% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +824% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+386% vs best_production, +967% vs best_all, +2569% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2529% vs best_production, +5677% vs best_all, +14348% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,024 (rhs 4,808 + local 0 + aux 0; cost_if_ported 12,024); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 1.77E-06; floor 6.88E-007; selected_production = DOPRI5 [production] cost_total 26,422; selected_best_all = 12,024 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22

- regime/config: `surface` / `production`
- target: 0.1 m
- best_production: `DOPRI5 [production]` at 26422
- best_ported: `RK4 (requires port)` at 12024
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4808
- best_all: `RK4 (requires port)` at 12024 (stand-only)
- selected_best_all_total: 12024
- selected_production: `DOPRI5 [production]` at 26422
- selected_production_vs_best_all: +120%; best_all_vs_production: -54%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +374% vs best_stand_raw (raw, not comparable)), DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +269% vs best_all, +823% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +824% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+386% vs best_production, +967% vs best_all, +2569% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2529% vs best_production, +5677% vs best_all, +14348% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,024 (rhs 4,808 + local 0 + aux 0; cost_if_ported 12,024); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-adaptive 22,800; err/target 6.7E-05; floor 2.75E-007; selected_production = DOPRI5 [production] cost_total 26,422; selected_best_all = 12,024 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- best_production: `DOPRI5 [production]` at 13376
- best_ported: `RK4 (requires port)` at 6080
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 2432
- best_all: `RK4 (requires port)` at 6080 (stand-only)
- selected_best_all_total: 6080
- selected_production: `DOPRI5 [production]` at 13376
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+401% vs best_production, +1002% vs best_all, +2655% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5792% vs best_all, +14629% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 6,080 (rhs 2,432 + local 0 + aux 0; cost_if_ported 6,080); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 11,552; err/target 5.36E-07; floor 1.98E-008; selected_production = DOPRI5 [production] cost_total 13,376; selected_best_all = 6,080 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- best_production: `DOPRI5 [production]` at 13376
- best_ported: `RK4 (requires port)` at 6080
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 2432
- best_all: `RK4 (requires port)` at 6080 (stand-only)
- selected_best_all_total: 6080
- selected_production: `DOPRI5 [production]` at 13376
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+401% vs best_production, +1002% vs best_all, +2655% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5792% vs best_all, +14629% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 6,080 (rhs 2,432 + local 0 + aux 0; cost_if_ported 6,080); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 11,552; err/target 1.7E-06; floor 1.20E-007; selected_production = DOPRI5 [production] cost_total 13,376; selected_best_all = 6,080 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface hover 0.5 km, 0 m/s relative

- regime/config: `surface` / `production`
- target: 0.1 m/s
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+287% vs best_production, +751% vs best_all, +2028% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2073% vs best_production, +4681% vs best_all, +11853% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 3.65E-08; floor 5.66E-008; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface hover 0.5 km, 0 m/s relative

- regime/config: `surface` / `production`
- target: 0.1 m
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+287% vs best_production, +751% vs best_all, +2028% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2073% vs best_production, +4681% vs best_all, +11853% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 6.47E-05; floor 2.26E-008; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 0.5 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2386% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5793% vs best_all, +14632% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 3.93E-07; floor 7.04E-007; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 0.5 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2386% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2578% vs best_production, +5793% vs best_all, +14632% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 7.37E-05; floor 2.82E-007; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 1 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2385% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2565% vs best_production, +5763% vs best_all, +14558% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 7.83E-05; floor 3.28E-007; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

## Surface level flight 1 km, 100 m/s horizontal

- regime/config: `surface` / `production`
- target: 0.1 m/s
- best_production: `DOPRI5 [production]` at 26400
- best_ported: `RK4 (requires port)` at 12000
- best_stand_raw: `RK4 (raw, requires port, not comparable)` at 4800
- best_all: `RK4 (requires port)` at 12000 (stand-only)
- selected_best_all_total: 12000
- selected_production: `DOPRI5 [production]` at 26400
- selected_production_vs_best_all: +120%; best_all_vs_production: -55%
- cheapest_edge: `none` at 
- edge_policy: edge policy: no edge runs in the executed grid
- acceptable: DOPRI5-fixed [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5-adaptive [requires port] (-14% vs best_production, +90% vs best_all, +375% vs best_stand_raw (raw, not comparable)), DOPRI5 [production] (+0% vs best_production, +120% vs best_all, +450% vs best_stand_raw (raw, not comparable)), DOP853 [production] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), DOP853-adaptive [requires port] (+68% vs best_production, +270% vs best_all, +825% vs best_stand_raw (raw, not comparable)), IAS15 [requires port] (+352% vs best_production, +894% vs best_all, +2385% vs best_stand_raw (raw, not comparable)), Encke-IAS15 [requires port] (+2565% vs best_production, +5763% vs best_all, +14558% vs best_stand_raw (raw, not comparable))
- n/a: -
- unresolved: -
- not_run: DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]
- why: RK4 is selected as cheapest comparable hit at 12,000 (rhs 4,800 + local 0 + aux 0; cost_if_ported 12,000); requires port: cost_if_ported = cost_total + 2*(raw - chunks); status ok; next DOPRI5-fixed 22,800; err/target 2.33E-07; floor 8.19E-007; selected_production = DOPRI5 [production] cost_total 26,400; selected_best_all = 12,000 (best_all base = cost_if_ported (stand port estimate)); edge policy: no edge runs in the executed grid; prescribed-force hover/level: no closed-loop altitude hold; tie band 20%; 8 ok, 0 edge-run row(s), 0 n/a, 0 unresolved

