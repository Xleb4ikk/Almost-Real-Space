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
| T2 (Lagrange) | `KnownLagrangeTriangle(m1,m2,m3,periods,...)` | `scenarios/KnownProblems.cs:82` | точная формула поворота; не `StudyProblem` |
| T3 (Pythagorean) | `PythagoreanInput()` + `TaylorNBody`/dd | `taylor/Pythagorean.cs:6` | готовый вход и dd-эталон; не `StudyProblem` |
| R1, R2 (DE441, Newton/1PN) | **нет** `StudyProblem`; есть `JplFile`/`JplNBody` (`jpl/JplRealism.cs:11,37,42`) и `JplDop853` (`jpl/Jpl1Pn.cs:269`) | — | нужен адаптер в qualification (M5) |
| R3 (корабль у Земли) | `StudyBridgeShipProblem` + кейсы Test549 | `StudyBridge.cs:64`, `SpacecraftOrbitMatrix.cs:4187` | готово |
| R4 (пролёт Луны) | `MoonFlybyProblem` | `PureGravityCases.cs:275` | готово (`StudyProblem` с certified ref) |
| L1 (1000 витков LEO) | кейс `"Earth LEO 400 km, 1000 orbits (~64 d)"` + `StudyShipMatrixBuildReferences` | `SpacecraftOrbitMatrix.cs:4292`, `:930` | готово; golden — `Study/integrators/golden/ship_1000_orbits_dd.json` |
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
* SHA256 по нормализованным строкам `PASS `/`FAIL ` (без таймингов):
  `4de9ad9ceef68b7afa702dced5c08e902ecd8a86183f01632dfda7a34e847eb1`.
* SHA256 сырого лога (включает `ok in ... ms`, поэтому нестабилен между прогонами):
  `96dbff56d0f73a91b4667e820e4c58d9cf55e8c3ffe4d6d1d8856ad31f28e497`.

После любого изменения сравнивать **число PASS/FAIL и хэш нормализованных строк**, а не
сырой лог. По правилу TASK.md (раздел 2) Test551 обязан давать тот же результат после
аддитивных изменений; любые расхождения — стоп и разбор.

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
