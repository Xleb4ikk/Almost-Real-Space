# Отчёт о проверке планировщика манёвров (Gate 5)

> Примечание при интеграции в «Almost Real Space»: это отчёт автономного стенда.
> После интеграции изменена одна константа — `ShootingCorrector.DampingRatio`
> (`1e-3` → `1e-4`, см. `Docs/Planner.md`); полный набор из 74 проверок C# после
> этого изменения прогнан заново и проходит.
>
> 2026-11-26: после независимого аудита исправлены `CaptureKind.None` (план больше
> не добавляет захват), откат n-body-пролёта на патч-коники и несколько мелких
> пунктов (см. `Docs/Planner.md`, «Review fixes»). Набор из 74 проверок в
> репозитории отсутствует; его нужно прогнать заново против HEAD.
>
> 2026-11-26 (второй аудит): формулы на HEAD перепроверены автономным стендом
> `Tools/PlannerCheck` (C# + Python/mpmath/DOP853); найдены и исправлены четыре
> дефекта сборки гравманёвра и две ошибки в цене/учёте (см. ниже и
> `Docs/Planner.md`, «Audit fixes 2»). Ни одна из проверок ниже (74 C# / 28 Python
> пакета) в репозитории по-прежнему не запускается — числа из отчёта пакета
> остаются историческим утверждением.

## Independent audit of HEAD (2026-11-26)

Стенд: `Tools/PlannerCheck` — те же исходники `Trajectory/Planner/*`, что и в игре,
скомпилированные без Unity; оракулы независимые от кода под тестом:

* распространение двух тел — прямое интегрирование ОДУ `scipy` DOP853 (rtol 1e-13),
  перекрёстно с классическими элементами + уравнением Кеплера в mpmath (50 знаков);
* геометрия коник — замкнутые формулы асимптот `π/2 ± asin(1/e)`;
* цены — аналитические Хоман/отлёт/захват в mpmath.

| Проверка | Случаев | Худшая ошибка |
|---|---|---|
| `LambertLagrange`, концы дуги (эллипс/гипербола/180°) | 61 | **1.2e-12** отн. |
| `LambertLagrange` при 180° против аналитического Хомана | 9 | в пределах O(ε) |
| `UniversalKepler` позиция/скорость | 30 | **2.6e-11**; обратимость 4.4e-14 |
| `EscapeSolver` энергия / асимптота / периапсис | 20 | 7e-16 / 8.6e-16 / 2.5e-15 |
| `FlybyGeometry` поворот и обе асимптоты | 28 | **1.4e-16 / 3.9e-15** |
| `CaptureGeometry.TryIncoming` | 20 | 4e-16 |
| `OrbitPrep.TryHohmann` (Δv и конечная орбита) | 10 | точно (1e-16) |
| `TransferScanner.ArcMinRadius` vs плотная выборка | 30 | 4e-7 (предел выборки) |
| Сквозной Хоман 1→1.524 а.е. (Земля→Марс) | 1 | отлёт/захват/сумма — **< 1e-6 м/с**, TOF +0.06 с |
| Поиск окон vs перебор 400×300 по (t0, tof) | 4 | уточнённый результат на 0.004 м/с **лучше** сетки |
| Поиск момента отлёта 600 vs 8000 сэмплов | 4 | совпадение до 1e-10 |

Найдено (все исправлены, см. `Docs/Planner.md`):

1. `MakeDraftCo` отменял план гравманёвра при недоборе поворота в **0.0074 м/с**
   (лучшее окно всегда лежит ровно на стенке `rp = rpMin`, фактическая дуга
   отлёта просит на 0.000225° больше). Теперь допустим недобор до
   `FlybyDeficitToleranceM` = 2 м/с + 1e-4 стоимости; он закрывается КОРРЕКЦИЕЙ 2.
2. `FlybyGeometry` брал `2·v∞·sin((min−need)/2)` за поворот **слабее** коридора SOI
   (439.5 м/с в измеренном случае при v∞ = 2.6 км/с) — это бесплатный поворот
   периапсисом за SOI. Теперь 0.
3. `PatchedDv` в пролёте смешивал фактический отлёт с идеальным пролётом и
   прибытием (потерян импульс 2.9 мм/с, в общем случае — любая разница).
4. `FlybyGeometry` при `v∞_in ∥ v∞_out` отдавал нулевую ось периапсиса
   (`PeriapsisPosition` = центр планеты).

Мелочи: импульс захвата теперь строго тангенциальный (`t̂·v_target − v`), а
`PlanePenaltyDv` не включает запрошенную подготовку орбиты.

**Не проверено стендом:** `SpacecraftPhysics`, `GamePlannerPropagator`,
`ShootingCorrector` (нужен n-body прогноз игры), эфемериды и UI. Локальный
характер корректора остаётся заявленным ограничением.

Среда: .NET 8 (C#), Python 3 + mpmath/NumPy/SciPy; оракулы — `orbit_oracle.py` навыка астронома (mp-60, `propagate_universal`) и независимые реализации.
Допуски объявлены ДО запуска (PHYSICS_SPEC.md) и не ослаблялись: исправлялись код, оракул или тест.

**Итог:** C# — 74 проверок пройдено, 0 провалов; Python — 28 независимых проверок пройдено, 0 провалов; мутации — 25 из 25 убиты.

## Что найдено проверкой (дефекты, не вошедшие в код благодаря тестам)

* **Ошибка в существующем `LambertSolver`**: `TimeOfFlight` при y ≤ 0 возвращал +∞; для короткого пути это сторона «время слишком мало», бисекция уходила не туда — 20 из 28 быстрых гиперболических дуг отбраковывались. Исправлено (`patches/LambertSolver.patch`).
* **Точность `LambertSolver` у дуги 180° (геометрия Хомана)** падает как 1/ε² (1.4e-10 при ε=1e-3, 2e-6 при 1e-5, ~1 при 1e-7), из-за λ²=1−c/s. Решение в проверке «в ширину» принималось оптимизатором за выгодное (на 1.8 м/с ниже Хомана при 1 % промахе конца дуги). Заменено собственным `LambertLagrange` (ε=1e-3: 6e-15; 1e-6: 4e-12; 1e-7: 3e-11).
* **Оракул SciPy DE «находил» оптимум 5570.7 м/с** ниже аналитического Хомана 5584.23: оптимизатор эксплуатировал потерю разрядов Ламберта у 180°. Кандидаты оракула теперь проверяются пропагацией; оптимум = аналитический Хоман.
* **Патч-коники промахиваются на ~1.8·10⁶ км** (нулевой радиус SOI) — без n-body коррекции план бесполезен. Реализована стрельба; после коррекции промах 50 м (переход) и ≈10 км на 18 годах пути (гравманёвр).
* **Вырожденный якобиан у дуги ≈180°** (положение по нормали не зависит от нормальной скорости старта): Ньютон раскачивал ΔV вдоль нулевого направления. Введены затухающий МНК (min-norm) и вторая стрельба TCM1 на 40 % пути.
* **Цель «периапсис в фиксированный момент» не сходится одним шагом** (дно потенциальной ямы): введена лестница целей (далёкая точка → средняя → периапсис).
* **Параметр «старая орбита корабля через сотни суток»**: двухтельная фаза орбиты уплывает; окно > 3 сут помечается «ориентировочно», ближе — состояние из n-body.
* **Наклонная орбита корабля стоит до +3.2 км/с к идеалу** — теперь показывается отдельной строкой («ОРБИТА НЕ СОГЛАСОВАНА С ОКНОМ»).
* Ошибки самих тестов, найденные и исправленные (не допуски): метрика направления (нормировка на |h|), тождество энергии при разных |v∞|, поиск корней перебором (пропускал второй корень), жёсткий путь `src/Tablet` в проекте тестов (мутации плана не компилировались), самосогласованная, но не независимая проверка смены СК (теперь сверка с состоянием НОВОГО тела).

## Результаты C#
```
PASS A  UniversalKepler position vs mp truth (rel)  worst=4.50E-013  budget=1.0E-010
PASS A  UniversalKepler velocity vs mp truth (rel)  worst=6.82E-013  budget=1.0E-010
PASS B  Lambert prograde: velocities vs mp truth (rel)  worst=2.79E-015  budget=1.0E-008
PASS B  Lambert via (−n, retrograde) equals prograde solution  worst=2.79E-015  budget=1.0E-008
PASS B2 antipodal Lambert: prograde about n (min of h·n̂ ≥ 0.999)  worst=2.22E-016  budget=1.0E-003
PASS B3 Lambert near 180° (|Δν−π| from 1e-10 to 0.1): solver never refuses  worst=0.00E+000  budget=0.0E+000
PASS B3 Lambert near 180°: error / declared budget  worst=2.47E-003  budget=1.0E+000
PASS D  periapsis ↔ turning angle round trip (rel)  worst=4.44E-014  budget=1.0E-010
PASS G  arc minimum radius vs dense-sampling oracle (rel)  worst=1.32E-013  budget=1.0E-007
PASS H  axes: ΔV → (T,N,B) → ΔV (rel)  worst=5.57E-016  budget=1.0E-012
PASS H  axes orthonormal & right-handed  worst=4.44E-016  budget=1.0E-012
PASS H  axes equal ApplyBurn construction (rel)  worst=5.57E-016  budget=1.0E-012
PASS H  normal points to the body on a circular orbit  worst=0.00E+000  budget=1.0E-012
PASS E  circular-coplanar: refined optimum not worse than independent DE optimum (rel excess)  worst=0.00E+000  budget=1.0E-006
PASS E  circular-coplanar: grid cell is within 2% of the optimum (resolution sanity)  worst=4.16E-005  budget=2.0E-002
PASS E  circular-coplanar: same departure date as DE (days)  worst=1.37E-003  budget=3.0E+000
PASS E  circular-coplanar: ship departure ΔV ≥ ideal escape ΔV of the same v∞ (violation, rel)  worst=0.00E+000  budget=1.0E-009
PASS E  eccentric-inclined: refined optimum not worse than independent DE optimum (rel excess)  worst=1.97E-014  budget=1.0E-006
PASS E  eccentric-inclined: grid cell is within 2% of the optimum (resolution sanity)  worst=1.62E-005  budget=2.0E-002
PASS E  eccentric-inclined: same departure date as DE (days)  worst=2.88E-006  budget=3.0E+000
PASS E  eccentric-inclined: ship departure ΔV ≥ ideal escape ΔV of the same v∞ (violation, rel)  worst=0.00E+000  budget=1.0E-009
PASS N  test n-body integrator vs Kepler over 400 d (rel)  worst=1.11E-010  budget=1.0E-009
PASS N  shooting converged to the aim point (converged=1)  worst=0.00E+000  budget=0.0E+000
PASS N  miss after correction ≤ 100 m  worst=5.02E+001  budget=1.0E+002
PASS N  total correction (departure fix + TCM1) is a modest part of the burn (sanity ≤ 5 %)  worst=8.94E-003  budget=5.0E-002
PASS N  patched-conic miss is real and large (sanity: > 1000 km, proves the corrector is needed)  worst=0.00E+000  budget=0.0E+000
PASS M  engine builds the spec for sibling bodies (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  engine best window vs independent optimum (rel excess)  worst=6.39E-008  budget=1.0E-006
PASS M  draft valid, refined, not preliminary (window < 3 d away) (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  n-body miss after correction ≤ 100 m  worst=5.08E+001  budget=1.0E+002
PASS M  impulses: departure → correction → capture, ascending times (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  refined ΔV within 3 % of the patched-conic ΔV (sanity)  worst=2.29E-003  budget=3.0E-002
PASS M  capture impulse leaves the ship at the circular speed of the periapsis radius (rel)  worst=1.35E-016  budget=1.0E-009
PASS M  independent replay: periapsis radius within 2 % of the target  worst=6.01E-005  budget=2.0E-002
PASS M  export: 3 maneuvers in the plan, locked frames, no conflicts (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  export: T/N/B reproduce each planned ΔV (rel)  worst=2.49E-016  budget=1.0E-009
PASS M  export: nothing is 'edited' right after export (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  re-export replaces the previous solution (count stays 3)  worst=0.00E+000  budget=0.0E+000
PASS P  controller: windows found and the first is selected (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS P  controller: draft spans several frames and ends valid & refined (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS P  controller: time-sliced draft equals the synchronous draft (refined ΔV, rel)  worst=5.75E-009  budget=1.0E-004
PASS P  controller: commit adds 3 maneuvers, remove clears them (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS P  controller: cancel stops the calculation and blocks commit (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  window beyond the horizon is flagged preliminary (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS M  manual altitude: prep(2) → departure → correction → capture (0 = ok)   worst=0.00E+000  budget=0.0E+000
PASS M  departure happens on the new circular orbit (radius rel)  worst=2.37E-015  budget=1.0E-006
PASS O  flyby search with default wide ranges within 0.5 % of the independent optimum (rel excess)  worst=0.00E+000  budget=5.0E-003
PASS O  flyby draft valid and refined (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS O  flyby impulses: dep, tcm1, flyby, tcm2, capture with ascending times (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS O  refined total within 5 % of the independent optimum (rel)  worst=2.52E-002  budget=5.0E-002
PASS O  periapsis burn at the giant: post-burn speed equals the exit speed (rel)  worst=0.00E+000  budget=1.0E-009
PASS O  capture burn at the far planet leaves the circular speed (rel)  worst=0.00E+000  budget=1.0E-009
PASS O  independent replay: periapsis at the giant within 3 % of the plan  worst=4.65E-005  budget=3.0E-002
PASS O  independent replay: periapsis at the far planet within 3 % of the target  worst=2.34E-004  budget=3.0E-002
PASS I  capture hyperbola frame orthonormal  worst=2.22E-016  budget=1.0E-012
PASS I  capture impulse = F-MAN-05 and leaves a circular-speed orbit (rel)  worst=1.09E-015  budget=1.0E-012
PASS J  Hohmann prep total ΔV equals F-MAN-01 (rel)  worst=2.12E-014  budget=1.0E-009
PASS J  after prep: circular orbit of radius r2 in the same plane (rel)  worst=2.11E-015  budget=1.0E-009
PASS K  flyby turn deficit ΔV equals vector distance to the best reachable direction (rel)  worst=5.13E-015  budget=1.0E-009
PASS K  flyby needed turn equals the angle between v∞ vectors (rad)  worst=4.44E-016  budget=1.0E-009
PASS L  planner maneuvers are inserted in time order without conflicts (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  AddPlanned: T/N/B reproduce the planned world ΔV (rel)  worst=2.27E-016  budget=1.0E-012
PASS L  fresh planner maneuver is not 'edited' (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  editing ΔV marks the maneuver 'edited' (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  RestorePlanned returns the planned ΔV and clears 'edited' (rel)  worst=2.27E-016  budget=1.0E-012
PASS L  moving the start marks 'edited'; RestorePlanned undoes it (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  manual maneuvers are never 'edited' (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  global frame selector changes manual maneuvers only (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  ReframeManeuver keeps the physical ΔV through 4 frame changes (rel)  worst=2.88E-016  budget=1.0E-009
PASS L  ReframeManeuver stores the ship state relative to the NEW body (rel)  worst=0.00E+000  budget=1.0E-009
PASS L  ΔV is not clamped at 6 km/s (30 000 km/s accepted); NaN/∞ rejected (0 = ok)  worst=0.00E+000  budget=0.0E+000
PASS L  plan clock: start time on the clock is invariant under countdown (s)  worst=0.00E+000  budget=1.0E-009
PASS L  RemoveByPlanner removes exactly the solution's maneuvers  worst=0.00E+000  budget=0.0E+000
PASS F  gravity-assist: best found vs independent DE optimum (rel excess, budget 1e-3)  worst=3.76E-011  budget=1.0E-003
```

## Результаты независимого верификатора Python (mpmath/SciPy)
```
PASS B2 antipodal Lambert: propagated endpoint & velocity (rel)  worst=7.01e-13  budget=1e-09
PASS B  Lambert: propagated endpoint (rel)  worst=6.36e-13  budget=1e-09
PASS C  escape: energy |v|²=v∞²+2μ/r (rel)  worst=1.15e-14  budget=1e-12
PASS C  escape: asymptote direction vs mp-40 conic geometry (abs)  worst=2.15e-14  budget=1e-09
PASS C  escape: velocity direction at r~e^45 from hyperbolic Kepler (abs)  worst=2.15e-14  budget=1e-09
PASS C  escape: reported ΔV equals v_post − v_ship (rel)  worst=0.00e+00  budget=1e-12
PASS C  escape: no cheaper solution found by 60-start independent root search (rel excess)  worst=6.72e-14  budget=1e-09
PASS D  flyby total turn vs asymptotes of the two periapsis hyperbolas (rad)  worst=4.44e-15  budget=1e-09
PASS D  passive flyby |Δv| = 2·v∞/e vs asymptote vectors (rel)  worst=3.92e-16  budget=1e-09
PASS D  FlybyGeometry recovers periapsis radius & powered ΔV from 3-D vectors (rel)  worst=7.28e-12  budget=1e-09
PASS D  FlybyGeometry recovers hyperbola axis and motion direction (abs)  worst=2.84e-14  budget=1e-09
PASS E  transfer solutions: planet states equal independent propagation (rel)  worst=2.40e-13  budget=1e-09
PASS E  transfer solutions: propagated Lambert arc reaches the target (rel)  worst=3.79e-15  budget=1e-09
PASS E  transfer solutions: arrival velocity matches propagation (rel)  worst=3.70e-15  budget=1e-08
PASS E  transfer solutions: cost recomputed independently (rel)  worst=0.00e+00  budget=1e-09
PASS E2 ship burn: ship state at burn time equals independent propagation (rel)  worst=1.05e-15  budget=1e-09
PASS E2 ship burn: post-burn asymptote equals independent Lambert v∞ direction (abs)  worst=1.57e-13  budget=1e-07
PASS E2 ship burn: post-burn energy equals v∞²+2μ/r (rel)  worst=7.77e-12  budget=1e-10
PASS F  planet states equal independent propagation (rel)  worst=2.81e-13  budget=1e-09
PASS F  leg 1 (Lambert) propagates from A to the flyby body (rel)  worst=3.52e-13  budget=1e-09
PASS F  leg 2 (Lambert) propagates from the flyby body to B (rel)  worst=9.76e-13  budget=1e-09
PASS F  real hyperbola asymptote reproduces v∞_in from the reported periapsis state (rel)  worst=4.18e-13  budget=1e-08
PASS F  real hyperbola asymptote reproduces v∞_out from the reported periapsis state (rel)  worst=1.50e-12  budget=1e-08
PASS F  heliocentric energy: |v_out|²−|v_in|² = 2V·Δv∞ + (|u_out|²−|u_in|²) (rel)  worst=1.67e-13  budget=1e-09
PASS F  total ΔV recomputed independently (brentq flyby) (rel)  worst=1.70e-13  budget=1e-09
PASS I  capture: incoming asymptote of the reported periapsis state equals v∞ direction (abs)  worst=4.15e-15  budget=1e-09
PASS I  capture: periapsis speed (vis-viva) and r ⟂ v (rel)  worst=1.72e-16  budget=1e-12
PASS I  capture: orbit normal equals x̂×ŷ (abs)  worst=7.53e-16  budget=1e-12
```

## Сквозные измерения (из логов)
```
INFO escape: outputs written for the mpmath verifier
INFO circular-coplanar: grid 402x206 in 420 ms; best cell 5584.463 m/s, refined 5584.2311 m/s at t0=15.43 d tof=255.23 d; independent DE optimum 5584.2311 m/s at t0=15.43 d tof=255.23 d; 2 local minima
INFO circular-coplanar: ship burn at t=15.296 d, ΔV=6729.9 m/s (ideal circular 3542.8 m/s), periapsis 6119 km
INFO eccentric-inclined: grid 402x206 in 343 ms; best cell 5641.559 m/s, refined 5641.4676 m/s at t0=538.00 d tof=335.29 d; independent DE optimum 5641.4676 m/s at t0=538.00 d tof=335.29 d; 3 local minima
INFO eccentric-inclined: ship burn at t=537.871 d, ΔV=8419.7 m/s (ideal circular 3725.9 m/s), periapsis 5836 km
INFO n-body shooting: patched-conic burn 6732.56 m/s misses the Mars aim by 1808233 km. A) departure min-norm fix 59.65 m/s -> miss 5031 km at the far point (2 it); B) TCM1 at 40 % of flight 0.561 m/s -> miss 50.2 m at periapsis (7 it). Total ΔV change 60.21 m/s
INFO engine windows: 3 found; best 5584.23 m/s at day 15.43 (independent optimum 5584.23 m/s); search span 1202 d
INFO engine draft: valid=True refined=True note='' ideal 5584.2 patched 8773.0 refined 8753.0 m/s; miss 1802810 km -> 50.8 m
INFO engine replay: closest approach to Mars 3790 km (target periapsis radius 3790 km)
INFO controller: draft built in 42 time slices (41 n-body shots)
INFO manual altitude 700 km: prep ΔV 164.3 m/s, total patched ΔV 8788.4 m/s (current orbit: 8773.0 m/s)
INFO flyby windows with DEFAULT wide ranges: 1050 ms, 10 candidates; best 16995.3 m/s at (401.2, 1377.0, 8226.8) d, feasible=True; independent DE optimum 16996.9 m/s (grid steps 9/33/128 d)
INFO flyby draft (4238 ms): valid=True refined=True prelim=False note=''; ideal 16995.3, patched 16995.2, refined 17425.7 m/s; miss at flyby before 20865454 km, final miss at target 6669.2 m
INFO flyby replay (pre-burn approach): closest approach to the giant 1079310 km vs planned periapsis 1079360 km
INFO flyby replay (all impulses): closest approach to the far planet 60014 km vs target 60000 km
INFO MGA: search 567 ms (12 candidates); best 16996.897 m/s [dep 6292.0 + flyby 243.93(+deficit 0.00) + arr 10461.0] at t=(401.9, 1394.0, 5402.3) d, rp=75067 km, turn 165.3° (max 165.3°), behind planet (gain); independent DE optimum 16996.897 m/s
```

## Мутационная проверка (Gate 5, 25 мутантов)
Каждый мутант ломает одну формулу; набор (C# + Python) обязан упасть.
```
KILLED   M1 escape dv: drop factor 2 in 2μ/r  (C#:1 python:0)  first failure: FAIL I  capture impulse = F-MAN-05 and leaves a circular-speed orbit (rel)  worst=4.52E+002  budget=1.0E-012
KILLED   M2 flyby turn: drop second asin term  (C#:0 python:1)  first failure: FAIL D  flyby total turn vs asymptotes of the two periapsis hyperbolas (rad)  worst=1.95e-01  budget=1e-09
KILLED   M3 flyby dv helio: 2v/e -> v/e  (C#:0 python:1)  first failure: FAIL D  passive flyby |Δv| = 2·v∞/e vs asymptote vectors (rel)  worst=1.00e+00  budget=1e-09
KILLED   M4 Lambert: sign of radial term rho  (C#:1 python:0)  first failure: FAIL B  Lambert prograde: velocities vs mp truth (rel)  worst=3.87E+000  budget=1.0E-008
KILLED   M5 Lambert: long-way lambda sign ignored  (C#:1 python:0)  first failure: FAIL B  Lambert prograde: velocities vs mp truth (rel)  worst=2.45E+000  budget=1.0E-008
KILLED   M6 Lambert: stable lambda uses P+D  (C#:1 python:0)  first failure: FAIL B  Lambert prograde: velocities vs mp truth (rel)  worst=8.07E-001  budget=1.0E-008
KILLED   M7 escape solver: other quadratic root  (C#:1 python:0)  first failure: FAIL E  circular-coplanar: ship departure found  worst=1.00E+000  budget=0.0E+000
KILLED   M8 escape solver: radial velocity sign  (C#:0 python:1)  first failure: FAIL C  escape: asymptote direction vs mp-40 conic geometry (abs)  worst=2.00e+00  budget=1e-09
KILLED   M9 axes: normal = tangent x binormal  (C#:1 python:0)  first failure: FAIL H  axes orthonormal & right-handed  worst=2.00E+000  budget=1.0E-012
KILLED   M10 kepler: wrong fdot sign  (C#:1 python:0)  first failure: FAIL A  UniversalKepler velocity vs mp truth (rel)  worst=8.87E+000  budget=1.0E-010
KILLED   M11 arc min radius: periapsis test inverted  (C#:1 python:0)  first failure: FAIL G  arc minimum radius vs dense-sampling oracle (rel)  worst=1.82E+000  budget=1.0E-007
KILLED   M12 transfer: arrival v_inf uses departure body  (C#:1 python:0)  first failure: FAIL E  circular-coplanar: refined optimum not worse than independent DE optimum (rel excess)  worst=1.32E+000
KILLED   M13 flyby axis: wrong b coefficient  (C#:0 python:1)  first failure: FAIL D  FlybyGeometry recovers hyperbola axis and motion direction (abs)  worst=2.00e+00  budget=1e-09
KILLED   M14 flyby search: arrival cost ignores capture  (C#:0 python:1)  first failure: FAIL F  total ΔV recomputed independently (brentq flyby) (rel)  worst=1.63e+00  budget=1e-09
KILLED   M15 stumpff series: wrong sign  (C#:1 python:0)  first failure: FAIL A  UniversalKepler position vs mp truth (rel)  worst=1.24E-002  budget=1.0E-010
KILLED   M16 flyby deficit: drop factor 2  (C#:1 python:0)  first failure: FAIL K  flyby turn deficit ΔV equals vector distance to the best reachable direction (rel)  worst=5.00E-001  
KILLED   M17 capture geometry: axis sign  (C#:0 python:1)  first failure: FAIL I  capture: incoming asymptote of the reported periapsis state equals v∞ direction (abs)  worst=2.00e+0
KILLED   M18 prep orbit: second impulse target speed  (C#:1 python:0)  first failure: FAIL J  Hohmann prep total ΔV equals F-MAN-01 (rel)  worst=9.69E-001  budget=1.0E-009
KILLED   M19 corrector: Newton step sign  (C#:1 python:0)  first failure: FAIL N  shooting converged to the aim point (converged=1)  worst=1.00E+000  budget=0.0E+000
KILLED   M21 flyby deficit: SOI branch drops factor 2  (C#:1 python:0)  first failure: FAIL K  flyby turn deficit ΔV equals vector distance to the best reachable direction (rel)  worst=5.00E-001  
KILLED   M22 plan: global frame selector ignores lock  (C#:1 python:0)  first failure: FAIL L  global frame selector changes manual maneuvers only (0 = ok)  worst=1.00E+000  budget=0.0E+000
KILLED   M23 plan: RemoveByPlanner removes wrong id  (C#:1 python:0)  first failure: FAIL L  RemoveByPlanner removes exactly the solution's maneuvers  worst=1.00E+000  budget=0.0E+000
KILLED   M24 engine: capture impulse sign  (C#:1 python:0)  first failure: FAIL M  capture impulse leaves the ship at the circular speed of the periapsis radius (rel)  worst=1.21E+000  
KILLED   M25 flyby draft: periapsis burn sign  (C#:1 python:0)  first failure: FAIL O  periapsis burn at the giant: post-burn speed equals the exit speed (rel)  worst=2.80E-003  budget=1.0E
KILLED   M20 plan reframe: ignores new body velocity  (C#:1 python:0)  first failure: FAIL L  ReframeManeuver stores the ship state relative to the NEW body (rel)  worst=3.10E+000  budget=1.0E-009
```
Первый прогон выявил пять выживших: три — дефект оснастки (мутации плана не компилировались), два — реальные дыры покрытия (знак импульса захвата и манёвра в периапсисе). Добавлены проверки, повторный прогон: все убиты.

## Не проверено
* Код, зависящий от Unity (`GamePlannerWorld`, `GamePlannerPropagator`, `PlannerView`, патчи `NavTabletView/Station/NavMapModel`): в Unity не компилировался; `PlannerView` — только против заглушек UI Toolkit.
* Реальная физика игры (`SpacecraftPhysics`): n-body проверен на независимом интеграторе DOPRI5 с эфемеридами на рельсах (структурно та же модель), но не на самом `SpacecraftPhysics`.
* Производительность в Unity (один выстрел за кадр; в тестовом проекте ≈ 40–100 мс на выстрел).