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

- Сценарии S1–S6, каждый прогон дважды; повторы побайтно идентичны
  (внутренняя проверка инструмента, `LANDING TRACES: ALL REPEATS IDENTICAL`).
- Мир (эталонный): Terra (μ=1.28e13, R=1.143e6, вращение 86400 с), рельеф
  EarthLike_Perlin (seed 24334543), атмосфера 100 км/ρ0=1.225/H=8500,
  корабль 5000 кг, старт lat=0 lon=0. Drag Cd=1: S1–S5 — A=10 м²
  (как `DragSource` в `SimulationRunner.Awake`), S6 — испытательный купол
  A=4000 м² (в игре купола пока нет).
- Колонки: `t, alt, v_radial, v_tangential, regime, lat, lon, event`;
  скорости — относительно со-вращающейся поверхности; событие — отдельной
  строкой в точный момент.

SHA-256 (ревизия 2, файлы `baseline/traces/`, канонические LF-байты):

```
S1_drop2km.csv    B87F47FBD3D91D5818A014063EBB8C7529D63D7B37A95B716258187A8221D40A
S2_drop100.csv    A8F9780E116DF45B7DCA9A51A44AEA585B97EFE5FBF9E0F49E37B23A243B65DD
S3_impact50.csv   19EE2BE54D16842496C12466CB39C3C4034630CC437A52F03DBBB4E8A04109E1
S4_entry.csv      1689BFB5B665DE609C5E565BE16A86F21E0B999E96CB841D403E8EC6F3ED9DFE
S5_stand.csv      7366EA394A24E158D26C3F1CED39E67FEDC5CD0D468092DA2CE63221B98623BA
S6_chute_soft.csv BAB9938771E356580A6C7CCE12D75CFDAC6EAEB68DF695D9D30E814A1699132A
```

**Ревизия 2 (2026-10-08).** В ревизии 1 писатель трейсов смешивал EOL:
заголовок через `AppendLine` (CRLF на Windows), данные через `\n`; git с
`* text=auto` нормализовал байты при checkout, и хэши ревизии 1 не
воспроизводились в срезе. Исправлено: писатель пишет только `\n` (включая
`summary.txt`), в `.gitattributes` добавлено `baseline/** -text`; эталон
пересснят на эталонной машине (Windows). Канонические LF-байты S1–S5 побайтно
совпали с legacy-блобами из git — паритет «до/после 0.4» подтверждён заново.
Хэши ревизии 1 недействительны.

Эталон текущего поведения: S1/S2/S3/S4 — разрушение (10 обломков, `Destroyed`),
S5 — стоянка `Landed` 3600 с без дрейфа, S6 — мягкое касание (`v_n=-4.470`,
`Landed`, без обломков). Мягкой посадки в S1–S4 нет и быть не может: тормозит
только `DragSource` без парашюта; мягкое касание ловит только S6 (см.
`baseline/parity.txt`).

Изменения по приёмке фазы 0 (обратная связь):
- `S2_descent2` переименован в `S2_drop100` — это свободное падение со 100 м
  (−41.7 м/с к касанию), а не контролируемое снижение; содержимое CSV не
  изменилось, legacy-копия переименована.
- Добавлен `S6_chute_soft` — купольный спуск с 5 км, купол A=4000 м²;
  касание `v_n=-4.470 м/с` (≤5) → `Landed`. Регрессии по ногам и мягкому
  касанию теперь ловятся baseline до фазы 3.
- Политика платформенных сравнений — `baseline/README.md`: побайтный гейт
  только на эталонной машине, в CI/на других ОС числовое сравнение с допуском.

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
4. Трейсы S1–S6 воспроизводимы побайтно — **да** (двойные прогоны;
   паритет «до/после 0.4» по S1–S5 — `baseline/parity.txt`; канонические
   LF-байты, `baseline/** -text`; ревизия 2 после исправления EOL).
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
- Платформенные хэши: дрейф `Test29` на Linux 6.52e-5 м против 1.96e-4 м на
  Windows (порог 0.01 м выполняется в обоих случаях). Test29 не использует
  WIP-файлы ветки — расхождение платформенное. Политика — `baseline/README.md`.
- Фаза 1: после смены `DryMassKg` на 3000 пересобрать S1–S6 и подтвердить,
  что при выключенной тяге трейсы не изменились (сравнение числовое, не по
  хэшам, если сборка не на эталонной машине).
- Эталонный мир трейсов зафиксирован: Terra из сцены (μ=1.28e13, R=1.143e6),
  а не Земля из первоначального плана; дополнение внесено в ТЗ
  (`promtII/ksp_collision_spec_v2.md`).
