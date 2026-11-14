# PROGRESS: Приложение D (динамический выбор интегратора в игре)

**TASK_D.md (v5) принят**, SHA256 `388f70f83fb28daba07c9b71cb0706795f0ec22f83db7d31908ace6471e07ffa`
(`Tools/P1bTests/Study/dynamic_policy/TASK_D.md`, заморожен, агент не редактирует).
Прочитаны также: `qualification/TASK.md` (v4, SHA256 `7779706cf649a4c7dfe1a12b34fcd9aee97084fe600c3c0ea44440d487db1b16`,
в первую очередь Приложение C), `qualification/PORT_REPORT.md`, `qualification/BACKLOG.md` (B14),
`qualification/FOUND.md` (F8, F13), `dynamic_policy/DECISION_TABLE.md`.
Папка `qualification/` заморожена: её файлы только читаются (исключения — D1 и D6, см. §4 TASK_D.md).

Ветка: `integrator-port/dynamic-policy`, создана от последнего коммита `integrator-port/wh-bm14a`
= `0b6334553cc4e0f7908d8fce5314cd3685eb3691` (`Q-C8: PORT_REPORT and full baseline for the WH-BM14A port`).
Слияние делает только владелец.
Дата старта: 2026-11-14.

**Текущий этап: D1 завершён. Остановка по §8.3: для Surface (R2) и Sun-coast (R3) доказательств в CSV нет.
D2 не начат — ждёт решения владельца (см. «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА (D1.c)» в конце).**

---

## Список этапов D0–D7 (копия TASK_D.md §7)

| этап | содержание | статус |
|---|---|---|
| **D0** | Ритуал и базовая линия (код не меняется) | **завершён 2026-11-14** |
| **D1** | Таблица решений и доказательства (код не меняется): сверка чисел по CSV, чистый повторный production-прогон, аудит Surface/Sun-coast, факты D1.d | **завершён 2026-11-14, стоп §8.3** |
| **D2** | Тесты на пробелы политики (T1–T6), только новые файлы тестов | не начат |
| **D3** | Исправления политики только по подтверждённому в D2 (+ наблюдаемость `LastSelectionReason`) | не начат |
| **D4** | Проверка веток через `VesselStep` (R1/R2/R3) → `dynamic.csv`, `DYNAMIC_REPORT.md` | не начат |
| **D5** | Сквозной сценарий смены режимов (поверхность → LEO → long-warp → тяга → спуск → касание) | не начат |
| **D6** | Регрессия §10 + итоговый `DYNAMIC_REPORT.md` + одна строка в `PORT_REPORT.md` | не начат |
| **D7** | Включение: агент ничего не включает, оформляет «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА» | не начат |

Правила этапов: один этап за раз; приёмка по списку этапа; коммит `Q-D<n>: …`; обновление этого файла.
После каждого этапа — регрессия §10: `Test551` на прежнем хэше, оба xUnit-набора, `Test561`,
`--compare` стенда, если менялся файл из `GameSources.props`; при выключенных флагах поведение побитово прежнее.

---

## D0. Ритуал и базовая линия — 2026-11-14

Commit этапа: `Q-D0` в ветке `integrator-port/dynamic-policy`; полный хэш —
`git log --format=%H -1 --grep=^Q-D0` (хэш не вписан в текст, чтобы правка этого файла не меняла его сама).
Код не менялся: `git diff` по `*.cs`, `*.csproj`, `*.props` пуст
(`git status` до коммита — только новый untracked каталог `Tools/P1bTests/Study/dynamic_policy/`).

### Что сделано (по пунктам D0)

1. **Ветка.** `git switch -c integrator-port/dynamic-policy` от `integrator-port/wh-bm14a`
   (`0b6334553cc4e0f7908d8fce5314cd3685eb3691`). HEAD новой ветки = тот же хэш.
2. **Файлы папки D.** Созданы `PROGRESS.md`, `BACKLOG.md`, `FOUND.md`, `INVENTORY.md` (раздел «D0»).
3. **Перепроверка таблицы §2 TASK_D.md по коду.** Все семь элементов подтверждены; ссылки
   «файл:строка» — в `INVENTORY.md` → раздел «D0». Расхождений с таблицей §2 не найдено.
   Дополнительно зафиксировано: ранний выход `ApplyIntegratorPolicy` срабатывает при
   `!EnableRegimeIntegratorPolicy && !EnablePortedIntegratorPolicy`, поэтому `EnableSunCoastPolicy=true`
   сам по себе ничего не включает (совпадает с комментарием `SimulationRunner` «Sun v1: both true»).
4. **Сборка и тесты.** Числа ниже; все совпали с ожидаемыми из TASK_D.md §7 D0.4.
5. **Хэши упорядоченных имён тестов** (метод `INVENTORY.md` папки Q, раздел C-Tests: имена из TRX,
   сортировка `StringComparer.Ordinal`, LF, UTF-8 без BOM, завершающий LF) — ниже.
6. **Наличие прогона.** `qualification/results/20261114T151403Z_e860827/` присутствует в рабочей копии
   (15 файлов: `runs.csv`, `matrix.csv`, `game_control.csv`, `chunks.csv`, `conditions.csv`,
   `oracle_audit.md`, `PRODUCTION_REPORT.md`, `compare.md`, `CONDITIONS_REPORT.md`, `COST_TABLE.md`,
   `METHOD_QUALIFICATION.md`, `RECOMMENDATIONS.md`, `real_system.csv`, `environment.txt`, `run.log`).
   Стоп-условие §8.1 не наступило.

### Числа базовой линии

Окружение: Windows 10.0.26200, AMD Ryzen 9 8940HX (16 ядер / 32 потока), .NET SDK 10.0.401,
целевой фреймворк `net8.0`. Дата прогона 2026-11-14.

| проверка | команда | результат |
|---|---|---|
| сборка стенда | `dotnet build Tools/P1bTests -c Release` | 0 ошибок, 16 предупреждений, 7.59 с |
| `GalilegoSimTests` | `dotnet test tests/GalilegoSimTests/GalilegoSimTests.csproj -c Release` | **60 / 60 passed**, 0 failed, 558 мс |
| `GalilegoPhysicsTests` | `dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj -c Release` | **283 / 283 passed**, 0 failed, 9 с |
| `Test551` | `powershell Tools\P1bTests\Study\qualification\baseline\verify_test551.ps1` | `lines=84 fails=0`, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` |
| `Test561` | `P1bTests.exe --exact Test561_IntegratorPolicy` | `ALL PASS`, 16 проверок, 96 мс |

Хэши имён тестов (базовая линия D0):

| набор | тестов | SHA256 |
|---|---|---|
| `GalilegoSimTests` | 60 | `cf88d38e6d55bd8e232b72066e20569a7ad9f87483741855e226b8118794a86e` |
| `GalilegoPhysicsTests` | 283 | `f491a27b3504c173c91d3c7a7c49a93592da1ac5b8b703a244995745b3ee37f7` |
| вместе (sim-блок, затем phys-блок, один LF-поток) | 343 | `1e50e0fc76a79c48fd839a415eb870b8865ab97bda843524611409c766797886` |
| вместе (sim-блок + пустая строка + phys-блок) | 343 | `a74221ba5c8e3ebfaf3fcec7b6ef103feea5754321431a4af6fa1b201ce4ae7a` |

Примечание: в `INVENTORY.md` папки Q для 44+279 имён указан один хэш «вместе» (`9913f91d…`); способ
склейки там не расшифрован. Для D0 записаны оба варианта, чтобы D2/D5 могли сравнивать однозначно.
Сортировка — **ordinal** (`StringComparer.Ordinal`), не культурная: `Sort-Object` даёт другой порядок
и другой хэш на тех же данных.

### Регрессия (§10 TASK_D.md)

* `Test551` — прежний хэш `54ae98e2…` (84/0).
* Оба xUnit-набора — 60/60 и 283/283, имена и число совпадают с `PORT_REPORT.md` §4.
* `Test561` — ALL PASS.
* Файлы из `GameSources.props` не менялись → `--compare` стенда в D0 не требуется.
* При выключенных флагах поведение не менялось: правок кода нет.

### Что не сделано

Ничего из списка D0. Работа D1 не начиналась.

### Вопросы к владельцу

Нет. Следующий этап D1 (в нём — чистый production-прогон, длительный) начинается после подтверждения.

---

## D1. Таблица решений и доказательства — 2026-11-14

Commit этапа: `Q-D1` в ветке `integrator-port/dynamic-policy`; полный хэш —
`git log --format=%H -1 --grep=^Q-D1`. Код игры не менялся (`git diff` по `*.cs`, `*.csproj`, `*.props` пуст).
`DECISION_TABLE.md`: заполнены только колонки «Ссылка (агент)» и «Итоговый статус (агент)» и раздел 4.

### D1.a. Сверка seed-чисел с CSV

Проверка выполнялась кодом (PowerShell + Python, скрипты в `%TEMP%\d1_*.ps1/py`, в репозиторий не добавлены:
D1 не меняет код). Формулы агрегатов взяты из `QualificationProductionReport.cs`
(`QualificationCommonGoal`: самый точный общий PASS-уровень метода и `Prod-DOP853`, затем геометрическое среднее
`prodPrice/methodPrice` по классу; `QualificationGameCallsRatio`: сумма RHS по 11 задачам; floor-aware порог
`max(1.5×Prod, floor)`).

Проверено по прогону `20261114T151403Z_e860827`:

| что | seed | по CSV | итог |
|---|---|---|---|
| ячейки | 833; PASS=746 FAIL=48 SKIP=39 | 833; 746/48/39 | совпало |
| WH-BM14A цена по классам | 5.73 / 1.11 / 0.47 / 0.26 | 5.7278 / 1.1065 / 0.4697 / 0.2569 | совпало |
| WH-BM14A game RHS | 0.49× | 0.49 | совпало |
| WH-BM14A GTO/Молния-only, HEO | 1.45×, 0.64× | 1.4521×, 0.6424× | совпало |
| WH-BM14A абсолютные ошибки | 7.94e-6 … 1.73e-2 м | 7.9365e-6 … 1.7346e-2 м | совпало |
| WH-BM14A floor / относительный гейт | 5.02e-6…9.52e-5; FAIL 2.8/1995/5.9/12.6/156× | 5.0210e-6…9.5212e-5; 2.818/1994.8/5.858/12.624/155.7× | совпало |
| WH-Y6 | 8.66 / 2.29 / 0.97 / 0.41; S03 433.8; S09 0.9099 | 8.6561 / 2.2948 / 0.9687 / 0.4086; 433.84; 0.90992 | совпало |
| RKN12(10) цена | 8.18 / 1.93 / 0.71 / 0.93; 2.48× GTO | 8.1792 / 1.9311 / 0.7055 / 0.9330; 2.4767× | совпало |
| IAS15 цена | 2.18 / 0.99 / 0.39 / 0.64 | 2.1826 / 0.9881 / 0.3874 / 0.6408 | совпало |
| GJ8 | leo 10.70, geo 5.82; 600 с S01 3.671e6, S03 5.379e7, S05 3.811e7, S14 1.150e8; control 110.9 с | 10.6974, 5.8234; 3.6706e6, 5.3795e7, 3.8110e7, 1.1495e8; 110.9 с | совпало |
| Prod-DOPRI5 | 0.86 / 1.09 / 0.60 / 1.48; game RHS 1.19 | 0.8614 / 1.0948 / 0.6000 / 1.4823; 1.19 | совпало |
| Prod-DOPRI5 ошибки flyby | 6.002e-5 / 2.552e-3 / 9.934e-4 м | 6.0024e-5 / 2.5516e-3 / 9.9344e-4 м | совпало |
| «самый дешёвый PASS» по задачам/целям | таблица §5 | пересчитана по `matrix.csv`, совпала во всех 65 строках | совпало |

**Расхождения** (внесены в `DECISION_TABLE.md` §4): числа `game_control.csv` для адаптивных study-методов
в старом прогоне сняты грязным деревом (протокол game_control появился после `e860827`:
`QualificationChunkTest.cs` +461 строка, коммит `7b3e7bb Q-C3`). Поэтому:
* IAS15 S04: было `3.2975e-6` (ниже floor `3.3902e-6` → PASS), стало `4.4209e-6` → **FAIL**; L1 `1.7328e-2` → FAIL;
* RKN12(10): S01 `2.371e-4` → `7.704e-5`, S02 `2.414e-4` → `7.039e-5`, S04 `3.283e-5` → `2.665e-4` и т. д. (все остаются FAIL);
* RKN12 wall 600 с: S02 `0.0827` → `0.0082` (стал PASS), S06/S12 остаются FAIL;
* IAS15 wall 600 с: диапазон `1.9–11×` → `1.47–3.57×` (хуже на всех 13 задачах).

Ни один из этих пунктов не меняет решения по строкам: R4/R10 — `OFF-BY-OWNER` (Q4/Q7 = НЕТ).
Пороги не менялись.

### D1.b. Чистый повторный production-прогон

* Папка: `qualification/results/20261114T193520Z_ff36340/` (13 файлов + отчёты, 15 всего).
* `environment.txt`: `profile: production`, `commit: ff36340cd5cd921e1cfef9cd09d07b754fde264b`,
  `branch: integrator-port/dynamic-policy`, **`dirty: clean`**, 17 методов, .NET 8.0.24.
* Длительность 948 031 мс; `833 matrix cells`, `PASS=746 FAIL=48 SKIP=39`; `game control: 221 ok, 0 aborted/error`;
  `chunks: 1105 ok, 0 error/aborted`; `ERROR = 0`.
* Сравнение с `20261114T151403Z_e860827` по §6 (скрипт, `matrix.csv` обеих папок):
  833 общих ячейки, **0 различий статусов**, **0 цен с отклонением > 10 %**, 0 пропавших ячеек. Гейт §6 пройден.
  `runs.csv` и `matrix.csv` двух прогонов совпали побитово (0 изменённых ячеек);
  различия есть только в `game_control.csv` (см. D1.a) и в таймингах `chunks.csv` (шум).
* Все числа для включения в таблице теперь ссылаются на новый прогон.

### D1.c. Аудит доказательств Surface и Sun-coast

Искали в репозитории (без внешних источников): `Study/integrators/results/cap_regime/Test563_*`,
`tests/GalilegoSimTests/IntegratorPolicyTests.cs`, `LiveChunkPolicyTests.cs`,
`Tools/P1bTests/Tests/IntegratorPolicyRuntime.cs`, `TouchdownEventIntegratorTests.cs`, все `*.csv` стенда,
коммиты `cdc9f18` (политика) и `cfd78a6` (порт WH).

**Surface (R2) — `NO-EVIDENCE`.**
* В production-профиле 13 задач (`S01,S02,S03,S04,S05,S06,S09,S14,S12,S15,R3,R4,L1`); ни одна не имеет
  6-DOF `FlightModelActive` и тяги. `chunks.csv` содержит строки 0.05 с для этих же 13 задач —
  это орбитальные чанки, не полёт; `game_control.csv` — те же 13 задач (221 строка).
* `Study/integrators/results/cap_regime/Test563_*` — про шаговый cap DOP853/DOPRI5 в LEO, Surface не покрывает.
* Единственные артефакты — тесты без CSV:
  `LiveChunkPolicyTests.cs:41-65` (500 м, 20 м/с², чанк 0.05 с, 10 с: policy vs DOP853 < 1e-6 м, RHS меньше);
  `IntegratorPolicyRuntime.cs:126-138` (`VesselStep`, 5 км, Flight, `StepFlying(1f)`: DOPRI5, 160 < 340 RHS, 2.842e-14 м);
  `TouchdownEventIntegratorTests.cs` (события посадки/SOI, DOPRI5 vs DOP853).
  По §5 п. 1 и 3 это не доказательство: нет CSV и нет проверки на игровых допусках `game_control.csv`.

**Sun-coast (R3) — `NO-EVIDENCE`.**
* Класса `heliocentric` в production-профиле нет (`PRODUCTION_REPORT.md` §«Classes»: «no task in the C4 production list
  (open question 10)»); в `chunks.csv`/`game_control.csv`/`matrix.csv` ровно те же 13 задач.
* `LiveChunkPolicyTests.cs:16-38`: 1 а.е., чанки 1/60 с (10 с) и 30 с (86 400 с) — DOPRI5, < 1e-2 м, RHS меньше.
* `IntegratorPolicyRuntime.cs:24-41`: 1 а.е. на 1/60 и 30 с → DOPRI5; `:71-84` — 5 а.е./cislunar-like → DOP853;
  `:86-92` — с тягой → DOP853.
* **600 с Sun-coast в репозитории не измерялся.** Комментарий `IntegratorPolicy.cs:93` ссылается на
  «the 30-50 s live chunk and the 600 s stress chunk»; 600-с теста нет — только 30 с.

### D1.d. Факты для владельца (только чтение)

1. **`J2 == 0`.** Все тела сцен и пресета: `OutdoorsScene.unity:426` и `:953` — `J2: 0` (два `BodyAuthoring`);
   в остальных трёх сценах `BodyAuthoring` есть, но поле `J2` не сериализовано → default 0.
   `ExampleSystemPresets.cs` J2 не задаёт (звезда, Планета А R=6371 км, её луна R=1737 км, Планета Б R=3390 км) — всё 0.
   **Орбит в диапазоне Ported у тел нет вовсе** — заданы только орбиты тел (Terra вокруг Sol, a=5.519e10 м и т. д.),
   а корабль стартует на поверхности (`SpawnOnSurface: 1` во всех четырёх сценах, `SpawnBodyName: Terra`).
   Terra: R=1 143 000 м, μ=1.28e13 → высоты 200/400/2000 км = r 1.343/1.743/3.743e6 м, v_circ 3087/2880/2018 м/с,
   v_escape у поверхности 4733 м/с, SOI 1.64e8 м. Дефолт кода `SpawnAltitudeMeters=200000` лежит ровно на нижней
   (включающей) границе ветки, но сценами не используется: `SpawnOnSurface=1`.
   `ContactZone`/`PlayerRoof`/`PlayerWalk`: `SpawnAltitudeMeters=200000`, `SpawnHorizontalSpeedMps=7800`;
   `OutdoorsScene`: 50 000 м и 3278 м/с (3278 ≈ v_circ на 50 км), но старт всё равно с поверхности.
2. **Гелиоцентрика и cislunar.** Солнце (`Sol`, μ=2.654e19) — корень всех четырёх сцен; Terra на 5.519e10 м.
   Гелиоцентрический coast ≥ 1e10 м геометрически возможен во всех сценах (нужно уйти за SOI Terra 1.64e8 м),
   но требует `EnableSunCoastPolicy` + `EnableRegimeIntegratorPolicy`, `CanCoast` и отсутствия тела рядом.
   **Луны в сценах нет** (только Sol + Terra); cislunar-режим в штатных сценах недостижим.
   `ExampleSystemPresets` (с луной) используется только `Tools/P1bTests` (`LandingTrace.cs`, `TestSupport.cs`), не сценами.
3. **Флаги в `.unity`.** Ни один из `EnableRegimeIntegratorPolicy`, `EnableSunCoastPolicy`,
   `EnablePortedIntegratorPolicy` не сериализован ни в одной сцене/префабе/ассете (`grep` по `Assets` — 0 совпадений).
   `SimulationRunner` присутствует во всех четырёх сценах, значения берутся из кода (`false`).
4. **Горизонт одного `AdvanceToTarget`.** `SimulationRunner.cs:568` → `StarSystem.BakedEndSeconds()`;
   `StarSystem.cs:711-724` возвращает минимум `Baked.EndSeconds` или `+∞`, если запечённых эфемерид нет.
   `Assets/StreamingAssets/Ephemerides` в репозитории отсутствует, `AttachFromStreamingAssets` (`SimulationRunner.cs:540`)
   ничего не прицепляет → **`BakedEndSeconds() = +∞`** в рабочей копии; игра пишет warning и считает на кеплеровых рельсах.
   Для D2 по §6 горизонт = 30 суток.

### Регрессия (§10 TASK_D.md)

* `Test551`: `lines=84 fails=0`, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` — прежний.
* `GalilegoSimTests`: 60/60; `GalilegoPhysicsTests`: 283/283 — прежние.
* `Test561_IntegratorPolicy`: ALL PASS.
* Код не менялся → `--compare` стенда не требуется; сам production-прогон и есть регрессия стенда (§6: 0 различий статусов и цен).

### Что не сделано

D2–D7 не начаты. `DYNAMIC_REPORT.md`, `dynamic.csv`, запрос D7 — этапы D4–D7.

---

## ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА (D1.c / §8.3)

**Пункт задания.** TASK_D.md §7 D1.c; §5 п. 1 и 3; §8.3; Q2 (§3).

**Что наблюдается.** Для Surface (R2) и Sun-coast (R3) в репозитории нет ни одной строки CSV стенда
и ни одной проверки на игровых допусках `game_control.csv` (в production-профиле 13 задач, все — орбитальные;
6-DOF/тяги и heliocentric среди них нет). Есть только тесты без CSV: `LiveChunkPolicyTests.cs:16-65`,
`IntegratorPolicyRuntime.cs:24-41,126-138`, `TouchdownEventIntegratorTests.cs`. Для Sun-coast 600-с измерения нет;
комментарий политики ссылается на него, но в репозитории его нет.
По формальному правилу §5 это `NO-EVIDENCE` для обеих веток. R2 — стоп по §8.3 (Q2 условный: «ДА только при
наличии доказательства»); R3 по Q3 и так `НЕТ`.

**Варианты.**

| # | что сделать | последствия | риск |
|---|---|---|---|
| **A (рекомендую)** | Зафиксировать `NO-EVIDENCE`; Q2 → НЕТ; Surface и Sun-coast остаются выключенными. Продолжить D2 (T1–T4, T6; T5 — WH-BM14A) и D4 только для R1 + отчёт R3 по Q3 | D-задание выполнимо без изменения TASK_D; включается только WH-BM14A (Q1) после D4–D6 | Surface-ветка остаётся мёртвым кодом в `IntegratorPolicy`; на игру не влияет (флаг false) |
| **B** | Владелец принимает существующие тесты (`LiveChunkPolicyTests`, Test561, `TouchdownEventIntegratorTests`) как доказательство для Surface; D4 R2 сгенерирует `dynamic.csv` как первую CSV-строку, ошибка/событие — против DOP853 | Q2 может стать ДА после D4, если D4 зелёный | это изменение §5 п. 1/3 (тест без CSV и без `game_control`); ошибка будет измерена против DOP853, а не certified reference — «1.5× пути DOP853» останется единственной метрикой |
| **C** | Добавить в стенд задачи Surface (6-DOF/тяга, 0.05 с) и heliocentric (30/600 с) и получить CSV | настоящее доказательство по §5 | это новое задание вне границ D (стенд заморожен, C4-список задач фиксирован); сроки D сдвигаются |

**Рекомендация.** Вариант A: он не меняет замороженные §5 и C4 и сохраняет главное правило «нет доказательства —
DOP853». Вариант B допустим только явным решением владельца с записью в TASK_D.md; вариант C — отдельная задача.

**До ответа:** D2 не начинаю.

