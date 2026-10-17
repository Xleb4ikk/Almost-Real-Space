# Планшет управления кораблём (Galilego.Tablet)

Скопировать папку `Tablet` в `Assets/_Project/Scripts/`. Ничего не ломает: новые файлы, пакеты не нужны.

## Быстрый запуск
1. На GO планшета: добавить `ShipTablet`, в `ScreenRenderer` указать меш экрана (материал лучше HDRP/Unlit).
2. Без `Link` сразу заработает `DemoTabletLink` (вымышленные данные, кнопки живые).
3. Чтобы открывать по E: коллайдер на планшете + `TabletInteractable` (поле `Tablet`).
   Esc / E — свернуть. Пока открыт — `PlayerController.InputBlocked` и камера не крадёт курсор.

## Панель «УПРАВЛЕНИЕ» (под ориентацией)
Тяга 0-100 % — клик или протяжка по полосе ставят газ прямо в точку под курсором (как Z/X).
«ДВИГАТЕЛЬ» — мастер-выключатель маршевых (как F9/F10); неактивна, пока нет модели полёта.
Кнопка ступени — тот же путь, что Space: «ЗАЖЕЧЬ», затем «ОТДЕЛИТЬ» (SpacecraftBuilder.StageJettison).

## Кресло у монитора
`CockpitSeat` (Scripts/Player): E на кресле — посадка (голова свободна — мышь
смотрит вокруг), E — встать. Кнопки монитора жмутся ПРЯМО мышью-прицелом:
луч камеры попадает в меш экрана (нужен UV-коллайдер на `..._Screen`, у
Operator_2 он есть), пиксель считается по `textureCoord`; стрелка-курсор
рисуется на самой панели, под прицелом анимация идёт на 30 Гц. ПКМ —
приблизиться «в упор» (камера: `FirstPersonCamera.Lean*`, сужение FOV и мышь
точнее). Полноэкранный режим (`ShipTablet.Open`) остался для `TabletInteractable`.

## Как подключить к игре (потом)
Один класс-наследник `TabletLink`:
- `Fill(TabletTelemetry t)` — положить в `t` данные симуляции (все поля подписаны, единицы СИ).
- `SetSasEnabled / SetSasMode / SetRcsEnabled / SetSpeedFrame / SetReferenceBody` — исполнить команду игрока.
- `SetThrottle / SetEnginesEnabled / RequestStage` — управление кораблём (панель «УПРАВЛЕНИЕ»).
- ΔV: `DvTabletAdapter.Apply(panel.Live ?? panel.Plan, t)` — копирует DvResult как есть.
Навбол: `Attitude` — поворот корабля относительно горизонта (X=восток, Y=вверх, Z=север),
`ProgradeDir / NormalDir / RadialOutDir` — в той же системе; обратные три считает планшет.

## Файлы
TabletData — контракт данных и команд · TabletScreen — раскладка и кнопки · NavballRenderer — навбол
PixelCanvas / PixelFont — пиксельный буфер и шрифт 5×7 (лат + кириллица) · TabletTheme — палитра
TabletFormat — числа/время/единицы · ShipTablet — Unity-компонент · TabletInteractable · DemoTabletLink · DvTabletAdapter
