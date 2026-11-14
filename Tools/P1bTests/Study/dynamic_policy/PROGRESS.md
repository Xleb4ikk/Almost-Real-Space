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

**Текущий этап: D0 завершён. Следующий — D1 (не начат).**

---

## Список этапов D0–D7 (копия TASK_D.md §7)

| этап | содержание | статус |
|---|---|---|
| **D0** | Ритуал и базовая линия (код не меняется) | **завершён 2026-11-14** |
| **D1** | Таблица решений и доказательства (код не меняется): сверка чисел по CSV, чистый повторный production-прогон, аудит Surface/Sun-coast, факты D1.d | не начат |
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
