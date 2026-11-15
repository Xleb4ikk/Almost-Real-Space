# Инструкции для агента

## Активное задание: RKN12(10) как production-интегратор выпечки эфемерид (TASK_RKN12.md, версия 1)

Перед любым действием в этом репозитории:

1. Прочитай `Tools/P1bTests/Study/rkn12/TASK_RKN12.md` **целиком**. Файл заморожен: агент его не редактирует.
2. Прочитай `Tools/P1bTests/Study/rkn12/PROGRESS.md` с конца (если файла нет — это начало, создай его в R0).
3. Продолжи ровно с текущего этапа R0–R7. Один этап за раз.

Если этот `AGENTS.md` не был загружен автоматически (в репозитории нет корневого `AGENTS.md`),
открой его по пути выше явно. Рабочая папка сессии — `Tools/P1bTests/Study/rkn12/`.

Кратко (полные правила — только в TASK_RKN12.md):

- RKN12(10) добавляется **только в offline-выпечку эфемерид** (`EphemerisBaker`), не в runtime корабля.
  Runtime-интегратор корабля остаётся DOP853; `SpacecraftPhysics`, `IntegratorPolicy`, `VesselStep`,
  `SimulationRunner`, `SpacecraftIntegrator` не трогать.
- Работа только в ветке `rkn12-bake`, созданной от коммита `Q-RK-TASK`; слияние делает владелец.
- Игровой код меняется только в границах §4 TASK_RKN12.md: новые файлы в `Ephemeris/`,
  минимальные правки `EphemerisBaker.cs`, `StarSystemAuthoring.cs`, `StarSystemAuthoringEditor.cs`,
  `Tools/GameSources.props` (по строке на каждый новый файл).
- Не трогать: DOP853/DOPRI5, события, источники сил, сцены, префабы, ассеты, `BakedEphemeris`,
  `EphemerisRuntime`, `qualification/`, `dynamic_policy/`, `Study/integrators/`, `Study/jpl/`.
- `Dop853Fixed = 0` и инициализатор поля bake-интегратора остаются **навсегда**: Unity иначе
  изменит старые сцены. RKN12 включается только явной установкой поля владельцем для конкретной
  системы; глобального default switch нет. Решения задания — `RQ1…RQ10`, не путать с `Q5` из
  `TASK_D.md`.
- Пороги §8 не менять. Провал ворот — стоп и «ЗАПРОС РЕШЕНИЯ ВЛАДЕЛЬЦА» в `PROGRESS.md`.
- 1024-летний reference — независимый (аналитика для 2-body, dd-Taylor для 4-body/P3); tighter DOP853 —
  только consistency, не единственный эталон.
- После каждого этапа: `Test551` на прежнем хэше, оба xUnit-набора, `Test565`; если менялся
  `Tools/GameSources.props` — ещё `--compare` стенда.
- Идеи вне задания — в `BACKLOG.md` папки R; баги вне задания — в `FOUND.md`; не реализовывать и не чинить.
- Коммиты: `Q-RK<n>: …`. В конце сессии обнови `PROGRESS.md` и закоммить.
- Любая неясность или противоречие с TASK_RKN12.md — остановись и спроси владельца.
