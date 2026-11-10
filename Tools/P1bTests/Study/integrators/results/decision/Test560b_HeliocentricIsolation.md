# Test560b - heliocentric 5 AU / 10 y isolation

The production `auto` local frame is the current game path. `forced-sun` and
`absolute` isolate the dominant-body switch. All rows are compared to the same
Sun-centred certified reference.

| variant | method | dominant | frame | max step | chunk | err max m | cost total | status | note |
|---|---|---|---|---:|---:|---:|---:|---|---|
| auto | DOP853 [production] | auto | local | 600 | 600 | 199051987.080305 | 19447200 | ok |  |
| forced-sun | DOP853 [production] | Sun | local | 600 | 600 | 1.2808757511069577 | 19447200 | ok |  |
| absolute | DOP853 [production] | auto | absolute | 600 | 600 | 1.2851476295813995 | 19447200 | ok |  |
| forced-sun-86400 | DOP853 [production] | Sun | local | 86400 | 86400 | 0.07844181345529094 | 135568 | ok |  |
| stand-600 | DOP853-adaptive | Sun | local | 600 | 600 | 0.40038459383678754 | 6832800 | ok |  |
| stand-86400 | DOP853-adaptive | Sun | local | 86400 | 86400 | 0.10139908849059144 | 47632 | ok |  |

**Finding:** the auto dominant-body switch is the outlier; forced-Sun and
absolute agree with the reference. This is a production frame-switch bug, not
an integrator ranking. The decision table uses the forced-Sun comparison and
keeps the auto row visible.
