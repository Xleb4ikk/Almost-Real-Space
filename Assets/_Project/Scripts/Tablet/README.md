# Планшет управления кораблём (Galilego.Tablet)

Скопировать папку `Tablet` в `Assets/_Project/Scripts/`. Ничего не ломает: новые файлы, пакеты не нужны.

## Быстрый запуск
1. На GO планшета: добавить `ShipTablet`, в `ScreenRenderer` указать меш экрана (материал лучше HDRP/Unlit).
2. Без `Link` сразу заработает `DemoTabletLink` (вымышленные данные, кнопки живые).
3. Чтобы открывать по E: коллайдер на планшете + `TabletInteractable` (поле `Tablet`).
   Esc / E — свернуть. Пока открыт — `PlayerController.InputBlocked` и камера не крадёт курсор.

## Как подключить к игре (потом)
Один класс-наследник `TabletLink`:
- `Fill(TabletTelemetry t)` — положить в `t` данные симуляции (все поля подписаны, единицы СИ).
- `SetSasEnabled / SetSasMode / SetRcsEnabled / SetSpeedFrame / SetReferenceBody` — исполнить команду игрока.
- ΔV: `DvTabletAdapter.Apply(panel.Live ?? panel.Plan, t)` — копирует DvResult как есть.
Навбол: `Attitude` — поворот корабля относительно горизонта (X=восток, Y=вверх, Z=север),
`ProgradeDir / NormalDir / RadialOutDir` — в той же системе; обратные три считает планшет.

## Файлы
TabletData — контракт данных и команд · TabletScreen — раскладка и кнопки · NavballRenderer — навбол
PixelCanvas / PixelFont — пиксельный буфер и шрифт 5×7 (лат + кириллица) · TabletTheme — палитра
TabletFormat — числа/время/единицы · ShipTablet — Unity-компонент · TabletInteractable · DemoTabletLink · DvTabletAdapter
