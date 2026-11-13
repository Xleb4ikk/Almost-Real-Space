# INVENTORY: Test564_MethodQualification

Результат этапа **M0** (TASK.md, раздел 12). Код не менялся: только этот файл.
Дата инвентаризации: 2026-11-11. Ветка: `qualification/test564-method-qualification`,
базовый commit: `1dff9e67de64fd33dc568d812be3051f30321ae3` (`main`).

Все пути ниже — от корня репозитория. Стенд целиком — `partial class P1bTests`
(один класс, разложенный по файлам), поэтому новый код в
`Tools/P1bTests/Study/qualification/` может вызывать `private static` хелперы
существующих файлов **без** их изменения. Это главный механизм переиспользования.

---

## (a) Методы: `StudyRegistry.All()`

Источник: `Tools/P1bTests/Study/framework/StudyRegistry.cs:206-229`. Сейчас **15 записей**
(DOPRI5 присутствует дважды: fixed и adaptive). Реестр — единственный источник списка
методов; в стенде он не хардкодится.

| # | `Name` | `Family` | `Kind` | `ExpectedOrder` | Где регистрация | `Supports(...)` |
|---|---|---|---|---|---|---|
| 1 | `RK4` | Runge-Kutta | Fixed | 4 | `StudyRegistry.cs:18` | всегда `true` |
| 2 | `DOPRI5-fixed` | Runge-Kutta | Fixed | 5 | `StudyRegistry.cs:38` | всегда `true` |
| 3 | `DOPRI5-adaptive` | Runge-Kutta | Adaptive | NaN | `StudyRegistry.cs:58` | всегда `true` |
| 4 | `DOP853-adaptive` | Runge-Kutta | Adaptive | NaN | `StudyRegistry.cs:76` | всегда `true` |
| 5 | `PEFRL` | Symplectic | Fixed | 4 | `StudyRegistry.cs:116` | `StudySupport.Conservative` |
| 6 | `Y6` | Symplectic | Fixed | 6 | `StudyRegistry.cs:135` | `StudySupport.Conservative` |
| 7 | `Y6C` | Symplectic | Fixed | 6 | `StudyRegistry.cs:135` | `StudySupport.Conservative` |
| 8 | `WH-Y6` | Kepler-split | Fixed | NaN | `StudyRegistry.cs:161` | `Conservative` **и** `CentralMu > 0` **и** `PosDim == 3` |
| 9 | `Leapfrog` | Symplectic | Fixed | 2 | `StudyExampleIntegrator.cs:22` | `StudySupport.Conservative` |
| 10 | `RKN12(10)` | Runge-Kutta | Adaptive | NaN | `StudyAdvancedIntegrators.cs:10` | `StudySupport.Conservative` |
| 11 | `IAS15` | Gauss-Radau | Adaptive | NaN | `StudyAdvancedIntegrators.cs:40` | `PosDim > 0 && 2*PosDim <= Dim` (зависимость от скорости **разрешена**) |
| 12 | `SRKN14A` | Symplectic | Fixed | 6 | `StudyAdvancedIntegrators.cs:77` | `StudySupport.Conservative` |
| 13 | `WH-BM14A` | Kepler-split | Fixed | NaN | `StudyAdvancedIntegrators.cs:105` | `Conservative` + центральное тело + `PosDim == 3` |
| 14 | `GJ8` | Multistep | Fixed | 10 | `StudyAdvancedIntegrators.cs:152` | `StudySupport.Conservative` |
| 15 | `Encke-IAS15` | Encke | Fixed | NaN | `StudyAdvancedIntegrators.cs:192` | `CentralMu > 0 && PosDim == 3 && Dim >= 6` (консервативность **не** требуется) |

Точный текст отказов (`StudyRegistry.cs:95-114`, `StudyAdvancedIntegrators.cs`):

| Условие | `reason` |
|---|---|
| `p.PosDim <= 0` | `no second-order [x, v] structure` |
| `!p.ConservativeSecondOrder` | `force depends on velocity or mass (drag/thrust): splitting order is lost` |
| нет центрального тела | `needs one central body at the origin (CentralMu > 0, PosDim == 3)` |
| `Encke`, `Dim < 6` | `state has no [r, v] block` |
| `IAS15`, нет `[x,v]` | `no second-order [x, v] structure` |

Диапазоны управления (`ControlRange`), важные для M4:

| Метод | coarse | fine | ratio |
|---|---|---|---|
| Fixed по умолчанию (RK4, DOPRI5-fixed, PEFRL, Y6, Y6C, Leapfrog, SRKN14A) | `0.25 * CharTime` | `coarse / 30000` | 1.5 |
| `WH-Y6`, `WH-BM14A` | `0.5 * CharTime` | `coarse / 30000` | 1.5 |
| `GJ8` | `0.02 * CharTime` | `coarse / 3000` | 1.5 |
| `Encke-IAS15` | `0.25 * CharTime` | `coarse / 64` | 2.0 |
| Adaptive по умолчанию (DOPRI5, DOP853) | `rtol = 1e-3` | `1e-16` | `sqrt(10)` |
| `RKN12(10)`, `IAS15` | `rtol = 1e-3` | `1e-13` | `sqrt(10)` |

`CharTime <= 0` заменяется на `Duration / 50`. `p.MaxControl > 0` ограничивает coarse сверху.

Метод нельзя исключить по имени: `StudyRegistry.Select(filter)` бросает исключение на
неизвестное имя и нужен только для ручных прогонов (`P1B_INTEGRATORS`). Стенд Test564
обязан вызывать `StudyRegistry.All()`.

---

## (b) `StudyProblem`: поля и как строится задача

Источник: `Tools/P1bTests/Study/framework/StudyOde.cs:29-273`.

### Поля

| Поле | Смысл |
|---|---|
| `Label` | имя задачи; используется как ключ в CSV и для `StudyKeplerPins` (вес Kepler-решения) |
| `Dim`, `PosDim` | размер состояния; `PosDim = K` для раскладки `[x(0..K-1), v(K..2K-1), extras]`, `0` = нет второй производной |
| `T0`, `Y0` | начало и начальное состояние |
| `Rhs` | `void (t, y, dy)`; **единственный** легальный источник силы для интегратора (через `StudyContext.Rhs`) |
| `Mode`, `ModeTags` | старые ключи режимов (для отчётов Test552/553/559); в Test564 не используются — теги каталога свои |
| `AltitudeKm`, `SpeedMps`, `Eccentricity`, `HasBreakpoints`, `Archived` | метаданные сценария; `Archived` исключает из карт/побед |
| `KeplerExact` | чистое двухтельное поле (Encke — аналитический эквивалент) |
| `EnergyFn`, `AngularMomentumFn` | инварианты для G3 |
| `CheckpointTimes`, `CheckpointRef` | времена и эталонные состояния; **последний checkpoint = конец прогона** |
| `Breakpoints` | разрывы силы; драйвер встаёт на них точно (`StudyStops.Build`) |
| `ConservativeSecondOrder` | `true` только если `a = a(t, x)` и extras постоянны — вход в `StudySupport.Conservative` |
| `CentralMu` | `> 0`: сила = Кеплер вокруг начала координат + возмущение; требует `PosDim == 3` (проверяется `Validate`) |
| `ErrorBlocks` | блоки `{start, length}` для нормы ошибки адаптивных; `null` = по одному блоку на 3-вектор |
| `AdaptiveAtol`, `AdaptiveScales` | абсолютные допуски и локальные масштабы блоков (планетный свип) |
| `DomainViolation` | `Func<double[], string>`: физическая проверка принятого состояния |
| `ErrorFn` | метрика `(state, ref) -> error`; `null` = норма первых 3 компонент |
| `Floor`, `Certified`, `ReferenceNote`, `ReferenceFamily`, `ReferenceMethod`, `ReferenceIndependent`, `ReferenceResolution`, `ReferenceCrossDiffM`, `ReferenceSecondMethod` | паспорт эталона и его погрешность |
| `CharTime` | масштаб времени для сетки управления (период и т. п.) |
| `MaxControl` | верхняя граница управления (для запечённых эфемерид) |
| `ScaleLength` | масштаб длины для относительных целей |

Методы: `GetBlocks()` (ленивая сборка блоков), `Error(state, ref)`, `Validate(out why)`,
`ShallowClone()`, `CloneEndOnly()`, свойства `EndTime`, `Duration`.

### Как строится задача

1. Заполнить обязательные поля: `Label`, `Dim`, `T0`, `Y0`, `Rhs`, `CheckpointTimes`,
   `CheckpointRef`, `PosDim`, `ConservativeSecondOrder`, `CharTime`, `ScaleLength`,
   `Floor`, `Certified`.
2. Для `[x, v]` задач: `Dim = 2*PosDim` (+ extras), `Y0 = [x, v, ...]`.
3. `Rhs` пишет `dy[0..K-1] = v`, `dy[K..] = a` (см. `StudySelfTest.MakeKeplerProblem`,
   `StudyBridgePlanetProblem`).
4. `p.Validate(out why)` вызывается свипом; при ошибке — исключение.
5. `StudyStops.Build(p)` сливает checkpoints и breakpoints (checkpoint имеет приоритет).

### Драйверы исполнения (то, что нельзя дублировать)

* `FixedStepIntegrator.Run` (`StudyIntegratorApi.cs:100-162`): равные подшаги внутри
  каждого интервала до stop-точки, `h_eff = span / ceil(span/h)`, `Flush` на каждом stop.
* `AdaptiveIntegrator.Run` (`StudyIntegratorApi.cs:207-338`): контроллер шага живёт
  сквозь checkpoints, `err <= 1`, потолок `maxStep`, floor `1e-14 * max(Duration,1)`,
  причина аборта записывается в `StudyRun.Abort`.
* `StudyContext` (`StudyOde.cs:309-355`): считает `Calls`, `AuxOps`, проверяет бюджет
  (`MaxCostRhs`/дедлайн) и бросает `StudyBudgetExceeded`.

---

## (c) Карта существующих Test5xx и пересечение с каталогом

Регистрация: `Tools/P1bTests/TestRegistry.cs:297-311`. Флаги `Slow`:
`--fast` запускает только `Slow == false`, `--slow` — только `Slow == true`;
`--exact` игнорирует оба флага (`Program.cs:714-722`).

| Тест | Файл (вход) | Что делает | `StudyProblem`? | Пересечение с каталогом TASK |
|---|---|---|---|---|
| 549 | `Study/integrators/SpacecraftOrbitMatrix.cs:4465` | 17 сценариев корабля × fixed/adaptive/adaptive-local/burn-split; эталон — certified fixed DOP853 + dd | нет, свой runner | builder'ы R3/R4/L1 (кейсы корабля) |
| 550 | `Study/integrators/ReferenceDiagnostics.cs:1056` | диагностика эталонов E0–E10, консоль | нет | нет |
| 551 | `Study/framework/StudyBridge.cs:998` → `StudySelfTest.RunAll()` | самотест фреймворка: порядки, формулы вызовов, бюджеты, смоук-свип | да (Kepler e=0.2, harmonic 3D) | **K1–K8 почти целиком**: `MakeKeplerProblem` (`StudySelfTest.cs:119`), `MakeHarmonicProblem` (`:1186`), `MakeDragKeplerProblem` (`:1147`); но все `private` внутри класса `StudySelfTest` |
| 552 | `Study/framework/StudyBridge.cs:167` | свип корабля, цели 100/10/1 м, cap ∞/600 с | да (`StudyBridgeShipProblem:64`) | R3/R4/L1 |
| 553 | `Study/framework/StudyBridge.cs:637` | свип планет: real solar 7 тел 10/100 лет и др., dd-Taylor эталон | да (`StudyBridgePlanetProblem:401`) | T1–T3, R1–R2 по форме (N-body, ошибка по парам) |
| 554 | `Study/integrators/OrbitEdgeCases.cs:20` | Луна/SOI, гипербола, 30 сут, 1000 витков на целях до 1 см | да | R4, L1 |
| 555 | `Study/integrators/LowAltitudeCases.cs:19` | парашют/взлёт/suicide burn/вход (ARCHIVED) | да | нет (вне каталога) |
| 556 | `Study/integrators/WorkPrecisionAnalysis.cs:42` | перечитывает CSV 552/553/554/559, кроссоверы | нет | формат отчётов (образец) |
| 557 | `Study/integrators/ProductionChunking.cs:33` | production-чанкинг (ARCHIVED) | нет | нет |
| 558 | `Study/integrators/InvariantsAndSun.cs:17` | J2 × наклонение + normalized Sun; `Test558_Invariants.csv` | да | G3 (инварианты), K7-подобная нормировка |
| 559 | `Study/integrators/PureGravityCases.cs:20` | чистая гравитация: орбиты Луны, hops, пролёты Луны/планет, гелиоцентрика | да | R4 (`MoonFlybyProblem:275`), L1 (`MoonOrbitProblem:211`) |
| 560 | `Study/tests/Test560.cs:14` | decision matrix | — | **не зарегистрирован**, мёртвый код (заменён 560b) |
| 560b | `Study/tests/Test560b.cs:13` | полная карта режим→метод→точность→цена | да | образец покрытия методов |
| 561 | `Tests/IntegratorPolicyRuntime.cs:16` | runtime policy `VesselStep` | нет | нет |
| 562 | `Study/integrators/ProductionWorkPrecision.cs:212` | production work/precision, 14 ситуаций, retime | нет | **G7 wall-time**: `Test562RetimeMode:2945` |
| 563 | `Study/integrators/LeoCapRegime.cs:159` | режим cap на LEO | нет | образец оформления `results/cap_regime/Test563_*` |

Существующие builder'ы задач каталога (раздел 6 TASK):

| Каталог | Готовый builder | Где | Пригодность |
|---|---|---|---|
| K1–K4 (Kepler e=0, 0.1, 0.5, 0.9) | `StudySelfTest.MakeKeplerProblemEcc(e, checkpoints, periods)` | `StudySelfTest.cs:124` | формула точная, но метод `private` внутри **другого класса** → не вызывается без правки существующего файла |
| K5, K6 (парабола, гипербола) | `AnalyticHyperbola(e, dt)` | `StudySelfTest.cs:81` | то же: `private`, `rp = 1`, `mu = 1` захардкожены |
| K7 (гармонический) | `StudySelfTest.MakeHarmonicProblem()` | `StudySelfTest.cs:1186` | `private`, одна точка `Period` |
| K8 (затухание) | `StudySelfTest.MakeDragKeplerProblem()` | `StudySelfTest.cs:1147` | эталон — нули (это тест «аборт/причина»), не решает K8 |
| T1 (figure-eight) | `KnownFigureEight(periods)` | `scenarios/KnownProblems.cs:142` | IC/период уточнённые (не 8 знаков); считает Taylor-эталон, но не `StudyProblem` |

**Источник ИК T1.** Базовые значения (8 знаков) — Chenciner & Montgomery, *A remarkable
periodic solution of the three-body problem*, Annals of Mathematics 152 (2000), и
C. Simó, *Dynamical properties of the figure eight solution of the three-body problem*,
Contemp. Math. 292 (2002). Уточнённые значения и период, которые использует стенд, взяты
из уже существующего кода `Study/scenarios/KnownProblems.cs:142-166`:
`y = -0.24308756818428653234`, `vx = 0.46620369792022003799`,
`vy = 0.43236571164315760836`, `T = 6.32591409785902117500`; `x = 0.97000436`,
`v3 = -2·v1`. Стенд вызывает эти значения через `QualificationFigureEightInput()`
(значения продублированы в `QualificationCatalog.cs` с комментарием-ссылкой).
| T2 (Lagrange) | `KnownLagrangeTriangle(m1,m2,m3,periods,...)` | `scenarios/KnownProblems.cs:82` | точная формула поворота; не `StudyProblem` |
| T3 (Pythagorean) | `PythagoreanInput()` + `TaylorNBody`/dd | `taylor/Pythagorean.cs:6` | готовый вход и dd-эталон; не `StudyProblem` |
| R1, R2 (DE441, Newton/1PN) | **нет** `StudyProblem`; есть `JplFile`/`JplNBody` (`jpl/JplRealism.cs:11,37,42`) и `JplDop853` (`jpl/Jpl1Pn.cs:269`) | — | нужен адаптер в qualification (M5) |
| R3 (корабль у Земли) | `StudyBridgeShipProblem` + кейсы Test549 | `StudyBridge.cs:64`, `SpacecraftOrbitMatrix.cs:4187` | готово |
| R4 (пролёт Луны) | `MoonFlybyProblem` | `PureGravityCases.cs:275` | готово (`StudyProblem` с certified ref) |
| L1 (1000 витков LEO) | кейс `"Earth LEO 400 km, 1000 orbits (~64 d)"` + `StudyShipMatrixBuildReferences` | `SpacecraftOrbitMatrix.cs:4292`, `:930` | готово; oracle — локальный сертифицированный DOP853 (`LocalError` = 1.824e-4 м), golden — только кросс-проверка |
| L2 (Kepler e=0.5, N витков) | — | — | строится как K3 с бо́льшим горизонтом; N фиксируется в M5 |

Готовые обёртки, которые Test564 должен вызывать (а не переписывать):

* `StudyBridgeShipProblem(c, refs)` — `StudyBridge.cs:64`.
* `StudyBridgePlanetProblem(label, nb, years, reference, alternative)` — `StudyBridge.cs:401`.
* `StudyBridgeAttachSecondReference(p)` / `AttachIndependentIas15Reference(p)` — независимый
  второй эталон (IAS15) для планет/чистой гравитации.
* `StudySweepRunner.Run(p, integrators, options, log)` — весь sweep, цена и уточнение
  брекетов (`StudySweep.cs:570-...`).
* `StudyReport.FinalizeCosts(rows, keplerWeight)` — цена `calls + w*aux`
  (`StudyReport.cs:53-59`), правило single-price из `integrators/REPORT_DATA_AUDIT.md`.
* `StudyCalibration.Resolve(p, options)` — вес одного Kepler-решения (пины
  `Study/integrators/kepler_weights.csv`, переопределение `P1B_KEPLER_WEIGHT`).
* `Test562RetimeMode`-схема для G7: прогрев, `GC.Collect`, батчи, медиана ≥5 повторов,
  `Test562Stats`/`Test562TimedBatch` (`ProductionWorkPrecision.cs:980,898`). Переиспользовать
  **методику**; код привязан к `Test562Cell`, поэтому в qualification нужен свой тонкий
  измеритель по той же схеме.

---

## (d) DE441 / Horizons: где данные и что нужно

Каталог: `Tools/P1bTests/Study/jpl/`.

| Файл | Что внутри / зачем |
|---|---|
| `horizons_real_2026_10y.json` (1.2 МБ) | **основной** oracle-набор: эпоха 2026-01-01 TDB, heliocentric ecliptic J2000, м/м·с⁻¹, 483 тела, 11 годовых состояний (дни 0…3650) |
| `horizons_real_2016_10y.json` | то же для 2016–2026 (holdout) |
| `horizons_short_2026.json`, `horizons_short_2016.json` | 8 планет, состояния на 0/1/10/30/90/180/365 д |
| `de441_asteroids.csv` | 343 строки `number,gm_au3d2` — точные GM астероидов DE441 (JPL IOM 392R-21-005) |
| `fetch_*.py`, `add_*.py`, `fix_*.py` | генераторы/модификаторы JSON (провенанс данных) |
| `JplRealism.cs` | DTO `JplFile`/`JplBody`/`JplState` (`:11-35`), `JplNBody(...)` (`:37`, `:42`) |
| `Jpl1Pn.cs` | силы `JplAcc1Pn`, `JplAccEih`, `JplAccZonalJ2`, `JplAccEarthJ2`, `JplPlanetsAsteroids`, `JplDop853` |
| `EihUnit.cs`, `FullComparison.cs`, `IntegratorComparison.cs`, `FullSystemIntegrators.cs`, `JupiterVariants.cs`, `MoonsAsBodies.cs`, `RingBelt.cs`, `Precession.cs`, `Asteroids.cs`, `Bake100y.cs`, `SpacecraftDop853.cs` | тесты 530–548 на этих данных |

Схема `horizons_real_*_10y.json`:

```
{ "epoch": "...", "frame": "heliocentric ecliptic J2000", "units": "m, m/s",
  "source": "NASA/JPL Horizons DE441", "days": [0, 365, ..., 3650],
  "bodies": [ { "name": "...", "mu": <system GM>, "mu_body": <planet-only GM>,
                "states": [ { "days": 0, "p": [3], "v": [3] }, ... ] } ] }
```

* `mu` — GM системы (планета+спутники) для Юпитера/Сатурна/Урана/Нептуна, иначе равно `mu_body`.
* 483 тела: 8 планет + Луна + галилеевы + Титан + 343 `Ast<номер>` + `Europa52` + 30 `Kbo*`
  + 36 `Kbr0..35` (у `Kbr*` только одно состояние, день 0).
* Для R1/R2 нужен поднабор **10 тел**: `Mercury, Venus, Earth, Mars, Jupiter, Saturn, Uranus,
  Neptune, Moon, Sun`. Солнце в файле отсутствует как тело (состояния гелиоцентрические);
  барицентрическую поправку и выбор `mu`/`mu_body` задаёт `JplNBody(JplFile, names,
  useSystemGm, barycentric, centerWithSystemGm)` (`JplRealism.cs:42`).

Важно: **ни один** файл в `Study/jpl/` не строит `StudyProblem` (`grep StudyProblem` → 0
совпадений). Адаптер DE441 → `StudyProblem` пишется в qualification (M5), поверх
`JplNBody` + `StudyBridgePlanetProblem`-схемы.

---

## (e) Команда запуска и точка сравнения Test551

Команда (из корня репозитория):

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test551_IntegratorFrameworkSelfTest
```

Результат на базовом commit `1dff9e6` (ветка от `main`, до любых изменений):

* `[Test551_IntegratorFrameworkSelfTest] ok`, `ALL PASS`, **84** строки `PASS`, **0** `FAIL`.
* Воспроизводимая точка сравнения — скрипт
  `Tools/P1bTests/Study/qualification/baseline/verify_test551.ps1`. Он запускает тест с
  `--log`, берёт строки `PASS `/`FAIL `, оставляет имя проверки (текст до ` | `),
  склеивает через LF, добавляет завершающий LF и считает SHA256 по UTF-8 без BOM.
* Результат скрипта: `lines=84 fails=0`, `SHA256=54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6`.
  Этот же хэш воспроизведён после всех Q-FIX-исправлений.

Почему не хэш всего вывода: две проверки печатают измеряемые величины, которые меняются
от прогона к прогону:

* `Kepler weight measurement ... | w=... cv=...` — wall-clock отношение (в двух прогонах
  M0/M1: `w=3.81E+001 cv=2.36E-001` и `w=4.00E+001 cv=8.04E-002`);
* строка `[Test551...] ok in <N> ms`.

Поэтому сравнивать нужно **число PASS/FAIL, имена проверок и их хэш**, а не сырой лог и не
строки с деталями. По правилу TASK.md (раздел 2) Test551 обязан давать тот же набор
PASS/FAIL после аддитивных изменений; любое расхождение в именах или статусах — стоп и разбор.

Среда этого прогона: .NET SDK 10.0.401, runtime `Microsoft.NETCore.App 8.0.24` (net8.0),
AMD Ryzen 9 8940HX, 32 логических CPU, Windows x64.

---

## Что переиспользуется, чего не хватает, риски

Переиспользуется как есть: `StudyProblem`/`StudyContext`/драйверы, `StudyRegistry.All()`,
`StudySweepRunner`, `StudyReport.FinalizeCosts`, `StudyCalibration`, `KeplerUniversal`,
`TaylorNBody` + dd-варианты, `StudyBridgeShipProblem`/`StudyBridgePlanetProblem`,
`StudyShipMatrixBuildCases`/`StudyShipMatrixBuildReferences`, `JplFile`/`JplNBody`/`JplDop853`,
`MoonFlybyProblem`/`MoonOrbitProblem`, golden `ship_1000_orbits_dd.json`.

Не переиспользуется без правки существующего кода (записано в `FOUND.md` при M2):

1. Аналитические Kepler/harmonic/drag задачи `StudySelfTest` — `private` в другом классе.
2. `StudyShipCertifiedReference` (`CertifiedReference.cs:421`) требует `StudyShipMatrixCase`
   и локальный/запечённый кадр; это не общий API «certified reference для произвольной
   задачи». Для T1/T3 эталон берётся из dd-Taylor, для R1/R2 — из `JplDop853`/`StudyRefRun`.
3. Свип `StudySweepRunner` оптимизирован под «найти цену при целях»; для Test564 нужны
   строки на каждом control (`runs.csv`) и статусы PASS/FAIL/N/A/SKIP/ERROR. Sweep-строки
   (`StudyRow`) содержат всё необходимое (`Err`, `Steps`, `Rejected`, `Calls`, `Aux`,
   `CapLimited`, `Note`), но уровень A1/A2/A3 и гейты G1–G8 считаются в qualification
   поверх `StudyRow`, а не внутри свипа.

Риски/вопросы для следующих этапов (не блокируют M0):

* `Program.cs` не передаёт тесту `--fast`/`--slow`; профиль `smoke`/`full` внутри Test564
  потребует своего переключателя (M1, решение фиксируется в `PROGRESS.md`).
* `.gitignore` игнорирует `Tools/P1bTests/Study/**/results/**/*.csv` и `*.log` — папки
  прогонов не попадут в git автоматически; baseline-указатель (`baseline/BASELINE.md`)
  коммитится отдельно (M7).
* `DE441 → StudyProblem` отсутствует; для R1/R2 нужен адаптер и решение, что считать
  «certified reference той же модели» (M5).
* Canonical `Sun` в Horizons-файле отсутствует (гелиоцентрические состояния). Барицентр
  восстанавливается по `JplNBody`; это надо проверить в M5 на первом же прогоне.

---

## M5: чем фактические R/L отличаются от формулировки TASK.md

Правило TASK.md §6: если существующий builder даёт задачу, отличающуюся от описанной,
берётся существующая, а отличие записывается здесь.

| id | что в TASK.md | что взято | отличие |
|---|---|---|---|
| R1 | DE441, 10 тел, Newtonian, 10 лет | `StudyRealSolar7` (тестовый фикстур, real solar 7-body) + `StudyMakeNBody` + два независимых dd-Taylor эталона (43200/21600 с) + `StudyBridgePlanetProblem` — тот же путь, что в Test553 | по указанию владельца взят проверенный тестовый фикстур вместо JSON-адаптера; 7 тел вместо 10, горизонт 10 лет в `full`, 1 год в `smoke`; `CentralMu = 0`, поэтому WH-Y6/WH-BM14A/Encke дают N/A |
| R2 | DE441, 10 тел, 1PN, 10 лет | то же + `JplAcc1Pn` (Sun-only 1PN — headline-модель проекта) | EIH и J2 не включены: TASK.md говорит «1PN», а в проекте headline — Sun-only 1PN |
| R3 | корабль у Земли + возмущения (Луна, Солнце), 30 сут | Test549-кейс «Earth LEO 400 km, 30 d» (локальный кадр, без тяги/drag) | возмущения идут через запечённые эфемериды системы; отдельного кейса «только Луна+Солнце» нет |
| R4 | пролёт Луны (гиперболический) | `MoonFlybyProblem(solar, 1000 м/с, 100 км)` (Test559), Moon-centred | вместо Test549-кейса «Earth-Moon flyby inside lunar SOI»; выбран гиперболический пролёт у Луны, как в тексте |
| L1 | 1000 витков LEO (чистая гравитация) | Test549-кейс «Earth LEO 400 km, 1000 orbits» + локальный сертифицированный DOP853 (`LocalReferenceOnly = true`) | «чистая гравитация» = без тяги и drag; возмущения Луны/Солнца через запечённые эфемериды присутствуют. Oracle — локальная модель, которую интегрируют методы (floor 1.824e-4 м); golden/локальное расхождение 8.522 м — frame difference (`FOUND.md` F8), golden только кросс-проверка. A1/A2 оцениваются, A3 SKIP по floor |
| L2 | Kepler e=0.5, N витков (N задать) | N = 100 витков, 4 checkpoint'а, замкнутая формула | N зафиксировано |

Уровни: `ScaleLength` берётся из builder'а (для корабля — радиус орбиты, для DE441 — 1 а.е.),
поэтому A1/A2/A3 — это разные абсолютные метры в разных задачах, как и требует §7.

---
---

# ПРИЛОЖЕНИЕ A (S0): инвентаризация сценариев

Результат этапа **S0** (TASK.md, A10). Дата: 2026-11-12. Ветка
`qualification/test564-method-qualification`. Код не менялся: только этот файл
(`git diff` по `*.cs` пуст).

Правило A2: параметры сценариев **не придумываются** — все берутся из существующих
builder'ов. Ниже перечислено, какой builder и какой эталон даёт каждую задачу S/P/F, какие
флаги `StudyProblem` выставляет builder, и какие силы он включает.

## A-S. Тир S (корабль, Test552)

Все 17 кораблевых кейсов строит **один** существующий builder —
`StudyShipMatrixBuildCases(gravity, j2OnlySystem, dragOnlySystem)`
(`Study/integrators/SpacecraftOrbitMatrix.cs:4187`). Системы — `StudyShipMatrixSystem(j2, atmosphere)`
(`:486`) поверх `StudyRealSolar7()` (`Study/fixtures/StudyFixtures.cs:80`); атмосфера —
`AtmosphereProfile` Земли (`SeaLevelDensity 1.225`, `ScaleHeight 8500`, `Top 2e6`), J2 Земли
`1.08263e-3`. Задача — `StudyBridgeShipProblem(case, refs)`
(`Study/framework/StudyBridge.cs:64`), эталон — `StudyShipMatrixBuildReferences(case)`
(сертифицированный фиксированный DOP853, 5 уровней + Richardson; независимый dd/IAS15 —
где включён). Ни один параметр в `qualification/` не задаётся заново.

Колонки: `C` = `ConservativeSecondOrder`, `μ` = `CentralMu`, `PD/D` = `PosDim/Dim`.
`Dim = 7` у всех кораблевых кейсов (`StudyBridgeShipProblem` всегда кладёт массу 7-м
компонентом; без тяги она постоянна), `PD = 3`.

| id | label в `StudyShipMatrixBuildCases` (№ Test552) | горизонт | силы (как в builder'е) | C | μ | PD/D | эталон | теги |
|---|---|---|---|---|---|---|---|---|
| S01 | `Earth LEO 400 km circular` (1) | 1 сут | gravity + third-body | true | Earth `3.986004418e14` | 3/7 | certified local DOP853 | leo |
| S02 | `Earth LEO 400 km polar` (2) | 1 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853 | leo |
| S03 | `Earth LEO 400 km sun-synchronous` (3) | 1 сут | gravity + **j2** + third-body | true | Earth | 3/7 | certified local DOP853 | leo, j2 |
| S04 | `Earth GTO 400 x 40000 km` (4) | 2 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853 | gto, ecc-high |
| S05 | `Earth Molniya 500 x 40000 km` (5) | 7 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853, `ReferenceStepSeconds=10` | molniya, ecc-high |
| S06 | `Earth GEO 35786 km` (6) | 30 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853 | geo, long-arc |
| S07 | `Sun 1 AU circular` (7) | 1 год | gravity + third-body (планеты) | true | Sun `1.32712440018e20` | 3/7 | certified local DOP853 | heliocentric, far-flight |
| S08 | `Sun 0.5 x 2 AU eccentric` (8) | 1 год | gravity + third-body | true | Sun | 3/7 | certified local DOP853 | heliocentric, far-flight, ecc-mid |
| S09 | `Earth 400 km + J2` (9) | 1 сут | gravity + **j2** (Drag = null) | true | Earth | 3/7 | certified local DOP853 | leo, j2 |
| S10 | `Earth 250 km + atmosphere drag` (10) | 1 сут | gravity + third-body + **drag** (зависит от скорости) | **false** | Earth | 3/7 | certified local DOP853 | leo, drag, velocity-dependent |
| S11 | `Earth LEO 400 km + 600 s prograde burn` (11) | 1 сут | gravity + third-body + **thrust** 2000 Н, Isp 300, ramp 20 с, переменная масса | **false** | Earth | 3/7 | certified local DOP853, breakpoints на 4 границах тяги | leo, thrust, velocity-dependent |
| S12 | `Earth-Moon flyby inside lunar SOI (Moon-centered)` (12) | 2 сут | gravity Луны + third-body (Earth/Sun), `SeamAwareReference` | true | Moon `4.9028e12` | 3/7 | certified local DOP853 **при условии**, что seam-aware эталон дошёл до асимптотической пары; иначе Test552 сам объявляет «no accuracy claim» (см. ниже) | flyby, moon-soi, close-encounter |
| S14 | `Earth HEO 300 x 120000 km` (14) | 5 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853, `ReferenceStepSeconds=10` | heo, ecc-high |
| S15 | `Earth hyperbolic flyby e=1.5` (15) | 1 сут | gravity + third-body | true | Earth | 3/7 | certified local DOP853, `ReferenceStepSeconds=5`, `ScaleLength=1e8` | flyby, escape, close-encounter |
| S17 | `Earth LEO 400 km + unaligned 600 s burn` (17) | 1 сут | gravity + third-body + **thrust**, ramp 1 с, переменная масса | **false** | Earth | 3/7 | certified local DOP853 (hard-edge случай: эталон может быть не асимптотическим — решает аудит) | leo, thrust, velocity-dependent |

Сценарии **13 и 16 не дублируются**: №13 — это `L1`, №16 — `R3` (см. A-RL).
`S13`/`S16` в каталоге отсутствуют по TASK.md A4.

Что важно для стенда:

* Сила кораблевых кейсов — **не только центральная**: `StudyShipMatrixLocalAcceleration`
  всегда добавляет возмущения Луны/Солнца (для гелиоцентрики — планет) из запечённых
  эфемерид. Поэтому `forces` в `conditions.csv` у S01–S09, S12, S14, S15 включает
  `third-body`, хотя таблица A4 называет только «гравитацию». Это отличие builder'а,
  а не новая постановка (A2: параметры задаёт builder).
* `StudyShipMatrixCaseOrbit` ставит `Drag` только если `Perturbed == true` **и** у
  центрального тела есть атмосфера. Поэтому S12 (`Perturbed = true`, Луна без атмосферы)
  остаётся консервативной; `ConservativeSecondOrder` у S12 = true.
* S03 и S09 включают J2 (`centralBody.J2 != 0`), но остаются консервативными
  (`a = a(x)`), поэтому методы расщепления для них **применимы** — это не нарушение A4.
* S10, S11, S17: `ConservativeSecondOrder = false` (builder: `c.Drag == null && !c.Thrust`).
  Это ровно три задачи A4, на которых проверяется правило §8.3.

### Эталоны тира S: сертификация

`StudyBridgeShipProblem` берёт `p.Floor = refs.ReferenceFloor` (`CertifiedFloor`:
`LocalError`, если эталон асимптотический, иначе `LocalFinestDelta`, если `LocalResolved`)
и `p.Certified = double.IsFinite(floor)`. `refs.LocalKind` содержит вид эталона. Отсюда:

* S01–S11, S14, S15: эталон сертифицируется существующим механизмом Test549; ожидаемый
  статус `VALID`, floor запишет аудит S2/S3 (числа не переносятся из старых прогонов).
* **S12**: у Test549 есть отдельная ветка «Earth-Moon flyby» — если seam-aware эталон не
  дошёл до асимптотической пары, кейс помечается «no accuracy claim (uncertified
  reference)» и `ReferenceFloor = NaN`. Тогда S12 в стенде будет `INVALID` с этой причиной
  (G8), а не «тихо оценён». Решает аудит S4.
* **S17**: hard-edge burn (`StudyShipUnsplitPathNoClaim`); при неасимптотическом эталоне
  Test549 тоже не делает accuracy claim. Если аудит S3 даст `INVALID`, причина
  записывается в `oracle_audit.md` (A10 S3 допускает «VALID или записанная причина»).

## A-P. Тир P (планеты, Test553)

Все P-задачи строятся существующим путём Test553: `StarSystem` из фикстура →
`StudyMakeNBody` → два независимых dd-Taylor эталона (`StudyPlanetIndependentReference`,
`StudyPlanetIndependentReferenceAlt`, шаг 43200/21600 с, выборка каждые 30 сут) →
`StudyBridgePlanetProblem`. P6 — отдельный существующий builder
`StudyBridgeHeliocentricProblem` (`StudyBridge.cs:860`). Параметры систем — в
`Study/fixtures/StudyFixtures.cs`.

| id | система / builder | горизонт | силы | C | μ | PD/D | эталон | теги |
|---|---|---|---|---|---|---|---|---|
| (R1) | `StudyRealSolar7` — 7 тел | 10 лет (full), 1 год (smoke) | gravity | true | 0 | 21/42 | два dd-Taylor, floor = худшая пара | см. M5 |
| P2 | `StudyRealSolar7` — 7 тел | 100 лет | gravity | true | 0 | 21/42 | два dd-Taylor, floor = худшая пара | planets, long-arc |
| P3 | `StudyCustomSystem` — 6 тел (Sol, Terra, Luna, Vesper, Jove, Ares) | 10 лет | gravity | true | 0 | 18/36 | два dd-Taylor | planets |
| P4 | `StudyTightBinarySystem` — 2 тела | 10 лет | gravity | true | 0 | 6/12 | два dd-Taylor | planets, close-encounter |
| P5 | `StudyEccentricSystem` — Sun, Comet (e=0.8), Jupiter | 10 лет | gravity | true | 0 | 9/18 | два dd-Taylor | planets, ecc-high |
| P6 | `StudyHeliocentricSystem` — Sun, Jupiter, Probe (`CentralMu = Sun`) | 10 лет | gravity Солнца + возмущение Юпитера | true | Sun | 3/6 | два dd-Taylor по относительному состоянию зонда | planets, probe-central |

Метрика ошибки P-задач — определённая builder'ом (`StudyBridgePlanetProblem`: худшая
относительная ошибка пары тел; P6: относительное состояние зонда). **Не переопределяется**
(A6). P6 — единственная P-задача с `PosDim == 3` и `CentralMu = Sun`, поэтому WH-Y6,
WH-BM14A и Encke для неё применимы (проверяется в S5).

Стоимость: два dd-Taylor эталона P2 (100 лет) доминируют; поэтому P2–P6 — профиль `full`
(в smoke не входят), иначе smoke перестаёт быть «минутами» (§13).

## A-RL. Совпадения с существующими R/L

| задача A4/A5 | существующая задача стенда | что делать |
|---|---|---|
| Test552 №13 (1000 витков) | **L1** | копию не заводить; добавить метровые цели (S1) |
| Test552 №16 (30 сут) | **R3** | копию не заводить; добавить метровые цели (S1) |
| Test559 `Moon flyby vinf 1000 alt 100` | **R4** (`MoonFlybyProblem`) | копию не заводить; добавить метровые цели (S1/S4) |
| Test553 `Real solar 7-body, 10 y` | **R1** | копию не заводить; добавить метровые цели (S1) |
| Test552 №12 (Earth-Moon flyby inside lunar SOI) | **S12** | НЕ совпадает с R4: R4 — гиперболический пролёт у Луны (`MoonFlybyProblem`), S12 — пролёт внутри лунной СОИ в Moon-centered кадре с другим builder'ом. Заводится отдельно |

## A-F. Тир F: отбор по A5

Правило A5 применено к сценариям Test559 (`Study/integrators/PureGravityCases.cs:20`).
Все сценарии Test559 проходят существующий гейт `pure-gravity suite: every scenario
certified` (`:205`), то есть эталон сертифицирован builder'ом; флоры запишет аудит S5.

Шаг 1 (есть сертифицированный эталон): все 60+ сценариев Test559.
Шаг 2 (исключить дубли R4): `Moon flyby vinf 1000 alt 100` = R4.
Шаг 3 (исключить повтор S01–S17 по орбите и силе):
* `Earth flyby 1.1/3/10 R vinf 1000/10000` — тот же класс «пролёт у Земли с солнечным
  приливом», что S15 (`Earth hyperbolic flyby e=1.5`, тот же builder-путь) → не заводим;
* `Moon flyby *` (кроме R4) — тот же класс, что R4 → не заводим;
* `Far flight 30 AU e 0.5 (normalized)` — та же физика, что `(physical)`, в
  нормированных единицах → не заводим;
* `Heliocentric 0.3/1/5/30 AU × e` — класс гелиоцентрики с возмущением Юпитера; S07/S08 —
  чистое центральное поле Солнца без Юпитера, поэтому это **не** дубль по силе; берём
  один представитель класса.
Шаг 4 (не больше 8, по одному на класс) — итог 5 задач:

| id | label Test559 / builder | класс | горизонт | силы | C | μ | PD/D | эталон | теги |
|---|---|---|---|---|---|---|---|---|---|
| F01 | `Far flight 30 AU e 0.5 (physical)` — `FarFlightCase` | far-flight | 1 год (min(period, year)) | gravity Солнца + third-body (Юпитер), `weak-acceleration` | true | Sun | 3/7 | certified local DOP853 (`CertifiedReferenceStepSeconds=86400`) + второй эталон | far-flight, heliocentric |
| F02 | `Heliocentric 1 AU e 0.5` — `HeliocentricProblem(solar, 1, 0.5)` | heliocentric | 1 год (min(period, year)) | gravity Солнца + third-body (Юпитер) | true | Sun | 3/7 | certified local DOP853 (`ReferenceStepSeconds=900`) | heliocentric, ecc-mid |
| F03 | `Small-body flyby vinf 50` — `SmallBodyFlybyProblem(50)` | flyby, малое тело | min(30 сут, 20·τ) | gravity малого тела `mu=1e9` + прилив Солнца | true | `1e9` | 3/7 | certified local DOP853 + второй эталон | flyby, close-encounter |
| F04 | `Moon orbit 100 km x1000 orbits` — `MoonOrbitProblem(solar, 100, 1000)` | moon-soi, long-arc | 1000 витков | gravity Луны + third-body (Земля) | true | Moon | 3/7 | certified local DOP853 + второй эталон | moon-soi, long-arc |
| F05 | `Moon hop 1000 m/s` — `MoonHopProblem(solar, 1000)` | «прыжок/переход» | `min(2v/g, 3600 с)` | gravity Луны + third-body (Земля), near-uniform | true | Moon | 3/7 | certified local DOP853 + второй эталон | moon-soi |

Исключённые сценарии Test559 записаны в `BACKLOG.md` (B8–B12) с причиной — A5.4.
F01–F05 не повторяют S01–S17 по орбите и силе: у F01/F02 есть возмущение Юпитера на
гелиоцентрике (S07/S08 — чистое поле Солнца), F03 — малого тела, F04/F05 — Луна
(S12 — пролёт, а не орбита/прыжок).

## A-Flags. Сводка флагов и сил новых задач

| группа | `ConservativeSecondOrder` | `CentralMu` | `PosDim/Dim` | сила зависит от скорости/массы |
|---|---|---|---|---|
| S01–S09, S12, S14, S15 | true | Earth / Moon / Sun > 0 | 3/7 | нет |
| S10 (drag) | **false** | Earth | 3/7 | скорость |
| S11, S17 (thrust) | **false** | Earth | 3/7 | скорость + масса |
| P2–P5 | true | 0 | 3n / 6n | нет |
| P6 | true | Sun | 3/6 | нет |
| F01–F05 | true | Sun / `1e9` / Moon | 3/7 | нет |

Проверка §8.3 (A4) для S10/S11/S17: методы, требующие `ConservativeSecondOrder`,
определяются автоматически (проба `Supports` на двух синтетических задачах) — это 9
методов: PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8. Все они
обязаны дать N/A на S10/S11/S17.

**Расхождение с A4 (записано в `FOUND.md` F10).** `Encke-IAS15.Supports` требует только
`CentralMu > 0`, `PosDim == 3`, `Dim >= 6` и **не** требует консервативной силы
(`StudyAdvancedIntegrators.cs:209`). Поэтому по коду Encke заявляет поддержку S10/S11/S17,
хотя A4 ожидает для него N/A. `Supports` не правится (§2, §8.2); проверка флагов S3 будет
сверять фактический ответ, а расхождение и вопрос владельцу записаны в `PROGRESS.md`.

## A-Goals. Метровые цели по задачам (A7)

| задачи | M100 | M10 | M1 | M0.1 | M0.01 |
|---|---|---|---|---|---|
| S01–S11, S14, S17 | да | да | да | нет | нет |
| S12, S15 | да | да | да | да | да |
| P2–P6 | да | да | да | нет | нет |
| F01, F02, F04, F05 | да | да | да | нет | нет |
| F03 (пролёт) | да | да | да | да | да |
| R1, R3, L1, R4 | да | да | да | да | да |

A1–A3 на S/P/F не вычисляются (A7.3); на R1/R3/L1/R4 остаются вместе с метровыми.
Порог G3 для метровой цели — `10 × (цель / ScaleLength)` (A7.2).

## A-Summary. Что именно переиспользуется

| нужно | готовое | как вызывается |
|---|---|---|
| S01–S17 | `StudyShipMatrixBuildCases` + `StudyShipMatrixBuildReferences` + `StudyBridgeShipProblem` | напрямую из `qualification/` (тот же partial-класс) |
| P2–P5 | `StudyRealSolar7`/`StudyCustomSystem`/`StudyTightBinarySystem`/`StudyEccentricSystem` + `StudyMakeNBody` + `StudyPlanetIndependentReference(Alt)` + `StudyBridgePlanetProblem` | тот же путь, что Test553 |
| P6 | `StudyHeliocentricSystem` + `StudyBridgeHeliocentricProblem` | тот же путь, что Test553 |
| F01–F05 | `FarFlightCase`/`HeliocentricProblem`/`SmallBodyFlybyProblem`/`MoonOrbitProblem`/`MoonHopProblem` + `BuildPureProblem` | тот же путь, что Test559 |
| R4 | `MoonFlybyProblem` | уже используется (M5) |
| цена/уровни/гейты | `QualificationEngine` | расширяется видом цели M (S1) |

Ограничение по времени: P2 (100 лет, два dd-Taylor) — самый дорогой эталон; S — дешёвые
(один fixed DOP853 на кейс). Это определяет профили: S и F — в smoke, P2–P6 — только full.

---

# ПРИЛОЖЕНИЕ B (B0): реальная Солнечная система и сравнение с Horizons

Результат этапа **B0** (TASK.md, Приложение B, B8). Код не менялся: только этот файл.
Дата инвентаризации: 2026-11-13. Commit: `05b7869` (`Q-FIX-20b`), рабочее дерево по коду чистое
(изменены только `TASK.md`/`AGENTS.md`/`CLAUDE.md` владельца).

## B-Data. Данные Horizons/DE441

| файл | что | эпоха (`epoch`) | размер | кто использует |
|---|---|---|---|---|
| `Study/jpl/horizons_real_2026_10y.json` | 483 тела, кадр `heliocentric ecliptic J2000`, единицы `m, m/s`; срезы `days = 0,365,…,3650` | `2026-01-01 00:00:00 TDB` | 1 221 121 Б | Test537/539/540/542/544/545/546, Test530/531/543/533/535, **R2** |
| `Study/jpl/horizons_real_2016_10y.json` | то же, второе окно (holdout) | `2016-01-01 00:00:00 TDB` | 1 183 339 Б | Test538, Test539, Test542 |
| `Study/jpl/horizons_short_2026.json`, `horizons_short_2016.json` | срезы 0/1/10/30/90/180/365 сут | те же | 14 КБ | Test539 (V0) |
| `Study/jpl/de441_asteroids.csv` | 343 номера главного пояса + точные GM DE441 (au^3/day^2), JPL IOM 392R-21-005 | — | 9 309 Б | только Python-построители данных; **C# его не читает** (массы уже в JSON) |

Состав `horizons_real_2026_10y.json`: 9 имён `ten` (`Mercury, Venus, Earth, Moon, Mars, Jupiter,
Saturn, Uranus, Neptune`) + центры `JupiterCenter`, `SaturnCenter` + 12 спутников
(`Io, Europa, Ganymede, Callisto, Mimas, Enceladus, Tethys, Dione, Rhea, Titan, Hyperion, Iapetus`)
+ 409 малых тел (`Ast*` 343, `Kbo*` 30, `Kbr*` 36) + прочие именованные малые тела (Ceres, Pallas,
Vesta и др., нужны Test533/535). Начальные состояния моделей берутся только из этого JSON;
сеть и SPICE не нужны.

## B-Access. Как вызывается `Study/jpl` из стенда

Все точки входа — `private static` члены `internal static partial class P1bTests`
(`Study/jpl/*.cs`). Стенд — тоже `partial class P1bTests`, поэтому вызовы идут **напрямую**,
без правок модификаторов. Вложенные `private`-классы `JplFile`, `JplBody`, `JplState`,
`JplCompareModel`, `IntegratorRow` доступны там же (члены одного типа).
**Правок `private` → `internal` для B1–B4 не требуется.** Если позже понадобится вызов
из другого типа — это будет отдельная аддитивная правка с записью в `PROGRESS.md` (B2).

| нужно | готовое (точка входа) | файл |
|---|---|---|
| прочитать JSON | `JsonSerializer.Deserialize<JplFile>` | `JplRealism.cs:27` |
| собрать 10-тельную систему (барицентр, системные GM) | `JplNBody(jpl, names, useSystemGm: true, barycentric: true)` | `JplRealism.cs:42` |
| собрать систему спутников (GM центра = GM Юпитера) | `JplNBody(..., centerWithSystemGm: true)` | `JplRealism.cs:44` |
| индекс тела по имени | `JplIndex(nb, name)` | `JplRealism.cs:167` |
| эталонный адаптивный интегратор | `JplDop853(nb, days, posAtol, velAtol, rtol, pn, eih, j2, maxStepSeconds, asteroidStart, earthJ2, planetJ2)` → `(p, v, calls, steps)` | `Jpl1Pn.cs:269` |
| силы | `JplAcc1Pn`, `JplAcc1PnC`, `JplAccEih`, `JplAccPlanetsAsteroids`, `JplAccEarthJ2`, `JplAccZonal*` | `Jpl1Pn.cs:9-267` |
| ошибка против Horizons | `JplStateError(...)` (тело и гео-Луна), `JplRelativeError(...)` (относительно центра) | `FullComparison.cs:36`, `:400` |
| ошибка против эталона той же модели | `IntegratorError`, `IntegratorBodyError`, `MoonRelativeError` | `IntegratorComparison.cs:24-48`, `FullSystemIntegrators.cs:228` |
| фиксированные шаги для слоя I (как в старых тестах) | `StudyRk4StepAcc`, `StudyDopri5FixedAcc`, `StudyPefrlAcc`, `StudyY6Acc` + делегат `StudyAccel` | `FullSystemIntegrators.cs:11-226` |

Модельный слой (V) и слой интеграторов (I) разделены уже в коде: `JplCompareModel`
(`FullComparison.cs:12`) — «имя + набор флагов силы», а `IntegratorRow` — «метод + шаг».

## B-Models. Модели Test537/538 (слой V)

`JplFullComparison` строит 8 моделей (`FullComparison.cs:106-116`); те же 8 в обоих окнах:

| # | имя | `pn` | `eih` | `j2` (солнечный) | `asteroidStart` | `earthJ2` | тел |
|---|---|---|---|---|---|---|---|
| 0 | `Newton` | нет | нет | нет | -1 | нет | 10 |
| 1 | `1PN` | да | нет | нет | -1 | нет | 10 |
| 2 | `EIH` | (внутри) | да | нет | -1 | нет | 10 |
| 3 | `1PN+J2` | да | нет | да | -1 | нет | 10 |
| 4 | `409small` | да | нет | нет | 10 | нет | 419 |
| 5 | `409small+J2` | да | нет | да | 10 | нет | 419 |
| 6 | `409small+EJ2` | да | нет | нет | 10 | да | 419 |
| 7 | `409small+J2+EJ2` (= `full409`) | да | нет | да | 10 | да | 419 |

Интегратор слоя V один: `JplDop853(..., 1e-6, 1e-9, 1e-13, ..., maxStep 86400 s)`; срезы 1, 5, 10 лет
(`horizons = 365/1825/3650`). Test544 добавляет `planetJ2` (J2/J4/J6 Юпитера и Сатурна, J2 лун)
и `maxStep 21600 s`; Test545 берёт 10-тельную систему и эталоны `rtol 1e-14` + `1e-15`/`maxStep 0.5 d`;
Test546 — `full409` (`asteroidStart 10`, `j2`, `earthJ2`) с эталоном той же силы.

## B-Tasks. Построители задач H1…H6

| id | задача | как строится (готовый путь) | эталон (та же модель/эпоха) | теги |
|---|---|---|---|---|
| H1 | DE441 10 тел, Ньютон, 2026–2036 | `JplNBody(2026, ten, true, true)` + `QualificationNBodyRhsNoCopy` (R2-код, Ньютон-ветка) | `QualificationCertifiedDop853` (fixed DOP853, 5 уровней, Richardson) | real-system, de441, many-body, long-horizon |
| H1h | то же, 2016–2026 | тот же код с `horizons_real_2016_10y.json` | то же | real-system, de441, many-body, holdout-epoch |
| (R2) | DE441 10 тел, 1PN, 2026–2036 | **уже есть** `QualificationCatalogRl.QualificationDe441Problem("R2", pn: true)` | fixed DOP853 5 уровней (floor `1.969 м`) | 1pn, velocity-dependent, long-horizon, many-body |
| H2h | DE441 10 тел, 1PN, 2016–2026 | тот же код, JSON 2016 | то же | real-system, de441, 1pn, velocity-dependent, holdout-epoch |
| H3 | 10 тел + 409 малых, `full409` | `JplNBody(2026, ten+Ast/Kbo/Kbr, true, true)` + `JplAccPlanetsAsteroids(..., pn:true, j2:true, planetMu)` + `JplAccEarthJ2` (ровно `acc` из Test546) | `JplDop853` с той же `acc` (Test546: rtol 1e-14/1e-15, maxStep 0.5/0.25 d) или `QualificationCertifiedDop853` по той же силе | real-system, small-bodies, 1pn, velocity-dependent, many-body |
| H6 | спутники из Test544 | `JplNBody(2026, 21 имя, true, true, centerWithSystemGm:true)` + `JplDop853(..., pn:true, earthJ2:true, planetJ2:true, maxStep 21600 s)` | тот же путь, эталон той же силы | real-system, satellites, many-body |

Совпадения с уже существующими задачами:

* **H2 = R2** (те же тела, та же сила, то же окно). Дублировать нельзя (B2): R2 остаётся R2,
  к нему добавляются H-цели M100…M1, цели Q и слой V (B3).
* **H1 ≠ R1.** R1 — фикстур `StudyRealSolar7` (7 тел, `BodyBlueprint`, без запечённых эфемерид),
  H1 — DE441 JSON (10 тел, барицентр, системные GM). Разные наборы тел и разные ИК.
* **H1h/H2h** — не дубли R1/H1: другое окно (2016–2026), holdout-эпоха.
* **H3** — сила `full409` из Test546 (419 тел); ни одна задача стенда её не повторяет.
* **H6** — 21 тело Test544; в стенде спутников нет.
* Пункт B2 «R2 (10-body 1PN, 2026) остаётся R2» выполнен: новая задача `H2` не заводится.

## B-V0. Предусловие V0: откуда берутся проверки

| проверка | готовый код | что проверяет |
|---|---|---|
| day-0 относительные состояния | `Test539_JplShortHorizon` / `JplStateError` на `horizons_short_*.json` | ошибка 0 м (и `4.7e-10 м` после комбинации GM/единиц) |
| начальные ускорения, отношение масс спутников Марса | `Test540_JplInitialAccel` (`JplAcc1Pn`, `JplAccEih`, `moonRatio ∈ [1e-8, 5e-8]`) | EIH−1PN по телам; `|d|` стартовых ускорений |
| согласованность эпох | `Test542_JplEpochConsistency` (GM обоих окон идентичны, day-0 состояния точны) | `worstGm < 1e-12`, `worst0 < 1e-3` |

V0 в стенде = те же три проверки, вызванные через адаптер на тех же данных; пороги берутся
из существующего кода (Test539/542), не изобретаются.

## B-G9. Эталон воспроизводимости: неизменённые Test537/538/544/545/546

Команды (запускались 2026-11-13, commit `05b7869`, `dotnet run --project Tools/P1bTests -c Release --no-build -- --exact <тест>`;
рабочий каталог закрепляется `Program.cs` в `Tools/P1bTests`):

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test537_JplFullComparison
dotnet run --project Tools/P1bTests -c Release -- --exact Test538_JplHoldout
dotnet run --project Tools/P1bTests -c Release -- --exact Test544_JplSatelliteComparison
dotnet run --project Tools/P1bTests -c Release -- --exact Test545_JplIntegratorComparison
dotnet run --project Tools/P1bTests -c Release -- --exact Test546_JplFullSystemIntegrators
```

Все пять — `ALL PASS`; полные логи лежат вне репозитория (временный каталог сессии), ниже —
числа, которые слой V обязан воспроизвести (G9: `|Δ| ≤ max(0.01 км, 0.5 %)` по каждому телу и модели).

### G9-1. Test537, 2026–2036: ошибка положения за 10 лет, км

| тело | Newton | 1PN | EIH | 1PN+J2 | 409small | 409small+J2 | 409small+EJ2 | 409small+J2+EJ2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Mercury | 1760.56 | 0.50 | 0.94 | 0.64 | 0.57 | 0.57 | 0.57 | 0.57 |
| Venus | 871.18 | 0.43 | 0.06 | 0.73 | 0.15 | 0.45 | 0.15 | 0.45 |
| Earth | 608.39 | 0.95 | 1.60 | 0.82 | 2.02 | 1.88 | 2.37 | 2.50 |
| Mars | 372.62 | 1.14 | 1.33 | 1.10 | 0.23 | 0.28 | 0.23 | 0.28 |
| Jupiter | 41.13 | 17.01 | 17.12 | 17.01 | 0.15 | 0.15 | 0.15 | 0.15 |
| Saturn | 3.77 | 10.41 | 10.43 | 10.41 | 0.50 | 0.50 | 0.50 | 0.50 |
| Uranus | 1.49 | 0.91 | 0.93 | 0.91 | 0.80 | 0.80 | 0.80 | 0.80 |
| Neptune | 6.84 | 6.47 | 6.49 | 6.47 | 1.58 | 1.58 | 1.58 | 1.58 |
| Moon(geo) | 178.05 | 177.15 | 222.67 | 177.15 | 177.15 | 177.15 | 68.08 | 68.08 |
| **RMS(8)** | **739.03** | **7.44** | **7.50** | **7.44** | **0.99** | **0.97** | **1.09** | **1.13** |

Время: 6 392 мс (parallel, jobs=32). Гейты теста: EIH−1PN по Юпитеру `0.11 км`, по Сатурну
`0.02 км`, 409small улучшает Юпитер (`0.15`) и Сатурн (`0.50`) — все PASS.
E_model для B6 берётся из этой таблицы: Newton 2026 `739.03 км`, 1PN `7.44`, full409 `1.13`,
Луна 1PN `177.15`, full409 `68.08`.

### G9-2. Test538, 2016–2026: ошибка положения за 10 лет, км

| тело | Newton | 1PN | EIH | 1PN+J2 | 409small | 409small+J2 | 409small+EJ2 | 409small+J2+EJ2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Mercury | 2925.33 | 1.16 | 1.76 | 0.76 | 1.20 | 0.73 | 1.20 | 0.73 |
| Venus | 898.94 | 0.03 | 0.06 | 0.33 | 0.15 | 0.15 | 0.15 | 0.15 |
| Earth | 610.86 | 2.53 | 2.90 | 2.63 | 1.97 | 2.02 | 1.52 | 1.57 |
| Mars | 246.03 | 4.13 | 4.00 | 4.17 | 0.13 | 0.17 | 0.13 | 0.17 |
| Jupiter | 46.84 | 1.06 | 1.05 | 1.06 | 0.32 | 0.32 | 0.32 | 0.32 |
| Saturn | 13.77 | 2.13 | 2.04 | 2.13 | 2.47 | 2.47 | 2.47 | 2.47 |
| Uranus | 0.99 | 0.52 | 0.49 | 0.52 | 1.01 | 1.01 | 1.01 | 1.01 |
| Neptune | 5.36 | 5.07 | 5.06 | 5.07 | 2.17 | 2.17 | 2.17 | 2.17 |
| Moon(geo) | 161.31 | 160.51 | 202.46 | 160.51 | 160.51 | 160.51 | 108.74 | 108.74 |
| **RMS(8)** | **1106.89** | **2.66** | **2.71** | **2.66** | **1.47** | **1.44** | **1.40** | **1.37** |

Время: 5 956 мс. E_model 2016 для H2h: 1PN `2.66 км`, Луна `160.51 км`.

### G9-3. Test544, спутники 2026–2036: ошибка на 10 лет

| тело | центр | dp (км) | dv (м/с) |
|---|---|---:|---:|
| Moon | Earth | 68.07 | 8.76E-02 |
| Io | JupiterCenter | 13 232.88 | 6.37E+02 |
| Europa | JupiterCenter | 14 121.51 | 4.19E+02 |
| Ganymede | JupiterCenter | 9 157.16 | 1.65E+01 |
| Callisto | JupiterCenter | 1 126.98 | 9.48E+00 |
| Mimas | SaturnCenter | 115 942.77 | 2.44E+04 |
| Enceladus | SaturnCenter | 374 187.37 | 1.34E+04 |
| Tethys | SaturnCenter | 390 562.83 | 1.22E+04 |
| Dione | SaturnCenter | 610 058.45 | 2.73E+03 |
| Rhea | SaturnCenter | 893 539.04 | 1.46E+04 |
| Titan | SaturnCenter | 124 324.70 | 6.02E+02 |
| Hyperion | SaturnCenter | 134 277.74 | 5.17E+02 |
| Iapetus | SaturnCenter | 7 419.71 | 7.72E+00 |
| JupiterCenter vs JPL | — | 266.29 | — |
| SaturnCenter vs JPL | — | 76.35 | — |

Время: 23 266 мс.

### G9-4. Test545, 10 тел, эталон той же силы

| строка | сила | шаг | calls (half+full) | ms | max err (км) |
|---|---|---|---:|---:|---:|
| DOP853 adaptive | newton | adaptive | 235 498 | 110 | 0.003 |
| DOPRI5 1 d | newton | 1 d | 32 852 | 13 | 67 092.078 |
| RK4 1 d | newton | 1 d | 21 900 | 7 | 737 155.445 |
| PEFRL 1 d | newton | 1 d | 21 900 | 7 | 19 859.878 |
| Y6 1 d | newton | 1 d | 38 327 | 11 | 5 534.424 |
| Y6C 1 d | newton | 1 d | 38 327 | 81 | 5 534.422 |
| DOPRI5 0.5 d | newton | 0.5 d | 65 702 | 23 | 1 259.895 |
| RK4 0.5 d | newton | 0.5 d | 43 800 | 11 | 120 665.215 |
| PEFRL 0.5 d | newton | 0.5 d | 43 800 | 12 | 1 246.554 |
| Y6 0.5 d | newton | 0.5 d | 76 652 | 19 | 88.442 |
| Y6C 0.5 d | newton | 0.5 d | 76 652 | 152 | 88.442 |
| DOP853 1PN | 1PN | adaptive | 235 498 | 122 | 0.001 |
| Y6C 1PN 112.5 s | 1PN | 112.5 s | 29 433 602 | 10 980 | 71.627 |

Эталонные floor'ы теста: Newton `6.85E-01 м` (Меркурий), 1PN `5.03E-01 м` (Луна).
`best Newtonian fixed-step: Y6 0.5 d`. Время: 11 779 мс.

### G9-5. Test546, `full409` (419 тел): интеграторы против эталона той же силы

Эталон: `DOP853 full409 rtol 1e-14`, 6 461 мс, 230 363 calls, self-convergence `1.07E+00 м`.
Ошибка модели (`same-force reference` vs Horizons): `RMS(8)=1.133 км`, Юпитер `0.150`, Сатурн `0.504`.

| строка | шаг | calls | ms | int err (км) | int rms8 (км) | total (км) | Юпитер | Сатурн | Moon(geo) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| DOP853 full409 | adaptive | 156 947 | 5 111 | 0.001 | 0.000 | 1.133 | 0.000 | 0.000 | 0.00 |
| DOPRI5 full409 | 1 d | 21 901 | 896 | 67 092.356 | 2 911.776 | 2 911.662 | 0.000 | 0.000 | 67 916.83 |
| RK4 full409 | 1 d | 14 600 | 615 | 584 807.869 | 206 768.344 | 206 768.572 | 0.000 | 0.000 | 409 829.08 |
| PEFRL full409 | 1 d | 14 600 | 636 | 42 945.580 | 15 194.718 | 15 194.504 | 0.173 | 0.006 | 20 239.81 |
| Y6 full409 | 1 d | 25 551 | 969 | 55 092.469 | 19 492.523 | 19 492.314 | 0.249 | 0.009 | 5 777.34 |
| DOPRI5 full409 | 0.5 d | 43 801 | 1 500 | 1 259.905 | 129.382 | 129.151 | 0.000 | 0.000 | 1 275.41 |
| RK4 full409 | 0.5 d | 29 200 | 1 039 | 120 665.649 | 6 525.046 | 6 525.314 | 0.000 | 0.000 | 122 150.97 |
| PEFRL full409 | 0.5 d | 29 200 | 1 044 | 19 418.264 | 6 870.396 | 6 870.184 | 0.086 | 0.003 | 1 323.58 |
| Y6 full409 | 0.5 d | 51 101 | 1 350 | 27 505.328 | 9 731.697 | 9 731.486 | 0.124 | 0.004 | 176.96 |

Время: 21 160 мс. Это и есть численная опора H3: сила, эталон и «модельная» ошибка
`1.133 км` получаются ровно этим путём.

## B-Summary. Что переиспользуется в Приложении B

| нужно | готовое | как вызывается |
|---|---|---|
| данные, ИК, GM | `horizons_real_2026_10y.json` / `_2016_` | `JplFile` + `JplNBody` |
| слой V (модели) | `JplCompareModel` + `JplDop853` + `JplStateError` | напрямую (B1–B2) |
| слой I (методы) | `QualificationEngine` (свип/гейты) + `StudyNBody`/`StudyProblem` адаптер | расширяется тиром H (B3–B4) |
| certified reference | `QualificationCertifiedDop853` / `CertifiedReference` | без изменений |
| H6 | `JplDop853(..., planetJ2: true)` (Test544) | напрямую (B4) |
| G9 | прогон неизменённых Test537/538/544/545/546 | числа записаны выше |
