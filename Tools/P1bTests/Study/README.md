# Карта стенда

Весь код стенда — `partial class P1bTests`, разложенный по папкам. Какой файл за что
отвечает:

## `core/`

`StudyConstants.cs` — `StudyYear`, `StudyAU`, `StudyShipT0`; общие типы
`StudyNBody`, `StudyRefResult`, `StudySample`.

## `fixtures/`

`StudyFixtures.cs` — `StudyBody`, `StudyRealSolar7`, `StudyCustomSystem`,
`StudyTightBinarySystem`, `StudyEccentricSystem`, `StudyHeliocentricSystem`,
`StudyMakeNBody`, `StudyShiftSubtree`. Здесь задаются системы и их начальные
состояния (включая барицентрическую поправку поддеревьев, как в `EphemerisBaker`).

## `framework/`

Независимое от движка ядро сравнения интеграторов (Test551–553):

| Файл | Что внутри |
|---|---|
| `StudyOde.cs` | `StudyProblem`, `StudyConfig`, `StudyRun`, `StudyContext`, сборка стоп-точек |
| `StudyIntegratorApi.cs` | `IStudyIntegrator`, базовые `FixedStepIntegrator` / `AdaptiveIntegrator` |
| `StudySteppers.cs` | RK4, DOPRI5, DOP853, PEFRL, Y6/Y6C, WH-Y6, leapfrog |
| `StudyAdvancedSteppers.cs` | RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 |
| `StudyAdvancedIntegrators.cs` | регистрация этих методов и правила `Supports` |
| `StudyIas15.cs` | ядро IAS15, общее для IAS15, старта GJ8 и внутреннего интегратора Encke |
| `StudyRegistry.cs` | список методов и выбор по `P1B_INTEGRATORS` |
| `StudySweep.cs` | прогон work-precision, `StudyOptions`, подсчёт стоимости |
| `StudyReport.cs` | таблицы, ничьи, CSV и Markdown |
| `StudySelfTest.cs` | Test551: порядок, стоимость вызовов, бюджеты, смоук-свип |
| `StudyBridge.cs` | единственный файл, знающий движок; корабли и планеты для Test552/553 |
| `KeplerUniversal.cs`, `Dop853Coefficients.cs` | универсальный Кеплер и таблица DOP853 |

Продвинутый набор (`RKN12(10)`, `SRKN14A`, `WH-BM14A`, `IAS15`, `GJ8`,
`Encke-IAS15`) портирован из автономного прототипа `AdvancedIntegrators`. `RKN12(10)`
и `SRKN14A` требуют консервативной второй производной; `GJ8` — того же и
постоянного шага (после старта IAS15 история пересобирается при смене `h`);
`WH-BM14A` дополнительно требует центральное тело в начале координат;
`Encke-IAS15` работает и с зависимостью силы от скорости/массы, но тоже требует
центральное тело. На чистой кеплеровской задаче `Encke-IAS15` не интегрирует
шумовой остаток возмущения, а сохраняет точный кеплеровский поток и поэтому
попадает в «below round-off floor», а не в рейтинг. `GJ8` — метод разностного
порядка 8; наблюдаемый порядок ошибки позиции на самотесте — 10, он и указан в
колонке `ExpectedOrder`.

Стоимость в отчётах — RHS-эквивалентные вызовы, **логарифмически интерполированные**
между ближайшей более дешёвой точкой выше цели и первой точкой ниже цели; наружу не
экстраполируется. Это убирает квантование сетки (`h` ~1,5, `rtol` ~sqrt(10)). Интерполяция
разрешена только если точки брекета отличаются по цене не более чем в 1,5 раза, а
локальный порядок попадает в ±30 % от заявленного; adaptive-методы используют
непрерывный `rtol`, fixed-step методы без объявленного порядка (WH-Y6/WH-BM14A/Encke)
не интерполируются вообще — их цена всегда первая реально прошедшая цель точка.
Для fixed-step локальный порядок считается по отношению `steps`, а не по номинальному
`h` (реальный шаг `h_eff = span/n`). В отчёте такие клетки без `*`, интерполированные —
с `*`. Полоса ничьей расширяется до ширины grid-шага worst first-hit клетки, чтобы
adaptive-цена не выглядела лучше fixed-цены только из-за интерполяции. Для
корабля цена вызова умножается на 3 (`P1B_RHS_MULTIPLIER` переопределяет это число);
планеты считаются по 1. Вес одного Kepler-решения в RHS-цене фиксирован (1), поэтому
повторные прогоны сравнимы; отдельно измеренное wall-clock (`ms`) даёт вторую таблицу
победителей, потому что Kepler/aux-работа в реальном времени стоит не ровно 1 RHS.
Значения `ms` меньше ~1 мс — шум таймера; для детерминированного рейтинга используйте RHS.
Методы в пределах 10 % от лучшего помечаются `~` как ничья для интерполированных
клеток; если интерполяция невозможна, полоса ничьей равна одному шагу сетки. Ничьи
между одним методом и его же cap-вариантом не считаются (это конфигурация, а не другой
метод). В «Who wins where» cap-вариант показывается отдельно только тогда, когда он
реально решает результат; иначе победитель называется по имени метода.

`EDGE` от cap-ограниченного прогона считается разрешённым; `EDGE` без cap-признака
помечается `unresolved`. Кроме того, если non-cap EDGE-соперник не дешевле победителя,
но его верхняя оценка в пределах 25 % от цены победителя, победитель тоже помечается
`unresolved` — иначе «≤912 против 912» выглядел бы решённым. У fixed-step методов фактический шаг ограничен checkpoint-
интервалом (`h_eff = span/n`, n >= 1), поэтому «более грубый h» сверх интервала
не меняет прогон; такой EDGE остаётся `unresolved`, а не выдаётся за результат.
Цель, которая меньше `10 x reference floor`, пропускается (колонка `skip`), чтобы
не мерить шум эталона.

Если цель или достигнутая ошибка прогона лежат ниже его собственного шума округления
(`16 eps * ScaleLength * sqrt(steps)`), клетка помечается `below round-off floor` и не
участвует в рейтинге. Сценарии с несертифицированным эталоном в сводку не попадают:
они перечислены отдельной строкой «Not ranked» и показаны только в деталях. Отказ
адаптивного/сплит-прогона записывает причину (пол шага, нефинитное состояние,
разрушение пары тел, несходимость Kepler-дрейфа), поэтому «no finite samples» больше
не является единственным объяснением. На планетах адаптивная норма ошибки меряется
по локальному масштабу пары (расстояние до ближайшего тела и его круговая скорость),
а не по барицентрическому радиусу, иначе `rtol` означал бы разное для разных тел.

Профиль тяги в игре: `SpacecraftBuilder` всегда собирает 6-DOF `VesselFlightModel`
и вызывает `SimulationRunner.SetVesselFlight`, который заменяет прямой stage-thrust
на flight source. В `VesselFlightModel.Prepare` уровень двигателя подходит к цели
через `ThrottleRatePerS * dt` (у двигателей 1.5–10 /с), поэтому резкой отсечки
тяги в штатном полёте нет. Рамп 1 с в сценарии «unaligned burn» — консервативный
production-like режим; сценарий с `ramp = 0` был бы стресс-тестом разрыва, а не
сравнением порядка методов.

## `methods/`

Старый (Test549) набор по одному файлу на метод/семейство:

| Файл | Что внутри |
|---|---|
| `Rk4.cs` | `StudyRk4Step` — RK4 |
| `Dopri5.cs` | `StudyDopri5Fixed` — DOPRI5, фиксированный шаг |
| `Pefrl.cs` | `StudyPefrl` — PEFRL |
| `Yoshida6.cs` | веса Ёсиды, `StudyY6` и `StudyY6C` (compensated) |
| `Dop853Tableau.cs` | таблица DOP853 (Hairer/Wanner) |
| `Dop853Ship.cs` | `StudyShipDop853`, `StudyShipDop853Local` |
| `FixedShip.cs` | `StudyShipFixed`, `StudyShipFixedLocal` (Y6/PEFRL для корабля) |
| `WisdomHolman.cs` | `StudyKeplerDrift`, `StudyWhStep*`, `StudyShipWh*` |
| `GamePath.cs` | `StudyShipGame`, `StudyCloneBakedSystem` — реальный игровой путь |
| `StepDispatch.cs` | `StudyStep` — диспетчер методов по имени |

## `references/`

| Файл | Что внутри |
|---|---|
| `NBodyReference.cs` | `StudyAcc`, `StudyAccC` (compensated), `StudyAdd` |
| `RefRun.cs` | `StudyRefRun`, `StudyRel`, `StudyEnergy` |
| `DoubleDouble.cs` | `StudyDd`, `StudyDd3`, `StudyForceDd` — dd-арифметика |
| `ShipReferences.cs` | `StudyShipRefA`, `StudyShipRefLocal`, `StudyShipY6CDd` |

## `scenarios/`

| Файл | Что внутри |
|---|---|
| `Planets.cs` | `StudySystemTable`, `StudyPlanetTable`, `StudyBake`, `StudyBakeStep` |
| `Ships.cs` | `StudyShipOrbit`, `StudyLocalAcceleration*`, `StudyShipTable`, `PrintShipRow` |
| `Events.cs` | `StudyLiveCost`, `StudyEvent`, `StudyShipFixedStep` — warp/событие |
| `ReferenceCases.cs` | `StudyReferenceCase`, `StudyKeplerReference`, `StudyTimeReversal`, `StudyPlanetReference` |
| `KnownProblems.cs` | `Test505_KnownProblems` (Lagrange/figure-eight/Аренсторф, double+dd) и `Test506_KnownConvergence` (оценка K-порядка) |

## `tests/`

| Файл | Тест |
|---|---|
| `Test500.cs` | `Test500_IntegratorStudyFast` — планеты 10 лет, 6 сценариев корабля, бейк, warp |
| `Test501.cs` | `Test501_IntegratorStudySlow` — 100 лет |
| `Test503.cs` | `Test503_ReferenceConvergence` — не в исходном реестре, доступен как `--reference` |

## `taylor/`

`TaylorReferenceTests.cs` — `Test504_TaylorReference` (Taylor vs Y6C/Y6/RK4/DOP853),
`ForceVsProduction.cs` — `Test519_ForceVsProduction` (сила Taylor/dd/local против
производственной), плюс пробы бюджета dd и Chebyshev.

## `TaylorVsY6/`

| Файл | Что внутри |
|---|---|
| `Compare.cs` | CLI-режимы `--real/--custom/--ships/--kepler/--y6c/--dd-*`; Taylor vs Y6 |
| `TaylorDd.cs` | dd-джеты: старая реализация, арена/in-place (`DdView`/`DdArena`), self-check, сравнение реализаций |

## `integrators/` и `jpl/`

Старые матрицы Test549/550 (`SpacecraftOrbitMatrix`, `CertifiedReference`,
`JplRealism`, `FullComparison` и т. д.). Они по-прежнему используют собственный
cost-модель и собственные эталоны; новые сравнения делаются в `framework/`.
Сводная методика теста орбит и итоговый выбор интегратора —
`integrators/ORBIT_INTEGRATOR_TEST_SCHEME.md`.
`Test552/553` — широкий свип, `Test554` — Луна/SOI, гипербола, 30 суток и
1000 витков на целях до 1 см; `Test555` — парашют/взлёт/suicide burn/вход;
`Test556` — work-precision кривые; `Test557` — production-чанкинг;
`Test558` — J2/наклонение и normalized Sun; `Test559` — чистая гравитация
(Луна/гелиоцентрика/пролёты) и карта режимов. `Test555`/`Test557` — ARCHIVED.
`Test554_Production.csv` — фактический
production-local DOP853 при 600-с чанках.
Поле `WorldChunkSeconds` относится только к production-пути Test549 (чанки
локального кадра); `framework/` его не читает, поэтому в Test552 у сценария
«Earth LEO 400 km, 30 d» больше нет пометки про world chunk.

## Как добавить новый метод

1. Написать stepper (`IFixedStepper` или `IAdaptiveStepper`) в `framework/StudySteppers.cs`
   либо в отдельном файле. Силу вызывать только через `ctx.Rhs(...)`.
2. Унаследоваться от `FixedStepIntegrator` / `AdaptiveIntegrator`, заполнить `Name`,
   `Family`, `ExpectedOrder` (fixed), `Supports(...)`, `CreateStepper(...)`.
3. Добавить экземпляр в `StudyRegistry.All()`.

Свип, стоимость, отчёты и CSV менять не нужно. Пример — `StudyExampleIntegrator.cs`.

## Как добавить сценарий

1. Для корабля — новый case в `StudyShipMatrixBuildCases` (`integrators/SpacecraftOrbitMatrix.cs`).
2. Для планет — фикстура в `fixtures/StudyFixtures.cs` и строка в
   `Test553_IntegratorFrameworkPlanetSweep` (`framework/StudyBridge.cs`).
3. Если нужен новый тип задачи (другая размерность/метрика), собрать `StudyProblem`
   в `StudyBridge.cs`: `Rhs`, `CheckpointTimes`, `CheckpointRef`, `Floor`, `CentralMu`,
   `ConservativeSecondOrder`, `ScaleLength`, `CharTime`.

## Запуск

```
dotnet build -c Release Tools/P1bTests/P1bTests.csproj
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test551_IntegratorFrameworkSelfTest
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test552_IntegratorFrameworkShipSweep
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test553_IntegratorFrameworkPlanetSweep
```

Фильтры: `P1B_SCENARIOS` (корабли), `P1B_PLANET_SCENARIOS` (планеты),
`P1B_INTEGRATORS` (список методов), `P1B_RHS_MULTIPLIER` (цена RHS).
`P1B_PLANET_YEARS` оставлен как быстрый шорткат: только реальная солнечная система
на указанном горизонте.

## Движок

`Assets/_Project/Scripts/` — настоящий код проекта (не копии). Ядро интегратора:
`Core/Integration`, `Spacecraft/Dynamics`, `Simulation/Events`, `Simulation/Warp`,
`Ephemeris`, `Universe`, `Core/Orbits`, `Core/Math`. Остальное — compile-closure
(террейн, декор, flight, solver). Менять их в пакете можно, но это уже не «стенд»,
а реальный движок.
