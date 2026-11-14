# BASELINE: Test564_MethodQualification (TASK.md v3 / Приложение B)

Указатель на прогон-эталон. Копия результатов лежит рядом:
`baseline/20261114T093336Z_f899097/`. Это **чистый** baseline (`dirty: clean`).
Он заменяет `20261112T233742Z_9580bd7` (описание сохранено в
`BASELINE_20261112T233742Z.md`); предыдущие эталоны — `BASELINE_20261112T184808Z.md`,
`BASELINE_20261112T204156Z.md`, `BASELINE_22f94b7.md`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-14 09:33:36 |
| commit | `f899097` (Q-B6 docs; код — `fecf0c4` (B5) + документы `9cfb814`/`f899097`) |
| ветка | `qualification/test564-method-qualification` |
| версия TASK.md | 3, SHA256 `d03f4941…` |
| профиль | `full` |
| `dirty` | **clean** |
| задач / методов | 47 / 15 |
| ячеек матрицы | 2925 (`PASS=1800 FAIL=194 SKIP=376 N/A=555 ERROR=0`) |
| строк `runs.csv` | 9452 |
| oracle | 47 `VALID`, 0 `INVALID` |
| время прогона | 7 162 407 мс (≈119 мин; машина была под нагрузкой) |
| `--compare` против `20261112T233742Z_9580bd7` | 0 статусов, **645 новых ячеек**, 0 ошибок >10 %, 0 цен >10 % |

## Что нового по сравнению с предыдущим эталоном

Приложение B целиком:

1. **Слой V** (`NASA_VALIDATION.md`, `nasa_validation.csv`): 8 моделей Test537/538
   (`Newton`, `1PN`, `EIH`, `1PN+J2`, `409small`, `409small+J2`, `409small+EJ2`,
   `409small+J2+EJ2`) на окнах 2026–2036 и 2016–2026 + спутники Test544
   (`nasa_satellites.csv`). V0 пройден (day-0 `0`, EIH−1PN `2.009e-10 м/с²`, отношение
   масс спутников Марса `1.891e-08`, GM эпох совпадают). **G9: 255 сравнений, 0 дрейфа.**
   Блок ограничений (6 пунктов) перенесён из `Study/jpl/README.md`.
2. **Тир H** (слой I): H1, H1h, H2h, H3, H6 + цели у R2.
   Эталоны и floor: H1 `1.053 м`, H1h `2.860 м`, H2h `2.573 м`, R2 `1.969 м`,
   H3 `2.231 м`, H6 `2.654 км`.
   H3 сверен с Test546 (`2.441 м`), H6 — с Test544 (`6.014 км`).
3. **Цели Q** (`Q10`, `Q1`) — B6: `rms8_ref ≤ factor × E_model` и
   `moon_ref ≤ factor × E_model(Moon)`; `E_model` берётся из слоя V этого же прогона.
   75 ячеек на уровень (5 задач) плюс справочная колонка против Horizons в `real_system.csv`
   (в ранжировании не участвует).
4. **`REAL_SYSTEM_REPORT.md`** и вспомогательный `real_system.csv`; `conditions.csv`
   дополнен `epoch_window`, `n_bodies`, `model`; `targets_evaluated` включает Q.
5. **Resume**: задачи, добавленные после создания папки, аудируются и дописываются
   (`QualificationAuditMissing`), готовые не пересчитываются.

## Состав каталога (ячейки)

| тир | задачи | ячеек |
|---|---|---|
| K/T | K1–K8, T1–T3 (A1–A3) | 11 × 45 = 495 |
| R/L | R1–R4, L1, L2: A1–A3 + M100…M0.01 (R1/R3/R4/L1), R2: A + M100…M1 + Q10/Q1, L2: A | 4 × 120 + 120 + 45 = 645 |
| S | S01–S12, S14, S15, S17 (M100…M1; S12/S15 ещё M0.1/M0.01) | 13 × 45 + 2 × 75 = 735 |
| P | P2–P6 (M100…M1) | 5 × 45 = 225 |
| F | F01–F05 (M100…M1; F03 ещё M0.1/M0.01) | 4 × 45 + 75 = 255 |
| H | H1, H1h, H2h, H3 (A1–A3 + M100…M1 + Q10/Q1), H6 (A1–A3 + M100…M1) | 4 × 120 + 90 = 570 |

Итого 2925. По целям: A1/A2/A3 по 330, M100/M10/M1 по 525, M0.1/M0.01 по 105,
Q10/Q1 по 75.

## Особенности эталона

* **H3** (full409): floor `2.231 м`; A1/A2/M100/Q10 оцениваются, A3/M10/M1/Q1 — SKIP
  по floor, 10 методов N/A по `Supports`. На A1 победитель DOP853-adaptive с ошибкой
  `91.7 км` (интегратор грубее модели 1.133 км на этой цели), на Q10 — `4.08 км`.
* **H6** (спутники, 10 лет): **ни одной оценённой ячейки** — 10 N/A и 5 SKIP
  (`oracle floor`/`RHS budget`). Система хаотична, бюджет `1e6` RHS на ячейку не позволяет
  дойти до A1. Записано в `FOUND.md` F12.
* **H1**: `E_model` 739.0 км; победитель A1/A2/M100/Q1 — RKN12(10), на M100 ошибка
  `0.0468 км`. **H1h**: `E_model` 1106.9 км; A1/Q10 — GJ8, A2/M100/Q1 — RKN12(10).
  **H2h**: `E_model` 2.657 км; A2/M100/Q1 — IAS15 (`0.027 км` на M100).
* **R2** (1PN): A1 — DOP853-adaptive, A2/M100/Q1 — IAS15, Q10 — DOP853-adaptive.
* **P2** (100 лет): 27 SKIP по бюджету, 9 N/A (WH/Encke), 8 PASS, 1 FAIL
  (DOPRI5-adaptive M100, G5 abort).
* **Encke** на S10/S11/S17 оценивается (решение владельца), N/A только у 9 методов
  расщепления + RKN12/GJ8.
* **G2**: RK4 на LEO и R1/R2/R3 — FAIL с измеренным наклоном (FOUND F6/F11);
  пороги не менялись.

## Файлы копии

| файл | содержание |
|---|---|
| `environment.txt` | дата, commit, .NET, CPU, профиль, SHA256 TASK.md, `dirty: clean` |
| `oracle_audit.md` | G8 по 47 задачам (все VALID) |
| `runs.csv` | 9452 строки: (метод, задача, control) |
| `matrix.csv` | 2925 ячеек: (метод, задача, цель) |
| `conditions.csv` | 47 строк: экстремумы, силы, floor, цели, `epoch_window/n_bodies/model` |
| `nasa_validation.csv`, `nasa_satellites.csv` | слой V: 1584 + 15 строк |
| `real_system.csv` | 174 строки: Q-метрики и справочная колонка против Horizons |
| `NASA_VALIDATION.md` | слой V, G9, метрики B5, блок ограничений |
| `REAL_SYSTEM_REPORT.md` | тир H: E_model, цели, «интегратор против модели», Horizons, класс |
| `CONDITIONS_REPORT.md` | условия и результаты по целям, классы |
| `METHOD_QUALIFICATION.md` | сертификаты, матрица, N/A/FAIL/SKIP, «Valid for» |
| `RECOMMENDATIONS.md` | §11 по тегам и целям (A, M, Q) |
| `COST_TABLE.md` | цены/времена по целям, инварианты |
| `compare.md` | `--compare` против `20261112T233742Z_9580bd7` |
| `run.log` | полный лог |

`run.log` добавляется в git принудительно: корневой `.gitignore` игнорирует `*.log`.

## Как сравнивать

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification --compare Tools\P1bTests\Study\qualification\baseline\20261114T093336Z_f899097
```

Отчёт пишется в `compare.md` нового прогона: изменения статусов, ошибок > 10 % и цен > 10 %.

## История эталонов

| baseline | commit | профиль | что изменилось |
|---|---|---|---|
| `20261111T171630Z_4df81a2` | `4df81a2` | full | R1 на тестовом фикстуре |
| `20261111T183727Z_c979ab0` | `c979ab0` | full | Q-FIX-1…10 |
| `20261111T192603Z_4270a7f` | `4270a7f` | full | Q-FIX-1…10b |
| `20261111T200320Z_2a36227` | `2a36227` | full | Q-FIX-12…14 |
| `20261111T203415Z_becd26a` | `becd26a` | full | решения владельца, full-бюджет |
| `20261111T225110Z_22f94b7` | `22f94b7` | full | `COST_TABLE.md`, R3 floor |
| `20261112T184808Z_2b3c95a` | `2b3c95a` | full | Приложение A (S0–S6), первый baseline A |
| `20261112T204156Z_919fb42` | `919fb42`+Q-FIX-19 | full | G4/G8/conditions/отчёты; dirty |
| `20261112T233742Z_9580bd7` | `9580bd7` | full | Q-FIX-20: G2-уточнение, классы, Encke (а); clean |
| **`20261114T093336Z_f899097`** | **`f899097`** | **full** | **Приложение B: слой V, тир H, цели Q; clean** |
