# Фаза 1 — верификация на эталонной машине (Windows)

Патч: `promtII/phase1_r2.patch` (от пользователя), применён на HEAD
`2c2a024` (ревизия 2 фазы 0). Оригинальный файл патча пришёл со смешанными
EOL (CR=974, LF=1029) и на CRLF-дереве Windows не проходил контекст;
применена LF-нормализованная копия `promtII/phase1_r2_lf.patch`
(`git apply --ignore-whitespace`), содержимое идентично.

HEAД после применения + проверок: см. коммит фазы 1 в `git log`.

## Прогоны (32 ядра)

| Набор | Кейсов | PASS-проверок | FAIL | Время |
|---|---|---|---|---|
| стенд `--fast` | 127 (123 + 200–203) | 297 | 0 | 623 с |
| стенд `--slow` (полный) | 36 | 47 | 0 | 2154 с (сумма по кейсам 2150 с) |
| `dotnet test` (xunit) | 10 | 10 | 0 | 0.49 с |

- Логи: `baseline/phase1/fast.txt`, `slow.txt`, `xunit.txt`.
- Новые тесты 200–203 на Windows проходят отдельным прогоном
  (`--from 200 --to 203`): T203 |ΔCOM|=8.2e-13 м, max|ΔI_диаг|/I_max=5.9e-5,
  max|ΔI_вне|/I_max=1.5e-16 (пороги те же).
- Медленные кейсы, затрагивающие посадку/части, в полном `--slow` зелёные:
  `Test29_DescentChain` (139 мс), `Test30_YearDrift`, `Test31_Flyby`,
  `Test32_Escape`, `Test33_Ascent`, `Test37_LunarYears`.

## Трейсы после `DryMassKg = 3000`

`baseline/phase1/traces_compare.txt`: все шесть файлов (S1–S6) побайтно
совпадают с каноническими LF-эталонами ревизии 2, включая S6 на Windows.
Внутренняя двойная проверка инструмента — `ALL REPEATS IDENTICAL`.
Подтверждает §14.5 п.5: при выключенной тяге сухая масса на трейсы не влияет.

## Unity (редактор 6000.6.0f1, редактор был открыт)

- Компиляция: `recompile_status` — `completed`, `compilationFailed=false`;
  `console_status` — `consoleErrors=0`. Ошибок нет.
- Загрузка T1: добавлен editor-инструмент
  `Assets/_Project/Scripts/Editor/TestVesselSceneTool.cs`
  (меню `Galilego > Test Vessel > Apply T1 From JSON`). Он читает
  `Tools/P1bTests/Data/test_vessel_t1.json`, собирает `PartDefinition[]`
  и записывает в `SimulationRunner.Parts` активной сцены, затем сохраняет сцену.
- Выполнено через Pipeline (`eval_file` + рефлексия по Assembly-CSharp-Editor):
  «T1: 8 деталей, сухая масса 3000 кг, сцена "OutdoorsScene" сохранена».
- Проверка сцены после сохранения: `DryMassKg=3000`, `Parts.Length=8`,
  массы 1000/2000/500/50×4/1300, ноги — цилиндры, кабина — коробка.
  Сериализация `Vector4 Orientation` и enum `PartShape` прошла (Shape: 2 = Cylinder).
- Изменённые строки сцены: `DryMassKg: 1000 → 3000` (патч) + блок `Parts` (инструмент).

## Гейт фазы 1 (§7)

1. Кейсы «сохранить» зелёные — **да** (`Test30_PartsTrap`, `Test31_*`,
   `Test20_*`, `Test29_DescentChain`, `Test70`, `Test78`, `Test33_*` в fast/slow).
2. Новые 200–203 зелёные — **да** (Windows, отдельный прогон + в fast).
3. Трейсы S1–S5 не изменились — **да** (побайтно; S6 — тоже, на эталонной машине).
4. `DryMassKg = 3000` — **да** (сцена + значение по умолчанию в `SimulationRunner`
   + константа трейс-стенда).
5. Unity компилируется, T1 загружен в сцену — **да**.

## Решения и отклонения

- Масса детали — явная; форма задаёт инерцию (ТЗ §14.5 п.1). Плановый
  `Test201_ShapeMassFromDensity` заменён на `Test201_ExplicitMassAndScaling`;
  запись в журнале §11.
- Данные-только поля стыков (`ParentIndex`, `JointAnchor`, `BreakTorqueNm`,
  `EngineOffset`) не проверяются до фаз 2/7/8 (§14.5 п.3).

## Замечания

- `.gitignore`: дамп `Test903_ShelfFixAb` теперь покрыт `**/shelf-fix-dump.txt`
  (находка отчёта фазы 1, п.2).
- Патч-файлы и отчёт пользователя лежат в `promtII/`.
