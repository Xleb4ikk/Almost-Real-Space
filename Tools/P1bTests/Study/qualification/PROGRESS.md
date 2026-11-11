# PROGRESS: Test564_MethodQualification

Журнал ведёт агент. Сначала читается `TASK.md` (он заморожен), потом этот файл.
Правило: после каждого значимого шага обновлять этот файл, а не держать состояние в памяти.

## Текущий этап

**M2: Каталог K и T + аудит oracle (G8)** (M1 завершён 2026-11-11)

## Чек-лист этапов

- [x] M0 Инвентаризация (`INVENTORY.md`, код не меняется)
- [x] M1 Скелет теста и профили smoke/full
- [ ] M2 Каталог K и T + аудит oracle (G8)
- [ ] M3 Support matrix + гейты G1, G2, G5, G6
- [ ] M4 Гейты G3, G4, G7, цена, уровни A1–A3, **заморозка порогов**
- [ ] M5 Тиры R и L
- [ ] M6 Отчёты и `--compare`
- [ ] M7 Полный прогон, baseline, Definition of Done

## Заморозка порогов (заполняется в конце M4)

- Дата / commit: _не заморожено_

## Принятые решения

_Формат: дата, этап, решение, причина (одна-две строки). Только то, что не следует прямо из TASK.md._

- 2026-11-11, M0: работа ведётся в ветке `qualification/test564-method-qualification`
  (создана от `main`, commit `1dff9e6`), по требованию владельца из чата.
- 2026-11-11, M0: точка сравнения Test551 — не сырой лог, а число строк `PASS`/`FAIL` и
  SHA256 **имён проверок** (до ` | `): 84 PASS / 0 FAIL,
  `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6`. Причина: в лог
  входят измеряемый `Kepler weight ... w=.../cv=...` и `ok in ... ms`, они меняются от
  прогона к прогону; имена проверок стабильны и сверены между M0 и M1.
- 2026-11-11, M1: профиль задаётся `P1B564_PROFILE=smoke|full`, по умолчанию `smoke`.
  `Program.cs` не передаёт тесту `--fast`/`--slow`, поэтому собственный переключатель
  необходим (новый механизм только для этого, см. «Вопросы к владельцу» №2).
- 2026-11-11, M1: Test564 зарегистрирован как `slow`, как Test563: полный прогон не должен
  попадать в быстрый набор; `--exact` и `--slow` его запускают.
- 2026-11-11, M1: папки прогонов в git не попадают (`study/qualification/results/.gitignore`
  игнорирует всё, кроме себя); коммитится только указатель `baseline/BASELINE.md` (M7).
  Причина: `.gitignore` репозитория и так игнорирует `results/**/*.csv`, а папка прогона —
  локальный артефакт, который никогда не перезаписывается.
- 2026-11-11, M0: K-тир (K1–K8) реализуется своими builder'ами в `qualification/`, потому
  что готовые аналитические задачи `StudySelfTest` приватны в другом классе
  (`FOUND.md` F1). Дублируется только формула oracle, не интегратор и не sweep.
- 2026-11-11, M0: для T1/T3 эталон — dd-Taylor (`TaylorNBody`), для R1/R2 — `JplDop853`
  плюс независимая проверка; `StudyShipCertifiedReference` не применим к произвольной
  задаче (`FOUND.md` F2).

## Изменения вне qualification/

_Каждое аддитивное изменение существующего кода: файл, что добавлено, подтверждение, что Test551 даёт прежний результат._

1. `Tools/P1bTests/TestRegistry.cs`: добавлена одна строка
   `new P1bCase("Test564_MethodQualification", Test564_MethodQualification, true)`.
   Ничего не удалено и не изменено. Подтверждение: `Test551` после изменения даёт тот же
   набор — 84 PASS / 0 FAIL, имена проверок совпали побитово с прогоном M0
   (хэш имён `54ae98e2...`). `--list` показывает `slow Test564_MethodQualification`.

## Вопросы к владельцу

_Что нельзя выяснить из репозитория или литературы. Агент продолжает с независимыми частями и не угадывает._

1. **R1/R2: что считать «certified reference той же модели».** Готового certified-механизма
   для DE441 нет (`FOUND.md` F2). Предлагается: DOP853 (фикс. шаг, 5 уровней шагов,
   Richardson) поверх `Jpl1Pn`-силы, плюс dd-Taylor там, где он успевает по бюджету.
   Подтвердить или указать другой эталон.
2. **Профиль `smoke` по умолчанию.** `Program.cs` не передаёт тесту `--fast`/`--slow`,
   поэтому Test564 нужен собственный переключатель. Предлагается `P1B564_PROFILE`
   (значения `smoke`/`full`, по умолчанию `smoke`). Возражения?
3. **L2: число витков N.** TASK.md отдаёт N на решение M5. Ориентир — 100 витков
   (достаточно для дрейфа, но smoke остаётся минутами). Подтвердить на M5.

## ЗАПРОС ИЗМЕНЕНИЯ ЗАДАНИЯ

_Пусто._

## Журнал сессий

- **2026-11-11 — M0 (завершён).** Создана ветка `qualification/test564-method-qualification`.
  Прочитаны `TASK.md`, `PROGRESS.md`, `Study/README.md`, `framework/*`, `StudySelfTest`,
  `StudyBridge`, `StudySweep`, `StudyReport`, `StudyRegistry`, `CertifiedReference`,
  `KnownProblems`, `Pythagorean`, `PureGravityCases`, `jpl/README.md` и схема JSON.
  Заполнен `INVENTORY.md` (методы и `Supports`, поля `StudyProblem`, карта Test5xx,
  данные DE441, точка сравнения Test551). Заведены `FOUND.md` (5 записей) и `BACKLOG.md`
  (7 идей). Запущен `Test551_IntegratorFrameworkSelfTest`: `ALL PASS`, 84 PASS / 0 FAIL,
  хэш нормализованных строк `4de9ad9c...eb1`. Код не менялся (`git diff` пуст).
  Дальше — M1: регистрация `Test564_MethodQualification`, профили, папка результатов,
  `environment.txt`, пустые `runs.csv`/`matrix.csv` с заголовками.
- **2026-11-11 — M1 (завершён).** Добавлен `Study/qualification/QualificationRunner.cs`
  (`partial class P1bTests`): вход `Test564_MethodQualification`, профиль
  `P1B564_PROFILE=smoke|full` (по умолчанию smoke), папка
  `Study/qualification/results/<UTC>_<hash>/`, `environment.txt` (дата, commit, ветка,
  .NET, ОС, CPU, число CPU, профиль, версия и SHA256 TASK.md, список методов),
  `runs.csv`/`matrix.csv` только с заголовками TASK.md, `run.log` (tee консоли).
  Регистрация — одна строка в `TestRegistry.cs` (записана выше). `--exact
  Test564_MethodQualification` проходит (`ALL PASS`, пишет 5 файлов). `Test551` — тот же
  результат (84 PASS / 0 FAIL, тот же хэш имён). Сборка: 0 ошибок.
  Дальше — M2: builder'ы K1–K8 и T1–T3, oracle, `oracle_audit.md` (G8).
