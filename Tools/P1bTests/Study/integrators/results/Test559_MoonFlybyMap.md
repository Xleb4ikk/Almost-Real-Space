# Test559: Moon flyby map

Generated from the same CSV. `D`/`M` show margin; `E` is an equivalent set; `F` is floor-limited; `S` is a same-family reference; `T` is trivial.

## 1 m

| v_inf \ alt km | 1 | 10 | 100 | 1000 |
|---|---|---|---|---|
| 50 | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E |
| 200 | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E |
| 500 | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E |
| 1000 | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E |
| 3000 | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive ~ RKN12(10) E |
| 10000 | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive E |

## 0.01 m

| v_inf \ alt km | 1 | 10 | 100 | 1000 |
|---|---|---|---|---|
| 50 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 200 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 500 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 1000 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 3000 | DOP853-adaptive ~ RKN12(10) E | DOP853-adaptive ~ RKN12(10) E | DOP853-adaptive ~ RKN12(10) E | DOP853-adaptive ~ RKN12(10) E |
| 10000 | floor: WH-BM14A, Encke-IAS15 | floor: WH-BM14A | floor: WH-BM14A, IAS15 | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E |

