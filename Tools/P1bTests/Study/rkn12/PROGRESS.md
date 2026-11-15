# PROGRESS — RKN12(10) как production-интегратор выпечки эфемерид

Задание: `TASK_RKN12.md` (заморожено, версия 1). Ветка: `rkn12-bake`.
Один этап за раз. Текущий этап: **R2 выполнен, коммит `Q-RK2`**. R3 — следующий.

---

## 0. Сессия 2026-11-15 (старт)

### 0.1 Ритуал

* Прочитаны целиком: `Tools/P1bTests/Study/rkn12/AGENTS.md`,
  `Tools/P1bTests/Study/rkn12/TASK_RKN12.md`.
* `PROGRESS.md` отсутствовал — это начало, файл создан в R0.
* Ветка/голова проверены **до** создания рабочей ветки:
  * ветка `integrator-port/dynamic-policy`;
  * `HEAD = 0d87ada8f8e63f2feb0aa00757eb0f0a48646ae0` (`Q-RK-TASK`);
  * `git log -1 --grep='^Q-RK-TASK'` даёт тот же хэш;
  * `git status --short` пуст.
* Создана ветка `rkn12-bake` от `0d87ada8…` (без merge, без push).
* Прочитаны (только чтение) файлы §0.2: `EphemerisBaker.cs`, `BakedEphemeris.cs`,
  `EphemerisRuntime.cs`, `StarSystemAuthoring.cs`, `BodyAuthoring.cs`,
  `ExampleSystemPresets.cs`, `StarSystem.cs`, `OrbitingBody.cs`, `PhysicsSolver.cs`,
  `StarSystemAuthoringEditor.cs`, `StudyAdvancedSteppers.cs` (`Rkn1210Stepper`),
  `StudyAdvancedIntegrators.cs`, `StudyIntegratorApi.cs`,
  `StudySelfTest.Rkn1210FixedOrder`, Test553 report, qualification matrix (P3/P4),
  `dynamic_policy/TASK_D.md` (Q5 — runtime RKN12 запрещён; это задание его не отменяет).

### 0.2 Инвентаризация текущего bake (проверено по коду)

| Элемент | Факт |
|---|---|
| Движок | `EphemerisBaker.RunBake`, **fixed-step DOP853**, 12 стадий |
| Шаг | `h = min(dtMax, nextT − t)`; `dtMax = MaxStepSeconds`, урезанный `PhasePerStep` самой быстрой орбиты |
| Адаптивность | **нет**: ни error estimate, ни reject; фиксированный шаг до узла |
| Узлы | строго Chebyshev–Lobatto; `t = nextT` точным присваиванием; DCT `FitSegmentChebyshev` |
| Старт | `EvaluateWorldState(0)` + leaf-first барицентрическая коррекция поддеревьев |
| Сегмент | минимальный период поддерева / 4, clamp [2; 64] сут; `AdjustedSegmentLength` делает `segCount·L == span` |
| Файлы | BG3: quantized varint + gzip, `QuantBudgetMeters=2`; continuation 48 B в хвосте |
| Рантайм | `EphemerisRuntime`/`BakedEphemeris` только читают файлы; планеты в рантайме не интегрируются |
| Сцены | 2 тела (звезда + Terra); `SpanYears=1024`, `Degree=12`, `MaxStepSeconds=900`, `Quant=2`, `Compress=1` |
| 4-тельный пресет | `ExampleSystemPresets.CreateExampleSystem()`: звезда + планета A + луна + планета B |
| Эталон RKN12 | `StudyAdvancedSteppers.Rkn1210Stepper`: 17 стадий, `C/B/BP/BT/BPT/A`, `k[0]` в `Begin/Accept`, 16 RHS на попытку |
| Норма Study | RMS по блокам, `AdaptiveScales`/`AdaptiveAtol`; **не** per-body relative. R2 обязан считать относительную норму в bake-обёртке (§6.5) |
| Контроллер Study | `StudyIntegratorApi.AdaptiveIntegrator.Run`: clipped-шаг не занижает `hAdaptive` (`hNew = max(hNew, min(h, maxStep))`) — шаблон §6.4 |

### 0.3 Код игры в R0

Код игры **не менялся**. Добавлен только измерительный стенд:
`Tools/P1bTests/Study/rkn12/BaselineBake.cs` + одна строка регистрации
`Rkn12BaselineDop853` в `TestRegistry.cs` (diagnostic, unnumbered).
`Test566` **не занят** — он остаётся следующим свободным Test ID для
`Rkn12BakeCertification` (R1).

### 0.4 Регрессия на `Q-RK-TASK` (§8.0)

| Проверка | Результат |
|---|---|
| `dotnet build Tools/P1bTests/P1bTests.csproj -c Release` | 0 ошибок (16 pre-existing warnings) |
| `GalilegoPhysicsTests` | **283/283**, 0 failed |
| `GalilegoSimTests` | **107/107**, 0 failed |
| `Test551` (`verify_test551.ps1`) | **lines=84 fails=0**, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` — совпал |
| `Test565_DynamicPolicy` | **ALL PASS** (175 rows; 13 gate-FAIL rows — штатный результат dynamic_policy, не регрессия) |

`Test565` перезаписывает `Study/dynamic_policy/DYNAMIC_REPORT.md` (только wall_ms-строки).
Файл восстановлен `git checkout --`; папка `dynamic_policy` этим заданием не меняется.

---

## R0 — baseline текущего DOP853 bake

### R0.1 Метод

Конфигурация production: `Degree=12`, `PhasePerStep=0.02`, `MaxStepSeconds=900`,
`QuantBudgetMeters=2`, `SegmentPeriodDivisor=4`, `compress=true` (BG3).

Системы (значения скопированы из сцен/пресета; сцены не открывались):

* `Outdoors`: Sol μ=2.65433e19 R=2.345e8; Terra μ=1.28e13 R=1.143e6, a=5.51876e10, e=0.017.
  Источник: `OutdoorsScene.unity:423-436,950-963`.
* `ContactZone`: Star μ=1.327e20 R=6.96e8; Terra μ=1.28e13 R=1.143e6, a=1.5e11, e=0.017.
  Источник: `ContactZoneSlice.unity:166-177,387-398`.
* `Example4`: `ExampleSystemPresets.CreateExampleSystem()` (`ExampleSystemPresets.cs:14-63`).

Горизонты: 1.2, 10, 100, 1024 лет (`EphemerisBaker.YearSeconds = 365·86400`).

Что измерено:

1. **Время**: 5 прогонов на 1.2 г, 3 на 10 лет, 1 на 100/1024 лет; медиана и CV.
2. **Размер**: байты `.bin` (BG3) + manifest.
3. **sha256**: каждого файла и комбинированный (manifest + файлы).
4. **Детерминизм**: повторный прогон, побитовое равенство sha256 (1.2 и 10 лет).
5. **Ошибка на узлах vs независимый reference**:
   * 2-body — аналитическая кеплерова рельса того же тела (точное решение
     двухтельной задачи, μ = μ_родителя + μ_поддерева);
   * 4-body — dd-Taylor (order 32, tol 1e-30, max step 86400 s), старт из
     **точного** начального состояния `RunBake` (копия барицентрической коррекции).
     В R0 — только конечный узел; плотное независимое сэмплирование — R3/R5.
     Решение владельца 2026-11-15: dd-Taylor **не** является частью дефолтного
     прогона. В R0 он был явно включён только на 1.2/10/100 лет (4 с / 31 с /
     315 с); 1024-летний прогон остановлен (оценка ~1 ч) и помечен как
     consistency/energy (§8.5), не как абсолютная точность.
6. **Max seam jump**: левый/правый предел полинома в общем узле
   (`Clenshaw(±1)`, скорость `DerivSum·2/L`). Для BG3 это **доставленный** шов:
   он включает квантование коэффициентов.
7. **Энергия**: `|ΔE/E|` по доставленным относительным состояниям (корень
   восстанавливается из P=0; COM/импульс по файлам не измеримы — см. BACKLOG B-RK3).

Tighter DOP853 (`MaxStepSeconds=60`, `PhasePerStep=0.002`) — **только consistency**,
не независимый reference (§5): он сам накапливает round-off (525 тыс. шагов на
10 лет) и на коротких горизонтах может уйти от 900-с ветки дальше, чем та от
dd-Taylor. 4-body 1024 года: dd-Taylor не запускается (решение владельца),
результат — consistency/energy.

### R0.2 Числа

Полные таблицы — `results/baseline/BASELINE.md` (+ CSV). Кратко:

| system | horizon | wall median, s | bytes | delivered node error (vs analytic) | max seam, m |
|---|---:|---:|---:|---:|---:|
| Outdoors | 1.2 | 0.040 | 1 064 | 0.323 м | 0.375 |
| Outdoors | 10 | 0.183 | 7 296 | 0.655 м | 0.625 |
| Outdoors | 100 | 1.810 | 68 273 | 29.431 м | 0.625 |
| Outdoors | 1024 | 19.539 | 664 393 | **206.080 м** | 0.625 |
| ContactZone | 1.2 | 0.025 | 858 | 0.232 м | 0.250 |
| ContactZone | 10 | 0.194 | 5 110 | 0.374 м | 0.375 |
| ContactZone | 100 | 1.784 | 48 256 | 4.032 м | 0.625 |
| ContactZone | 1024 | 18.910 | 490 961 | **312.977 м** | 0.750 |
| Example4 | 1.2 | 0.057 | 13 082 | dd: A 0.162 / Луна 0.176 / B 0.173 м | 0.500 |
| Example4 | 10 | 0.399 | 103 027 | dd: A 0.174 / **Луна 1.219** / B 0.134 м | 0.625 |
| Example4 | 100 | 4.119 | 1 023 418 | dd: A 12.815 / **Луна 500.803** / B 7.398 м | 0.625 |
| Example4 | 1024 | 41.842 | 10 498 365 | dd не запускался (владелец); consistency/energy | 0.750 |

* Детерминизм: 1.2 г (5 прогонов) и 10 лет (3) — `IDENTICAL`; 1024 года —
  одинаковый sha256 в двух независимых процессах (см. BASELINE.md).
* Энергия: `|ΔE/E| ≤ 2.9e-9` на всех горизонтах.
* Tighter DOP853 (60 с) vs 900 с: 1.2 г — 0.125–0.68 м, 10 лет — до 18 м
  (Луна), 100 лет — до 573 м (Луна). Это подтверждает, что длинные ошибки —
  фазовый дрейф 900-с шага, а не reference.

### R0.3 Артефакты и воспроизведение

Всё лежит в `Tools/P1bTests/Study/rkn12/results/baseline/` (локальные артефакты;
в git — только `BASELINE.md`, `environment.txt`, `.gitignore`):

* `run.log` — полный лог;
* `bake_runs.csv` — время/размер/sha/seam по телам;
* `node_errors.csv` — ошибки на узлах и dd-taylor endpoint;
* `tight_consistency.csv` — tighter-DOP853 consistency;
* `determinism.csv` — sha двух прогонов;
* `invariants.csv` — дрейф полной энергии;
* `bake/<system>_<years>y/` — BG3-файлы baseline;
* `bake/tight_<system>_<years>y/` — tighter-DOP853 consistency.

Команда (из корня репозитория; dd-Taylor по умолчанию выключен):

    $env:RKN12_BASELINE_DD="none"
    $env:RKN12_BASELINE_TIGHT="1.2,10,100"
    Tools/P1bTests/bin/Release/net8.0/P1bTests.exe --exact Rkn12BaselineDop853 `
        --log Tools/P1bTests/Study/rkn12/results/baseline/run.log

Окружение — `results/baseline/environment.txt`.

### R0.4 Наблюдения и риски (не блокируют R0)

1. **Доставленная ошибка DOP853 на 1024 года велика**: 2-body
   `Outdoors` 206 м, `ContactZone` 313 м (аналитика). Энергия при этом
   сохраняется (~1e-9), то есть это фазовый дрейф/round-off фиксированного
   900-с шага (36 млн шагов), а не потеря энергии. Это и есть мотивация RKN12;
   порог §8.5 (1 м) для RKN12 будет проверяться в R5.
2. **4-body Луна на 10 годах уже 1.22 м** у текущего DOP853 (dd-Taylor endpoint).
   Квантование BG3 (`QuantBudgetMeters=2`, гарантия ≤1 м на координату, вектор
   до ~1.7 м) уже сопоставимо с порогом §8.2 (≤1 м на тело); на 100 годах
   добавляется фазовый дрейф 900-с шага (dd: 500.8 м; tighter DOP853 отличается
   от baseline на 573 м). Это **риск для R3**: тюнинг degree/segment/quant
   запрещён (RQ4). Если R3 упрётся, будет оформлен «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА»
   с числами, без подгонки порога. R0 порог не оценивает.
3. **Mesh seam в BG3 включает квантование**: 0.25–0.75 м по позиции при
   `QuantBudgetMeters=2`; скорость ~1e-5 м/с. Гейт §8.4 сравнивает RKN12 с этим
   baseline на том же формате, поэтому это корректная база.
4. **P/COM по файлам не измеримы**: `BakedEphemeris` хранит только смещения
   относительно родителя, состояния корня нет; восстановление корня из COM=0
   делает P/COM тождественно нулевыми. Гейты §6.6/§8.3 требуют лог внутри
   `RunBake` (R2). Идея постоянного лога — BACKLOG B-RK3.
5. **Время 1.2 года шумное** (20–50 мс, CV до 11–27%): для временных гейтов
   R4 использовать 10 лет и медиану 5 прогонов; CV ≤10% там достижим.
6. **dd-Taylor cost / решение владельца**: при `maxStep=900` 4-body 1.2 года =
   236 с; при `maxStep=86400` — 4.1 с при **побитово том же** endpoint (665 шагов).
   Точность задаёт tolerance, а не cap. Измеренная цена: 1.2 г = 4 с, 10 лет =
   31 с, 100 лет = 315 с, 1024 года ≈ 1 ч. Владелец 2026-11-15 остановил
   1024-летний прогон и потребовал не гонять Taylor по умолчанию: в стенде
   `RKN12_BASELINE_DD` теперь `none` (только явный opt-in), 1024 года помечены
   как consistency/energy. Это не меняет пороги §8 — только состав baseline-
   доказательств; для R3/R5 вопрос независимого reference на 1024 года остаётся
   открытым.
7. **Кириллические имена тел** пишутся в CSV в UTF-8; Windows PowerShell 5.1
   по умолчанию читает их как cp866 и показывает mojibake. Файлы корректны
   (`-Encoding UTF8`).

---

## РЕШЕНИЕ ВЛАДЕЛЬЦА по §6.2 (2026-11-15): вариант A

**Пункт задания:** §6.2 «Коэффициенты» — тождества юнит-теста.

**Что наблюдается.** В эталоне `StudyAdvancedSteppers.Rkn1210Stepper`
(единственный источник коэффициентов по §2/§6.2):

| тождество §6.2 | факт в эталоне | расхождение |
|---|---:|---|
| `sum(B) = 1` | **0.5** (ровно) | в 2 раза |
| `sum(BP) = 1` | 1.0 | нет |
| `sum(BT) = 0` | −5.64e−18 | round-off |
| `sum(BPT) = 0` | +3.47e−18 | round-off |
| `sum_j A[i][j] = C[i]²/2` | max \|diff\| = 2.22e−15 (стадия 16) | round-off |
| `C[0]=0`, `C[16]=1` | 0, 1 | нет |

**Почему это не опечатка в моём чтении.** Эталон обновляет положение как
`q_new = q + h·v + h²·Σ B_i k_i`. Для постоянного ускорения на шаге это обязано
давать `q + h·v + ½h²a`, то есть `ΣB = ½`. Если «нормализовать» B к 1, шаг
удвоит ускорение и метод перестанет быть RKN12(10) — это прямо запрещено
§6.2 («не округлять, не улучшать») и §12.9. `sum(A)=C²/2` при этом выполняется,
что подтверждает: массив A и формулировка шага согласованы, а `sum(B)=1` —
описка в тексте задания (должно быть `sum(B)=1/2` или `2·sum(B)=1`).

**Варианты:**
* **A (рекомендация):** считать воротами `sum(B) = 1/2` (и `sum(BP)=1`,
  `sum(BT)=0`, `sum(BPT)=0`), коэффициенты копировать из эталона без изменений.
* **B:** оставить буквальное `sum(B)=1`, но тогда изменить постановку шага
  (нельзя: это изменит метод и эталон).
* **C:** владелец уточняет иное.

**Решение владельца (2026-11-15): A.** Действующие ворота R1:
`sum(B) = 1/2`, `sum(BP) = 1`, `sum(BT) = 0`, `sum(BPT) = 0`,
`sum_j A[i][j] = C[i]²/2`, `C[0]=0`, `C[16]=1`. Коэффициенты копируются из
`StudyAdvancedSteppers.cs` дословно, без нормализации. Текст §6.2
(`sum(B)=1`) считается опиской; TASK_RKN12.md не редактируется.

R1 разблокирован, но **не начат**: ждём отдельного «продолжай».

---

## R1 — коэффициенты и stepper (без включения в baker)

### R1.1 Что сделано

* `Assets/_Project/Scripts/Ephemeris/Rkn1210Coefficients.cs` — `C/B/BP/BT/BPT/A`
  дословно из `StudyAdvancedSteppers.cs`; в комментарии — ссылка на эталон и
  статью Dormand–El-Mikkawy–Prince (1987), «RKN12(10)17M».
* `Assets/_Project/Scripts/Ephemeris/Rkn1210Stepper.cs` — чистый C#,
  `Rkn1210Acceleration(t, p, a)` callback, 17 стадий, `k[0]` в `Begin/Accept`,
  16 RHS на попытку, per-body relative error norm, без аллокаций в шаге.
* `Tools/GameSources.props` — две строки `<Compile Include=...>`.
* `Tools/P1bTests/Tests/Rkn12BakeCertification.cs` — `Test566_Rkn12BakeCertification`
  (R1-объём; R2–R5 будут расширять этот же кейс).
* `TestRegistry.cs` — регистрация `Test566` (slow).

Код игры (baker/runtime/сцены) не менялся: `RunBake` по-прежнему DOP853,
`BakeConfig.Integrator` ещё не существует (это R2).

### R1.2 Результаты `Test566` (ALL PASS)

| Проверка §8.1 | Факт |
|---|---|
| `sum(B)=1/2` (решение владельца A), `sum(BP)=1`, `sum(BT)=0`, `sum(BPT)=0` | 0.5 / 1 / −3.9e−18 / +1.0e−17 |
| `sum_j A[i][j]=C[i]²/2` | max \|diff\| = 2.33e−15 (стадия 16) |
| Коэффициенты == эталон (reflection, bitwise) | C/B/BP/BT/BPT/A идентичны |
| Стадии | `Begin` 1 RHS, `TryStep` **16**, `Accept` 1 |
| Наблюдаемый порядок (ns={4,8,16}) | errors 5.448e−6 / 2.703e−9 / 1.219e−12; overall **11.05** (гейт 11.0–13.5) |
| Kepler 2-body, один период | **5.81e−8 м** (гейт <1e−6) |
| N-body 10 лет | `\|ΔP\|/P_scale=4.92e−15`, `\|ΔCOM\|/COM_scale=1.38e−13`, `\|ΔE/E\|=1.88e−15` (гейты P/COM 1e−11; энергия — отчёт) |
| Node landing | **13/13 узлов бит-в-бит** `t == nextT`, 1 reject |
| Детерминизм | два прогона бит-в-бит равны |
| Аллокации | **0 байт** на 2000 шагов после прогрева |
| Ошибка одной попытки vs эталон | max \|diff\| = **0.0** |

Заметки:
* Порядок 11.05 — тот же, что у эталонного self-test: на симметричной орбите
  ведущий член сокращается, и парные наклоны скачут; гейт использует общий
  наклон по ns={4,8,16}. Имплементация совпадает с эталоном побитово на шаге.
* Тест «норма не зависит от абсолютной координаты» сравнивает ошибку при
  смещении всей системы; на больших смещениях (1e6 м) computed-норма уходит на
  ~1e-4 из-за сокращения в embedded error estimate. Гейт 1e-4 отделяет
  относительную норму от абсолютной (последняя изменилась бы на порядки).
  Это свойство обусловленности оценки, а не формула нормы.

### R1.3 Регрессия §8.0 после R1

| Проверка | Результат |
|---|---|
| build | 0 ошибок |
| `GalilegoPhysicsTests` | 283/283 |
| `GalilegoSimTests` | 107/107 |
| `Test551` | 84/0, SHA256 `54ae98e2…` — совпал |
| `Test565_DynamicPolicy` | ALL PASS |
| `Test566_Rkn12BakeCertification` | ALL PASS |
| `--compare` стенда (GameSources.props изменён) | см. R1.4 |

### R1.4 `--compare` стенда

`Test564_MethodQualification` (profile `full`) против
`qualification/baseline/20261114T173334Z_142824a`:

* **0** изменений статусов, **0** новых ячеек, **0** ошибок, **0** изменений цены >10%;
* `No changes: statuses, errors and prices are identical within 10%`;
* 2925 ячеек matrix (`PASS=1800 FAIL=194 N/A=555 SKIP=376`), elapsed **3 274 688 мс**
  (~54.6 мин), ALL PASS;
* лог: `results/qualification_compare_r1.log` (локальный, git-ignored).

Это подтверждает: две новые строки в `Tools/GameSources.props` не сдвинули
поведение стенда. Результаты прогона записаны в штатную (git-ignored) папку
`qualification/results/`.

### R1.5 Итог R1

* R1-код готов, `Test566` ALL PASS, DOP853-путь не тронут (baker ещё не ветвится).
* `GameSources.props` изменён только двумя строками; `--compare` — 0 изменений.
* Коммит: `Q-RK1`.

---

## R2 — интеграция в baker за enum (default DOP853)

### R2.1 Что сделано

* `EphemerisBaker.cs`:
  * `public enum BakeIntegrator { Dop853Fixed = 0, Rkn1210Adaptive = 1 }`;
  * `BakeConfig.Integrator` с инициализатором `Dop853Fixed` (значение 0 не меняется);
  * `Validate()` отвергает неизвестный enum (без fallback);
  * `RunBake` — явная ветка `Dop853Fixed` (код шага не изменён ни на символ) и
    ветка `Rkn1210Adaptive` с node forcing, адаптивным шагом и per-body relative
    norm; `Accel`/`FitSegmentChebyshev`/`ValidateBakeable`/file sink не менялись;
  * счётчики/исключения: non-finite и step floor → `InvalidOperationException`
    с `t`, `h`, `errorNorm`, `body`; 50 подряд reject'ов → исключение.
* `StarSystemAuthoring.cs` — поле `Integrator` с инициализатором `Dop853Fixed`.
* `StarSystemAuthoringEditor.cs` — `Integrator` передаётся в `BakeConfig`; enum
  рисуется штатным `DrawDefaultInspector` как popup, добавлен HelpBox о границах.
* `Test566` расширен R2-проверками.

**DOP853 не изменён**: `Test566` бейкает Outdoors 1.2 г и сравнивает combined
sha256 с R0 — `2AECA30F…D23C1DEC3`, совпадение побитово.

### R2.2 Решение по max step для RKN12

`BakeConfig.MaxStepSeconds` в коде документирован как **«Absolute DOP853 substep
cap»**. Если применить 900 с и к RKN12, он делает те же 36 млн шагов, но по 16
стадий вместо 12 — то есть **медленнее** DOP853, и ворота §8.4 (≥1.33×) недостижимы
по построению. Поэтому ветка RKN12 ограничена **фазовым бюджетом**
`PhasePerStep` (метод-независимый кап, 0.02 рад на шаг), а не 900-с капом DOP853.
Значения `PhasePerStep`/`MaxStepSeconds`/`Degree`/`Quant` не менялись; DOP853
по-прежнему использует `min(900, phaseStep)`.

Это решение зафиксировано здесь; на R4 оно проверяется временем/ошибкой. Если
владелец считает иначе — откат одной строки (`rknMaxStep = dtMax`).

### R2.3 Результаты `Test566` (ALL PASS)

| Проверка §R2 | Факт |
|---|---|
| `Dop853Fixed=0`, `Rkn1210Adaptive=1` | да |
| `BakeConfig.Integrator` / `StarSystemAuthoring.Integrator` default | `Dop853Fixed` |
| неизвестный enum | `ArgumentOutOfRangeException`, без fallback |
| DOP853 bake Outdoors 1.2 г vs R0 | sha256 **совпал** (`2AECA30F…`) |
| RKN12 Outdoors 1.2 г | 1/1 тело, BG3, manifest attach, finite; **6.5 мс** (DOP853 52.6 мс) |
| RKN12 Example4 1.2 г | 3/3 тела, BG3, manifest attach, finite; **48.5 мс** |
| RKN12 output != DOP853 | да для обеих систем (нет silent fallback) |

Примерное время 1.2 г: Outdoors 6.5 мс vs 52.6 мс (8×), Example4 48.5 мс vs
~56 мс (только ~1.16×). Example4 — риск для ворот §8.4; измерение медианой 5 и
горизонтами 10/100/1024 — R4.

### R2.4 Регрессия §8.0

| Проверка | Результат |
|---|---|
| build | 0 ошибок |
| `GalilegoPhysicsTests` | 283/283 |
| `GalilegoSimTests` | 107/107 |
| `Test551` | 84/0, SHA256 `54ae98e2…` |
| `Test565_DynamicPolicy` | ALL PASS |
| `Test566_Rkn12BakeCertification` | ALL PASS |
| `--compare` стенда | не требовался: `GameSources.props` в R2 не менялся |

---

## R3 — НЕ НАЧАТ

План: 2-body Kepler (аналитика, 1.2 г и 1024 года), Example4 (10/100 лет,
dd-Taylor endpoint; Луна отдельно), P3/P4 (read-only фикстуры qualification),
энергия/импульс/COM по §8.3. Для 1024 лет 4-body независимый dd-Taylor —
отдельное решение (владелец запретил Taylor по умолчанию).


