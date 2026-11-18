# План исправлений коллизий

Дата: 2026-11-18
Ревизия: **9** (по результатам исполнения)
Статус: **реализовано, часть проверок осталась за Unity**
Основание: статический аудит + прогоны в срезе (см. §1.7 и §16)
Смежные документы: `promtII/ksp_collision_spec_v2.md`, `baseline/slice/verification.md`

Цель — убрать ошибки, при которых корабль/игрок проваливается, «взрывается» на площадке,
течёт память и тратится кадр. Порядок фаз выбран так, чтобы каждая следующая опиралась
на замеры предыдущей и не маскировала ошибки кадра настройкой солвера.

**Первый шаг — раздел 2 (фаза 0):** снять пять чисел и прогнать `land00`, затем `land30`.
Без этих данных любая правка будет угадыванием; если `land30` не воспроизводится —
FIX-01 снимается с повестки, а план начинается с **FIX-04** (подтверждённый живой баг,
он же искажает `land30`/`handoff`) и затем FIX-02.

---

## 1. Что изменилось в ревизии 2

| # | Было в ревизии 1 | Стало |
|---|---|---|
| 1 | FIX-03 удалял sync и в `SyncVesselRoot`, и в `SetPlatformFrame` | `SyncVesselRoot` **оставлен** (он в `Update` прямо перед `StepPlayer`); удаляются только per-substep `SetFrame`/`SetPlatformFrame` |
| 2 | FIX-01 собирал патч вокруг `frame` (кадр входа) | вокруг `current = frame.At(t)`, иначе меш уезжает от корня на сотни метров |
| 3 | FIX-01 аварийно выходил из зоны | + запрет входа и `gate.Reset()` в `Exit`, иначе цикл вход/выход каждый кадр |
| 4 | FIX-04 «латентно, замаскировано» | **живой баг**: `ApplyRigidState` возвращает тот же неверный вектор; проверять Δv в `handoff` |
| 5 | FIX-02 «20 итераций постоянно» | `defaultSolverIterations` после создания тел на них не влияет; но глобальное значение остаётся 20 и **второй вход** идёт на других итерациях. Правка per-body |
| 6 | FIX-06 «COM отличается у всех» | только у **frustum** (капсула); `verification.md:74` — про тензор, не про COM |
| 7 | FIX-12 «CCD не работает с jointed» | CCD работает; причина — стоимость и рекомендация фильтровать CCD между связанными телами |
| 8 | FIX-07 `IgnoreCollision` всех пар | внутри острова — игнор, между островами (обломки) — столкновения; карта островов |
| 9 | FIX-08 `Destroy(mesh)` сразу | сначала `sharedMesh = null`, потом `Destroy` (как в `TerrainPatch.Clear`) |
| 10 | FIX-10 буфер 32, без маски | буфер 64 + именованная маска + лог переполнения; слои — отдельная задача |
| 11 | P1b `152/152`, SimTests `17/17` | P1b: 278 кейсов, **180** в `--fast`; SimTests: **68** `[Fact]/[Theory]` |
| 12 | Новые тесты `Test309–312` | заняты (310–320 — `TestsPlayerSupport`), брать **Test321–324** |
| 13 | `slope30`/`hatch100`/`handoff` в `ContactZoneSliceProbe.Case` | срез — плоский `SphericalTerrain` + T1 без капсулы и полётной модели; разнести по сценам |
| 14 | DEC-01: «Test306–308 — решатель» | 306 — `AdvanceTime`, 307/308 — `ZoneIslands`, и `ZoneIslands` **используется** рантаймом |

## 1.1. Что изменилось в ревизии 3

| # | Замечание | Правка |
|---|---|---|
| 1 | FIX-03: синк «в конце `Enter`» стоял бы **после** `GetCenterOfMassState` | синк перенесён сразу после `Proxy.Build`/`HandoffIn`, **до** `GetCenterOfMassState`; добавлен в критерий |
| 2 | FIX-01: `Proxy.LastContactCount` не существует | ссылка убрана; вместо неё — таймер и предложение добавить счётчик контактов только если просадка проявится |
| 3 | FIX-07: `SeverForStaging` сваливал все `keep == false` в один остров | `stagedCuts` + острова по графу (`ZoneIslands.Split`); заодно чинится потеря стыковочных разрезов при следующем разрыве |
| 4 | FIX-10: `SupportLayers = ~0` — маска без фильтра | явно: FIX-10 закрывает только GC; для правила маски добавлен **FIX-10b** (слои) |
| 5 | FIX-01: порог входа 0.5 м мог навсегда заблокировать низкий вход | порог заменён на защёлку `entryBlocked`, снимается выше 5 м; вход разрешён при `altitude > 0` |
| 6 | FIX-01: лог `[CZ] патч пересобран` отсутствовал в сниппете | добавлен |
| 7 | FIX-08: аналогия с `OrionCollisionMeshes` неточная | уточнено: обнуление `sharedMesh` — приём из `TerrainPatch.Clear`, `OrionCollisionMeshes` только уничтожает |
| 8 | FIX-06: не упомянут `automaticInertiaTensor` | добавлен `rb.automaticInertiaTensor = false` перед `inertiaTensor` (в 6000.1 это записываемое поле) |
| 9 | `Vector2` в «чистом» `ContactZoneGate` | чистые функции переведены на `double x, double z`; гейт остаётся без `UnityEngine` |
| 10 | «точный патч 120×120, шаг 2 м» — это цель, не текущее | подписано «целевой шаг ~2 м (Resolution 61)» |

## 1.2. Что изменилось в ревизии 4

| # | Замечание | Правка |
|---|---|---|
| 1 | неверные строки: `SyncVesselRoot` 1730, `CabinGravity` 1804-1816 | 1914-1931 (sync 1930), 2004-2020 (gravity 2010) — сверено с текущим файлом |
| 2 | «8 sync» вместо 9 | 9: `SpacecraftBuilder` 466/562/1120, `SimulationRunner` 1930, `ContactZoneProxy` 223, `ContactZoneHost` 157, `MeshSupportBase` 112/129, `SurfaceSite` 190 |
| 3 | FIX-03: критерий не сходится — остаются `ApplyRigidState` и `ReturnToBuild` | список синков расширен до фактических 6; `CheckCapsuleQueries` теперь *добавляет* sync (его там нет) |
| 4 | FIX-07: `stagedCuts` не чистится | `stagedCuts.Clear()` в `Build` и `Clear` |
| 5 | FIX-01: `entryBlocked` не снимается в `ForceExit` | сбрасывается в `ForceExit` и в `Enter` |
| 6 | FIX-01: `lastPatchRebuildTime` не сбрасывается при откате времени | сбрасывается в `Enter` |
| 7 | FIX-01: аварийный `Exit` ниже −2 м не спасает | добавлена «Правка 3»: rescue на последнюю валидную точку после `Exit` |
| 8 | FIX-01: `CanEnter(>0)` может блокировать взлёт с площадки | добавлен прогон `build → flight`; если пад на ≈0 — допуск или site-aware проверка |
| 9 | «бесконечное падение» | на самом деле ~12 с, ~735 м от входа (~435 м ниже поверхности) до `speed > 120`, затем выход; корабль остаётся под землёй |
| 10 | «≤ 3 пересборок» на `land30` | ≈4 для среза (60 м триггер) и ≈10 для Outdoors (24 м); критерий переписан |
| 11 | §4.1: `iters = 6` не виден через глобальное значение | логировать `BodyAt(0).solverIterations` (per-body) |
| 12 | CLEAN-01: `rg "TryResolve\("` даёт ложные срабатывания | проверка сужена до `SiteBoxRegistry\.TryResolve|PushOutMesh\(` |
| 13 | FIX-10: 64 «потому что 150 коллайдеров» — неверно | 64 — запас; `RaycastAll` возвращает только попадания вдоль луча |
| 14 | FIX-06: `automaticInertiaTensor` и версия Unity | проект 6000.6, свойство записываемое; добавлен `automaticCenterOfMass = false` |
| 15 | Test323/324: `ContactZoneProxy` не в `GameSources.props`, `ToUnity` private | математика вынесена в чистый `ContactZoneFrameMath` и добавлена в props |
| 16 | FIX-10b: класса `EvaProbe` нет | `evaProbeCollider` (private) и объект `SiteProbe` на layer 2 |
| 17 | FIX-02: не упомянуты velocity-итерации | проект 1, код ставит 4 после создания тел — тот же недетерминизм |
| 18 | FIX-09: `Clear()` снимает id с живых build-mode коллайдеров; `warnedNonConvex` не чистится | выбран вариант A (prune), добавлена чистка `warnedNonConvex` |

## 1.3. Что изменилось в ревизии 5

| # | Замечание | Правка |
|---|---|---|
| 1 | **FIX-07: `ZoneIslands.Split` берёт корень от `ParentIndex < 0`, а при отстреле корень (двигатель) улетает** | `Split` получил явные `rootSeeds`; `SeverForStaging` передаёт `keep`-индексы, разрыв стыка — по умолчанию `ParentIndex < 0` |
| 2 | **FIX-01: `lastValidCom` — это низ, а не COM** | `current.ToWorld(com, comVelocity, ...)` вместо `anchor` |
| 3 | FIX-01: `lastValidOk` не сбрасывается | `false` в `Enter` и `ForceExit` |
| 4 | FIX-01: rescue возвращает точку в 1 м с той же скоростью вниз | снимок только выше 5 м; при восстановлении `+10 м` вверх и обнуление радиальной скорости; защёлка снимается на следующем кадре, зона входит заново |
| 5 | FIX-01: `entryBlocked = false` в `ForceExit` после раннего `return` | сброс — **первой строкой** метода, до `if (!Active)` |
| 6 | FIX-01: снимок до `gate.Update` даёт высоту прошлого кадра | снимок после `gate.Update` |
| 7 | FIX-04: `AstroFrame` без `using Galilego.Universe` | добавлен `using` (или полное имя) |
| 8 | FIX-03: `SurfaceSite.OnEnable` двигает коллайдеры вне `LateUpdate` | отмечено отдельно: локальный sync в `OnEnable` (редкий путь) или один кадр старой позы |
| 9 | §3: «8 `SyncTransforms`» | 9 |
| 10 | §1: историческое «310–312» | 310–320 |
| 11 | FIX-10: «сотни массивов за кадр» | «десятки–сотни» (зависит от числа подшагов) |

## 1.4. Что изменилось в ревизии 6

| # | Замечание | Правка |
|---|---|---|
| 1 | **`OnPartJointBreak` без seeds: после отстрела следующий разрыв снова сеет от `ParentIndex < 0`** | `CurrentRootSeeds()` — «основа» текущего корня; **не** все `rootMask`-детали: только что отломанный ребёнок ещё помечен корнем, и заливка от него объявила бы корнем его поддерево |
| 2 | `DetachedIslandCount` без seeds | те же `CurrentRootSeeds()` + `AllCuts()` |
| 3 | rescue: горизонталь не клампится, при > `EnterSpeedMps` зона не войдёт | кламп до `0.8 · EnterSpeedMps` |
| 4 | `lastValidShipPoint` в `Enter` — это `ship.Position` (в полёте COM, не низ) | низ считается явно: `com − anchorUp` |
| 5 | FIX-03 `Enter` без `lastValidOk = false` | добавлено; сниппет ссылается на rescue-снимок FIX-01 |
| 6 | `SurfaceSite.OnEnable` не в критерии FIX-03 | добавлен в критерий как условное исключение |
| 7 | `RescueLiftMeters` — публичное поле | tooltip + пометка «если не нужен в инспекторе — `[SerializeField] private`» |

## 1.5. Что изменилось в ревизии 7

| # | Замечание | Правка |
|---|---|---|
| 1 | **rescue клампит мировую горизонталь, а гейт проверяет скорость относительно поверхности** | кламп относительной горизонтали: `relative = velocity − surfaceVelocity`, затем `ship.Velocity = surfaceVelocity + horizontalRel` |
| 2 | тест №2: после отстрела двигатель уже detached, `Detached.Count == 1` упадёт | проверять по деталям (`rootMask[бак]`, `islandOf[двигатель]`, `islandOf[отделитель]`) и `Detached.Count == 2` |
| 3 | `keep` может быть несвязным — обе группы получат island 0 | зафиксировано допущение «keep — одна связная группа»; тест №3 относится к **detached** группам |
| 4 | `lastValidShipPoint` в `Enter` — `com − anchorUp` по X/Y, а не `lp` | используется `lp` с точным комментарием: это `ship.Position` на входе (низ в стройке, COM в полёте), rescue берёт ветку по `PositionIsCom` |
| 5 | Appendix B не упоминает условный `SurfaceSite.OnEnable` | добавлен |
| 6 | Appendix A: 339 — метод, присваивание `Physics.gravity` — 344 | поправлено на `339-345 (344)` |

## 1.6. Что изменилось в ревизии 8

| # | Замечание | Правка |
|---|---|---|
| 1 | FIX-05: «дефолт 7 рад/с» неверен — в проекте `m_DefaultMaxAngularSpeed: 50` | FIX-05 переписан: сначала измерить `rb.maxAngularVelocity`/`maxLinearVelocity`; `50` — no-op, линейный предел поднимать только по замеру |
| 2 | FIX-01: «~700 м под поверхность» | ~735 м от входа → ~435 м ниже поверхности (300 + 435) |
| 3 | Test323: `ZoneToWorld(SiteToZone(v)) ≠ v` математически неверно | round-trip `== v`; «наивное» `SiteToZone(v)` в роли мира `≠ v` |
| 4 | FIX-09: критерий «`nextPartId` стабилен» невыполним при prune | убрано; критерий — размеры словарей ограничены, `nextPartId` растёт линейно (не утечка) |
| 5 | FIX-08: сниппет закрывает только Cylinder | добавлена Frustum-ветка (`Prism(..., 16)`) |
| 6 | FIX-06: frustum — не только капсула; Point даёт нулевой тензор | уточнено; тензор не задаётся для `PartShape.Point` |
| 7 | FIX-01: обоснование `gate.Reset()` в `Exit` | reset нужен для нового пути `BelowFloor` (при обычном `Exit` `Inside` уже false) |
| 8 | FIX-01: пороги −0.5 и −1 расходятся | везде −0.5 м; rescue — при −2 м |
| 9 | FIX-01: `lastValidOk = true` в `Enter` без проверки высоты | объяснено: вход разрешён только при `altitude > 0`, lift 10 м делает rescue безопасным и для 0.2 м |
| 10 | приоритет: FIX-04 не зависит от `land30` | первый коммит — FIX-04, затем FIX-02, затем FIX-01 |
| 11 | FIX-03: не проанализированы читатели физики, не только писатели | добавлена таблица читателей и вывод: локальный sync нужен только `CheckCapsuleQueries` |
| 12 | FIX-07: мгновенное включение коллизий после разрыва может дать импульс | добавлен риск и запасной вариант с задержкой 1–2 кадра |
| 13 | FIX-03: откат перечисляет не все удалённые синки | добавлены `ApplyPose`, `UpdateVisuals`, `MirrorVisualsToBodies` |
| 14 | Appendix A: описание `114-143`, `automaticInertiaTensor` в `PhysicsModule.xml`, нет `m_DefaultMaxAngularSpeed` | поправлено, добавлена строка 38 и `m_DefaultMaxDepenetrationVelocity: 10` |

## 1.7. Ревизия 9 — исполнение

Ветка `collision/audit-fixes`, 14 коммитов, компиляция чистая, Console errors = 0.
Журнал — §16 (файл `promtII` в `.gitignore`, поэтому журнал не в коммитах).

| FIX | Статус | Доказательство | Остаток |
|---|---|---|---|
| FIX-04 | **done** | `land30`: Δv 59.96 → **0.0000**, ход +30.0 м за 1 с | — |
| FIX-02 | **done** | `stand` ×2: `iters_body0=20` на обоих, дрейф 1e−5…2e−5; `drop100` 100/100; `rg` пуст | `breakLeg` момент 7.5e−6 при пороге 1e−6 (до FIX было 7.6e−6 — не регресс) |
| FIX-01 | **done, 2 коммита** | `land00` +1.21 м; `land30` 4 пересборки, 1 вход/0 выходов; rescue: 1 выход, +10 м, повторный вход, горизонт 0.71 м | `land30` осел на −1.79 м (удар 68 м/с) — критерий −0.5 м не применим, см. ниже; `build → flight` не проверен |
| FIX-03 | **done** | `autoSync=false`; ровно 7 `SyncTransforms`; `stand`/`drop100`/`land30` без регресса | `hatch100`, EVA, прогулка, профайлер — нет |
| FIX-05 | **closed by measurement** | `maxAngular=50`, `maxLinear=1e16` — оба выше 120 м/с | — |
| FIX-06 | **done** | COM = `d.Offset`; `stand` стабилен | `handoff ΔCOM` не измерен (в срезе нет полётной модели) |
| FIX-07 | **done** | `rootSeeds`/острова/`stagedCuts`/отложенные пары; `breakLeg` 6.36e−6; `drop100` 100/100 | «корень внизу» и две detached-группы не прогонялись |
| FIX-08 | **done** | 20 входов/выходов: `Mesh` 388 → 388 | — |
| FIX-09 | **implemented** | prune + чистка `warnedNonConvex` | порог 256 не достигнут (8×20=160), prune не сработал |
| FIX-10 | **implemented** | `rg "RaycastAll|CapsuleCastAll"` пуст; пробы работают | `GC.Alloc` и `hatch100` не измерены |
| FIX-10b | **deferred** | — | нужны `TagManager.asset` и слои в сценах/префабах |
| FIX-11 | **implemented** | `Probe(radius,height)`, реальная высота в `TryPushOut` | `hatch100`/присед не проверялись |
| FIX-12 | **implemented** | `ContinuousDynamic` только на корне; `drop100`/`land30` без туннелирования | `Physics.Processing` не сравнивался |
| FIX-13 | **deferred** | — | профиль и `Physics.BakeMesh`, риск API |
| FIX-14 / DEC | **deferred** | — | исследования, отдельный документ |

**Отклонения от плана, подтверждённые замерами** (в плане оставлены как история, в коде — так):

1. **Rescue-снимок — site-local, не world.** Мировой снимок дал уход 3059 м (снимок стар на 0.1–0.2 с при орбитальной скорости 30 км/с). Site-local → 0.71 м.
2. **Rescue ставит `VesselRegime.Flying`.** Иначе `StepLanded` стирал +10 м, проецируя корабль обратно на грунт.
3. **Триггер пересборки — от последнего центра патча, не от корня зоны.** Иначе 35+ пересборок за 17 с; от центра — 4.
4. **`anchorUp` пересчитывается по низу текущего корневого острова.** Снятый на входе, после отрыва ног врал на −4.87 м и вызывал ложный rescue.
5. **Пары коллизий включаются по непересечению `Collider.bounds`** (кап 30 кадров), а не по таймеру 2 кадра (таймер не спас `breakLeg`).
6. **`land30` — это краш-тест 68 м/с.** Критерий «−0.5 м» к нему не применим: отдельный `crash30` с критерием «нет туннелирования, пенетрация ≤ 2 м / разрушение», а `land30` для края патча — с управляемым снижением (вертикаль ~5 м/с).

**Открытые ворота (только Unity):** `hatch100`, EVA у люка, прогулка по площадке,
`handoff` (ΔCOM/Δv/Δattitude), `build → flight` (риск: `CanEnter(altitude>0)` на паде),
«корень внизу» + две detached-группы, профайлер (`SyncTransforms`/`GC.Alloc`/`Processing`/
пик FIX-13), 20 посадок с достижением порога prune.

**Внешний блокер:** `Tools/P1bTests --fast` не собирается — `GasGiantProfileAsset` нет в
`Tools/GameSources.props`, затем `UnityStubs` без `KeyCode.RightControl`, `Color.gray`,
`Mathf.InverseLerp/SmoothStep`. Это не регресс коллизий, но без него 180/278 не проверить.

---

## 2. Правила работы

1. **Сначала замер, потом правка.** Ни один пункт не считается сделанным без лога/пробы.
2. **Один FIX — один коммит** в ветке `collision/audit-fixes`. Откат любого пункта не тянет за собой остальные.
3. **Не менять два слоя сразу.** Сначала кадр/порядок, потом формы, потом тюнинг (правило скилла: frame → shapes → tuning).
4. Перед мержем: `dotnet test tests/GalilegoPhysicsTests`, `dotnet test tests/GalilegoSimTests`, `Tools/P1bTests --fast`.
5. Каждый FIX заканчивается записью в разделе «Журнал»: дата, что измерено до/после, кто проверял.

Легенда уверенности:
- **П** — подтверждено кодом (видно в исходнике).
- **В** — вероятно, требуется проба (как именно — указано).

---

## 3. Сводка

| ID | Проблема | Файлы | Тяжесть | Увер. | Трудо-<br>ёмкость | Фаза |
|---|---|---|---|---|---|---|
| FIX-01 | Патч рельефа — остров 120×120 м, зона не переезжает → падение сквозь планету | `ContactZoneHost.cs`, `TerrainPatch.cs`, `ContactZoneGate.cs` | критично | П | M | 1 |
| FIX-02 | Глобальные `Physics.gravity` / `defaultSolverIterations`, не восстанавливаются | `ContactZoneProxy.cs`, `ContactZoneHost.cs`, `SimulationRunner.cs` | критично | П | M | 1 |
| FIX-04 | Локальная скорость пишется в мировой `rb.linearVelocity` — **живой** баг на каждом входе | `ContactZoneProxy.cs` | критично | П | S | 1 |
| FIX-03 | Нет единой политики синхронизации поз: `autoSync=true` + 9 `SyncTransforms` | `SimulationRunner.cs`, `MeshSupportBase.cs`, `*View.cs`, `SurfaceSite.cs` | высоко | П | L | 2 |
| FIX-05 | Пределы скоростей не проверены; `maxAngular` уже 50 (`DynamicsManager:38`), линейный — по замеру | `ContactZoneProxy.cs` | низко | П | S | 2 |
| FIX-06 | Разные COM/инерция у PhysX и модели полёта → скачок на handoff | `ContactZoneProxy.cs` | высоко | П | S | 3 |
| FIX-07 | Самоколлизия отключена только для пар «стык» | `ContactZoneProxy.cs` | высоко | П/В | M | 3 |
| FIX-08 | Меши призм не уничтожаются при каждом входе в зону | `ContactZoneProxy.cs` | средне | П | S | 4 |
| FIX-09 | Словари `VesselSupport` растут на каждой посадке | `VesselSupport.cs` | средне | П | S | 4 |
| FIX-10 | `RaycastAll`/`CapsuleCastAll` в цикле игрока (сотни аллокаций/кадр) | `MeshSupportBase.cs` | высоко (GC) | П | M | 5 |
| FIX-10b | Слои для запросов игрока: маска `~0` сейчас ничего не фильтрует | `Planet/Surface`, `Building`, `Simulation/Player` | средне | П | M | 5 |
| FIX-11 | Проба push-out высотой 1.8 м при игроке 1.45/0.8 м | `SiteBox.cs`, `MeshSupportBase.cs` | средне | В | S | 5 |
| FIX-12 | CCD на всех деталях цепочки — стоимость без пользы | `ContactZoneProxy.cs` | средне | В | S | 5 |
| CLEAN-01 | Мёртвый push-out путь `SiteBoxRegistry.TryResolve/PushOutMesh` | `SiteBox.cs` | низко | П | S | 5 |
| FIX-13 | Кукинг патча на главном потоке при входе в зону | `TerrainPatch.cs` | средне | П | M | 6 |
| FIX-14 | Бюджет коллайдеров капсулы Orion (~150 хуллов) | `OrionCollisionBuilder.cs` | средне | В | L | 6 |
| DEC-01 | Кастомный решатель не в рантайме, а его тесты — часть приёмки | `Spacecraft/Solver/*`, `tests/*` | архитектура | П | L | 7 |
| DEC-02 | Базовый сценарий «мир телепортируется каждый кадр» | `SurfaceSite.cs`, `FloatingOrigin.cs` | архитектура | П | L | 7 |

S ≈ до 1 часа, M ≈ 2–4 часа, L ≈ день и больше (с пробами).

---

## 4. Фаза 0 — измерения (до любых правок)

Ничего не менять, пока эти числа не сняты. Пробел в данных — главный риск этого плана.

### 4.1. Пять чисел в момент входа в зону

Добавить в `ContactZoneHost.Enter` (временный `Debug.Log`, потом убрать):

```csharp
Proxy.GetCenterOfMassState(frame, t, out Galilego.Core.Vector3d comZone, out _);
Debug.Log("[CZ-IN] lat=" + frame.LatitudeDegrees.ToString("F5") + " lon=" + frame.LongitudeDegrees.ToString("F5")
    + " root.rot=" + transform.rotation.eulerAngles
    + " gravity=" + Physics.gravity
    + " iters_global=" + Physics.defaultSolverIterations
    + " iters_body0=" + (Proxy.BodyAt(0) != null ? Proxy.BodyAt(0).solverIterations : -1)
    + " com_zone=" + comZone
    + " com_flight=" + (Runner.Flight != null ? Runner.Flight.Com : default)
    + " v_body0=" + (Proxy.BodyAt(0) != null ? Proxy.BodyAt(0).linearVelocity : Vector3.zero)
    + " com_physx0=" + (Proxy.BodyAt(0) != null ? Proxy.BodyAt(0).worldCenterOfMass : Vector3.zero)
    + " maxAngular=" + (Proxy.BodyAt(0) != null ? Proxy.BodyAt(0).maxAngularVelocity : -1f)
    + " maxLinear=" + (Proxy.BodyAt(0) != null ? Proxy.BodyAt(0).maxLinearVelocity : -1f));
```

Лог стоит в `Enter` **после** `Proxy.Build`, поэтому `Physics.defaultSolverIterations`
уже равен 20 и ничего не говорит о телах. Реальные итерации первого входа видны только
через `rb.solverIterations` (`iters_body0`): 6 на первом входе, 20 на втором.

Ожидаемые «здоровые» значения: `gravity` ~ 9.8 по местной вертикали (не `(0,-9.81,0)`),
`iters_body0 = 6`, `v_body0` — это `v_local`, повёрнутая в мир,
`com_physx0 ≈ com_flight` для капсулы (у симметричных деталей совпадает и так),
`maxAngular = 50` (из проекта), `maxLinear` — записать фактическое и сравнить с 120 м/с
(порог выхода зоны).

### 4.2. Геометрия посадки

Логировать раз в 0.5 с внутри зоны: горизонтальное расстояние от центра патча
(`transform.InverseTransformPoint(FloatingOrigin.ToRender(worldPosition))`, компоненты x/z),
высоту из гейта, число контактов.

**Критерий проблемы FIX-01.** При свободном падении с 300 м (7.8 с) край патча
достигается при горизонтальной скорости ≈ 7.7 м/с для `HalfSize = 60` и ≈ 19.2 м/с
для `HalfSize = 150`. С вертикальной скоростью время меньше, порог выше; порядок —
единицы–десятки м/с.

### 4.3. Профиль кадра

Профайлер, кадр в зоне и кадр на прогулке по площадке:
`Physics.SyncTransforms`, `Physics.Processing`, `GC.Alloc` вокруг `PlayerSurfaceController.Step`.
Записать: число вызовов синка за кадр, байты GC за кадр, мс на физику.

### 4.4. Инвентарь коллайдеров

На сцене с собранным кораблём: число `Collider` под капсулой Orion, общее число
`MeshCollider` в сцене, число Rigidbody в зоне.

### 4.5. Пробные сценарии — по сценам, а не всё в срезе

Срез (`ContactZoneSlice`) — плоский `SphericalTerrain`, детали T1, **без капсулы Orion,
без люка и без полётной модели** (`ContactZoneSliceTool.cs:61-70`). Поэтому:

| Case | Сцена | Что делает | Что ловит |
|---|---|---|---|
| `stand` | срез | уже есть: покой на плоской площадке | регресс стыков |
| `drop100` | срез | уже есть: 100 посадок с 2 м/с | регресс FIX-07/08 |
| `breakLeg` | срез | уже есть: разрыв ноги | регресс островов, FIX-07 |
| `land00` | срез | старт 300 м, **нулевая** горизонтальная скорость | контроль для FIX-01: если падает и он — виноват не край патча |
| `land30` | срез | старт 300 м, 30 м/с по горизонтали | FIX-01 (край патча) и FIX-04 (направление v) |
| `slope30` | **отдельная сцена** с наклонным terrain | дроп 5 м на склон 30° | стабильность контакта, FIX-03 |
| `hatch100` | **OutdoorsScene** (капсула Orion + игрок) | 100 проходов через люк | зазоры капсулы (DEC-03) |
| `handoff` | **OutdoorsScene / build path** | собрать, взлететь, войти в зону, выйти; лог COM, attitude, **Δv** | FIX-04, FIX-06 |

Приёмка фазы 0: `land00` зелёный (иначе это не край патча), `land30` воспроизводит
уход с патча, `handoff` показывает ненулевой Δv, `hatch100` — фактические зацепы.
Только после этого — код.

---

## 5. Фаза 1 — безопасность (не падать, не ломать сцену)

### FIX-01. Патч рельефа переезжает за кораблём + защита от падения сквозь мир

**Проблема.** `TerrainPatch` строится один раз в точке входа (`ContactZoneHost.Enter`).
`HalfSize = 60` в `OutdoorsScene.unity:599` → 120×120 м. Гейт открывается на 300 м при
скорости до 60 м/с (`ContactZoneGate.cs:15-18`). Горизонтальная скорость уносит корабль
с патча; ниже поверхности гейт выходит только при `altitude > 400` или `speed > 120`
(`ContactZoneGate.cs:43-50`). При свободном падении это ~12 с и ~735 м от точки входа,
то есть ~435 м ниже поверхности (300 м спуска + 435 м под землёй), после чего зона
«выходит» — но корабль уже под землёй, а у полётной модели нет столкновения с рельефом. То есть это не бесконечное падение, а падение с последующим
застреванием под поверхностью.

**Правка 1 — патч центрируется по кораблю и по ТЕКУЩЕМУ кадру.**

`Assets/_Project/Scripts/Simulation/ContactZone/TerrainPatch.cs`:

```csharp
// было
public void Build(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double t)

// стало (старая сигнатура — обёртка для совместимости)
public void Build(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double t)
    => Build(body, terrain, frame, t, Vector3.zero);

public void Build(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double t, Vector3 centerLocal)
{
    col = GetComponent<MeshCollider>();

    // Пересборка теперь вызывается многократно: старый меш обязан умереть,
    // иначе каждый переезд патча — утечка (та же ошибка, что FIX-08 у призм).
    col.sharedMesh = null;
    if (mesh != null) Destroy(mesh);
    mesh = new Mesh();

    // ...
    double east  = centerLocal.z - HalfSize + (2d * HalfSize * i / (n - 1));
    double north = centerLocal.x - HalfSize + (2d * HalfSize * j / (n - 1));
    double up    = HeightAt(body, terrain, frame, east, north, t);
```

`Assets/_Project/Scripts/Simulation/ContactZone/ContactZoneHost.cs`, поля и `LateUpdate`:

```csharp
/// <summary>Доля HalfSize: ближе к центру патч не трогаем, дальше — пересобираем вокруг корабля.</summary>
public float PatchRecenterFraction = 0.4f;

/// <summary>Не чаще, с: пересборка = кукинг меша и потеря contact manifold на кадр.</summary>
public float PatchRebuildIntervalSeconds = 0.5f;

/// <summary>После аварийного выхода вход запрещён, пока корабль не поднимется выше этой высоты.</summary>
public double EntryUnblockAltitudeMeters = 5d;

private double lastPatchRebuildTime = double.NegativeInfinity;
private bool entryBlocked;

// в ветке Active, ПОСЛЕ ApplyPose(current, t) — тогда transform уже стоит в current:
Vector3 shipLocal = transform.InverseTransformPoint(FloatingOrigin.ToRender(worldPosition));
if (ContactZoneGate.ShouldRecenterPatch(shipLocal.x, shipLocal.z, Patch.HalfSize,
        PatchRecenterFraction, t, lastPatchRebuildTime, PatchRebuildIntervalSeconds))
{
    // ВАЖНО: current, а не frame. Высоты HeightAt() меряются от Origin/East/North
    // переданного кадра, а меш ставится под корень, который уже в current.
    Patch.Build(body, terrain, current, t, new Vector3(shipLocal.x, 0f, shipLocal.z));
    lastPatchRebuildTime = t;
    Debug.Log("[CZ] патч пересобран: x=" + shipLocal.x.ToString("F1") + " z=" + shipLocal.z.ToString("F1"));
}
```

`shipLocal` считается из `worldPosition` — это точка корабля, посчитанная в этой же
ветке (`current.ToWorld`), а не `ship.Position` прошлого кадра (запись идёт ниже, `146-147`).

Сессионное состояние сбрасывается в `Enter` (и в `ForceExit`):

```csharp
// ContactZoneHost.Enter, в начале
entryBlocked = false;
lastPatchRebuildTime = double.NegativeInfinity;
lastValidOk = false;

// ContactZoneHost.ForceExit — ПЕРВОЙ строкой, до if (!Active)
entryBlocked = false;
lastValidOk = false;
```

`ForceExit` начинается с `if (!Active) { gate.Reset(); return; }`. После rescue `Active`
уже `false`, поэтому сброс, поставленный после этой проверки, не выполнится и защёлка
останется навсегда. Сброс — первой строкой метода.

Зачем: `ReturnToBuild` откатывает мир к снапшоту (`SpacecraftBuilder.cs:1091`), время
идёт назад. Если `lastPatchRebuildTime` остался из прошлой сессии больше нового `t`,
патч не будет пересобираться, пока время не догонит. А `entryBlocked`, оставшийся
`true` после аварийного выхода, не даст войти в зону на следующей посадке (пад на ≈0 м).
`lastValidOk = false` не даёт rescue телепортировать корабль на точку прошлой сессии.

**Правка 2 — защёлка входа: не зацикливаться после аварийного выхода.**

`ContactZoneHost.LateUpdate`, ветка `!Active`:

```csharp
bool inside = gate.Update(body, terrain, ship.Position, ship.Velocity, t);

// Аварийный выход снимает защёлку только когда корабль реально поднялся.
if (entryBlocked && gate.LastAltitudeMeters > EntryUnblockAltitudeMeters)
{
    entryBlocked = false;
}

if (inside && !entryBlocked && ContactZoneGate.CanEnter(gate.LastAltitudeMeters))
{
    Enter(body, terrain, ship, t);
}

return;
```

Защёлка, а не жёсткий порог входа: низкий вход (0.2 м) остаётся разрешённым, а после
аварийного `Exit` зона не «пере-входит» каждый кадр. `gate.Reset()` в ветке `!Active`
не нужен: мы просто не входим, а `Inside` сам пересчитается.

`ContactZoneGate` — хранит последнюю высоту и даёт чистые решения **без `UnityEngine`**
(координаты — `double`, не `Vector2`):

```csharp
public double LastAltitudeMeters { get; private set; }

public bool Update(...)
{
    Measure(..., out double altitude, out double speed);
    LastAltitudeMeters = altitude;   // + строка
    // ...
}

/// <summary>Вход запрещён на нуле и под рельефом.</summary>
public static bool CanEnter(double altitudeMeters) => altitudeMeters > 0d;

/// <summary>Зона не держится ниже рельефа.</summary>
public static bool BelowFloor(double altitudeMeters) => altitudeMeters < -2d;

/// <summary>Пора пересобрать патч вокруг корабля (гейт остаётся чистым C#).</summary>
public static bool ShouldRecenterPatch(double x, double z, float halfSize, float fraction,
    double now, double lastRebuild, double intervalSeconds)
    => now - lastRebuild > intervalSeconds
        && (x * x) + (z * z) > (halfSize * fraction) * (halfSize * fraction);
```

`ContactZoneHost.Exit` — добавить `gate.Reset()` перед `Active = false`. В обычном пути
`Exit` вызывается только когда `gate.Update` вернул `false`, то есть `Inside` уже `false`
(`ContactZoneGate.cs:43-51`), и reset там был не нужен. Он нужен **новому пути
`BelowFloor`**: `Exit` вызывается при `Inside == true`, и без reset следующий кадр снова
войдёт в зону.

Аварийный выход в ветке `Active` (после `gate.Update`):

```csharp
if (ContactZoneGate.BelowFloor(gate.LastAltitudeMeters))
{
    entryBlocked = true;    // не входить, пока не поднимемся выше EntryUnblockAltitudeMeters
    Exit(ship, t);          // gate.Reset() внутри
    return;
}
```

**Правка 3 — rescue: выходить не «под землю», а на безопасную точку.**

`Exit` пишет COM из физики (уже под поверхностью) и отдаёт корабль полётной модели,
у которой нет столкновения с рельефом. Нужно запоминать безопасное состояние и
восстанавливать его. Важно: `anchor` в `LateUpdate` — это **низ** корабля
(`ContactZoneHost.cs:128`), а не COM; для `PositionIsCom` нужен именно COM.

```csharp
// поля
[Tooltip("На сколько поднять корабль над последней безопасной точкой при rescue, м.")]
public double RescueLiftMeters = 10d;   // если не нужен в инспекторе — [SerializeField] private
private bool lastValidOk;
private Vector3d lastValidShipPoint;      // ship.Position на входе (низ/COM), мир
private Vector3d lastValidShipVelocity;
private Vector3d lastValidCom;            // COM, мир
private Vector3d lastValidComVelocity;

// ContactZoneHost.Enter, ПОСЛЕ GetCenterOfMassState/anchorUp:
// lp — это ship.Position на входе: низ в стройке, COM в полёте.
// Rescue возьмёт ту же ветку, что и PositionIsCom после Exit, поэтому lp корректен.
frame.ToWorld(lp, lv, out lastValidShipPoint, out lastValidShipVelocity);
frame.ToWorld(com, comVelocity, out lastValidCom, out lastValidComVelocity);
lastValidOk = true;

// ContactZoneHost.LateUpdate, ПОСЛЕ gate.Update — высота уже этого кадра:
if (gate.LastAltitudeMeters > EntryUnblockAltitudeMeters)
{
    lastValidShipPoint = worldPosition;
    lastValidShipVelocity = worldVelocity;
    current.ToWorld(com, comVelocity, out lastValidCom, out lastValidComVelocity);  // COM, не anchor!
    lastValidOk = true;
}

// выход по полу
if (ContactZoneGate.BelowFloor(gate.LastAltitudeMeters))
{
    entryBlocked = true;
    Exit(ship, t);                       // внутри пишет COM и attitude, gate.Reset()
    if (lastValidOk)
    {
        bool com = ship.PositionIsCom;   // Exit выставил его по Flight != null
        Vector3d point = com ? lastValidCom : lastValidShipPoint;
        Vector3d velocity = com ? lastValidComVelocity : lastValidShipVelocity;
        Vector3d up = current.Up;        // кадр на момент выхода

        // Гейт сравнивает скорость ОТНОСИТЕЛЬНО поверхности (ContactZoneGate.Measure),
        // поэтому клампим относительную горизонталь, а не мировую.
        body.GetSurfaceState(frame.LatitudeDegrees, frame.LongitudeDegrees,
            frame.TerrainHeightMeters, t, out _, out Vector3d surfaceVelocity);
        Vector3d relative = velocity - surfaceVelocity;
        double radialRel = Vector3d.Dot(relative, up);
        Vector3d horizontalRel = relative - (up * radialRel);
        double maxSpeed = gate.EnterSpeedMps * 0.8d;   // 48 м/с при дефолтных 60
        if (horizontalRel.Magnitude > maxSpeed)
        {
            horizontalRel *= maxSpeed / horizontalRel.Magnitude;
        }

        ship.Position = point + (up * RescueLiftMeters);   // не 1 м над землёй
        ship.Velocity = surfaceVelocity + horizontalRel;   // вход в зону гарантирован
    }
    return;
}
```

Зачем подъём и обнуление радиальной скорости: снимок «последняя точка выше 5 м» — это
всё ещё падающий корабль; вернуть его туда с 30 м/с вниз бессмысленно. После rescue
корабль стоит на `RescueLiftMeters` выше последней безопасной точки с нулевой радиальной
и ограниченной горизонтальной скоростью; на следующем кадре высота >
`EntryUnblockAltitudeMeters`, защёлка снимается, зона входит заново и строит патч под
текущей точкой.

Кламп — именно **относительной** горизонтали (`velocity − surfaceVelocity`): гейт
(`ContactZoneGate.Measure`) сравнивает скорость с поверхностью, а не с миром. На Terra
у экватора поверхность идёт ~83 м/с, поэтому кламп мировой горизонтали мог оставить
относительную > 60 и зона бы не вошла. После клампа `|relative| ≤ 48 < 60` для любого
направления. Для `land30` относительная горизонталь 30 м/с, кламп ничего не меняет.
Если `lastValidOk == false` (не должно случиться — `Enter` его ставит), rescue не
выполняется, остаётся обычный `Exit`.

Снимок входа ставится безусловно, и это согласовано с правилом «снимок только выше 5 м»:
`Enter` вызывается лишь при `altitude > 0` (`CanEnter`), а rescue поднимает на
`RescueLiftMeters`, поэтому даже вход на 0.2 м даёт безопасные 10.2 м. Правило «> 5 м»
относится к периодическим снимкам, а не к точке входа.

Оговорки: attitude/угловая скорость остаются текущими; rescue — страховка, а не замена
FIX-01: патч должен переезжать и не терять корабль.

**Проверка входа с площадки.** `EnterFlightAt` (`SpacecraftBuilder.cs:862`) ставит низ
корабля на пад, и высота над *terrain* может быть ≈0. `CanEnter(altitude > 0)` в этом
случае не пустит зону. Обязательный прогон `build → flight`: зона должна войти на паде.
Если не входит — заменить порог на site-aware проверку (корабль стоит на `SiteBox`/паде)
или допуск `altitude > -0.5`, но не убирать защёлку.

**Риски.** Пересборка меша во время контакта теряет contact manifold на 1 кадр →
просадка на несколько см. Поэтому: не чаще `PatchRebuildIntervalSeconds` (0.5 с) и только
по достижении отступа. Если просадка окажется заметной — добавить счётчик контактов
(`OnCollisionStay` в `ContactZoneProxy`) и откладывать пересборку до `contacts == 0`;
сейчас такого свойства в `ContactZoneProxy` нет (есть только в модельном
`RigidAssemblySolver`), поэтому в сниппет он не заложен.

**Целевое решение (после минимального).** Два кольца: грубый патч 600×600 м (шаг 10 м)
+ точный 120×120 м (**целевой шаг ~2 м → Resolution 61**), точный переезжает по FIX-01,
грубый строится один раз. Тогда пересборка почти не случается.

**Проверка.** `land00` — касание, высота не уходит ниже −0.5 м. `land30` — касание
в пределах патча (или пересборка), падения сквозь мир нет; число пересборок ≈ путь /
триггер: для среза (`HalfSize 150`, триггер 60 м) ≈ 4, для `OutdoorsScene` (`60`, 24 м)
≈ 10; критерий — не больше 6 и 15 соответственно. `drop100` — без регресса.
**По результатам исполнения:** старт с 300 м без вертикального управления — это удар
~68 м/с, а не посадка; критерий «высота > −0.5 м» к нему не применим (получено −1.79 м).
Разделить сценарии: `land30` — управляемое снижение (вертикаль ~5 м/с, горизонталь 30)
для проверки края патча; `crash30` — свободное падение 68 м/с с критерием «нет
туннелирования, пенетрация ≤ 2 м / разрушение», а не «мягкая посадка».
Аварийный выход ниже −2 м: в логе ровно один `выход`, rescue поднимает корабль на
`RescueLiftMeters` выше последней безопасной точки, радиальная скорость обнулена,
**относительная горизонталь ≤ 0.8 · EnterSpeedMps**, корабль остаётся выше 1 м хотя бы
1 с, защёлка снимается, зона входит заново и сажает его. Лог `[CZ] патч пересобран` не чаще интервала. `build → flight` — зона
входит на паде.

**Критерий готовности.** При штатной посадке `altitude` не уходит ниже −0.5 м; при промахе
мимо патча rescue срабатывает один раз и возвращает корабль выше 1 м; нет цикла
`вход → выход → вход` в логе (каждая запись `[ContactZone] вход` — один раз на посадку).

---

### FIX-02. Убрать глобальные `Physics.*` из зоны

**Проблема.** `ContactZoneProxy.Build` ставит `Physics.defaultSolverIterations = 20`
**после** создания тел. По документации Unity это значение не меняет уже созданные
Rigidbody, поэтому:

- **первый** вход в зону идёт на проектном значении (6 позиционных / **1** velocity,
  `DynamicsManager.asset:13-14`), а не на 20/4;
- глобальное значение остаётся 20/4 навсегда (`Clear` его не возвращает);
- **второй и последующие** входы создают тела уже с 20/4.

То есть одинаковые посадки считаются разным солвером — недетерминизм, который легко
принять за «физика иногда глючит». Плюс `SetFrameGravity` каждый кадр пишет глобальную
`Physics.gravity`, а `Exit/ForceExit` восстанавливают её в магические `(0,-9.81,0)`.

**Правка.**

`ContactZoneProxy.cs`:

```csharp
/// <summary>Гравитация кадра в мировых осях (замена глобальной Physics.gravity).</summary>
public Vector3 GravityWorld { get; private set; }

public void SetFrameGravity(Vector3d localGravity)
{
    GravityWorld = transform.rotation * ToUnity(localGravity);
    // Physics.gravity больше не трогаем
}

// Build(), в цикле по деталям — вместо двух глобальных строк:
rb.useGravity = false;
rb.solverIterations = 20;
rb.solverVelocityIterations = 4;

// и ОДИН раз на тело каждый физический шаг:
private void FixedUpdate()
{
    for (int i = 0; i < bodies.Count; i++)
    {
        Rigidbody rb = bodies[i];
        if (rb != null) rb.AddForce(GravityWorld, ForceMode.Acceleration);
    }
}
```

Важно: гравитацию применяет **сама зона**, а не `FlightRuntime` — иначе в сценах без
модели полёта (срез `stand`) тела всплывут. `ForceMode.Acceleration` не зависит от массы,
так что при обновлении `rb.mass` в `FlightRuntime` ничего не ломается.

`SimulationRunner.CabinGravity`:

```csharp
// было: effective = Physics.gravity - ...
effective = host.Proxy.GravityWorld
    - host.Proxy.transform.TransformDirection(platformCarry.PlatformAccelerationZone);
```

`ContactZoneHost.ForceExit/Exit`: удалить обе строки `Physics.gravity = ...`.
`ContactZoneSliceProbe.cs:280`: `gAtWindow = Proxy.GravityWorld;` (не `Physics.gravity`).

**Проверка.** `Debug.Log` в `Awake` и на выходе из зоны: `Physics.gravity` и
`defaultSolverIterations` не менялись. `stand` — дрейф 0 на **первом и втором** входе
подряд (проверяет недетерминизм итераций). `breakLeg`/`drop100` без регресса.

**Критерий готовности.** Ни одного присваивания `Physics.gravity` и
`Physics.defaultSolver*` в `Assets/_Project/Scripts` (проверять `rg`).

---

### FIX-04. Локальная скорость → мировая (живой баг)

`ContactZoneProxy.cs:146`:

```csharp
// было
rb.linearVelocity = vel;
// стало
rb.linearVelocity = transform.TransformDirection(vel);
```

**Это не «латентно».** `ApplyRigidState` (`199-224`) усредняет
`transform.InverseTransformDirection(bodies[i].linearVelocity)` и затем пишет обратно
`transform.TransformDirection(v)`. Для одинаковой скорости всех тел это ровно
`R · R⁻¹ · vel = vel` — то есть неверный вектор сохраняется. На каждом входе из полёта
корабль стартует в зоне с направлением скорости, повёрнутым на угол сайта (а не на
малый угол: `RenderRotation` — произвольный).

**Проверка.** `handoff`: `Δv = |v_zone − v_flight|` < 0.01 м/с в момент входа.
`land30`: горизонтальная скорость направлена туда же, куда летел корабль.
Без проверки Δv этот баг тест не поймает.

Чтобы строку можно было покрыть юнит-тестом (стенд не компилирует `ContactZoneProxy`),
тот же перевод оформить чистой функцией и использовать её в `Build`:

```csharp
// ContactZoneFrameMath (Core-типы, без UnityEngine), см. §13.
// AstroFrame — из Galilego.Universe: в ContactZoneProxy.cs этого using нет,
// добавить его или писать полное имя Galilego.Universe.AstroFrame.ToSimulation.
Vector3d worldVel = ContactZoneFrameMath.ZoneToWorld(
    ContactZoneFrameMath.SiteToZone(lv), frame.East, frame.North, frame.Up);
rb.linearVelocity = AstroFrame.ToSimulation(worldVel);
```

Однострочный вариант `transform.TransformDirection(vel)` корректен, но проверяется
только сценарно.

---

## 6. Фаза 2 — кадр и синхронизация

### FIX-03. Один sync на кадр, `autoSync` выключен

**Проблема.** В `DynamicsManager.asset` стоит `m_AutoSyncTransforms: 0` (проект настроен
на выключенный autoSync, и комментарии в коде это подтверждают), но
`SimulationRunner.Awake` включает его обратно. При этом `Physics.SyncTransforms()`
вызывается ещё в **9** местах: `SpacecraftBuilder` 466/562/1120, `SimulationRunner` 1930,
`ContactZoneProxy` 223, `ContactZoneHost` 157, `MeshSupportBase` 112/129,
`SurfaceSite` 190. Два из них — **на каждый подшаг игрока**
(`MeshSupportBase.SetFrame/SetPlatformFrame`). Единой политики нет.

**Правка 1.** `SimulationRunner.Awake`:

```csharp
Physics.autoSyncTransforms = false;   // один sync на писателей, см. PoseSync и SyncVesselRoot
```

**Правка 2.** Новый файл `Assets/_Project/Scripts/Simulation/PoseSync.cs`:

```csharp
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Синхронизация поз после всех писателей LateUpdate (BodyView −100, SurfaceSite −90,
    /// ContactZoneHost −50, ShipView −49, VesselMirror −48) и до читателей (камера −40).
    /// Запросы следующего Update видят этот кадр.
    /// </summary>
    [DefaultExecutionOrder(-41)]
    public sealed class PoseSync : MonoBehaviour
    {
        private void LateUpdate()
        {
            Physics.SyncTransforms();
        }
    }
}
```

Повесить на объект `SimulationRunner` (или на `ContactZone`, если раннер в другой сцене —
тогда продублировать в обеих сценах).

**Правка 3.** Удалить только то, что покрыто `PoseSync`:
- `MeshSupportBase.SetFrame`, `SetPlatformFrame` — **per-substep**, главный выигрыш;
- `ContactZoneHost.ApplyPose` (каждый кадр в зоне);
- `SurfaceSite.ApplyPose` (каждый кадр на площадке);
- `SpacecraftBuilder.UpdateVisuals` (free flight) и `MirrorVisualsToBodies`.

**НЕ удалять `Physics.SyncTransforms()` в `SimulationRunner.SyncVesselRoot` (1930).**
Он вызывается в `Update` (строка 846) **прямо перед** `StepPlayer` (847), а сам только что
перенёс корень корабля. Без него при `autoSync = false` запросы игрока в этом же `Update`
увидят позу прошлого кадра: на орбитальной скорости — метры, в кабине — «провалился
сквозь пол / вытолкнуло из корабля». Это и есть та единственная точка, которая позволяет
удалить per-substep синки в `SetFrame`/`SetPlatformFrame`.

Итого в кадре:
- `SyncVesselRoot` — в `Update`, после переноса корня, до `StepPlayer`;
- `PoseSync` — в `LateUpdate`, после сайта/визуала;
- в зоне `SyncVesselRoot` не вызывается, и это нормально: позы зоны пишет `LateUpdate`
  (`ApplyPose` → `PoseSync`).

Место одноразового синка в `ContactZoneHost.Enter` — **после** `Build`/`HandoffIn` и
**до** чтения COM:

```csharp
entryBlocked = false;                            // см. FIX-01: сессионное состояние
lastPatchRebuildTime = double.NegativeInfinity;  // время могло откатиться
lastValidOk = false;                             // rescue не должен взять точку прошлой сессии

Proxy.Build(Runner.Parts, frame, lp, lv, centerOfMass);
if (centerOfMass)
{
    Galilego.Flight.FlightRuntime.HandoffIn(Runner, Proxy, frame);
}

// ОДИН sync на вход: ApplyRigidState сдвинул тела (или они только что созданы),
// а GetCenterOfMassState ниже читает worldCenterOfMass для anchorUp.
Physics.SyncTransforms();

Proxy.SetFrameGravity(EffectiveLocalGravity(body, frame, t));
Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out Vector3d comVelocity);
anchorUp = com.Z - lp.Z;
// rescue-снимок входа (lp и com) — см. FIX-01, «Правка 3»
```

**Исключения, которые остаются (по одному вызову, не в цикле):**
- `SyncVesselRoot` — каждый кадр в обычной ветке (см. выше).
- `ContactZoneHost.Enter` — один раз, как в сниппете выше (после `Build`/`HandoffIn`,
  до `GetCenterOfMassState`). Для входа из полёта `ApplyRigidState` уже синкает, но
  безусловный синк нужен и для входа из стройки, где `ApplyRigidState` не вызывается.
- `SpacecraftBuilder.CheckCapsuleQueries` — внутри LateUpdate (−48) до `PoseSync`.
  **Синка там сейчас нет**: метод полагается на `Physics.SyncTransforms()` из
  `MirrorVisualsToBodies` (562), поэтому при удалении того синка его надо **добавить**
  в начало `CheckCapsuleQueries` (раз в 15 кадров).
- `SpacecraftBuilder.ReturnToBuild` (1120) — вызывается из `Update` билдера
  (order −110) до `StepPlayer` раннера (0), поэтому синк после `RefreshVisualLocals`
  нужен в том же кадре. Оставить.

`SiteBox.EnsureBuilt` синка не требует: новый коллайдер вставляется в broadphase
с текущей позой; важно не двигать его после создания в том же кадре.

`SurfaceSite.OnEnable` (`SurfaceSite.cs:83-86`) вызывает `Refresh()` вне `LateUpdate`.
После удаления sync из `ApplyPose` включение сайта в рантайме (стриминг/пересоздание)
даст один кадр старой позы для запросов игрока. Путь редкий; если сайты включаются
в игре — оставить один `Physics.SyncTransforms()` в `OnEnable` после `Refresh()`.

**Читатели физики при `autoSync = false`.** Список выше — писатели. Читатели, которые
бегут до единственного per-frame sync (`SyncVesselRoot`), проверены отдельно:

| Читатель | Когда | Что видит |
|---|---|---|
| `PlayerController.UpdateFocus` (460) | Update, order −100 | позу прошлого `LateUpdate` — то, что нарисовано; корректно |
| `LaunchPadZone` (162) | Update, order −120 | то же |
| `SpacecraftBuilder` 725 (`CheckCapsuleQueries`) | LateUpdate −48, до `PoseSync` | **нужен локальный sync** (см. выше) |
| `SpacecraftBuilder` 2004 (`TryRayToSurface`) | Update −110 | прошлый кадр; для размещения допустимо |
| EVA `CapsuleCastNonAlloc`/`OverlapCapsuleNonAlloc`/`ComputePenetration` (`SimulationRunner` 2531/2597/2662) | Update 0, после `SyncVesselRoot` | текущую |
| `ShipTablet` (326) | Update, порядок 0 | зависит от порядка; один кадр лага допустим |
| `FirstPersonCamera` (535) | LateUpdate −40, после `PoseSync` | текущую |

Вывод: дополнительных синков, кроме `CheckCapsuleQueries`, не требуется; запросы до
`SyncVesselRoot` согласованы с нарисованной позой прошлого кадра, а не с «протухшей».

**Почему это безопасно.** Сайт/патч/визуал корабля двигаются в `LateUpdate` и
синхронизируются `PoseSync`; игрок ходит в `Update` следующего кадра. Корень корабля
в обычной ветке двигается в `Update` — и там остаётся свой sync. Per-substep синки
были нужны только потому, что `autoSync` был выключен комментариями, а фактически
включён; с `false` их роль берёт на себя `SyncVesselRoot`.

**Проверка.** Профайлер до/после: число `Physics.SyncTransforms` за кадр
(ожидание: 2 в обычной ветке, 1 в зоне/стройке), мс физики, `GC.Alloc`.
Функционально: `hatch100`, прогулка по площадке, `drop100`, и отдельно — EVA у люка
(там `SyncVesselRoot` обязателен: без него корабль «уезжает» из-под ног).

**Критерий готовности.** `autoSyncTransforms == false`; `Physics.SyncTransforms()`
есть только в `PoseSync`, `SyncVesselRoot`, `Enter`, `ApplyRigidState`,
`CheckCapsuleQueries` и `ReturnToBuild` (+ `SurfaceSite.OnEnable`, если сайты
включаются в рантайме; иначе этот путь не используется); в `SetFrame`/`SetPlatformFrame`
его нет. Если sync из `ApplyRigidState` решено убрать (его покрывает `Enter`), в `rg`
остаётся на одну точку меньше — это допустимо, но зафиксировать в сообщении коммита.

---

### FIX-05. Пределы скоростей — сначала измерить, потом ставить

**Проблема с прежней формулировкой.** «Дефолт 7 рад/с» — из старого XML
`Rigidbody.maxAngularVelocity`. В этом проекте `DynamicsManager.asset:38` —
`m_DefaultMaxAngularSpeed: 50`, то есть `rb.maxAngularVelocity = 50f` в `Build` — **no-op**,
и никакого «молча режет вращение» нет. Что реально может резать — `maxLinearVelocity`
(значение зависит от версии); это надо измерить, а не угадывать.

В фазе 0 логировать до и после `Build`:

```csharp
Debug.Log("[CZ] maxAngular=" + rb.maxAngularVelocity + " maxLinear=" + rb.maxLinearVelocity);
```

Правка — только если замер покажет предел ниже нужного:

```csharp
rb.maxLinearVelocity = 3000f;   // зона выходит на 120 м/с, запас на «выброс»
// maxAngularVelocity не трогать: 50 из проекта достаточно
```

**Проверка.** `handoff`: `maxAngular`/`maxLinear` залогированы, ω из полёта не обрезана;
если `maxLinearVelocity <= 120` — поднять. Если оба предела уже выше — FIX-05 закрывается
без правки кода (только лог).

---

## 7. Фаза 3 — модель масс и самоколлизия

### FIX-06. Один COM у PhysX и у модели полёта

**Проблема.** PhysX считает COM из объёма коллайдера при равномерной плотности
(`rb.centerOfMass` нигде не задан). Для **box/cylinder/sphere** коллайдер симметричен,
COM = центр детали = `d.Offset` — расхождения нет. Реально расходится **frustum** —
и это не только капсула: `BuildMath.ToPartDefinitions` (`BuildMath.cs:655-656`) ставит
`PartShape.Frustum` любой детали с `TopRadius != Radius` (капсула, переходники, часть
двигателей). У капсулы Orion `TopRadius = 0.38 × Radius` (`PartCatalog.cs:234`),
центроид усечённого конуса смещён от середины высоты — для высоты ~3 м это ~0.4 м.
Модель полёта (`AssemblyStats`) кладёт COM детали в `d.Offset` (середина высоты,
`PartMetrics` center = `height * 0.5`). На `HandoffOut` в `Ship.Position` пишется
COM PhysX; на следующем кадре рендер-корень смещается на `Flight.Com` → скачок
(на сборке — единицы–десятки сантиметров) и другая динамика вращения.

В `baseline/slice/verification.md:74` записан **тензор инерции** («автоматический, не
точный из фазы 1»), а не COM — это два разных пункта, ссылку не смешивать.

**Минимальная правка** (совместить COM, не трогая инерцию) — `ContactZoneProxy.Build`:

```csharp
AddShapeCollider(go, d);
rb.automaticCenterOfMass = false;   // не даём Unity пересчитать COM от коллайдера
rb.centerOfMass = Vector3.zero;     // COM детали = её Offset, как в AssemblyStats
```

Для симметричных деталей это no-op; для капсулы — снимает скачок.

**Полная правка** (если после замера инерция тоже расходится) — задать тензор:

```csharp
// PartInertia.PerKgShipFrame уже есть в Galilego.Spacecraft (Assembly/Parts.cs).
// Ориентация identity: тензор нужен в ЛОКАЛЬНЫХ осях детали, а тело повёрнуто
// на d.Orientation само по себе (ось Y — ось тела, тензор диагональный).
if (d.Shape != PartShape.Point)   // для Point PerKgShipFrame возвращает нулевой тензор
{
    var dims = new Vector3d(d.Dimensions.x, d.Dimensions.y, d.Dimensions.z);
    Matrix3x3 perKg = PartInertia.PerKgShipFrame(d.Shape, dims, QuaternionD.Identity);
    // Оператора «матрица × скаляр» в Matrix3x3 нет — диагональ масштабируем умножением
    // на вектор массы (для диагонального тензора это то же самое).
    Vector3d i = perKg * new Vector3d(d.MassKg, d.MassKg, d.MassKg);
    rb.automaticInertiaTensor = false;   // иначе PhysX пересчитает тензор от старого центроида
    rb.inertiaTensor = new Vector3((float)i.X, (float)i.Y, (float)i.Z);
    rb.inertiaTensorRotation = Quaternion.identity;
}
```

Для `PartShape.Point` тензор не трогаем: `PartInertia` даёт ноль, а нулевой
`inertiaTensor` — некорректное тело; пусть остаётся автоматический от крошечного
коллайдера.

В 6000.6 `automaticInertiaTensor` — записываемое свойство (`P:` в
`UnityEngine.PhysicsModule.xml`, не в `UnityEngine.xml`). Перед присваиванием тензора
его нужно выключить, иначе PhysX может пересчитать тензор от старого центроида.

**Проверка.** `handoff`: `rb.worldCenterOfMass` (сумма по корневым телам) vs `Flight.Com`
до и после — расхождение < 1 см; `Ship.Position` на выходе не прыгает (лог Δ).
`stand`: дрейф 0. `drop100`: разрывов стыков не больше, чем до.

**Критерий готовности.** Лог `[CZ] ΔCOM` < 0.01 м на входе и выходе.

---

### FIX-07. Самоколлизия: внутри острова — игнор, между островами — столкновения

**Проблема.** `fj.enableCollision = false` действует только на пару родитель–ребёнок.
Детали через одну ступень, соседние блоки РСУ, солидная призма капсулы и отделитель
продолжают сталкиваться. Но простое «игнорировать все пары» сломает обломки: после
разрыва стыка тела остаются в `bodies` до `Clear`, и отвалившаяся нога должна
сталкиваться с кораблём.

**Правка** — карта островов и один фильтр.

```csharp
// ContactZoneProxy
private int[] islandOf;   // 0 = корневой остров, 1..N = отсоединённые

private void ApplyIslandCollisions()
{
    for (int a = 0; a < bodies.Count; a++)
    {
        Collider ca = bodies[a] != null ? bodies[a].GetComponent<Collider>() : null;
        if (ca == null) continue;
        for (int b = a + 1; b < bodies.Count; b++)
        {
            Collider cb = bodies[b] != null ? bodies[b].GetComponent<Collider>() : null;
            if (cb == null) continue;
            bool sameIsland = islandOf == null
                || (a < islandOf.Length && b < islandOf.Length && islandOf[a] == islandOf[b]);
            Physics.IgnoreCollision(ca, cb, sameIsland);
        }
    }
}
```

Заполнение `islandOf` — одним пересчётом. Важно: **сторона «корня» при отстреле не та,
что при разрыве стыка.** `ZoneIslands.Split` по умолчанию заливает корень от деталей с
`ParentIndex < 0`; в `BuildSpikeStack` (`SpacecraftBuilder.cs:2127-2136`) корень — это
двигатель внизу, и при отстреле первой ступени он улетает. Поэтому нужен явный набор
затравок:

```csharp
// ZoneIslands: явные затравки корня (null — как раньше, ParentIndex < 0)
public static SplitResult Split(IReadOnlyList<PartDefinition> parts,
    IReadOnlyCollection<int> brokenChildIndices,
    IReadOnlyCollection<int> rootSeeds = null);

private static bool[] FloodFromSeeds(IReadOnlyList<PartDefinition> parts,
    List<int>[] adjacency, IReadOnlyCollection<int> rootSeeds)
{
    bool[] inRoot = new bool[parts.Count];
    if (rootSeeds != null)
    {
        foreach (int i in rootSeeds) if (i >= 0 && i < inRoot.Length) inRoot[i] = true;
    }
    else
    {
        for (int i = 0; i < inRoot.Length; i++) if (parts[i].ParentIndex < 0) inRoot[i] = true;
    }

    // далее прежний BFS от всех затравок
    // ...
}
```

```csharp
private readonly List<int> stagedCuts = new List<int>();   // дети, отрезанные при отстреле

/// <summary>
/// rootSeeds == null — корень от ParentIndex &lt; 0 (разрыв стыка: корневая деталь
/// осталась в корабле). Для отстрела — индексы keep: корень это оставшаяся ступень,
/// даже если деталь с ParentIndex &lt; 0 (двигатель внизу) улетела.
/// </summary>
private void RebuildIslands(IReadOnlyCollection<int> rootSeeds = null)
{
    if (partDefs == null)
    {
        rootMask = null;
        islandOf = null;
        ApplyIslandCollisions();
        return;
    }

    var cuts = AllCuts();

    ZoneIslands.SplitResult split = ZoneIslands.Split(partDefs, cuts, rootSeeds);
    rootMask = new bool[bodies.Count];
    islandOf = new int[bodies.Count];

    for (int i = 0; i < split.Root.Length; i++)
    {
        int index = split.Root[i];
        if (index < 0 || index >= rootMask.Length) continue;
        rootMask[index] = true;
        islandOf[index] = 0;
    }

    for (int k = 0; k < split.Detached.Count; k++)
    {
        int[] island = split.Detached[k];
        for (int i = 0; i < island.Length; i++)
        {
            int index = island[i];
            if (index >= 0 && index < islandOf.Length) islandOf[index] = k + 1;
        }
    }

    ApplyIslandCollisions();
}
```

- `Build` — `islandOf = null` (один остров), затем `ApplyIslandCollisions()`.
- `OnPartJointBreak` — `brokenChildren.Add(index)` (как сейчас), затем
  `RebuildIslands(CurrentRootSeeds())`.
- `SeverForStaging` — в цикле разрыва стыков добавлять `i` в `stagedCuts`; собрать
  `List<int> keepSeeds` из `keep == true`; после раздачи kick-скоростей —
  `RebuildIslands(keepSeeds)`.

Затравки текущего корня — **не все `rootMask`-детали**, а только «основа» острова:

```csharp
/// <summary>
/// Затравки текущего корневого острова: детали, у которых родитель не в корне
/// (или его нет). До отстрела это деталь с ParentIndex &lt; 0; после отстрела —
/// ближайшая оставшаяся (например, бак, когда улетел двигатель).
/// Брать ВСЕ rootMask-детали нельзя: только что отломанный ребёнок ещё помечен
/// root, и заливка от него объявит корнем его собственное поддерево.
/// </summary>
private List<int> CurrentRootSeeds()
{
    if (rootMask == null || partDefs == null) return null;

    var seeds = new List<int>();
    for (int i = 0; i < partDefs.Length; i++)
    {
        if (!rootMask[i]) continue;
        int p = partDefs[i].ParentIndex;
        if (p < 0 || p >= rootMask.Length || !rootMask[p]) seeds.Add(i);
    }

    return seeds.Count > 0 ? seeds : null;
}

private List<int> AllCuts()
{
    var cuts = new List<int>(stagedCuts);
    cuts.AddRange(brokenChildren);
    return cuts;
}
```

`RebuildIslands` использует `AllCuts()`. `DetachedIslandCount` тоже должен считать
острова от текущего корня, иначе после отстрела лог «отломков» считает обломками
оставшийся корабль:

```csharp
private int DetachedIslandCount()
{
    if (partDefs == null) return 0;
    return ZoneIslands.Split(partDefs, AllCuts(), CurrentRootSeeds()).Detached.Count;
}
```

Почему не «все `rootMask`-детали»: при разрыве стыка `OnPartJointBreak(partIndex)`
ребёнок ещё имеет `rootMask[partIndex] == true` (он был в корне до разрыва). Если
посадить заливку и с него, его поддерево останется «корнем», а уцелевшая часть —
«обломками». Заливка от «основы» (детали без родителя в корне) всегда идёт от
уцелевшей части.

`stagedCuts` — состояние сессии: очищать его в `Build` (до создания тел) и в `Clear`
(рядом с `brokenChildren.Clear()`), иначе после повторной посадки в массиве останутся
старые индексы и первый же разрыв разрежет не те детали.

**Заодно чинится существующая ошибка.** Сейчас `RecomputeRootMask` строит острова
только по `brokenChildren`. `SeverForStaging` разрезает стыки, но **не** регистрирует
разрезы, поэтому следующий `OnPartJointBreak` пересчитывает маску без них и
возвращает отстреленную ступень в корневой остров — COM корабля снова начинает
включать обломки. `stagedCuts` закрывает и это.

`Physics.IgnoreCollision(ca, cb, false)` включает столкновения обратно — именно это
нужно при разрыве. O(N²) на вход и на разрыв/отстрел — для 10–30 деталей ничто.

**Риск депенетрации.** В момент разрыва детали ещё могут пересекаться в стыке; мгновенное
включение коллизий между островами даст импульс депенетрации
(`DynamicsManager.asset:10` — `m_DefaultMaxDepenetrationVelocity: 10`) и может изменить
момент/разрыв, который проверяет `breakLeg`. Если это проявится — включать пары с
задержкой 1–2 кадра (список `deferredCollisionPairs`, обрабатывать в `FixedUpdate`),
а не сразу в `RebuildIslands`.

**Проверка.** `stand`: нет контактов «деталь–деталь» (Physics Debugger). `drop100`:
ложных разрывов 0. `breakLeg`: **отвалившаяся нога сталкивается с кораблём и с землёй**
(это новое поведение — проверить, что момент и разрыв не изменились).

**Обязательный сценарий «корень внизу»** (как `BuildSpikeStack`: двигатель — `parentIndex -1`,
бак — 0, отделитель — 1):
1. Отстрелить первую ступень. Ожидание: `rootMask` = верхний стек, `islandOf` двигателя
   = 1 (detached), COM корабля **не** включает двигатель. `ZoneIslands.Split` без
   `rootSeeds` здесь дал бы обратную картину — тест должен падать на старом варианте.
2. Затем сломать стык **в оставшемся стеке** (отделитель от бака). Ожидание:
   `rootMask[бак] == true`, `islandOf[отделитель] != 0`, `islandOf[двигатель] != 0`,
   `Detached.Count == 2` (двигатель уже detached с шага 1). Проверять по деталям, а не
   `Detached.Count == 1`. Вариант с seeds «все `rootMask`-детали» оставит отделитель
   корнем — тест должен падать и на нём.
3. Ступенчатое отделение двух **detached** групп (keep при этом — одна связная группа):
   каждая detached-группа — свой остров, они сталкиваются друг с другом; после ещё одного
   разрыва отстреленные группы не возвращаются в корень (лог `rootMask`/COM до и после).

Допущение: `keep` при отстреле связный. Если он окажется несвязным, все keep-компоненты
получат `islandOf = 0` и не будут сталкиваться друг с другом — для корректного stage
такого не бывает; если понадобится, это отдельная правка (разные id для keep-компонент).

**Критерий готовности.** Внутри корневого острова контактов нет; между островами —
есть; `breakLeg` не регрессирует; на «корне внизу» COM считается по оставшейся ступени.

---

## 8. Фаза 4 — утечки

### FIX-08. Уничтожать меши призм

`ContactZoneProxy.cs` — меши из `new Mesh()` (Prism) не уничтожаются, `Clear` убивает
только GameObject. Каждый вход в зону = N утёкших мешей + N PhysX-кухонь.

```csharp
private readonly List<Mesh> partMeshes = new List<Mesh>();

// AddShapeCollider: обе ветки создают new Mesh(), обе обязаны регистрироваться.
// Cylinder:
Mesh prism = Prism(d.Dimensions.x, d.Dimensions.x, d.Dimensions.y, 12);
partMeshes.Add(prism);
mc.sharedMesh = prism;

// Frustum (капсула, переходники): вторая ветка, раньше её забывали.
Mesh frustum = Prism(d.Dimensions.x, d.Dimensions.z, d.Dimensions.y, 16);
partMeshes.Add(frustum);
fmc.sharedMesh = frustum;

// Clear(): сначала отвязать меш от коллайдера, потом уничтожать.
for (int i = 0; i < bodies.Count; i++)
{
    if (bodies[i] == null) continue;
    MeshCollider mc = bodies[i].GetComponent<MeshCollider>();
    if (mc != null) mc.sharedMesh = null;
}

for (int i = 0; i < partMeshes.Count; i++)
{
    if (partMeshes[i] != null) Destroy(partMeshes[i]);
}
partMeshes.Clear();
```

`Destroy(gameObject)` отложен до конца кадра; обнуление `sharedMesh` до `Destroy(mesh)`
гарантирует, что коллайдер не держит ссылку на уничтожаемый меш. Приём взят из
`TerrainPatch.Clear` (там `sharedMesh = null` есть); `OrionCollisionMeshes.OnDestroy`
меши уничтожает, но `sharedMesh` не обнуляет — как образец для этого пункта он неполон.

**Проверка.** Профайлер → Memory: 20 циклов вход/выход в зону, число `Mesh` не растёт.

---

### FIX-09. Чистить словари `VesselSupport` (и `warnedNonConvex`)

`VesselSupport.cs:19-25` — `partIds`/`platforms` копят уничтоженные компоненты и
трансформы; на каждой посадке новые `VesselPartCollider` → новые id, `nextPartId` растёт
без предела и **никогда** не переиспользуется.

**Вариант A (предпочтительный) — prune мёртвых ключей.** Полный `Clear()` на выходе из
зоны небезопасен: `VesselPartCollider` висит и на build-mode визуальных коллайдерах
капсулы. Очистка снимет id с живых коллайдеров, и `GroundSourceId` игрока, стоящего на
капсуле, на один кадр станет невалидным (`TryGetPlatform` → false → `DetachCabin`).
Поэтому удаляем только `null`-ключи:

```csharp
if (partIds.Count > 256) PruneDead();

private void PruneDead()
{
    var dead = new List<VesselPartCollider>();
    foreach (var kv in partIds)
    {
        if (kv.Key == null) dead.Add(kv.Key);
    }

    for (int i = 0; i < dead.Count; i++)
    {
        if (partIds.TryGetValue(dead[i], out int id)) platforms.Remove(id);
        partIds.Remove(dead[i]);
    }
}
```

**Вариант B (если всё же нужен полный `Clear()`)** — только когда игрок ни на чём не
стоит (`platformCarry.Active == false`) и после `ReturnToBuild` визуальные коллайдеры уже
пересозданы. Это хрупко, поэтому в плане не выбирается.

**Заодно `MeshSupportBase.warnedNonConvex`** (`HashSet<Collider>`, строка 33) — та же
утечка: уничтоженные коллайдеры остаются в множестве на всю сессию. Добавить чистку при
`Count > 64` (как `roles.Count > 512`) или удалять `null`-элементы в `SetFrame`.

**Проверка.** 20 посадок + 5 циклов `build → flight`: размеры `partIds`/`platforms` и
`warnedNonConvex` ограничены. `nextPartId` растёт линейно — это не утечка памяти (int),
но id не переиспользуются; если это когда-нибудь станет важно — free-list отдельной
задачей, в критерий не входит.

**По результатам исполнения:** 8 деталей × 20 посадок = 160 < 256, порог prune не
достигнут, и очистка ни разу не сработала. Либо снизить порог до 64 (при 8–30 деталях
это несколько посадок), либо добавить прогон с 300 id; иначе пункт остаётся
«implemented, не проверен».

---

## 9. Фаза 5 — GC и стоимость

### FIX-10. NonAlloc в поддержке игрока

`MeshSupportBase.cs:240,308,349` — `RaycastAll` (5 лучей на `Floor`), `RaycastAll` на
`Ceiling`, `CapsuleCastAll` на `Sweep`. `Floor` вызывается 6+ раз за подшаг, `Sweep` —
до 4 итераций × до 5 уровней приседа. Десятки–сотни массивов за кадр (зависит от числа
подшагов).

```csharp
// Все объекты пока на layer 0, поэтому маска ничего не фильтрует; именуем её,
// чтобы при введении слоёв (отдельная задача) правка была в одном месте.
private const int SupportLayers = ~0;

private readonly RaycastHit[] floorHits = new RaycastHit[64];
private readonly RaycastHit[] ceilingHits = new RaycastHit[64];
private readonly RaycastHit[] sweepHits = new RaycastHit[64];

// Floor:
int count = Physics.RaycastNonAlloc(origin, -renderUp, floorHits, (float)maxDrop + Lift,
    SupportLayers, QueryTriggerInteraction.Ignore);
if (count == floorHits.Length) DebugBlocker = "floor buffer full (" + count + ")";
if (!TryPickFloor(floorHits, count, out double h, out Vector3d n, out int id)) continue;

// TryPickFloor: for (int i = 0; i < count; i++)  — обязательно по count, не по Length
```

Буфер 64 — с запасом. `RaycastAll` возвращает не все коллайдеры объекта, а только
попадания **вдоль луча**: на капсуле Orion вниз от игрока их единицы. 64 безвреден, но
обосновывать его «150 коллайдеров» нельзя. При `count == buffer.Length` пишем в
`DebugBlocker`; если лог когда-нибудь сработает — это сигнал вводить слои, а не
увеличивать буфер вслепую.

То же для `Ceiling` и `Sweep`. `CompositePlayerSupport` и `VesselSupport` менять не нужно —
сигнатура `Floor/Ceiling/Sweep` та же.

**Проверка.** Профайлер `GC.Alloc`: было (записать в фазе 0) → ожидание < 1 КБ/кадр в
`PlayerSurfaceController.Step`. Функционально: `hatch100`, лестница/пандус, `drop100`.

**Критерий готовности.** В `MeshSupportBase` нет `RaycastAll`/`CapsuleCastAll`.
`PlayerRoofProbe.cs` (отладочная проба) в критерий не входит — его трогать не обязательно.

**FIX-10 закрывает GC, но не правило маски.** `SupportLayers = ~0` — это не фильтр:
все объекты пока на layer 0, поэтому по маске ничего не отсекается. Правило скилла
«every query has a layer mask» закрывается отдельным пунктом FIX-10b; до него
критерий FIX-10 — только «нет аллокаций», а не «маска есть».

---

### FIX-10b. Слои для запросов игрока

**Проблема.** Вся сцена на layer 0. Маска `~0` в запросах игрока не отсекает ничего,
поэтому каждый луч/капсула проходит через все коллайдеры, а фильтрация идёт уже после
хита по компонентам (`PlayerIgnore`, `PlayerObstacleOnly`, `VesselPartCollider`).

**Правка (отдельный коммит после FIX-10).**

1. Завести слои: `SupportTerrain`, `SupportSite`, `SupportVessel`, `PlayerIgnored`,
   `Probe` (EVA-проба, `SiteProbe`), остальное — `Default`.
2. Присвоить при создании коллайдеров:
   - `TerrainPatch`, `SurfaceRoad` → `SupportTerrain`;
   - `SiteBox.CreateColliderHolder` (+ авторские коллайдеры) → `SupportSite`;
   - `PartCatalog`/`OrionCollisionBuilder`/`ContactZoneProxy` → `SupportVessel`;
   - мониторы, планшет, кресло (`PlayerIgnore`) → `PlayerIgnored`;
   - `SiteBoxRegistry.Probe` и `SimulationRunner.evaProbeCollider` (private-поле, объект
     «EVA Body Probe») → `Probe`; объект `SiteProbe` сейчас сидит на layer 2
     («Ignore Raycast») — при назначении слоёв это учесть.
3. В `MeshSupportBase` заменить `SupportLayers` на
   `SupportTerrain | SupportSite | SupportVessel` (без `PlayerIgnored`).
4. E-луч (`PlayerController.UpdateFocus`) — `Default | SupportVessel | PlayerIgnored`,
   чтобы мониторы по-прежнему наводились.
5. `ShipTablet`/`ShipTablet`-указатель — тот же набор, что E-луч.

**Проверка.** Профайлер: число обработанных коллайдеров на запрос падает; `hatch100`,
`drop100`, E-луч по монитору работает. `PlayerIgnore` остаётся видимым для лучей, но не
для поддержки — это и есть цель.

**Критерий готовности.** Ни одного `~0` в запросах игрока; слои назначены при создании,
а не поиском по сцене в рантайме.

---

### FIX-11. Проба push-out по реальной высоте игрока

`SiteBoxRegistry.Probe(radius)` создаёт капсулу высотой `PlayerHeight = 1.8` м,
`MeshSupportBase.TryPushOut` меняет только радиус. Реальная капсула — 1.45 м (0.8 в
приседе). В люке/низком проёме проба находит стену на 35 см выше головы → «невидимая
стена».

```csharp
// SiteBox.cs
internal static CapsuleCollider Probe(float radius, float height)
{
    if (probe == null) { /* как было, но height/center выставить ниже */ }
    probe.radius = radius;
    probe.height = Mathf.Max(height, radius * 2f + 0.01f);
    probe.center = new Vector3(0f, probe.height * 0.5f, 0f);
    return probe;
}

// MeshSupportBase.TryPushOut(..., float height)
CapsuleCollider probe = SiteBoxRegistry.Probe(radius, height);
// вызов в Sweep: TryPushOut(hit.collider, feet, radius, (float)height, out wallN)
```

`SiteBox.PushOutMesh` — **мёртвый код** (`SiteBoxRegistry.TryResolve` не вызывается
нигде, проверено `rg`). Удалить вместе с `PlayerHeight`/`TryResolve` (CLEAN-01).

**Проверка.** `hatch100`: проход в приседе без зацепов; стоя — блокируется (это правильно).
Замер: высота потолка, при которой игрок ещё «не влезает», должна стать 1.45, а не 1.8.

---

### FIX-12. CCD — точечно

`ContactZoneProxy.cs:149` — `ContinuousDynamic` на каждой детали цепочки. PhysX CCD
применяет и к jointed-телам; проблема не в неработоспособности, а в стоимости и в том,
что Unity/PhysX рекомендуют отфильтровывать CCD-взаимодействия между связанными телами
(ragdoll), иначе растёт стоимость и может теряться время.

План: оставить `ContinuousDynamic` только на корневом (нижнем) теле, остальным —
`ContinuousSpeculative` или `Discrete`; сравнить `Physics.Processing` в `drop100`.
Если разницы нет — оставить `ContinuousSpeculative` всем.

**Критерий готовности.** Нет туннелирования на `land30` (30 м/с) при снижении стоимости.

---

### CLEAN-01. Удалить мёртвый push-out

`SiteBoxRegistry.TryResolve` (`642`) и `SiteBox.PushOutMesh` (`457`) не вызываются;
вместе с ними уходят `PlayerHeight` и предупреждение о невыпуклых мешах в этом пути.
Отдельный коммит, без изменения поведения. Проверка:
`rg "SiteBoxRegistry\.TryResolve|PushOutMesh\("` — пусто. Общий `rg "TryResolve\("`
даёт ~20 ложных срабатываний на `SurfaceSceneSystem.TryResolve`, его не использовать.

---

## 10. Фаза 6 — стоимость

### FIX-13. Кукинг патча не на критическом кадре

`TerrainPatch.Build` присваивает `sharedMesh`; PhysX кует 32k треугольников
(в `ContactZoneSlice`; в `OutdoorsScene` 4.6k) синхронно, ровно на подлёте.

`Physics.BakeMesh(EntityId, convex, MeshColliderCookingOptions)` в 6000.6 есть, но:

- `cookingOptions` должны **точно совпадать** с `MeshCollider.cookingOptions`, иначе
  кукинг повторится при присваивании;
- генерация вершин и `RecalculateNormals` остаются на главном потоке (для 129² это
  заметные миллисекунды) — план убирает только PhysX-кук.

Поэтому: `Physics.BakeMesh` + вызов `Build` не в кадре касания; для 32k-случая —
либо кэш/двухкольцевой патч, либо построение сетки в job.

**Проверка.** Профайлер: пик кадра на входе в зону до/после; число пересборок за посадку.

---

### FIX-14. Бюджет коллайдеров капсулы (исследование)

`OrionCollisionBuilder`: до 48 секторов × 3 полосы = 144 хулла + сетка люка + мебель.
Каждый — отдельный `Mesh` + `MeshCollider`, обходится всеми запросами игрока и
`GetComponentsInChildren` (`InsideBounds`, `SetPartColliders`, `CheckCapsuleQueries`).

Шаги:
1. Посчитать фактические числа (лог в конце `Build`) — капсула, весь корабль.
2. Профайл `Physics.Processing` и `SetPartColliders`/`InsideBounds` в зоне.
3. Если это > 10% кадра — объединить секторы (24×2), сетку люка оставить только в
   зоне люка (она и так там), мебель — один хулл на предмет (уже так).

**Критерий готовности.** Задокументированный бюджет (например, ≤ 60 коллайдеров на
капсулу) и профайл, подтверждающий стоимость.

---

## 11. Фаза 7 — решения по архитектуре

### DEC-01. Кастомный решатель не в рантайме

`RigidAssemblySolver`, `SiteColliderSet`, `ConvexHull` и их контактный путь не
используются игрой: контакты в игре делает PhysX-зона.

Важно не путать:
- `Test306_AdvanceTimeMonotonic` — время;
- `Test307/308_IslandSplit*` — `ZoneIslands`, и он **используется рантаймом**
  (`ContactZoneProxy.cs:288, 321`);
- модельные тесты решателя — `Test204–215` (свободное падение, контакты),
  `Test220–223` (ноги), `Test240–242` (контакты с постройками) и
  `tests/GalilegoSimTests/ContactSolverTests.cs`.

Решение:
- **A (рекомендуется на ближайший этап):** оставить PhysX; добавить Unity-пробы
  (фаза 0) и пометить xUnit-тесты решателя как «модельные, не shipped path».
- **B (по спецификации, фаза 6):** перевести зону на `RigidAssemblySolver`, тогда
  PhysX остаётся только для визуала/игрока. Большая работа, отдельный план.

Пока решение не принято, в отчётах не писать «решатель проверен» без оговорки, что
проверен модельный путь. `ZoneIslands` — исключение: он общий для тестов и рантайма.

### DEC-02. Мир телепортируется каждый кадр

`SurfaceSite.ApplyPose`, `ContactZoneHost.ApplyPose`, `ShipView` двигают статические
коллайдеры каждый кадр (следствие вращающейся планеты и floating origin). Это причина
FIX-03 и пересборки contact manifold на посадке.

Целевое: порог re-anchor (например, 200 м) — двигать весь мир только когда игрок
сместился; внутри окна позы не менять. Отдельный рефакторинг `FloatingOrigin` +
`SurfaceSite` + `BodyView`. До него — не «лечить» это солвером (frame → shapes → tuning).

### DEC-03. Зазор люка

Присед 0.8 м, проём ~1.0 м, выпуклые хуллы «съедают» ~0.06 м с каждой стороны
(по комментарию в `OrionCollisionBuilder`). Остаётся ~8 см — впритык. Проба `hatch100`
и замер проёма через `Physics.ComputePenetration` дадут факт.

Размеры для справки: радиус капсулы 0.25 → **диаметр 0.5**, присед — высота 0.8;
жёсткий минимум PhysX — `2 × radius = 0.5`. Комментарий в `PlayerSurfaceController`
(«не ниже 0.7 — это диаметр капсулы») неверен: 0.7 — не диаметр; поправить текст
при случае, не менять поведение.

Если проход нестабилен — расширить вырез люка на 2–3 см, а не уменьшать капсулу ниже 0.8.

---

## 12. Регрессия

После каждой фазы:

| Что | Команда / сцена | Ожидание |
|---|---|---|
| Core-тесты | `dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Tier!=Slow"` | зелено |
| Sim-тесты | `dotnet test tests/GalilegoSimTests/GalilegoSimTests.csproj` | 68 `[Fact]/[Theory]` (кейсов в выводе может быть больше) |
| Стенд P1b | `Tools/P1bTests --fast` | **180** не-slow кейсов из 278, 0 FAIL |
| Срез: покой | `ContactZoneSlice`, `Case=stand` | дрейф 0, \|v\| = 0, на первом и втором входе |
| Срез: дроп | `Case=drop100` | 100 посадок, 0 ложных разрывов |
| Срез: нога | `Case=breakLeg` | момент сохраняется, разрыв ровно один, нога сталкивается с кораблём |
| Срез: вертикаль | `Case=land00` | касание, высота > −0.5 м |
| Срез: край | `Case=land30` | касание, нет падения сквозь мир, Δv ≈ 0 |
| Наклон | отдельная сцена, `slope30` | не сползает/не дрожит |
| Люк | `OutdoorsScene`, `hatch100` | 100/100 проходов |
| Handoff | build path, `handoff` | ΔCOM < 1 см, Δv < 0.01 м/с, Δattitude < 0.1° |
| Профайл | `Physics.SyncTransforms`, `Physics.Processing`, `GC.Alloc` | записать до/после |

Новые логи:

- `[CZ-IN]` — числа из фазы 0.
- `[CZ] патч пересобран` — с координатами и числом.
- `[CZ] ΔCOM` и `[CZ] Δv` — на handoff.
- `[Support] буфер полон` — если `NonAlloc` упирается в размер.

---

## 13. Тесты, которые нужно добавить

`ContactZoneGate`/`SiteFrame` — чистый C# (компилируются в стенд P1bTests), новую
логику выносить туда. Номера: **Test310–320 заняты** (`TestsPlayerSupport.cs`),
брать с **Test321**. Чистые функции уже определены в FIX-01:

```csharp
ContactZoneGate.ShouldRecenterPatch(x, z, halfSize, fraction, now, lastRebuild, intervalSeconds);
ContactZoneGate.CanEnter(altitude);
ContactZoneGate.BelowFloor(altitude);
```

`ContactZoneGate` остаётся без `UnityEngine`: координаты — `double`, а не `Vector2`.
Защёлка `entryBlocked` — состояние `ContactZoneHost`, поэтому проверяется сценарно.

**Test323/324 нельзя писать «как есть».** `ContactZoneProxy` не входит в стенд
(`Tools/GameSources.props` включает только `SiteFrame`, `ContactZoneGate`,
`ZoneIslands` — строки 45-47), а `ToUnity` — private. Поэтому математику перевода
надо вынести в чистый файл:

```csharp
// Simulation/ContactZone/ContactZoneFrameMath.cs (Core-типы, без UnityEngine)
public static class ContactZoneFrameMath
{
    // site (east, north, up) -> zone-local Unity axes (north, up, east)
    public static Vector3d SiteToZone(Vector3d site) => new Vector3d(site.Y, site.Z, site.X);

    // zone-local -> world through the site axes
    public static Vector3d ZoneToWorld(Vector3d zone, Vector3d east, Vector3d north, Vector3d up)
        => (north * zone.X) + (up * zone.Y) + (east * zone.Z);
}
```

`ContactZoneProxy.Build` и `FlightRuntime` используют его; файл добавить в
`Tools/GameSources.props` (рядом со строками 45-47). Тест 323 проверяет три вещи:
`SiteToZone(v)` — не identity; round-trip `ZoneToWorld(SiteToZone(v)) == v`; «наивное»
значение `SiteToZone(v)` в роли мира отличается от `v` на непроизвольном базисе.
Тест 324 — то же для гравитации (знак/оси), без `Physics.gravity`.

| Тест | Что проверяет |
|---|---|
| `Test321_PatchEdgeDecision` | `ShouldRecenterPatch`: внутри, на границе, за границей, интервал 0.5 с |
| `Test322_EntryFloor` | `CanEnter`: −1.9 / 0 / 0.6 м; `BelowFloor`: −1.9 / −2.1; защёлка — сценарно (аварийный выход → входа нет до +5 м) |
| `Test323_LocalVelocityToWorld` | `SiteToZone(v)` — не identity (перестановка `(x,y,z) → (y,z,x)`); round-trip `ZoneToWorld(SiteToZone(v)) == v` на ортонормированном базисе; «наивное» `SiteToZone(v)` в роли мира `≠ v` |
| `Test324_ZoneGravityFrameMath` | `ContactZoneFrameMath` + оси `SiteFrame` для гравитации; глобальная `Physics.gravity` в формулу не входит |

Unity-специфичное (патч, меши, `ComputePenetration`, `handoff`) — только пробами в сценах.

---

## 14. Порядок и откат

1. Фаза 0 — замеры (без коммита в игровой код, только логи/пробы/сцены).
2. FIX-04, FIX-02, FIX-01 — отдельные коммиты. **FIX-04 первым**: он подтверждён кодом
   и не зависит от того, воспроизводится ли край патча; он же искажает `land30`/`handoff`.
3. FIX-03 — самый рискованный по охвату; отдельная ветка, если что — откат одного коммита.
4. FIX-05, FIX-06, FIX-07.
5. FIX-08, FIX-09.
6. FIX-10, FIX-10b, FIX-11, FIX-12, CLEAN-01.
7. FIX-13, FIX-14.
8. DEC-01…03 — только решения, не код в этом плане.

Откат: FIX-03 откатывается возвратом `autoSyncTransforms = true` и удалением `PoseSync`,
а также восстановлением синков, удалённых в `SetFrame`/`SetPlatformFrame`, `ApplyPose`,
`UpdateVisuals` и `MirrorVisualsToBodies`; остальные — `git revert` одного коммита.
Данные для отката (гравитация, итерации, COM, Δv) логируются в фазе 0.

---

## 15. Чего не делать

- Не «лечить» проваливание увеличением `solverIterations`, `contactOffset` или
  `defaultContactOffset`: это маскирует ошибку кадра.
- Не делать невыпуклые `MeshCollider` на динамических телах и не полагаться на
  `ComputePenetration` с ними.
- Не двигать статические коллайдеры «на всякий случай» ещё чаще.
- Не включать `Physics.autoSyncTransforms` обратно как «быстрый фикс» — это удвоение
  стоимости и потеря контроля порядка.
- Не менять `Time.fixedDeltaTime` и `Physics.defaultSolver*` как побочный эффект сборки
  корабля.
- Не считать xUnit-тесты решателя доказательством работы игрового пути (DEC-01), но и
  не записывать `ZoneIslands` в «неиспользуемое» — он общий.
- Не добавлять `slope30`/`hatch100`/`handoff` в `ContactZoneSliceProbe.Case`: срез для
  этого не приспособлен (плоский terrain, T1, нет капсулы и полётной модели).

---

## 16. Журнал

| Дата | FIX | Что измерено | Результат | Кто |
|---|---|---|---|---|
| 2026-11-18 | — | аудит кода | план создан (ревизия 1) | — |
| 2026-11-18 | — | разбор плана | ревизия 2: исправлены FIX-01/02/03/04/06/07/08/10/12, числа и номера тестов | — |
| 2026-11-18 | — | второй разбор | ревизия 3: sync в `Enter`, защёлка входа, `stagedCuts`, FIX-10b, `automaticInertiaTensor`, чистый гейт без `Vector2` | — |
| 2026-11-18 | — | третий разбор | ревизия 4: строки 1930/2004, 9 sync, rescue, сброс защёлки/времени, `stagedCuts.Clear`, prune `VesselSupport`, чистый `ContactZoneFrameMath`, `automaticCenterOfMass` | — |
| 2026-11-18 | — | четвёртый разбор | ревизия 5: `rootSeeds` в `ZoneIslands.Split` (корень при отстреле — `keep`), rescue по COM + подъём + обнуление радиальной, `lastValidOk`, `ForceExit` первой строкой, `OnEnable` сайта | — |
| 2026-11-18 | — | пятый разбор | ревизия 6: `CurrentRootSeeds()` для разрыва после отстрела, `DetachedIslandCount` с seeds, кламп горизонтали в rescue, низ в `Enter` через `anchorUp` | — |
| 2026-11-18 | — | шестой разбор | ревизия 7: rescue клампит относительную горизонталь (surfaceVelocity), `lp` в `Enter`, тест №2 по деталям (`Detached.Count == 2`), допущение о связном `keep` | — |
| 2026-11-18 | — | седьмой разбор | ревизия 8: FIX-05 переписан (maxAngular 50, линейный по замеру), 435 м, Test323 round-trip, Frustum-меши, Point-тензор, читатели FIX-03, приоритет FIX-04 | — |
| 2026-11-18 | Phase 0 | `ContactZoneSlice`, Unity 6000.6.0f1, `[CZ-IN]` после `Proxy.Build`; Terra R=1 143 000 м, h≡0, T1 8 деталей, патч `HalfSize=150` | `stand`/`drop100`/`breakLeg`: `iters_global=20`, **`iters_body0=6`**, `maxAngular=50`, **`maxLinear=1E+16`**, `gravity=(-9.79,0,0)` (локальная вертикаль), `com_zone.z=6.075`, `com_physx0=(5.50,0,4.00)`, `v_body0=0`, `dv=0`. `stand`: дрейф после 2 с = 0.015 м, зона держит; штатный exit-тест пробы шёл вдоль world Y — исправлен на `Proxy.transform.up` (это проба, не игра). `drop100`: 100/100, ложных разрывов 0. `breakLeg`: разрыв на 0.020 с, 424.1 кН, 1 обломок; **моментный тест `|ΔP−MgΔt|/Σm|v| = 2.295` (порог 1e-6) — аномалия, разобрать до FIX-07** | измерено |
| 2026-11-18 | Phase 0 | `land00`: старт 300 м, горизонталь 0 | вход на 300 м, `iters_body0=6`; падение ~7.8 с, удар ~68 м/с, разорвано 6 стыков, «высота» осела на **−4.87 м**, зона не вышла. Проверка геометрии: `anchorUp=6.075` снят на входе; после потери всех ног остаётся корпус с низом ~1.2 м над грунтом, `1.2−6.075=−4.87` — это **устаревший anchor, а не провал сквозь грунт**. Для FIX-01 критерий `>−0.5` на `land00` невыполним без обновления anchor после разрыва; `BelowFloor(−2)` даст ложный rescue | измерено; FIX-01 требует уточнения anchor |
| 2026-11-18 | Phase 0 | `land30`: старт 300 м, 30 м/с по горизонтали | вход: `v_body0=(0.00,−0.20,29.98)`, ожидаемое `(−0.20,0.00,−29.98)`, **Δv=59.96 м/с** — горизонталь не только повёрнута, а инвертирована (FIX-04 подтверждён). Уход с патча: 235 м от старта при `HalfSize=150`; на 7.88 с высота **−3.26 м**, скорость 82.6 м/с, выходов из зоны 0 — падение сквозь мир воспроизведено (FIX-01 подтверждён) | измерено |
| 2026-11-18 | FIX-04 | `Build`: скорость сайта `(east,north,up)` проходит через `ContactZoneFrameMath.ZoneToWorld` + `AstroFrame.ToSimulation` вместо записи локальной тройки в мировой `rb.linearVelocity` | `land30`: до — Δv 59.96 м/с и ход −28.8 м за 1 с; после — `v_body0=(−0.20,0.00,−29.98)`, `v_expected` совпадает, **Δv=0.0000**, ход +30.0 м за 1 с. `Test323_LocalVelocityToWorld` добавлен. `P1bTests --fast` **не собирается по независящей причине**: `GasGiantProfileAsset` отсутствует в `Tools/GameSources.props`, а `UnityStubs` не имеют `KeyCode.RightControl`, `Color.gray`, `Mathf.InverseLerp/SmoothStep` (появились в d51a622/после); это блокер стенда, не FIX | измерено; P1b — блокер |
| 2026-11-18 | FIX-02 | `Physics.gravity` и `Physics.defaultSolver*` в зоне удалены; тела `useGravity=false`, `solverIterations=20`, `solverVelocityIterations=4`; `FixedUpdate` → `AddForce(GravityWorld, Acceleration)`; `CabinGravity` и проба читают `Proxy.GravityWorld` | `stand`, два входа подряд (проба `SecondEntry`): `gravity=(0,−9.81,0)` (глобальная не тронута), `iters_global=6`, **`iters_body0=20` на обоих**; дрейф 0.00001 / 0.00002 м; выход без скачка (|Δ|=0.686 м за кадр при 95 м/с). `drop100`: 100/100, ложных 0. `breakLeg`: 1 разрыв, момент `отн.=7.477E−06` (до FIX-02 было 2.295; порог пробы 1e−6 всё ещё не выполнен — вероятно, интеграционная/импульсная погрешность окна, не гравитация). `rg "Physics\.gravity\s*=|defaultSolver(Iterations|VelocityIterations)\s*="` — пусто. `Test324_ZoneGravityFrameMath` добавлен | измерено частично: моментный порог 1e−6 открыт |
| 2026-11-18 | FIX-01 (recenter) | `TerrainPatch.Build` с `centerLocal` и уничтожением старого меша; `ShouldRecenterPatch`; в `LateUpdate` пересборка вокруг `current` | Первый прогон: триггер от **корня зоны** дал **35+ пересборок** за 17 с (mesh пересобирается вокруг корабля, корень стоит) — критерий ≤6 не выполнялся. После перехода на смещение от **последнего центра патча**: `land30` — **4 пересборки**, `land00` — 0; `[CZ] патч пересобран` не чаще 0.5 с. Это отклонение от сниппета плана, подтверждённое логом | измерено |
| 2026-11-18 | FIX-01 (entry/rescue) | `gate.LastAltitudeMeters/CanEnter/BelowFloor`, защёлка `entryBlocked`, снимки rescue, `anchorUp` по низу текущего корня, rescue + `SetShipState(Flying)` | `land00`: +1.21 м, 1 вход, 0 выходов, 0 rescue (до пересчёта низа корня было −4.87 м и ложный rescue). `land30`: осел на −1.79 м, |v|=0, 1 вход, 0 выходов, сквозь мир не провалился; критерий >−0.5 на этом ударе 68 м/с **не выполнен** (остаточное заглубление 1.8 м). Rescue: 1 выход, rescue +10 м, повторный вход (2 входа), высота 6.40 м, горизонт 0.71 м, относительная горизонталь 0. Мировой снимок давал **3059 м** ухода при повторном входе (снимок стар на 0.1–0.2 с, орбитальная скорость 30 км/с) — заменён на site-local. `StepLanded` стирал +10 м — rescue теперь ставит режим `Flying`. `stand`/`drop100`/`breakLeg` без регресса. **`build → flight` не проверен** (нужен путь сборки OutdoorsScene): `CanEnter(>0)` может не пустить зону на паде при высоте ≈0 | измерено, кроме build→flight |
| 2026-11-18 | FIX-05 | замер `rb.maxLinearVelocity`/`maxAngularVelocity` в `[CZ-IN]` (3 входа) | `maxAngular=50` (совпадает с проектом), **`maxLinear=1E+16`** — оба предела выше 120 м/с, правка не требуется; FIX-05 закрывается замером без кода | измерено |
| 2026-11-18 | FIX-03 | `autoSyncTransforms=false`; `PoseSync` (−41, добавляется в `Awake` раннера); удалены синки из `MeshSupportBase.SetFrame/SetPlatformFrame`, `ContactZoneHost.ApplyPose`, `SurfaceSite.ApplyPose`, `UpdateVisuals`, `MirrorVisualsToBodies`; добавлены в `Enter` и `CheckCapsuleQueries`; `SurfaceSite.OnEnable` — условный | `rg "Physics\.SyncTransforms\(\)"` = 7: `PoseSync`, `SyncVesselRoot`, `Enter`, `ApplyRigidState`, `CheckCapsuleQueries`, `ReturnToBuild`, `SurfaceSite.OnEnable`; `autoSyncTransforms = true` нет. Срез: `stand` два входа — дрейф 0.00002 м, `drop100` 100/100, `land30` 4 пересборки, осел на −1.77 м, 1 вход/0 выходов, сквозь мир не провалился. **Не проверены** `hatch100`, EVA у люка, прогулка по площадке и число `Physics.SyncTransforms` в профайлере (нужен OutdoorsScene/игрок); визуальная проверка не выполнялась | измерено частично |
| 2026-11-18 | FIX-06 | `rb.automaticCenterOfMass=false; rb.centerOfMass=Vector3.zero` в `Build`; тензор не задавался (замер расхождения не делался) | T1: `com_zone` до/после совпадает (6.07500171661377), `stand` без регресса. `handoff ΔCOM` не измерен — в срезе нет полётной модели; критерий <1 см не подтверждён | измерено частично |
| 2026-11-18 | FIX-07 | `ZoneIslands.Split(..., rootSeeds)`, `CurrentRootSeeds/AllCuts/RebuildIslands/ApplyIslandCollisions`, `stagedCuts`, отложенные пары между новыми островами | Первый вариант с немедленным включением пар дал `breakLeg` `отн.=2.301` (план предупреждал о депенетрации). Задержка «2 кадра» **не помогла** (`2.677`); включение по непересечению `Collider.bounds` (кап 30 кадров) вернуло `6.355E−06` — на уровне pre-FIX-07 `7.6E−06`. `drop100` 100/100, ложных 0. Сценарий «корень внизу» (`BuildSpikeStack`) и две detached-группы **не прогонялись** — в срезе нет такой сборки; тест по seeds не добавлен | измерено частично |
| 2026-11-18 | FIX-08 | `partMeshes` для Cylinder/Frustum; `Clear` сначала `sharedMesh=null`, затем `Destroy` | Проба `cycles`: 20 входов/выходов, **Mesh 388 → 388**; `stand`/`drop100` без регресса | измерено |
| 2026-11-18 | FIX-09 | prune мёртвых ключей `VesselSupport` (порог 256), чистка `warnedNonConvex` (порог 64) | 20 циклов входа/выхода без ошибок; порог 256 (8 деталей × 20 = 160) **не достигнут**, сам prune не сработал; 5 циклов `build → flight` не прогонялись. Размеры словарей не логировались | измерено частично |
| 2026-11-18 | FIX-10 | `RaycastNonAlloc`/`CapsuleCastNonAlloc`, буферы 64, циклы по `count`, `SupportLayers=~0`; переполнение пишется в `DebugBlocker` | `rg "RaycastAll|CapsuleCastAll" MeshSupportBase.cs` пуст; `stand` и `drop100` работают без ошибок. `GC.Alloc` профайлером **не измерен**; `hatch100` не прогонялся | измерено частично |
| 2026-11-18 | FIX-11 + CLEAN-01 | `Probe(radius,height)`, `TryPushOut` передаёт реальную высоту; удалены `TryResolve`/`PushOutMesh`/`PlayerHeight` | `rg "SiteBoxRegistry\.TryResolve|PushOutMesh\(|PlayerHeight"` пуст; компиляция чистая. `hatch100`/присед в люке **не проверялись**; измерение «высота 1.45 вместо 1.8» не снято | статически проверено |
| 2026-11-18 | FIX-12 | `ContinuousDynamic` только на корне (`ParentIndex<0`), остальным `ContinuousSpeculative` | `drop100` 100/100, `land30` 4 пересборки, осел −1.79 м, туннелирования нет. `Physics.Processing` в профайлере **не сравнивался** | измерено функционально |
| 2026-11-18 | FIX-10b / FIX-13 / FIX-14 / DEC-01..03 | — | **Не реализованы:** слои требуют `TagManager.asset` и назначения при создании коллайдеров в сценах/префабах; `BakeMesh` и двухкольцевой патч — без профиля; FIX-14/DEC — исследования, отдельный документ. Не смешивались с исправлениями | отложено |
| 2026-11-18 | регрессия | `dotnet test` оба проекта; `P1bTests --fast` | Physics: **260/260**, Sim: **107/107**. `P1bTests` **не собирается** (не связано с FIX): `GasGiantProfileAsset` отсутствует в `Tools/GameSources.props`; после добавления — `UnityStubs` без `KeyCode.RightControl`, `Color.gray`, `Mathf.InverseLerp/SmoothStep`. Ожидаемые 180/278 не проверены | блокер стенда |
| | | | | |

---

## Приложение A. Индекс «файл:строка» из аудита

| Место | Что |
|---|---|
| `Simulation/ContactZone/ContactZoneHost.cs:114-143` | ветка `LateUpdate` (вход/выход), `ApplyPose` каждый кадр |
| `…ContactZoneHost.cs:146-147` | запись `ship.Position` в конце ветки |
| `…ContactZoneHost.cs:151-157` | `SetPositionAndRotation` + `SyncTransforms` |
| `…ContactZoneHost.cs:163` | `Patch.Build` — единственный вызов, кадр входа |
| `…ContactZoneHost.cs:67, 210` | глобальная гравитация |
| `…ContactZoneHost.cs:181-182` | `GetCenterOfMassState` → `anchorUp` |
| `…ContactZoneHost.cs:192-212` | `Exit` без `gate.Reset()` |
| `Simulation/ContactZone/TerrainPatch.cs:15-17, 22-61` | размеры патча, кукинг |
| `…TerrainPatch.cs:63-68` | `Clear` с обнулением `sharedMesh` |
| `…TerrainPatch.cs:72-81` | `HeightAt` от `frame.Origin/East/North` |
| `Simulation/ContactZone/ContactZoneGate.cs:15-18, 43-50` | пороги входа/выхода |
| `Simulation/ContactZone/ContactZoneProxy.cs:103, 146` | локальная скорость в мировой |
| `…ContactZoneProxy.cs:144-149` | масса, CCD |
| `…ContactZoneProxy.cs:170-176` | `enableCollision` только для пары |
| `…ContactZoneProxy.cs:179-180` | глобальные итерации солвера (6/1 → 20/4) |
| `…ContactZoneProxy.cs:199-224` | `ApplyRigidState` (не исправляет линейную v) |
| `…ContactZoneProxy.cs:288, 321` | `ZoneIslands` в рантайме |
| `…ContactZoneProxy.cs:324-337` | `Clear` без мешей |
| `…ContactZoneProxy.cs:339-345` | `SetFrameGravity`, `Physics.gravity` на **344** |
| `Simulation/SimulationRunner.cs:544` | `autoSyncTransforms = true` |
| `…SimulationRunner.cs:846-847` | `SyncVesselRoot` → `StepPlayer` |
| `…SimulationRunner.cs:1914-1931` | `SyncVesselRoot`, sync на **1930** |
| `…SimulationRunner.cs:2004-2020` | `CabinGravity`, `Physics.gravity` на **2010** |
| `Building/SpacecraftBuilder.cs:466, 562, 1120` | остальные `SyncTransforms` |
| `…SpacecraftBuilder.cs:699` | `CheckCapsuleQueries` (своего sync нет) |
| `…SpacecraftBuilder.cs:862` | `EnterFlightAt` — низ корабля на пад |
| `Simulation/Player/MeshSupportBase.cs:33` | `warnedNonConvex` без чистки |
| `…MeshSupportBase.cs:112, 129` | синк на подшаг |
| `…MeshSupportBase.cs:240, 308, 349` | аллоцирующие запросы |
| `Simulation/Player/VesselSupport.cs:19-25, 51-67` | словари без чистки |
| `Planet/Surface/SurfaceSite.cs:186-190` | движение статики + синк |
| `Planet/Surface/SiteBox.cs:457, 590-642` | мёртвый `PushOutMesh`/`TryResolve`, проба 1.8 м |
| `Spacecraft/Parts/OrionCollisionBuilder.cs:28, 383-386, 431` | 48 секторов × 3 полосы, до 144 хуллов |
| `Spacecraft/Parts/OrionCollisionMeshes.cs` | образец уничтожения мешей (без `sharedMesh = null`) |
| `ProjectSettings/DynamicsManager.asset:10, 13-14, 22, 38` | maxDepenetration 10; solver 6/1; `m_AutoSyncTransforms: 0`; `m_DefaultMaxAngularSpeed: 50` |
| `Scenes/OutdoorsScene.unity:599` | `HalfSize: 60` |
| `Scenes/ContactZoneSlice.unity:543` | `HalfSize: 150` |
| `Editor/ContactZoneSliceTool.cs:61-70` | срез: T1, плоский terrain, без капсулы |
| `Tools/GameSources.props:45-47` | стенд компилирует только `SiteFrame`/`Gate`/`ZoneIslands` |
| `Tools/P1bTests/TestRegistry.cs:229-239` | Test310–320 заняты |

## Приложение B. Чек-лист приёмки плана

- [ ] Фаза 0: пять чисел сняты и записаны в журнал; `land00` зелёный.
- [ ] `land30` не проваливается; минимальная высота > −0.5 м; нет цикла вход/выход; rescue не срабатывает или срабатывает один раз.
- [ ] `build → flight`: зона входит на паде (низ корабля на площадке).
- [ ] Аварийный выход ниже −2 м: ровно один `выход`, rescue поднял корабль на +10 м, радиальная 0, относительная горизонталь ≤ 48 м/с, корабль остаётся выше 1 м ≥ 1 с, зона входит заново и сажает его.
- [ ] `handoff`: ΔCOM < 1 см, **Δv < 0.01 м/с**, Δattitude < 0.1°.
- [ ] `rg "Physics\.gravity\s*=" Assets/_Project/Scripts` — пусто.
- [ ] `rg "defaultSolver(Iterations|VelocityIterations)\s*=" Assets/_Project/Scripts` — пусто.
- [ ] `rg "Physics\.SyncTransforms\(\)" Assets/_Project/Scripts` — `PoseSync`, `SyncVesselRoot`, `Enter`, `ApplyRigidState`, `CheckCapsuleQueries`, `ReturnToBuild` (+ `SurfaceSite.OnEnable`, если сайты включаются в рантайме); в `SetFrame`/`SetPlatformFrame` — нет.
- [ ] `rg "RaycastAll|CapsuleCastAll" Assets/_Project/Scripts/Simulation/Player/MeshSupportBase.cs` — пусто.
- [ ] FIX-10: нет аллокаций; **FIX-10b (маска слоёв) — отдельный пункт**, до него правило маски не закрыто.
- [ ] 20 циклов вход/выход в зону: число `Mesh` не растёт; `partIds`/`platforms`/`warnedNonConvex` стабильны.
- [ ] `stand` (×2 входа): дрейф 0; `drop100`: 0 ложных разрывов; `breakLeg`: нога сталкивается с кораблём.
- [ ] «Корень внизу» (`BuildSpikeStack`): отстрел первой ступени → `rootMask` = верхний стек, двигатель — detached, COM без двигателя; затем разрыв отделителя от бака → `rootMask[бак] == true`, `islandOf[двигатель] != 0`, `islandOf[отделитель] != 0`, `Detached.Count == 2` (вариант seeds = все `rootMask`-детали обязан падать).
- [ ] Ступенчатое отделение двух **detached**-групп (keep — одна связная группа): каждая — свой остров; после нового разрыва группы не возвращаются в корень.
- [ ] `hatch100`: 100/100.
- [ ] `dotnet test` оба проекта + `P1bTests --fast` (180 не-slow, 0 FAIL).
- [ ] В отчёте написано, что именно измерено, а что не проверялось.
