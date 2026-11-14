# DECISION_TABLE: режим → интегратор → доказательство (Приложение D)

Это карта решений для динамического выбора интегратора. Она построена из выводов стенда
(`Tools/P1bTests/Study/qualification/results/20261114T151403Z_e860827/PRODUCTION_REPORT.md`, `Tools/P1bTests/Study/qualification/PORT_REPORT.md`) и кода
(`Assets/_Project/Scripts/Spacecraft/IntegratorPolicy.cs`, `Simulation/VesselStep.cs`).

**Как читать.** Числа в этом файле — стартовые (seed): они перенесены из отчётов и ещё не сверены с CSV.
Этап D1 сверяет каждое число с CSV, заполняет колонки «Ссылка (агент)» и «Итоговый статус (агент)».
Файл лежит в `Tools/P1bTests/Study/dynamic_policy/`. Менять агент может **только** эти две колонки и раздел 4 (журнал расхождений). Остальное — владелец.

**Обозначения.**
* `price` = `price(Prod-DOP853) / price(метод)`: больше 1 — метод дешевле (по лучшему control, `matrix.csv`).
* `wall 600` = wall метода на чанке 600 с против `Prod-DOP853` (гейт C6).
* `err` = ошибка против certified reference на игровых допусках (`game_control.csv`), метры.
* `DEFAULT` = остаётся DOP853. Всё, что не перечислено в таблице, — `DEFAULT`.
* Цели точности (м): S01–S09/S14 — 1; S12 — до 0.01; S15, R4 — 0.01; L1 — 1.

---

## 1. Что запрещено использовать в игре (не зависит от режима)

| метод | причина | источник |
|---|---|---|
| GJ8 | C3 исключает; шаг 600 с даёт S01 `3.671e6 м`, S03 `5.379e7 м`, S05 `3.811e7 м`, S14 `1.150e8 м`; победа в матрице при control 110.9 с — не игровой контроль | папка Q: `TASK.md` C3; `PRODUCTION_REPORT.md`, «Limits» п. 6 |
| RK4, Leapfrog, PEFRL, Y6, Y6C, DOPRI5-fixed, SRKN14A, Encke-IAS15 | в C3 исключены; в C6 не выигрывают или ошибка на 600 с катастрофическая | `TASK.md` C3; `PRODUCTION_REPORT.md` |
| DOP853-adaptive, DOPRI5-adaptive | study-варианты; игровые DOP853 и DOPRI5 уже существуют | `PORT_REPORT.md` §7 |
| любой метод при ненулевых тяге, drag, массовом расходе | у кандидатов `Supports` даёт N/A (силы от скорости, S10/S11/S17); остаётся DOP853 | `TASK.md` C4 |

---

## 2. Таблица режимов

Условия приведены в терминах `IntegratorContext` (`ChunkSeconds`, `FlightModelActive`, `SunDominant`,
`SunCoastAllowed`, `CanCoast`, `AltitudeMeters`, `VerticalSpeedMps`, `CentralMu`, `Eccentricity`, `PortedAllowed`).

| ID | режим и условия | интегратор | решение владельца (§3 TASK_D) | seed-доказательство | Ссылка (агент) | Итоговый статус (агент) |
|---|---|---|---|---|---|---|
| **R0** | всё, что не попало в строки ниже; абсолютный путь без локального кадра; чанк < 600 с вне flight; любая тяга, drag, массовый расход | **DOP853** | всегда | сертифицированный путь по умолчанию | qualification/results/20261114T193520Z_ff36340/matrix.csv: Prod-DOP853,M1,price_at_tier (baseline, 833/833 ячеек); код: IntegratorPolicy.cs:172 | DEFAULT |
| **R1** | планетоцентрический coast, `PortedAllowed`, `CanCoast`, не `SunDominant`, не `FlightModelActive`, `ChunkSeconds ≥ 600`, высота `200 000…2 000 000 м`, `0 ≤ e < 0.05`, `CentralMu > 0` (LEO, long-warp) | **WH-BM14A** | Q1: ДА | LEO price **5.73×**; wall 600 с **0.009 мс против 0.066 мс** (7.3× быстрее); абсолютная ошибка S01 7.94e-6, S02 1.07e-6, S03 2.25e-2, S09 6.95e-6, S04 1.22e-5, S05 1.07e-4, S15 9.78e-6, R4 3.81e-5, L1 1.73e-2 м — все ниже целей. **Относительный гейт ≤ 1.5× не выполнен** на S01 (2.8×), S03 (1995×), S04 (5.9×), S15 (12.6×), R4 (156×): исключение владельца, вариант A | qualification/results/20261114T193520Z_ff36340/matrix.csv: S01,WH-BM14A,M1,price_at_tier; qualification/results/20261114T193520Z_ff36340/chunks.csv: S01,WH-BM14A,600,wall_ms_per_chunk; qualification/results/20261114T193520Z_ff36340/game_control.csv: S01,WH-BM14A,,err_max; dynamic_policy/results/20261114T215832Z_e4eba2d/dynamic.csv: R1,* | REJECTED (D4: wall 0.998–1.112× > 0.5×; ошибка ≤ 0.0528 м, события 0 с, аллокации PASS) |
| **R2** | 6-DOF flight, `FlightModelActive`, `ChunkSeconds ≤ 0.05`, высота `< 10 000 м`, не (`VerticalSpeedMps < 0` и высота `< 2000 м`) | **DOPRI5** | Q2: ДА при наличии доказательства | **в стенде доказательств нет**: чанки 0.05 с в `chunks.csv` информационные, у frame-bound режимов выигрыш по цене не засчитывается (C6). Искать в `Study/integrators/results/cap_regime/Test563_*` и тестах политики | NO-EVIDENCE: CSV-строки нет; в production-профиле нет 6-DOF/тяги (chunks.csv — 13 задач S01…L1). Артефакты без CSV: tests/GalilegoSimTests/LiveChunkPolicyTests.cs:41-65; Tools/P1bTests/Tests/IntegratorPolicyRuntime.cs:126-138 | OFF-BY-OWNER (Q2 = НЕТ, вариант A; доказательство — NO-EVIDENCE) |
| **R3** | `SunDominant`, `SunCoastAllowed`, `CanCoast`, расстояние до Солнца `≥ 1e10 м`, рядом нет других тел | **DOPRI5** | Q3: НЕТ (только отчёт D4) | **в стенде доказательств нет**: класс `heliocentric` без задач (открытый вопрос 10). Код ссылается на «30–50 с и 600 с чанки», источник найти | NO-EVIDENCE: heliocentric-задачи нет в production (PRODUCTION_REPORT.md:88-89, вопрос 10); артефакты без CSV: LiveChunkPolicyTests.cs:16-38; IntegratorPolicyRuntime.cs:24-41; 600-с Sun-coast в репозитории не измерялся; dynamic_policy/results/20261114T215832Z_e4eba2d/dynamic.csv: R3,* | OFF-BY-OWNER (Q3 = НЕТ; D4: 1 AU PASS, 5 AU выбирает DOPRI5 — FOUND F5) |
| **R4** | GTO/Молния (S04, S05): e≈0.745, перигей-защита нужна | DEFAULT (**DOP853**) | Q4: НЕТ (BACKLOG B14) | WH-BM14A: GTO/Молния-only price **1.45× < 1.5**; S05 `1.07e-4` м = **1.12× floor** (верхняя оценка, не точность); S04 относительная ошибка 5.9×. WH-Y6 price 3.01×, но без J2-проверки для GTO. RKN12 price 2.48×, но wall и ошибка не проходят | qualification/results/20261114T193520Z_ff36340/matrix.csv: S04,WH-BM14A,M1,price_at_tier; S05,WH-BM14A,M1,price_at_tier (GTO/Молния-only 1.4521x); qualification/results/20261114T193520Z_ff36340/game_control.csv: S05,WH-BM14A,,err_max (1.0655e-4 = 1.119x floor); qualification/results/20261114T193520Z_ff36340/chunks.csv: S06,RKN12(10),600,wall_ms_per_chunk | OFF-BY-OWNER (Q4 = НЕТ) |
| **R5** | HEO (S14) | DEFAULT (**DOP853**) | — | WH-BM14A price **0.64×** (дороже); Prod-DOPRI5 price 1.27×, но err S14 `0.5612 м` и общий C6 не пройден | qualification/results/20261114T193520Z_ff36340/matrix.csv: S14,WH-BM14A,M1,price_at_tier (0.6424x); S14,Prod-DOPRI5,M1,price_at_tier (1.2704x); qualification/results/20261114T193520Z_ff36340/game_control.csv: S14,Prod-DOPRI5,,err_max (0.5612 м) | DEFAULT |
| **R6** | GEO (S06) | DEFAULT (**DOP853**) | — | WH-BM14A price **0.47×**; wall 600 FAIL S06 `0.009 > 0.0077 мс`. Prod-DOPRI5 price 0.60×. Граница R1 (≤ 2000 км) остаётся; любое будущее расширение на GTO не должно захватывать GEO условием по высоте (BACKLOG B14) | qualification/results/20261114T193520Z_ff36340/matrix.csv: S06,WH-BM14A,M1,price_at_tier (0.4697x); S06,Prod-DOPRI5,M1,price_at_tier (0.6000x); qualification/results/20261114T193520Z_ff36340/chunks.csv: S06,WH-BM14A,600,wall_ms_per_chunk (0.0087 > 0.0060) | DEFAULT |
| **R7** | пролёт / гипербола / SOI Луны (S12, S15, R4) | DEFAULT (**DOP853**) | Q8: НЕТ | Prod-DOPRI5 flyby price **1.48× < 1.5**; err S12 `6.002e-5`, S15 `2.552e-3`, R4 `9.934e-4` м (ниже целей 0.01 м, но относительный гейт не пройден). WH-BM14A flyby price **0.26×**; IAS15 0.64× | qualification/results/20261114T193520Z_ff36340/matrix.csv: S12,Prod-DOPRI5,M0.01,price_at_tier (flyby 1.4823x); qualification/results/20261114T193520Z_ff36340/game_control.csv: S12,Prod-DOPRI5,,err_max; S15,Prod-DOPRI5,,err_max; R4,Prod-DOPRI5,,err_max (6.002e-5 / 2.552e-3 / 9.934e-4 м) | OFF-BY-OWNER (Q8 = НЕТ) |
| **R8** | близкие к круговым орбиты с J2 на Земле, не в диапазоне R1 (< 200 км или > 2000 км) | DEFAULT (**DOP853**) | — | WH-Y6 дешёв (leo 8.66×), но **J2 ломает**: S03 `433.8 м`, S09 `0.9099 м` | qualification/results/20261114T193520Z_ff36340/game_control.csv: S03,WH-Y6,,err_max (433.8 м); S09,WH-Y6,,err_max (0.9099 м); qualification/results/20261114T193520Z_ff36340/matrix.csv: S03,WH-Y6,M1,price_at_tier (leo 8.6561x) | DEFAULT |
| **R9** | тела с `J2 == 0`, near-circular coast (гипотетически WH-Y6) | DEFAULT (**DOP853**) | Q6: НЕТ; в D1 только факты | WH-Y6 S04/S05 без J2 проходят, но порта нет; нужна отдельная ветка и решение | qualification/results/20261114T193520Z_ff36340/matrix.csv: S04,WH-Y6,M1,price_at_tier; S05,WH-Y6,M1,price_at_tier (без J2 — PASS); J2-контрпример — R8 (S03/S09) | OFF-BY-OWNER (Q6 = НЕТ) |
| **R10** | хаос / тесные сближения (гипотетически IAS15) | DEFAULT (**DOP853**) | Q7: НЕТ | IAS15 err PASS, но wall 600 с хуже в 1.9–11× на всех клетках, price leo 2.18×, geo 0.39× | qualification/results/20261114T193520Z_ff36340/chunks.csv: S01,IAS15,600,wall_ms_per_chunk; S06,IAS15,600,wall_ms_per_chunk (IAS15 хуже Prod-DOP853 на всех 13 задачах, 1.47–3.57x); qualification/results/20261114T193520Z_ff36340/game_control.csv: S04,IAS15,,err_max (4.4209e-6 > floor 3.3902e-6 → FAIL) | OFF-BY-OWNER (Q7 = НЕТ) |

---

## 3. Условия ветки R1 из кода (для дословной проверки в D1/D3)

`IntegratorPolicy.cs`, ветка Ported (константы: `PortedChunkSeconds=600`, `PortedMinAltitudeMeters=200_000`,
`PortedMaxAltitudeMeters=2_000_000`, `PortedMaxEccentricity=0.05`):

```
PortedAllowed && CanCoast && !SunDominant && !FlightModelActive
&& ChunkSeconds >= 600 && AltitudeMeters >= 200000 && AltitudeMeters <= 2000000
&& CentralMu > 0 && Eccentricity >= 0 && Eccentricity < 0.05
→ WhBm14a, иначе → Dop853
```

Порядок проверки в `Select`: Surface → Sun-coast → Ported → DOP853. Surface и Ported не пересекаются
(`FlightModelActive` разный); Sun-coast и Ported не пересекаются (`SunDominant` разный).
`VesselStep.ApplyIntegratorPolicy` включает `UseDenseRefinement = false` только при WH-BM14A: события
переинтегрируются от начала принятого шага.

---

## 4. Журнал расхождений (заполняет агент в D1)

| дата | строка | было в seed | по CSV | комментарий |
|---|---|---|---|---|
| 2026-11-14 | §5 ошибки, IAS15 | «IAS15: только S04 3.298e-6, ниже floor 3.390e-6 → PASS (прежнее +5 % было шумом)» | на чистом прогоне S04 4.4209e-6 > max(1.5×2.088e-6, floor 3.3902e-6) → FAIL; PASS остаются S01/S02/S03/S05/S06/S09/S12/S14/S15/R3; FAIL также L1 1.7328e-2 | старый прогон был dirty (game_control до Q-C3); R10 остаётся OFF-BY-OWNER (Q7 = НЕТ) |
| 2026-11-14 | §5 ошибки, RKN12(10) | S01 2.371e-4; S02 2.414e-4; S03 3.578e-5; S09 2.136e-4; S04 3.283e-5; S05 3.025e-3; S06 4.491e-3; R4 7.167e-7 | S01 7.704e-5; S02 7.039e-5; S03 4.494e-5; S09 8.038e-5; S04 2.665e-4; S05 5.186e-3; S06 3.436e-4; R4 6.259e-7 | все ячейки остаются FAIL относительно порога; числа — из чистого прогона |
| 2026-11-14 | §2/R4 wall 600, RKN12(10) | S02 0.0827 > 0.0666; S06 0.0323 > 0.0077; S12 0.0458 > 0.006 | S02 0.0082 < 0.0648 (PASS); S06 0.0082 > 0.0060; S12 0.0082 > 0.0059 | вердикт R4 «wall не проходит» держится на S06/S12; S02 больше не FAIL |
| 2026-11-14 | §2/R10 wall 600, IAS15 | хуже в 1.9–11× на всех клетках | хуже на всех 13 задачах: 1.47× (S15) … 3.57× (S06), 2.23× (S01) | числа из чистого прогона; R10 остаётся OFF-BY-OWNER |
| 2026-11-14 | §2/R6 wall 600, WH-BM14A | S06 0.009 > 0.0077 | S06 0.0087 > 0.0060 | тот же вердикт; S12 0.0136 > 0.0059 |
| 2026-11-14 | §2/R1 wall 600, WH-BM14A | 0.009 мс против 0.066 (7.3×) | 0.0087 против 0.0644 (7.4×) | тот же порядок; абсолютные ошибки, floor и цена 5.7278× совпали с seed |
| 2026-11-14 | §5 счётчики | 13 задач, 833 ячейки, PASS=746 FAIL=48 SKIP=39, прогон dirty | 13 задач, 833 ячейки, PASS=746 FAIL=48 SKIP=39, ERROR=0, dirty: clean | чистый прогон; статусы и цены совпадают с прежним (0 различий) |
| 2026-11-15 | §6 wall-гейт, R1 | стенд: WH 7.3× быстрее на целом 600-с шаге | D4 через `VesselStep`: 0.998–1.112× (12/12 FAIL при пороге ≤0.5×); причина — 30-с гранулярность событий (`FOUND.md` F4) | R1 → **REJECTED** по wall-критерию; ошибка ≤ 0.0528 м, события 0 с, аллокации PASS |
| 2026-11-15 | R3, 5 AU | TASK_D D4 R3: «5 AU и cislunar остаются на DOP853 при включённом флаге» | 5 AU → **DOPRI5**; cislunar-like → DOP853 | `FOUND.md` F5; флаг остаётся false (Q3 = НЕТ) |

---

## 5. Seed-числа для сверки (источники для D1.a)

Production-прогон `20261114T151403Z_e860827`: 13 задач, 833 ячейки, `PASS=746 FAIL=48 SKIP=39 ERROR=0`; прогон был `dirty`.

**Цена vs `Prod-DOP853` по классам (`price`, больше 1 — дешевле):**

| метод | leo | gto / molniya / heo | geo | flyby | game RHS× | wall 600 |
|---|---:|---:|---:|---:|---:|---|
| WH-BM14A | 5.73 | 1.11 | 0.47 | 0.26 | 0.49 | FAIL: S06 0.009 > 0.0077; S12 0.0137 > 0.006 |
| WH-Y6 | 8.66 | 2.29 | 0.97 | 0.41 | 0.25 | FAIL: S12 0.0065 > 0.006 |
| RKN12(10) | 8.18 | 1.93 | 0.71 | 0.93 | 0.15 | FAIL: S02 0.0827 > 0.0666; S06 0.0323 > 0.0077; S12 0.0458 > 0.006 |
| IAS15 | 2.18 | 0.99 | 0.39 | 0.64 | 0.87 | FAIL: все 600-с клетки, 1.9–11× медленнее |
| GJ8 | 10.70 | — | 5.82 | — | 0.12 | FAIL: все 600-с клетки |
| Prod-DOPRI5 | 0.86 | 1.09 | 0.60 | 1.48 | 1.19 | FAIL: S01 0.0756 > 0.066; S02 0.0761 > 0.0666; S03 0.0789 > 0.0684; S09 0.0794 > 0.0694; S05 0.0941 > 0.0911; S06 0.0097 > 0.0077 |

WH-BM14A: GTO/Молния-only (S04+S05) **1.45×**, HEO S14 **0.64×**.

**Ошибки FAIL по относительному гейту `max(1.5×Prod-DOP853, floor)`, метры:**

| метод | ячейки |
|---|---|
| WH-BM14A | S01 7.936e-6; S03 2.247e-2; S04 1.223e-5; S15 9.782e-6; R4 3.811e-5 (S02/S05/S09 — PASS) |
| WH-Y6 | S01 3.383e-4; S02 1.379e-3; S03 433.8; S09 0.9099; S04 5.024e-3; S05 0.2208; S14 0.1706; S15 0.03235; R4 0.05874 |
| RKN12(10) | S01 2.371e-4; S02 2.414e-4; S03 3.578e-5; S09 2.136e-4; S04 3.283e-5; S05 3.025e-3; S06 4.491e-3; R4 7.167e-7 |
| IAS15 | только S04 3.298e-6, ниже floor 3.390e-6 → PASS (прежнее «+5 %» было шумом) |
| Prod-DOPRI5 | S01 0.4662; S02 0.4662; S03 0.4652; S09 0.4711; S04 0.2722; S05 2.877 (M10); S14 0.5612; S06 3.072 (M10); S12 6.002e-5; S15 2.552e-3; R4 9.934e-4 |

**Для R1 (WH-BM14A), абсолютно:**

| задача | WH-BM14A | Prod-DOP853 | floor | цель | абсолютно | относительно |
|---|---:|---:|---:|---:|---|---|
| S01 | 7.94e-6 | 2.82e-6 | 5.02e-6 | 1 | PASS | FAIL 2.8× |
| S02 | 1.07e-6 | 8.39e-6 | 1.46e-6 | 1 | PASS | PASS 0.13× |
| S03 (J2) | 2.25e-2 | 1.13e-5 | 2.62e-6 | 1 | PASS | FAIL 1995× |
| S09 | 6.95e-6 | 7.83e-6 | 2.57e-6 | 1 | PASS | PASS 0.89× |
| S04 | 1.22e-5 | 2.09e-6 | 3.39e-6 | 1 | PASS | FAIL 5.9× |
| S05 | 1.07e-4 | 8.91e-5 | 9.52e-5 | 1 | PASS | PASS, 1.12× floor |
| S15 | 9.78e-6 | 7.75e-7 | 1.82e-6 | 0.01 | PASS | FAIL 12.6× |
| R4 | 3.81e-5 | 2.45e-7 | 3.68e-7 | 0.01 | PASS | FAIL 156× |
| L1 | 1.73e-2 | 3.08e-3 | 1.82e-4 | 1 | PASS (локальный эталон) | 5.6× |

L1 оценивается против локального certified DOP853: мировой dd-golden расходится на 8.52 м (`FOUND.md` F8).

**Самый дешёвый PASS по задачам (production; это не «самый точный» и не «кандидат на порт»):**

| задача | M100 | M10 | M1 | M0.1 | M0.01 |
|---|---|---|---|---|---|
| S01, S02, S09 | GJ8 | GJ8 | WH-Y6 | — | — |
| S03 | GJ8 | GJ8 | GJ8 | — | — |
| S04 GTO | DOPRI5-adaptive | WH-Y6 | WH-Y6 | — | — |
| S05 Молния | DOP853-adaptive | WH-Y6 | WH-Y6 | — | — |
| S06 GEO | GJ8 | GJ8 | GJ8 | — | — |
| S14 HEO | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive | — | — |
| S12 Moon SOI | GJ8 | GJ8 | GJ8 | GJ8 | GJ8 |
| S15 гипербола | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive |
| R4 | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive | DOPRI5-adaptive |
| R3 | GJ8 | WH-Y6 | WH-Y6 | WH-Y6 | — |
| L1 1000 витков | GJ8 | WH-Y6 | WH-Y6 | WH-Y6 | WH-Y6 |

Эта таблица **не** является правилом выбора. Победители GJ8 и study-вариантов в игру не переносятся (раздел 1).
