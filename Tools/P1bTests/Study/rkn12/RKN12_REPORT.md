# RKN12(10) как интегратор выпечки эфемерид — итоговый отчёт

Задание: `TASK_RKN12.md` (заморожено, v1). Ветка: `rkn12-bake`.
Решение владельца: **A** — production остаётся на DOP853, RKN12(10) не включается.
Дерево игры (сцены, префабы, ассеты, `Dop853Fixed=0`, инициализаторы) не менялось.

## 1. Что сделано

* `Assets/_Project/Scripts/Ephemeris/Rkn1210Coefficients.cs` — коэффициенты
  RKN12(10)17M, дословно из `StudyAdvancedSteppers.cs`; сверены с первоисточником
  MATLAB `rkn1210.m` (`rodyo/FEX-RKN1210`, коэффициенты из `rknint.f`;
  Dormand–El-Mikkawy–Prince 1987). `C/A/Bhat/Bphat/B/Bp` совпадают с нашими
  `C/A/B/BP/BT/BPT` в пределах double.
* `Assets/_Project/Scripts/Ephemeris/Rkn1210Stepper.cs` — чистый C#, 17 стадий,
  16 новых RHS на попытку, `k[0]` в `Begin/Accept`, per-body relative error norm,
  без аллокаций в шаге; time-base с Kahan в bake-ветке.
* `EphemerisBaker.cs` — `BakeIntegrator { Dop853Fixed=0, Rkn1210Adaptive=1 }`,
  поле `BakeConfig.Integrator` (default `Dop853Fixed`), ветка `RunBake` с node
  forcing/адаптивным шагом; DOP853-путь не изменён (sha256 совпадает с R0).
* `StarSystemAuthoring.cs` / `StarSystemAuthoringEditor.cs` — opt-in поле и
  предупреждение; инициализатор `Dop853Fixed` навсегда.
* `Tools/GameSources.props` — две строки; стенд `--compare` — 0 изменений.
* `Test566_Rkn12BakeCertification` + диагностики `Rkn12RoundoffProbe`,
  `Rkn12CompensatedProbe`, `Rkn12DdProbe`, `Rkn12DdForceProbe`.

## 2. Архитектура

* Bake выбирает stepper по `config.Integrator`; физика `Accel`, `FitSegmentChebyshev`,
  `ValidateBakeable`, сегменты, квантование и формат не менялись.
* Узлы Chebyshev–Lobatto: `t = nextT` точным присваиванием; RKN12-ветка
  клипует шаг на узле, не занижает `hAdaptive` после clipped-шага; rejections
  не переступают узел; 50 reject'ов/`MinStep` — исключение, без fallback.
* Норма — per-body relative: `atol + rtol·max(|rel|)` для `(r_i−r_parent, v_i−v_parent)`;
  корень в fit-норму не входит.
* **Time-base**: `t += h` в double на 3.2e10 с накопил 0.5006 с за 642k шагов
  (≈10–15 км при 30 км/с). Kahan-компенсация в RKN12-ветке; DOP853-путь
  оставлен бит-в-бит (нельзя менять).

## 3. Ворота §8

| Этап | Результат |
|---|---|
| §8.0 build | 0 ошибок |
| §8.0 `GalilegoPhysicsTests` | 283/283 |
| §8.0 `GalilegoSimTests` | 107/107 |
| §8.0 `Test551` | 84/0, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` |
| §8.0 `Test565` | ALL PASS |
| §8.0 DOP853 bitwise | `2AECA30F…D23C1DEC3` = R0; PASS |
| §8.0 `--compare` стенда | 0 статусов, 0 ячеек, 0 ошибок, 0 цен >10% |
| §8.1 R1 (stepper) | **PASS**: порядок 11.05, Kepler 5.81e−8 м, P 4.92e−15, COM 1.38e−13, node 13/13, alloc 0 |
| §8.2 R3 (1 м) | **FAIL**: 2-body 1024 — 247/176 м; 4-body 100 — 19/54/16 м; P3 3.77 vs 2.15 м |
| §8.3 R3 (инварианты) | энергия PASS; P PASS; COM 1024 — 2.05e−10/3.99e−11 **FAIL** |
| §8.4 R4 | **NOT RUN** (остановка на R3, §9) |
| §8.5 R5 | **NOT RUN** |
| §8.6 R6 | **A** — ничего не включать |

## 4. Точность: R0 baseline DOP853 (доставленная, BG3)

`results/baseline/bake_runs.csv`, `node_errors.csv`.

| system | 1.2 г | 10 лет | 100 лет | 1024 года |
|---|---:|---:|---:|---:|
| Outdoors, ошибка vs аналитика | 0.323 м | 0.655 м | 29.43 м | 206.08 м |
| ContactZone, ошибка vs аналитика | 0.232 м | 0.374 м | 4.03 м | 312.98 м |
| Example4, 100 лет: A / Луна / B | — | 0.174 / 1.219 / 0.134 м | 12.81 / 500.80 / 7.40 м | dd не гонялся |

Время/размер: Outdoors 0.040/0.183/1.81/19.54 с, 1.1/7.3/68/664 КБ;
Contact 0.025/0.194/1.78/18.91 с, 0.86/5.1/48/491 КБ; Example4
0.057/0.399/4.12/41.84 с, 13 КБ/103 КБ/1.0 МБ/10.5 МБ. Seam 0.25–0.75 м
(включает квантование BG3), энергия ≤2.9e−9, детерминизм 1.2/10 лет IDENTICAL.

## 5. Точность RKN12 после Kahan (R3)

`synthetic/synthetic.csv`, `synthetic_r3_kahan.log`.

| случай | RKN12 | reference |
|---|---:|---|
| Outdoors 2-body 1.2 г | 0.260 м | аналитика |
| Outdoors 2-body 1024 г | **246.95 м** | аналитика |
| ContactZone 2-body 1024 г | **176.43 м** | аналитика |
| Example4 10 лет: A / Луна / B | 0.444 / 1.804 / 0.220 м | dd-Taylor |
| Example4 100 лет: A / Луна / B | **19.16 / 53.52 / 15.93 м** | dd-Taylor |
| P4 tight binary 10 лет | 1.72 м | аналитика (предел) |
| P3 Custom 6, 10 лет | 3.77 м vs DOP853 2.15 м | tighter DOP853 (consistency) |

## 6. Причина 10 км и floor

* До Kahan 2-body 1024 давал 10 027 м; эталонный stepper — 10 760 м, наша
  реализация — 10 760 м, разница **0.0 м**. `tAccum−tExact = 0.5006 с`;
  на точном времени ошибка **69.08 м**. Time-base, не RKN12.
* Kahan: 10 027 → 246.95 м; 4-body A/B 726/623 → 19.16/15.93 м; P3 6.65 → 3.77 м.
* Double-floor на точном времени: h=50k → 69.1 м, h=25k → **14.9 м**,
  h=12.5k → 62.3 м (V-образная кривая: round-off, не truncation).
* B-пробы: Neumaier-состояние 9.46 м; dd state 21.4 м; dd state+force
  20.5 м @50k, 10.1 м @25k, 2.41 м @6.3k. Ошибка ~линейна по h; 1 м требует
  h≈2.6 тыс. с (~12.4 млн шагов, ~60 с 2-body) против DOP853 19.5 с.
* Коэффициенты сверены с первоисточником; транскрипционной ошибки нет.
  Остаток — пред-асимптотическое поведение RKN12 в режиме h≈0.02 рад, не
  «мало битов» и не лечится dd-состоянием.

## 7. Инварианты

* Полная энергия по доставленным относительным состояниям: ≤2.9e−9 на всех
  горизонтах (R0 DOP) и ~1e−9 (RKN12); гейт PASS.
* P/COM: 1.2 г — ≤1e−14; 1024 г — `dCOM/COM` 2.05e−10/3.99e−11 (FAIL 1e−11).
  P/COM по файлам не измеримы (корень не хранится); direct stepper.
* 1024 4-body dd-Taylor не запускался (решение владельца).

## 8. Что НЕ проверено

* R4: полная bake-регрессия 1.2 г и 1024 года на трёх играх-системах,
  медиана 5, CV, per-body; R5: 1024 года 4-body vs dd-Taylor.
* P2 100 лет 7-body, IAS15, tight binary за пределами 10 лет.
* Влияние time-base на DOP853-путь (путь заморожен бит-в-бит).
* Тюнинг `QuantBudgetMeters`/degree/segment (RQ4, запрещён).

## 9. Рекомендация

**A:** production остаётся DOP853; RKN12-код не включать. RKN12-ветка —
opt-in исследовательский код; `Test566` остаётся красным на R3-воротах как
честная сертификация. Если 1 м на 1024 года обязателен — нужен IAS15/другой
метод (или пересмотр порога/формата, C); dd force + h≈2.6 тыс. с технически
возможен, но медленнее DOP853 и не даёт production-выигрыша.

**Риски:** 1-м гейт ниже double/format floor; BG3 quantization ~1 м; DOP853
на 1024 годах сам даёт 206/313 м (текущий production floor, не регрессия);
`Test566` красный по R3.
