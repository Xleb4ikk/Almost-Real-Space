# TASK RKN12: RKN12(10) как production-интегратор выпечки эфемерид (N-body планеты), версия 1

**СТАТУС: ЗАМОРОЖЕНО.** Файл меняет только владелец проекта (Xleb4ikk). Агент его не редактирует.
Если задание неверно, неполно или противоречит коду/другому файлу, агент пишет в `PROGRESS.md`
раздел «ЗАПРОС ИЗМЕНЕНИЯ ЗАДАНИЯ», останавливается и ждёт ответа (§9).

Файл лежит в `Tools/P1bTests/Study/rkn12/TASK_RKN12.md`. Рабочая папка задания — `Tools/P1bTests/Study/rkn12/`
(далее «папка R»). Она новая; `qualification/`, `dynamic_policy/`, `Study/integrators/`, `Study/jpl/`
и старые Test5xx **не редактируются**.

Это задание **не меняет** решение `dynamic_policy/TASK_D.md` Q5 («портировать RKN12 в runtime — НЕТ»).
Важно: **Q5 здесь — это решение TASK_D.md, а не RQ5 этого задания**; решения этого задания имеют
префикс `RQ1…RQ10`, коммиты — `Q-RK<n>`. Q5 запрещает runtime-путь корабля. Это задание разрешает
RKN12 **только в offline-выпечке эфемерид** (планеты/N-body), где сила — консервативная гравитация.
Runtime-интегратор корабля остаётся DOP853.
Если агент считает, что это противоречит TASK_D.md, он останавливается и спрашивает владельца (§9).

---

## 0. Ритуал начала сессии (обязателен, особенно после сброса контекста)

1. Прочитать `AGENTS.md` папки R, этот файл целиком, `PROGRESS.md` папки R с конца.
2. Прочитать (только чтение):
   * `Assets/_Project/Scripts/Ephemeris/EphemerisBaker.cs` — текущий bake;
   * `Assets/_Project/Scripts/Ephemeris/BakedEphemeris.cs`, `EphemerisRuntime.cs` — формат/рантайм;
   * `Assets/_Project/Scripts/Universe/StarSystemAuthoring.cs`, `BodyAuthoring.cs`, `ExampleSystemPresets.cs`,
     `StarSystem.cs`, `OrbitingBody.cs`, `PhysicsSolver.cs`;
   * `Assets/_Project/Scripts/Editor/Inspectors/StarSystemAuthoringEditor.cs`;
   * `Tools/P1bTests/Study/framework/StudyAdvancedSteppers.cs` (`Rkn1210Stepper`, `Rkn1210Integrator`) —
     эталонная реализация коэффициентов и алгоритма;
   * `Tools/P1bTests/Study/framework/StudyAdvancedIntegrators.cs` — обёртка;
   * `Tools/P1bTests/Study/integrators/results/Test553_FrameworkPlanetSweep.md` — старые планетные данные;
   * `Tools/P1bTests/Study/qualification/TASK.md` (Приложение A, P2–P6) и
     `Tools/P1bTests/Study/qualification/results/20261114T220333Z_92f1756/matrix.csv` — новые планетные данные;
   * `Tools/P1bTests/Study/dynamic_policy/TASK_D.md` — границы runtime-политики (не менять).
3. Проверить ветку: работа только в `rkn12-bake`, созданной от коммита **`Q-RK-TASK`**
   в `integrator-port/dynamic-policy`. Этот коммит содержит `TASK_RKN12.md` и `AGENTS.md`;
   его полный хэш: `git log --format=%H -1 --grep=^Q-RK-TASK`. Хэш записать в `PROGRESS.md`.
   Дерево должно быть чистым (`git status --short` пуст). Если HEAD не `Q-RK-TASK`
   или дерево грязное — остановиться и спросить.
4. Продолжить ровно с текущего этапа R0–R7. Завершённые этапы не переделывать и не «улучшать».
5. Перед каждым коммитом ответить: «Какой пункт TASK_RKN12.md это изменение выполняет?»
   Нет ответа — откатить изменение, идею записать в `BACKLOG.md` папки R.
6. Конец сессии: обновить `PROGRESS.md` (этап, сделано, числа, решения, вопросы) и закоммитить.
   Коммиты: `Q-RK<n>: <краткое описание>`.

---

## 1. Цель и главное правило

**Цель.** RKN12(10) (Dormand–El-Mikkawy–Prince 1987, «RKN12(10)17M») становится полноценным
production-интегратором **offline-выпечки эфемерид** для N-body систем планет: точным, быстрым,
детерминированным и устойчивым на горизонте 1024 года. Игра в рантайме читает запечённые
Chebyshev-эфемериды; runtime-интегратор корабля не меняется (DOP853).

**Почему это «полноценно», а не «полумера».** Планеты в игре не интегрируются в рантайме: production-путь
планет — это bake (`EphemerisBaker`), а рантайм только интерполирует файлы. RKN12 в bake — это
именно production-путь планет, а не тестовая ветка. Runtime-корабль остаётся на DOP853, потому что
RKN12 не поддерживает drag/тягу/переменную массу и не должен их поддерживать.

**Главное правило.** RKN12 применяется **только** там, где правая часть — консервативное поле
второго порядка `r'' = a(t, r)` (чистая гравитация N тел). Он **никогда** не выбирается для drag,
тяги, массового расхода, событий корабля или runtime-пути. Если система содержит tight binary/тесные
сближения, где RKN12 не проходит ворота §8, bake остаётся на DOP853, а в отчёте пишется, что для
1 м нужен IAS15 (порт IAS15 — вне этого задания).

**Что считается успехом.** Не «RKN12 появился в коде», а: bake на RKN12 как минимум не хуже текущего
DOP853 по доставленной точности, быстрее по времени, детерминирован, устойчив на 1024 года, а runtime
корабля побитово прежний. Переключение default — только после зелёных R0–R5 и отдельного решения
владельца (R6).

---

## 2. Что уже есть (проверено по коду; агент перепроверяет в R0)

| Элемент | Где | Состояние |
|---|---|---|
| Глобальный N-body bake | `EphemerisBaker.RunBake` | pairwise `Accel`, все тела, иерархия/барицентрическая коррекция |
| Интегратор bake | `RunBake`, внутренний цикл | **fixed-step DOP853**, 12 стадий, шаг `h = min(dtMax, nextT - t)` |
| Шаг bake | `BakeConfig` | `MaxStepSeconds=900`, `PhasePerStep=0.02`, `Degree=12`, `QuantBudgetMeters=2`, `SegmentPeriodDivisor=4`, сегмент 2–64 сут |
| Сэмплы | `RunBake` | строго в Chebyshev–Lobatto узлах; DCT `FitSegmentChebyshev` |
| Формат | `BakedEphemeris` | Chebyshev-коэффициенты относительного положения; рантайм `EvaluateWorldState` |
| Runtime | `EphemerisRuntime`, `StarSystem` | читает файлы; **не интегрирует планеты** |
| Runtime корабля | `SpacecraftPhysics`, `IntegratorPolicy`, `VesselStep` | DOP853; RKN12 сюда не добавляется |
| Сцены | `Assets/_Project/Scenes/*.unity` | 4 сцены, в каждой **2 тела**: звезда + Terra |
| 4-тельный пресет | `ExampleSystemPresets.CreateExampleSystem()` | Sun–Earth–Moon–Mars-подобная система (тесты) |
| Эталон RKN12 | `StudyAdvancedSteppers.cs` | `Rkn1210Stepper`, 17 стадий, 12(10), N-body через `PosDim=3n` |
| Данные планет | Test553 / qualification | RKN12 выигрывает 10–100 м и нормальные 1 м; P4 tight binary на 1 м FAIL |

Текущие игровые системы (для тестовых фикстур; сцены не менять):

| Сцена | Звезда | Планета | a, м | e |
|---|---|---:|---:|---:|
| `OutdoorsScene` | μ=2.65433e19, R=234 500 000 м | μ=1.28e13, R=1 143 000 м | 55 187 600 000 | 0.017 |
| `ContactZoneSlice`, `PlayerRoofSlice`, `PlayerWalkSlice` | μ=1.327e20, R=696 000 000 м | μ=1.28e13, R=1 143 000 м | 150 000 000 000 | 0.017 |

Факты, на которые опирается задание:

* Старый Test553: RKN12 — 12/16 первых мест и 14/16 «победитель или ничья» на планетах;
  real solar 7-body 10 лет: 21 854 / 25 922 / 30 444 RHS на 100/10/1 м против DOP853 58 465 / 77 665 / 197 137.
* Новый qualification: `planets` M100/M10 — RKN12 лучший; M1 — IAS15 (P4 tight binary); P3/P5/P6
  RKN12 проходит 1 м; P3 1 м — 51 059 RHS против 145 945 у DOP853.
* Текущий bake: 1.2 года, DOP853 900 с, degree 12, quant 2 м — worst error vs Y6C ≈ 0.237 м;
  сегментные швы ~0.06 м; 1024 года — default `SpanYears`.
* `EphemerisBaker` уже интегрирует **все тела pairwise**, а не кеплеровские рельсы. Задача — заменить
  только внутренний stepper, не менять физику, инициализацию, фит и формат.

---

## 3. Решения владельца (предзаполнено; агент не меняет)

| № | Вопрос | Решение |
|---|---|---|
| RQ1 | Добавить RKN12 в bake как selectable-интегратор | **ДА**. Default остаётся `Dop853Fixed`; RKN12 включается явно для конкретной системы/конфига (см. RQ9) |
| RQ2 | Добавить RKN12 в runtime корабля (`SpacecraftPhysics`/`IntegratorPolicy`) | **НЕТ**. Runtime остаётся DOP853. RKN12 не поддерживает drag/тягу/массу |
| RQ3 | Менять силу bake (J2/GR/PN/третьи тела) | **НЕТ**. Только текущая pairwise-ньютоновская гравитация |
| RQ4 | Менять Chebyshev degree/segment/quantization | **НЕТ** в этом задании. Только интегратор; тюнинг фита — отдельный BACKLOG |
| RQ5 | Менять `qualification/`, `dynamic_policy/`, `Study/integrators/`, `Study/jpl/`, старые Test5xx | **НЕТ**. Только чтение |
| RQ6 | Включать/менять сцены, префабы, ассеты | **НЕТ**. Сцены не открывать/не сохранять |
| RQ7 | Менять пороги §8 и подгонять их под результат | **НЕТ**. Провал ворот — стоп и отчёт |
| RQ8 | Tight binary / close encounters | RKN12 может не пройти 1 м; не форсировать. В отчёте — рекомендация IAS15; порт IAS15 вне задания |
| RQ9 | Как «включать» RKN12 и что считать default | **`Dop853Fixed = 0` и инициализатор поля остаются навсегда.** Unity при отсутствии поля в YAML берёт инициализатор из кода, поэтому смена инициализатора изменила бы старые сцены. RKN12 включается **только явно**: владелец выставляет `StarSystemAuthoring.Integrator = Rkn1210Adaptive` (или `BakeConfig.Integrator`) для конкретной системы. Глобального переключения default нет. Агент сцены не правит |
| RQ10 | Ветка | `rkn12-bake` от коммита `Q-RK-TASK` в `integrator-port/dynamic-policy`; слияние — только владелец |

---

## 4. Границы

### Делаем

* Новые файлы в `Assets/_Project/Scripts/Ephemeris/`:
  * `Rkn1210Coefficients.cs` — коэффициенты 17 стадий;
  * `Rkn1210Stepper.cs` — N-body stepper 12(10) (pure C#, без Unity);
  * при необходимости — `Rkn1210Diagnostics.cs` (только если без него не обойтись; обосновать).
* Минимальные правки:
  * `EphemerisBaker.cs` — enum `BakeIntegrator`, поле в `BakeConfig`, ветка выбора stepper в `RunBake`,
    диагностика/ошибки; физика `Accel`, `FitSegmentChebyshev`, `ValidateBakeable`, file sink — не меняются;
  * `StarSystemAuthoring.cs` — сериализуемое поле `BakeIntegrator`; **инициализатор поля и значение 0 —
    `Dop853Fixed`, навсегда** (Unity при отсутствии поля в YAML берёт инициализатор; см. RQ9);
  * `StarSystemAuthoringEditor.cs` — выпадающий список в инспекторе;
  * `Tools/GameSources.props` — по одной строке `<Compile Include=...>` на каждый новый файл
    (обычно две; если появился `Rkn1210Diagnostics.cs` — три, с обоснованием).
* Новые тесты:
  * `Tools/P1bTests/Tests/Rkn12BakeCertification.cs` (новый Test ID, следующий свободный после `Test565`);
  * при необходимости — `Tools/P1bTests/Study/rkn12/GameSystemFixtures.cs` (фикстуры систем из сцен;
    значения с комментарием «источник — сцена, строка YAML», сцены не менять).
    `Tools/P1bTests/P1bTests.csproj` уже включает `Tests\**\*.cs` и `Study\**\*.cs`, поэтому новые
    файлы в этих папках компилируются автоматически; **править csproj не нужно**.
* Новые документы/результаты только в папке R:
  `PROGRESS.md`, `BACKLOG.md`, `FOUND.md`, `RKN12_REPORT.md`, `results/`, `rkn12.csv`, `environment.txt`, логи.
* Работа в ветке `rkn12-bake`; слияние — владелец.

### НЕ делаем (запрещено)

* Не меняем runtime-интеграторы: `SpacecraftOrbitIntegrator`, `OrbitIntegrator`, `DoP853Step`,
  `DoPri5Step`, `SpacecraftPhysics`, `IntegratorPolicy`, `VesselStep`, `SimulationRunner`,
  `WhBm14aIntegrator`, `KeplerUniversalDrift`, `DenseSegment`.
* Не меняем события, детекторы, `EventDrivenPropagator`, `DetectionGrain`, `LongWarpDriver`.
* Не меняем источники сил, `Accel`/`AccelParallel`, физику, сцены, префабы, ассеты, Unity-настройки.
* Не меняем `BakedEphemeris`/`EphemerisRuntime` (формат и рантайм-чтение).
* Не меняем `degree`, `segment`, `quantization`, `MaxStepSeconds`, `PhasePerStep` по умолчанию
  (кроме нового поля `BakeIntegrator`).
* Не меняем `qualification/`, `dynamic_policy/`, `Study/integrators/`, `Study/jpl/`, старые Test5xx.
* Не добавляем RKN12 в `SpacecraftIntegrator` enum, `IntegratorPolicy`, `VesselStep`, `SimulationRunner`.
* Не добавляем silent fallback RKN12→DOP853 внутри bake: провал RKN12 — это ошибка offline-задания,
  а не повод незаметно подменить метод. Fallback — только выбор `Dop853Fixed` в конфиге.
* Не меняем пороги §8 и не «подгоняем» tolerance/шаг под результат.
* Не добавляем per-step аллокации и неломаем детерминизм (см. §6.7).
* Не коммитим сгенерированные эфемериды в `Assets/StreamingAssets/Ephemerides`.

---

## 5. Правило доказательств

Каждое утверждение в отчёте подтверждается **CSV/логом, сгенерированным кодом**, а не руками.

Этап принимается только если есть:

1. точная команда, commit hash, ветка, `dirty/clean`, окружение (CPU, .NET, ОС);
2. путь к CSV/логу;
3. сравнение с DOP853-baseline **на той же машине, той же конфигурации, том же наборе тел**;
4. ошибка против **независимого reference**, а не против самого RKN12. Для 1024 лет «tighter DOP853»
   **не является независимым**: он разделяет систематику и упирается в round-off (~0.3 м на 1 а.е.
   при 60-с шаге за 1024 года). Для 1024-летних ворот использовать:
   * 2-body — аналитическое решение Кеплера (точное);
   * 4-body/P3 — double-double Taylor reference из `Tools/P1bTests/Reference/TaylorNBody.cs` и
     `Tools/P1bTests/Study/references/TaylorDd.cs` (`RunTaylorNBodyDdOptSeconds`, order 32,
     tolerance 1e-30), как в `QualificationCatalog.QualificationDdTaylorStates`;
   * либо тройное согласие: dd-Taylor + DOP853 с компенсированным суммированием + RKN12 (tight).
   Если dd-Taylor для системы недоступен, 1024-летний результат — **consistency/energy check**,
   а не абсолютное утверждение точности; это явно пишется в отчёте.
5. детерминизм: два прогона -> побитово равные файлы (`sha256`).

Запрещено:

* брать числа из старых отчётов без ссылки на CSV;
* сравнивать `ms` на зашумлённой машине без медианы и CV;
* называть `EDGE`/`tie` победой;
* писать «примерно», если есть CSV.

---

## 6. Архитектура и требования к коду

### 6.1. Конфигурация

* `public enum BakeIntegrator { Dop853Fixed = 0, Rkn1210Adaptive = 1 }`.
* `BakeConfig.Integrator` — инициализатор `Dop853Fixed`; **значение 0 и инициализатор не меняются**.
* `StarSystemAuthoring.Integrator` — инициализатор `Dop853Fixed`; **значение 0 и инициализатор не меняются**.
  Причина: Unity при отсутствии поля в YAML берёт инициализатор из кода; смена инициализатора
  изменила бы старые сцены. RKN12 включается только явной установкой поля владельцем (RQ9).
* Инспектор — enum popup; новый default для «новых систем» не вводится в этом задании.
* `RunBake` выбирает stepper по `config.Integrator`; **все остальные параметры** (`Degree`,
  `PhasePerStep`, `MaxStepSeconds`, `QuantBudgetMeters`, segment length) остаются как есть.
* `EphemerisBaker.BakeToFiles`/`Bake` сигнатуры не меняются (кроме `BakeConfig` field).
* `BakeIntegrator` не попадает в runtime `SpacecraftIntegrator`.

### 6.2. Коэффициенты

* Новый `Rkn1210Coefficients` содержит `C[17]`, `B[17]`, `BP[17]`, `BT[17]`, `BPT[17]`, `A[17][]`
  **точно** как в `StudyAdvancedSteppers.cs` (не округлять, не «улучшать»).
* В комментарии — ссылка на `Tools/P1bTests/Study/framework/StudyAdvancedSteppers.cs` и статью
  Dormand, El-Mikkawy, Prince (1987), «RKN12(10)17M».
* Юнит-тест тождеств:
  * `sum(B)=1`, `sum(BP)=1`, `sum(BT)=0`, `sum(BPT)=0`;
  * для каждой стадии `i`: `sum_j A[i][j] = C[i]^2 / 2`;
  * `C[0]=0`, `C[16]=1` (или как в эталоне; если отличается — записать и объяснить).
* Если тождество не выполняется — коэффициенты неверны; стоп, не «нормализовать».

### 6.3. Stepper

* Чистый C#, `double`, SI; без `UnityEngine`, без `Time.deltaTime`, без глобалов.
* Состояние: `p[3n]`, `v[3n]`; ускорение `a(t, p)` — callback, который вызывает текущий `Accel`
  (`p`, `m`, `g`, `n`, `partials`). Не дублировать физику.
* 17 стадий: `k[0]` — ускорение в принятом состоянии, оно переиспользуется из `Begin`/`Accept`;
  `TryStep` вычисляет `k[1..16]`, то есть **16 новых RHS-вызовов на попытку**. Не считать `k[0]`
  новым вызовом на каждом шаге: в отчётах писать `stages=17`, `rhs_per_attempt=16`
  (плюс один `k[0]` в `Begin` и один в каждом `Accept`). RKN12 не FSAL в классическом смысле;
  не называть 17-ю стадию FSAL.
* `TryStep(t, h, p, v, out pNew, out vNew, out errorNorm)` — **чистая** функция: не мутирует `p/v`;
  при reject вызывающий отбрасывает `pNew/vNew` и пробует меньший `h`.
* Никакого dense output: выход bake — Chebyshev-фит, не интерполятор stepper'а.
* Ошибки: non-finite, `h <= MinStep`, ошибка > допуска после N попыток — `InvalidOperationException`
  с `t`, `h`, `body`, `errorNorm`, `step`. Не молчать, не clamp'ить.
* Масса/тяга/drag: stepper не знает о них; если в будущем кто-то передаст velocity-dependent силу,
  это ошибка вызова, а не «примерно сработает».

### 6.4. Node forcing (обязательно)

Bake сэмплирует **точно** в Chebyshev–Lobatto узлах. Поэтому:

* на каждом интервале до следующего узла `nextT`:
  `h = min(hAdaptive, nextT - t)`;
* если шаг принят — `t = nextT` (не «почти»), сэмпл берётся в узле;
* если шаг отвергнут — уменьшить `h` (например, `h *= 0.5` или по errorNorm), но **не переступать**
  `nextT`; повторять до accept или до `MinStep`;
* `hAdaptive` хранится между узлами. Если шаг был **усечён** до `nextT - t` (`clipped`), нельзя
  уменьшать `hAdaptive` по результату этого шага: иначе контроллер застрянет на размере узла.
  Действовать как в `StudyIntegratorApi.Run`: `if (clipped) hNew = max(hNew, min(hAdaptive, maxStep))`;
  иначе `hAdaptive = hNew`.
* Проверка node landing — **точное равенство**: цикл присваивает `t = nextT` (тот же `double`),
  поэтому тест проверяет `t == nextT` побитово (`BitConverter.DoubleToInt64Bits`), а не
  `1e-9 * max(1,t)` (при t ≈ 3e10 с это ~30 с и ничего не проверяет).
* Нельзя «интегрировать до nextT + h и потом интерполировать»: это меняет физику выборки и ломает
  сравнение с DOP853.

### 6.5. Error norm (обязательно)

* Норма per-body и **по относительному вектору**, не по абсолютным координатам:
  * для каждого **baked** тела (child, `parentIdx >= 0`) ошибка считается для
    `(r_i - r_parent, v_i - v_parent)`; абсолютная ошибка родителя в норму **не входит**
    (иначе абсолютная ошибка Земли «съест» Луну);
  * `scalePos = atolPos + rtol * max(|r_i - r_parent|, |r_i_new - r_parent_new|)`;
    `scaleVel = atolVel + rtol * max(|v_i - v_parent|, |v_i_new - v_parent_new|)`;
  * `err = max_i max(errPos_i/scalePos_i, errVel_i/scaleVel_i)`;
  * корень (star) участвует в динамике, но не в fit-норме; его COM/импульс проверяются отдельно.
* Если `Rkn1210Stepper` из Study считает норму по абсолютному `y`, передавать относительные
  масштабы через `AdaptiveScales`/`AdaptiveAtol` или считать относительную ошибку в bake-обёртке;
  не менять математику stepper'а.
* Defaults для R2: `rtol=1e-12`, `atolPos=1e-3 м`, `atolVel=1e-6 м/с`. Если по результатам R3
  нужно изменить — это **решение владельца** (RQ7), не самовольная подгонка; записать в PROGRESS.
* Норма не должна зависеть от абсолютной мировой координаты (иначе Солнце «съест» Луну).
* Для 2-тельной системы норма — относительно барицентра/родителя; для 4-тельной — Луна отдельно
  в отчёте, не только глобальный max.

### 6.6. Диагностика

* На каждом узле/году (не в горячем цикле, с шагом, чтобы не тормозить):
  * `t`, `body`, `|r_rel|`, `|v_rel|`;
  * **полная энергия системы** `E_total = Σ 1/2 m_i |v_i|² − Σ_{i<j} G m_i m_j / |r_i−r_j|`
    (с теми же `G`/массами, что в `Accel`) — это **гейт** §8.3;
  * удельная энергия тела относительно родителя `E_rel = v²/2 − mu/r` — **только диагностика**,
    не гейт: для Луны она не сохраняется из-за солнечного прилива;
  * суммарный импульс `P = Σ m_i v_i`; COM `Σ m_i r_i / Σ m_i`; для них — **масштабы**, а не `P0/COM0`:
    `P_scale = Σ m_i |v_i|`, `COM_scale = Σ m_i |r_i| / Σ m_i` (или max|r_i−COM|);
    `|ΔP|/P_scale`, `|ΔCOM|/COM_scale` — гейт §8.3. `P0 ≈ 0` после барицентрической коррекции,
    делить на него нельзя;
  * принятые/отвергнутые шаги, средний `h`, min/max `h`;
  * для tight binary — periapsis distance.
* Всё в CSV; не `Debug.Log` в цикле.
* Диагностика не должна менять результаты (только чтение состояния).

### 6.7. Детерминизм и производительность

* Один и тот же вход + конфиг -> побитово одинаковые файлы (проверять `sha256` двух прогонов).
* `Accel` для `n < 64` — последовательный, как сейчас. Для `n >= 64` — текущий `AccelParallel`
  (детерминированное слияние по телам); не менять порядок суммирования.
* Никаких аллокаций в шаге: все буферы (`k[17][3n]`, `pNew`, `vNew`, `error`) выделяются один раз
  на bake/тело.
* Время bake: мерить медианой 5 прогонов, CV <= 10%; отдельно интегратор и I/O, если возможно.
* 1024 года — целевой горизонт; 1.2 года и 100 лет — быстрые итерации.

### 6.8. Что обязано остаться неизменным

* `Accel`/`AccelParallel` — физика и порядок сумм.
* Барицентрическая коррекция и стартовые состояния — как в `RunBake`.
* `FitSegmentChebyshev`, `AdjustedSegmentLength`, `ValidateBakeable`, `FileSink`, `BakedEphemeris`
  формат/квантование/компрессия.
* `Keplerian continuation` в конце bake.
* Runtime-чтение `EphemerisRuntime`/`BakedEphemeris`.
* Значения `Degree`, `PhasePerStep`, `MaxStepSeconds`, `QuantBudgetMeters`, segment length.
* Все runtime-тесты и поведение корабля.

---

## 7. Этапы R0–R7

Один этап за раз. После каждого — регрессия §8.0 и обновление `PROGRESS.md`.
Коммит: `Q-RK0`, `Q-RK1`, …

### R0. Ритуал, инвентаризация, baseline (код не меняется)

* Прочитать §0; проверить ветку/HEAD/чистоту; записать в `PROGRESS.md`.
* Собрать `Tools/P1bTests` Release; прогнать `GalilegoSimTests`, `GalilegoPhysicsTests`, `Test551`,
  `Test565_DynamicPolicy`; записать числа/хэши.
* Зафиксировать baseline **текущего DOP853 bake**:
  * системы: `OutdoorsScene` (Sol+Terra), `ContactZone` (Sun+Terra), `ExampleSystemPresets` (4 тела);
  * горизонты: 1.2 года, 10 лет, 100 лет, 1024 года (1024 — один раз, сохранить CSV/файлы/время);
  * параметры: `Degree=12`, `PhasePerStep=0.02`, `MaxStepSeconds=900`, `QuantBudgetMeters=2`;
  * метрики: время, размер, max error на узлах vs **независимый reference** (§5):
    для 2-body — аналитика; для 4-body/P3 — dd-Taylor; tighter DOP853 (`MaxStepSeconds=60`,
    `PhasePerStep=0.002`) — только вторичная consistency-проверка, не единственный эталон;
    max seam jump, `sha256` файлов.
* Создать `results/baseline/`; всё воспроизводимо командами.
* Стоп-условие: если baseline не воспроизводится или дерево грязное — стоп.

### R1. Коэффициенты и stepper (без интеграции в baker)

* Добавить `Rkn1210Coefficients.cs`, `Rkn1210Stepper.cs`; `GameSources.props` — две строки.
* Юнит-тесты: тождества §6.2, порядок по шаблону `StudySelfTest.Rkn1210FixedOrder`
  (`ns={4,8,16}` периодов, fixed step, ошибки >1e-14 и <0.1, overall order в (11.0, 13.5);
  не «4 halving» вслепую — на 12-м порядке round-off достигается за 2–3 шага),
  Kepler 2-body (один период, ошибка < 1e-6 м), N-body энергия/импульс, node landing (точное
  равенство), детерминизм, отсутствие аллокаций в шаге (или явный замер).
* Сравнить с эталоном `StudyAdvancedSteppers` на одинаковых состояниях: коэффициенты и результат
  шага должны совпадать в пределах round-off (если эталон доступен как код в P1bTests; не линковать
  Unity-код в Study).
* В baker **не включать**; default остаётся DOP853.
* Ворота: R1-тесты зелёные; существующие тесты не изменились.

### R2. Интеграция в baker за enum (default DOP853)

* `BakeConfig.Integrator`, `StarSystemAuthoring.Integrator`, inspector.
* `RunBake`: ветка `Dop853Fixed` — текущий код без изменений; ветка `Rkn1210Adaptive` — новый цикл
  с node forcing и per-body norm.
* Проверить: при `Integrator=Dop853Fixed` bake **побитово** совпадает с R0 (файлы `sha256`).
* Проверить: `Rkn1210Adaptive` доходит до конца на 2-тельной и 4-тельной системах, 1.2 года;
  файлы читаются `EphemerisRuntime`.
* Ворота: DOP853-путь не изменился; RKN12-путь работает; ошибки/исключения диагностичны.

### R3. Сертификация на синтетических системах

* 2-body Kepler (аналитика): 1.2 года и 1024 года; ошибка на узлах и между узлами.
* `ExampleSystemPresets` (4 тела): 10 лет и 100 лет; per-body error (Луна отдельно), энергия/импульс.
* P3 Custom 6-body (из qualification, read-only): 10 лет; сравнить с Test553/qualification числами
  качественно (не подгонять).
* P4 tight binary: 10 лет; ожидаемо 1 м FAIL — записать, не форсировать.
* Порядок: шаблон §8.1; Kepler < 1e-6 м; энергия/импульс — §8.3.
* Ворота: §8.2/§8.3. Если tight binary не проходит — это **не** провал задания, а документированный
  предел; RKN12 не включать для такой системы.

### R4. Bake-регрессия на игровых системах

* Системы R0; 1.2 года и 1024 года; RKN12 vs DOP853 baseline; сравнение:
  * delivered error (случайные точки, не только узлы) vs **независимый reference** §5
    (2-body — аналитика; 4-body/P3 — dd-Taylor; tighter DOP853 — только consistency);
  * per-body (Terra, Луна, планеты);
  * seam jumps, размер файлов, `sha256`, детерминизм (2 прогона);
  * время (медиана 5, CV).
* Ворота: §8.4. Если RKN12 медленнее 1.33× или ошибка > 1.1× DOP853 — RKN12 не включать;
  отчёт с числами.

### R5. Длинный горизонт и устойчивость

* 1024 года на 2-тельной и 4-тельной системах; полная энергия/импульс/COM; per-body дрейф;
  отсутствие non-finite; время/размер.
* P4 tight binary 10 лет и, если возможно, 100 лет; зафиксировать предел.
* Проверить `MinStep`/`MaxStep`/отказы; если RKN12 упирается в `MinStep` — это стоп/отчёт, не clamp.
* Проверить, что `Keplerian continuation` после bake не изменилась и seam на конце нет.
* Ворота: §8.5. 1024 года для игровых систем — зелёные; tight binary — отчёт.

### R6. Явное включение RKN12 для конкретных систем (не смена default)

* Агент **не** меняет инициализатор `Dop853Fixed` и не правит сцены. Он оформляет
  «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА» в `PROGRESS.md` с числами R4/R5 и вариантами:
  (A) оставить все системы на DOP853; (B) владелец **вручную** выставляет
  `StarSystemAuthoring.Integrator = Rkn1210Adaptive` для выбранных систем (агент не трогает сцены);
  (C) отложить.
* После письменного «B» — отдельный коммит `Q-RK6` **не меняет default**: он добавляет/проверяет
  только код, тесты и документацию; владелец сам ставит поле в инспекторе. Если владелец просит
  изменить сцену — это делает он, не агент.
* Ворота: §8.6. Если владелец выбрал A/C — ничего не менять, отчёт.

### R7. Финальный отчёт и запрос

* `RKN12_REPORT.md` из CSV; `PROGRESS.md` финализирован; `BACKLOG.md`/`FOUND.md` актуальны.
* «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА»: что включено/не включено, какие числа, какие риски, что не проверено.
* Ничего не мержить; слияние — владелец.

---

## 8. Ворота и пороги

### 8.0. Регрессия после каждого этапа

* `dotnet build Tools/P1bTests/P1bTests.csproj -c Release` — 0 ошибок.
* `dotnet test tests/GalilegoPhysicsTests/... -c Release` — **283/283** (или больше, если добавлены
  новые тесты; старые имена/числа сохранить).
* `dotnet test tests/GalilegoSimTests/... -c Release` — **107/107** (или больше; старые сохранить).
* `P1bTests.exe --exact Test551_IntegratorFrameworkSelfTest` — `lines=84 fails=0`, SHA256
  `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` (или зафиксированный в R0).
* `P1bTests.exe --exact Test565_DynamicPolicy` — ALL PASS.
* Если менялся `Tools/GameSources.props` — `--compare` стенда против baseline (как в TASK_D §10):
  0 изменений статусов/цен/ошибок.
* `git diff` по запрещённым файлам (§4) — пуст.
* При выключенном RKN12 (`Dop853Fixed`) bake-файлы побитово равны R0.

### 8.1. R1 (stepper)

* Тождества коэффициентов §6.2 — PASS.
* Наблюдаемый порядок: шаблон `StudySelfTest.Rkn1210FixedOrder` (`ns={4,8,16}` периодов,
  ошибки >1e-14 и <0.1, overall order в (11.0, 13.5)); не «4 halving» вслепую.
* Kepler 2-body, один период: ошибка < **1e-6 м**.
* N-body: `|ΔP|/P_scale <= 1e-11`, `|ΔCOM|/COM_scale <= 1e-11` за 10 лет; полная энергия — отчёт.
* Node landing: `t == nextT` побитово на всех узлах (не временной допуск).
* Детерминизм: два шага/прогона побитово.
* Аллокации: 0 байт в шаге после прогрева (или < 1 КБ/шаг с обоснованием).

### 8.2. R3 (синтетика)

* 2-body: delivered error <= **1 м** на 1024 года (аналитика; ожидается ~мм).
* 4-body preset: per-body delivered error <= **1 м** на 100 лет vs dd-Taylor (не tighter DOP853);
  Луна — отдельно, <= **1 м**; если Луна хуже — стоп/отчёт.
* P3: качественно согласуется с Test553/qualification (RKN12 не хуже DOP853 по ошибке);
  числа — в CSV, не подгонять.
* P4: 1 м может FAIL — записать; не менять порог.

### 8.3. R3 (инварианты)

* **Гейт — полная энергия системы** `E_total` (§6.6), не parent-relative:
  `|ΔE_total/E_total|` за 100 лет: RKN12 <= max(2× DOP853, **1e-7**); за 1024 года — отчёт +
  <= **1e-6** (если не проходит — стоп, не «улучшать» tolerance без решения владельца).
  Parent-relative энергия Луны — только диагностика, в гейт не входит.
* `|ΔP|/P_scale <= 1e-11`; `|ΔCOM|/COM_scale <= 1e-11`, где масштабы — §6.6 (не `P0`, который ≈ 0).
  Если измеренный round-off floor выше порога — стоп и запрос владельцу, не тихое ослабление.
* Нет non-finite, нет `MinStep`-залипания.

### 8.4. R4 (bake-регрессия)

* Delivered error: `err_RKN12 <= 1.1 * err_DOP853` на той же конфигурации и системе, vs
  **независимый reference** §5 (2-body — аналитика; 4-body/P3 — dd-Taylor; tighter DOP853 —
  только consistency).
* Max seam jump: `<= 1.1 * baseline` (позиция и скорость).
* Размер файла: `<= 1.02 * baseline` (тот же Chebyshev-формат).
* Время: `median_RKN12 <= 0.75 * median_DOP853` (>=1.33× ускорения; 5 прогонов, CV <= 10%);
  если ускорение < 1.33× — RKN12 не включать.
* Детерминизм: `sha256` двух прогонов равны.
* Runtime: `EphemerisRuntime` читает файлы; позиции/скорости на случайных точках совпадают с
  baseline-файлами в пределах error gate.

### 8.5. R5 (1024 года)

* 2-body: delivered error <= **1 м** на 1024 года vs аналитика.
* 4-body: delivered error <= **1 м** на 1024 года vs dd-Taylor; Луна <= **1 м**.
  Если dd-Taylor недоступен — результат помечается как **consistency/energy check**, не как
  абсолютная точность 1 м.
* Полная энергия/импульс/COM — §8.3; нет non-finite; время/размер — отчёт.
* Continuation: после конца bake положение/скорость непрерывны; seam jump <= baseline.
* Tight binary: отчёт с periapsis/ошибкой; если 1 м FAIL — зафиксировать, не форсировать.

### 8.6. R6 (явное включение, не default switch)

* Все §8.0 зелёные; `Dop853Fixed`-путь по-прежнему доступен и побитово равен baseline.
* **Инициализатор `Dop853Fixed` и значение 0 не меняются**; старые сцены без поля получают
  `Dop853Fixed` — это и есть проверяемое поведение.
* RKN12 включается **только явной установкой поля владельцем** для конкретной системы; агент
  сцены не правит. Глобального default switch нет.
* RKN12 не меняет runtime-поведение корабля (runtime-тесты побитово/зелёные).
* `--compare` стенда — 0 изменений; `Test551` hash — прежний.
* Владелец письменно выбрал B; иначе ничего не менять.

---

## 9. Стоп-условия и запросы владельцу

Остановиться и написать «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА» в `PROGRESS.md`, если:

1. нужно тронуть запрещённый файл (§4);
2. любой порог §8 не выполняется, а обойти его можно только подгонкой;
3. RKN12 медленнее 1.33× или ошибка > 1.1× DOP853 на игровых системах;
4. система содержит tight binary/close encounters, где 1 м не достигается;
5. требуется изменить `BakeConfig` defaults (degree/segment/quant/tolerance) вне §6.1;
6. требуется изменить runtime-политику/`SpacecraftIntegrator`/сцены;
7. найдено расхождение с `TASK_D.md` или `qualification/TASK.md`;
8. 1024 года дают non-finite/дрейф выше §8.3;
9. Unity-сериализация старых сцен подхватывает не `Dop853Fixed` (это означало бы смену
   инициализатора/default — стоп);
10. независимый reference для 1024 лет недоступен/сам на пределе гейта;
11. любая неясность в задании.

В запросе: пункт задания, что наблюдается, числа/CSV, варианты A/B/C, рекомендация. До ответа — не продолжать.

---

## 10. Отчётность и артефакты

Папка R:

```
Tools/P1bTests/Study/rkn12/
  TASK_RKN12.md          (этот файл, заморожен)
  AGENTS.md              (указатель на этот файл)
  PROGRESS.md            (этапы, числа, решения, запросы)
  BACKLOG.md             (идеи вне задания)
  FOUND.md               (баги вне задания)
  RKN12_REPORT.md        (итоговый отчёт из CSV)
  results/
    environment.txt
    baseline/            (DOP853 R0)
    rkn12_stepper.csv    (R1)
    synthetic.csv        (R3)
    bake_compare.csv     (R4)
    long_horizon.csv     (R5)
    dd_taylor_reference.csv (R3/R5; независимый reference)
    *.log
```

Требования к `RKN12_REPORT.md`:

* цель и что изменено (файлы/строки);
* архитектура: stepper, node forcing, error norm, конфиг;
* таблица ворот §8 с фактическими числами и PASS/FAIL;
* точность: узлы и случайные точки, per-body (Луна/планеты), 2-body/4-body/P3/P4;
* **какой reference использован** для 1024 лет (аналитика/dd-Taylor/three-way) и его floor;
* время/размер/детерминизм;
* 1024 года: полная энергия/импульс/COM, пределы; parent-relative энергия — только диагностика;
* что **не** проверено (P2 100 лет, если не гонялся; IAS15; tight binary за пределами 10 лет);
* рекомендация владельцу (A/B/C) и риски.

Все числа — из CSV; в отчёте — ссылка на CSV-строку/колонку. Ручные числа запрещены.

---

## 11. Definition of Done

- [ ] R0–R7 выполнены по порядку; `PROGRESS.md` актуален.
- [ ] RKN12-степпер в `Assets/_Project/Scripts/Ephemeris/`, чистый C#, без Unity.
- [ ] `BakeIntegrator` enum; **инициализатор и значение 0 — `Dop853Fixed` навсегда**;
      `Dop853Fixed`-путь побитово прежний; RKN12 — только явный выбор владельца.
- [ ] Node forcing точен (t == nextT); per-body/relative error norm; нет silent fallback;
      нет per-step аллокаций; clipped-шаг не занижает `hAdaptive`.
- [ ] R1–R5 ворота §8 зелёные (кроме документированного tight-binary предела).
- [ ] R4/R5: RKN12 быстрее >=1.33× и ошибка <=1.1× DOP853 на игровых системах.
- [ ] 1024 года: 2-body/4-body зелёные vs аналитика/dd-Taylor; полная энергия/импульс в §8.3.
- [ ] Runtime корабля, события, сила, сцены, формат — не изменены; все тесты зелёные.
- [ ] `--compare` стенда — 0 изменений; `Test551` hash — прежний.
- [ ] `RKN12_REPORT.md` из CSV; `BACKLOG.md`/`FOUND.md` заполнены.
- [ ] Решение владельца по включению (A/B/C) оформлено; сцены/инициализатор не менялись.
- [ ] Ветка `rkn12-bake`; слияние — владелец.

---

## 12. Чего делать нельзя (повтор)

1. Не добавлять RKN12 в runtime корабля, `SpacecraftIntegrator`, `IntegratorPolicy`, `VesselStep`.
2. Не менять DOP853/DOPRI5, события, силу, сцены, ассеты, `BakedEphemeris`/`EphemerisRuntime`.
3. Не менять degree/segment/quantization/tolerance defaults вне §6.1/§6.5.
4. Не подгонять пороги §8; провал — стоп.
5. Не делать silent fallback RKN12→DOP853.
6. Не коммитить сгенерированные эфемериды.
7. Не редактировать `qualification/`, `dynamic_policy/`, `Study/integrators/`, `Study/jpl/`.
8. Не мержить ветку; слияние — только владелец.
9. Не «улучшать» RKN12-коэффициенты; они копируются из эталона и проверяются тождествами.
10. Не выдавать `ms` на шумной машине за доказательство; медиана + CV + CSV.
11. Не путать `Q5` из `TASK_D.md` с `RQ` этого задания; решения здесь — `RQ1…RQ10`,
    коммиты — `Q-RK<n>`.
12. Не менять инициализатор `Dop853Fixed` и не править сцены: Unity иначе изменит старые сцены.
