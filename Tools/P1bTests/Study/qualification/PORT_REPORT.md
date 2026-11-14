# PORT_REPORT: Приложение C (WH-BM14A)

Дата: 2026-11-14. Ветка: `integrator-port/wh-bm14a`.
Production-прогон C3: `20261114T151403Z_e860827` (`e860827`); отчёт C6 пересобран
из его CSV после `Q-C3b` (floor-aware error, game RHS, отдельный GTO/Molniya).
Новый `full` baseline: `20261114T173334Z_142824a` (`142824a`, `dirty: clean`).

## 1. Решение владельца

Владелец выбрал вариант **A**: абсолютная метрика C7.5 принята как исключение для узкого
LEO-режима. Относительный гейт `≤1.5× Prod-DOP853` в LEO с третьими телами/J2 не выполняется,
но абсолютная ошибка остаётся ниже игровой цели 1 м. `EnablePortedIntegratorPolicy` остаётся
`false`; включение в сценах — отдельное решение владельца.

## 2. Что перенесено

Перенесён только **WH-BM14A** (Blanes–Moan 14A, Kepler-split, 14 стадий, order 6).

| файл | роль |
|---|---|
| `Assets/_Project/Scripts/Core/Integration/KeplerUniversalDrift.cs` | универсальный Kepler-drift (Stumpff + Laguerre; эллипс/парабола/гипербола) |
| `Assets/_Project/Scripts/Core/Integration/WhBm14aIntegrator.cs` | композиция WH-BM14A: kick по возмущению + точный Kepler-drift; кубический Эрмит для рендера |
| `Assets/_Project/Scripts/Spacecraft/SpacecraftPhysics.cs` | ветка `WhBm14a` в локальном кадре; абсолютный путь — безопасный откат на DOP853 |
| `Assets/_Project/Scripts/Spacecraft/IntegratorPolicy.cs` | ветка выбора WH-BM14A; поля `CentralMu`, `Eccentricity`, `PortedAllowed` |
| `Assets/_Project/Scripts/Simulation/VesselStep.cs` | `EnablePortedIntegratorPolicy` (false) и `UseDenseRefinement=false` для WH |
| `SpacecraftIntegrator` | новое значение `WhBm14a` в конец enum |

DOP853 остаётся значением по умолчанию. DOPRI5 остаётся опцией; его ветки
(`EnableRegimeIntegratorPolicy`, `EnableSunCoastPolicy`) не включались.

## 3. Условия выбора

WH-BM14A выбирается только при одновременном выполнении:

* `PortedAllowed` (сценический флаг `EnablePortedIntegratorPolicy`, по умолчанию `false`);
* `CanCoast` — нет тяги, drag и массового расхода;
* не Sun-dominant и не flight-режим;
* chunk ≥ 600 с (в игре long-warp/stress chunk; `EventDrivenPropagator.MaxChunkSeconds=600`);
* высота 200–2000 км;
* эксцентриситет `0 ≤ e < 0.05`;
* `CentralMu > 0`.

Всё остальное — DOP853. Абсолютный путь (`SpacecraftPhysics` без локального кадра) всегда
откатывается на DOP853. Любая ошибка WH (Kepler solve, mass flow, non-finite state) — откат на
DOP853 в этом чанке, без исключения в игровом цикле.

Dense output: кубический Эрмит для рендера; для событий `UseDenseRefinement=false`, событие
переинтегрируется от начала принятого шага.

## 4. Тесты

| тест | результат |
|---|---|
| `tests/GalilegoPhysicsTests/WhBm14aTests.cs` | 4/4: точный Kepler, оборот, dense endpoints, отказ при mass flow |
| `tests/GalilegoSimTests/WhBm14aProductionTests.cs` | 8/8: production wrapper, absolute fallback, dense, touchdown/timed event, детерминизм, аллокации, J2 < 1 м |
| `IntegratorPolicyTests` | 20/20, из них 8 новых ported-сценариев |
| `Test561_IntegratorPolicy` | ALL PASS, включая 4 сценария через `VesselStep`; off == direct Step |
| `GalilegoSimTests` | **60/60** (44 прежних + 8 WH + 8 policy) |
| `GalilegoPhysicsTests` | **283/283** (279 прежних + 4 WH) |
| `Test551` | **84 PASS / 0 FAIL**, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` |

Ключевые факты: чистый Kepler — ошибка < 1e-8 м за шаг и < 1e-6 м за оборот; production
one period < 1e-6 м; J2-сутки < 1 м; timed/touchdown event в пределах 1e-6/1e-3 с;
детерминизм побитовый; аллокации не выше DOP853.

## 5. C7.5: абсолютная метрика

Цель — 1 м для S01–S09/S14, 0.01 м для S15/R4, 1 м для L1; L1 — локальный certified DOP853
(мировой dd-golden расходится на 8.52 м, `FOUND.md` F8).

| задача | WH-BM14A, м | Prod-DOP853, м | floor, м | цель, м | абсолютно |
|---|---:|---:|---:|---:|---|
| S01 | 7.94e-6 | 2.82e-6 | 5.02e-6 | 1 | PASS |
| S02 | 1.07e-6 | 8.39e-6 | 1.46e-6 | 1 | PASS |
| S03 (J2) | 2.25e-2 | 1.13e-5 | 2.62e-6 | 1 | PASS |
| S09 | 6.95e-6 | 7.83e-6 | 2.57e-6 | 1 | PASS |
| S04 | 1.22e-5 | 2.09e-6 | 3.39e-6 | 1 | PASS |
| S05 | 1.07e-4 | 8.91e-5 | 9.52e-5 | 1 | PASS (floor-limited) |
| S15 | 9.78e-6 | 7.75e-7 | 1.82e-6 | 0.01 | PASS |
| R4 | 3.81e-5 | 2.45e-7 | 3.68e-7 | 0.01 | PASS |
| L1 | 1.73e-2 | 3.08e-3 | 1.82e-4 | 1 | PASS (local) |

Относительный C7.5 (`≤1.5× Prod-DOP853`) не выполняется на S01/S03/S04/S15/R4; S02/S09/S05
проходят. Это и есть исключение, принятое владельцем. S05 — 1.12× floor, поэтому 1.07e-4 —
верхняя оценка, а не разрешённая точность.

## 6. C6 production: почему это не общий победитель

| класс | ratio цены WH-BM14A vs Prod-DOP853 | wall 600 с | error (floor-aware) |
|---|---:|---|---|
| leo | 5.73× | 7.3× быстрее | FAIL S01, PASS S02/S09 |
| gto / molniya / heo | 1.11× | ~7–10× быстрее | FAIL S04, PASS S05 |
| gto / molniya only (S04+S05) | 1.45× | ~10× быстрее | — |
| geo | 0.47× | 1.17× медленнее | PASS по цели |
| flyby | 0.26× | 2.3× медленнее | FAIL relative S15/R4 |

C6 требует ≥1.5× на ≥2 классах. WH-BM14A проходит только `leo`; `gto` даёт 1.11×, а
GTO/Молния-only — 1.45×, всё ещё ниже 1.5. Поэтому порт — **исключение владельца**, а не
C6-победа. C6 verdict остаётся: ни один метод не проходит все три условия.

## 7. Что не перенесено

| метод | причина |
|---|---|
| **RKN12(10)** | price 8.18× leo / 1.93× gto, но wall хуже на S02/S06/S12 (600 с) и error 84× S01, 300× L1 (game control); не первый порт |
| **WH-Y6** | price 8.66× leo / 2.29× gto, но J2 убивает: S03 `433.8 м`, S09 `0.91 м`; S04/S05 без J2 проходят, но J2-тест для GTO/Молнии не сделан |
| **GJ8** | C3 прямо исключает; при одном шаге 600 с S01 `3.67e6 м`, S03 `5.38e7 м`; matrix-win при 110.9 с (`0.0296 м`) не является игровым контролем |
| **IAS15** | не ценовой победитель: game-control ~0.87–0.91× RHS на LEO, wall 1.9× медленнее; 2–11× медленнее в flyby; только хаос/сближения, если понадобится |
| **DOPRI5-adaptive / DOP853-adaptive** | это study-варианты; игровые DOPRI5/DOP853 уже есть; новых портов нет |
| **SRKN14A, Y6/Y6C, PEFRL, Leapfrog, Encke** | нет выигрыша или не проходят C6 |

RKN12 при этом не «небезопасен абсолютно»: S01 `0.24 мм`, L1 `0.92 м` при цели 1 м (8 %
запаса). Его блокируют wall и относительный C6-гейт. WH-Y6 не «плох всегда»: без J2 он
проходит S04/S05, но на Земле J2 всегда есть, поэтому общий Earth-режим на нём строить нельзя.

## 8. Флаги и включение

* `EnablePortedIntegratorPolicy` — **false** по умолчанию; в сценах не выставлен.
* `EnableRegimeIntegratorPolicy` — false; `EnableSunCoastPolicy` — false.
* `SpacecraftIntegrator.Dop853` — значение по умолчанию.
* Для включения WH-BM14A владелец сцены ставит `EnablePortedIntegratorPolicy=true`; режим
  остаётся узким: планетоцентрический LEO coast, 600 с, 200–2000 км, e<0.05, без тяги/drag.
* GTO/Молния **не включены**. Для них нужны perigee-guard (e≈0.745), J2, события, 2–7 суток
  и эталон лучше S05 floor; это отдельный этап, а не текущий C8.

## 9. Регрессия и baseline

* Новый `full` baseline: `20261114T173334Z_142824a`, commit `142824a`, `dirty: clean`.
* `--compare` против `20261114T093336Z_f899097`: **0** статусов, 0 новых ячеек, 0 ошибок,
  0 изменений цены > 10 %.
* `Test551`: 84 PASS / 0 FAIL, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6`.
* `GalilegoSimTests`: 60/60; `GalilegoPhysicsTests`: 283/283.
* `Test561_IntegratorPolicy`: ALL PASS.
* Предыдущий baseline сохранён как `BASELINE_20261114T093336Z.md`.

## 10. Ограничения

1. C7.5 относительный не выполнен; принято абсолютное исключение.
2. Цена C6 — cheapest goal-reaching control; ошибка/game RHS — отдельный game-control прогон.
   Это разные конфигурации; цена — best case.
3. S05 near oracle floor (1.12×); GTO/Молния требуют более точного эталона.
4. L1 — локальный эталон; мировой dd-golden 8.52 м, это не ошибка WH.
5. Wall 1/60 и 30 с в `chunks.csv` — информационно, с overhead стенда; игровой гейт — 600 с.
6. Стенд не проходит полный `VesselStep`/`SimulationRunner` путь; он покрыт игровыми тестами.
