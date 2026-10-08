# Фаза 0 — отчёт (шаги 0.1–0.7 ТЗ v2)

Ветка: `feature/contact-physics` (от `main` @ bf37492; WIP пользователя перенесён
коммитом 4110e87). Все шаги выполнены, ни один кейс не пропущен, падений нет.

## 0.1 Очистка

- Из индекса удалены логи `Tools/P1bTests`: `t60err.txt`, `t60out.txt`, `t60out2.txt`,
  `t60out3.txt`, `t60out4.txt`, `probe60out.txt`.
- Удалены `bin/`, `obj/` (стенд и xunit) и пустой `Tools/DevEval2/`.
- `.gitignore`: возвращён трекинг `tests/` (каталог был закрыт целиком — новые
  тестовые файлы молча не добавлялись бы), добавлено исключение для
  `tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj`, добавлен
  `/shelf-fix-dump.txt` (диагностический дамп `Test903_ShelfFixAb` пишется в CWD).
- xunit-проект (`GalilegoPhysicsTests.csproj`) теперь отслеживается git.

## 0.2 Разделение DecorExclusion (перенос без изменения поведения)

- Новый `Assets/_Project/Scripts/Planet/Decor/DecorExclusionCore.cs`:
  `DecorExclusionData`, `Empty`, `IsExcluded`, `TableEqual`, `Same`, `Release`
  и partial-хук `static partial void AfterRelease();`.
- `DecorExclusion.cs` — тот же `partial class`, игровая часть (`Apply`, `TryBuild`,
  `FlushRetired`), `AfterRelease → FlushRetired(true)`. Порядок
  `Dispose → Empty → AfterRelease` сохранён. Мета существующего файла не тронута.
- Файлы из `promtII/`, приведены к CRLF.

## 0.3 Манифест стенда

- Из `Tools/P1bTests/P1bTests.csproj` убраны 14 игровых файлов: `SkyEnvironment`,
  `SurfaceFrame`, `SimulationRunner`, `PlanetAtmosphereView`, `SunBillboard`,
  `UnderwaterEffect`, `FirstPersonCamera`, `Interactables`, `PlayerController`,
  `PlayerView`, `BodyView`, `DebrisView`, `ShipView`, `StarFieldView`.
- Добавлен `DecorExclusionCore.cs`. Стенд собирается без стабов физических типов:
  `dotnet build Tools/P1bTests` — 0 ошибок.
- `SimulationRunner` (исключён из стенда) дополнительно проверен headless-компиляцией:
  временный проект со ссылкой на `P1bTests.dll` и стабом `SiteBoxRegistry` —
  0 ошибок (проект вне репозитория, в `%TEMP%`).

## 0.4 Вынос логики шага (без изменения поведения)

- Новый `Assets/_Project/Scripts/Simulation/VesselStep.cs`: `StepFlying`,
  `StepLanded`, `HandleOccurrence`, `ApplyBreakup`, `NormalizeAssembly`; владеет
  `Ship`, `Regime`, `DominantBody`, `TimeSeconds`, Кахен-аккумулятором,
  `RawThrottle`; хук наблюдаемости `OccurrenceHandled` (для трейсов и будущих фаз).
- `SimulationRunner` — тонкая обёртка: ввод, игрок, `FloatingOrigin`, обломки.
- Паритет: S1–S5 через временный слепок старого кода и через `VesselStep`
  совпали побайтно по всем CSV (см. `baseline/parity.txt`), summary отличается
  только строкой-ярлыком источника.
- Временный слепок (`Tools/P1bTests/LegacyVesselStep.cs`) удалён после проверки
  (коммит eff735d); трейс-инструмент работает только через `VesselStep`.

## 0.5 Baseline тестов

| Набор | Кейсов | PASS-проверок | FAIL | Время |
|---|---|---|---|---|
| `dotnet test` (xunit) | 10 | 10 | 0 | 0.47 с |
| стенд `--fast` | 123 | 293 | 0 | 616 с |
| стенд `--slow` | 36 | 47 | 0 | 2433 с (сумма по кейсам 2428 с) |

- Логи: `baseline/xunit.txt`, `baseline/fast.txt`, `baseline/slow.txt`;
  каждая строка кейса — `[Name] ok in N ms` (per-case время).
- Slow прогнан 5 частями по диапазонам `--from/--to`; выполнены все 36,
  «не выполнено» нет. Падений нет, пороги не подстраивались.
- Окружение: Windows, .NET SDK 10.0.401, runtime 8.0.24, 32 логических ядра;
  nuget.org доступен (restore xunit прошёл).

## 0.6 Трейсы посадки

- Сценарии S1–S5, каждый прогон дважды; повторы побайтно идентичны
  (внутренняя проверка инструмента, `LANDING TRACES: ALL REPEATS IDENTICAL`).
- Мир: Terra (μ=1.28e13, R=1.143e6, вращение 86400 с), рельеф EarthLike_Perlin
  (seed 24334543), атмосфера 100 км/ρ0=1.225/H=8500, `DragSource` Cd=1 A=10,
  корабль 5000 кг, старт lat=0 lon=0.
- Колонки: `t, alt, v_radial, v_tangential, regime, lat, lon, event`;
  скорости — относительно со-вращающейся поверхности; событие — отдельной
  строкой в точный момент.

SHA-256 (файлы `baseline/traces/`):

```
S1_drop2km.csv   5E02E4EDB33F62AD87FA25E4376BE7944C6F5DA5AED6B0A6756CB2A0ECEECCD0
S2_descent2.csv  89941E70AA22D6FB27959DB9AED2F1E747421B07D5A2CA7EB46411A436DFD493
S3_impact50.csv  78DE0B789AC38074E8F97078692AA495861422405F3B68A5FE1084DEBBF50325
S4_entry.csv     149DFA233C7286DB370098F61795C9646EAC710A8AE9A54D7C26A9E8FD2707BD
S5_stand.csv     1BD21C1246B9B624DA7903E2E054B10AAD3EDFFC002E8708D87160B2022A15CD
```

Эталон текущего поведения: S1/S2/S3/S4 — разрушение (10 обломков, `Destroyed`),
S5 — стоянка `Landed` 3600 с без дрейфа. Мягкой посадки в S1–S4 нет и быть не
может: тормозит только `DragSource` без парашюта (см. `baseline/parity.txt`).

## 0.7 Изменённые файлы фазы 0 (от коммита WIP 4110e87)

Продакшн:
- `Assets/_Project/Scripts/Planet/Decor/DecorExclusion.cs` (M, 0.2)
- `Assets/_Project/Scripts/Planet/Decor/DecorExclusionCore.cs` (+.meta, 0.2)
- `Assets/_Project/Scripts/Simulation/VesselStep.cs` (+.meta, 0.4)
- `Assets/_Project/Scripts/Simulation/SimulationRunner.cs` (M, 0.4)

Инструменты/инфраструктура:
- `.gitignore`, `Tools/P1bTests/P1bTests.csproj`, `Tools/P1bTests/Program.cs`
- `Tools/P1bTests/LandingTrace.cs` (новый, трейс-режим `--trace <dir>`)
- `tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj` (взят под git)
- удалены 6 логов `Tools/P1bTests/t60*/probe60out.txt`
- `baseline/` (логи, трейсы, parity, этот отчёт)

## Гейт фазы 0

1. Стенд собирается без стабов физических типов — **да** (0 ошибок).
2. `dotnet test` и `--fast` дают зафиксированный baseline — **да**
   (10/10; 123 кейса, 293 PASS, 0 FAIL).
3. `--slow` завершён — **да** (36/36, 0 FAIL, ни одного «не выполнено»).
4. Трейсы S1–S5 воспроизводимы побайтно — **да** (двойные прогоны +
   паритет «до/после 0.4»).
5. В коде физики нет изменений, кроме 0.2 (чистый перенос) и 0.4 (вынос
   без изменения поведения) — **да** (`DecorExclusion*.cs`, `SimulationRunner.cs`,
   `VesselStep.cs`).

## Замечания и переносы

- `DryMassKg` в сцене = 1000, §8 ТЗ требует 3000 — это данные тестового
  аппарата, изменение отложено к фазе 1 (в шагах 0.1–0.7 его нет).
- WIP пользователя (SiteBox, сцена, префаб, архив, `promtII/`) перенесён на
  ветку коммитом 4110e87 и в изменения фазы 0 не входит.
- `Test903_ShelfFixAb` пишет `shelf-fix-dump.txt` в CWD — файл удалён,
  шаблон добавлен в `.gitignore`.
- Мета `VesselStep.cs` создана вручную; мета `DecorExclusion.cs` не менялась
  (GUID сохранён).
- Slow-категория в стенде не отражает время: `Test29_DescentChain` помечен slow,
  но выполняется за 135 мс; самые долгие кейсы — в fast-наборе (Test135 ~190 с).
  Это зафиксировано, но категории не менялись (вне рамок фазы 0).
