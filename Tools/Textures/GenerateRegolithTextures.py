"""Generate the lunar regolith detail set: albedo + normal + cavity (tileable).

Why a separate set: the terrain detail textures (`terrain_*_clean.jpg`) are the
EARTH photo set (green grass, wet rock). On an airless body the shader uses the
detail set as a RELATIVE albedo modulation (TerrainProfile.DetailAlbedoMean > 0):
the palette and the baked macro albedo (maria/highlands) stay in charge of the
absolute colour, the detail set only adds sub-metre grain.

WHAT THIS SET IS (and is not)
  * angular, crushed grains and glassy agglutinates — NOT sand ripples. There is
    deliberately almost no 1/f low-frequency content: a dune field on the Moon is
    the single most recognisable "this is not regolith" artifact.
  * micro-craterlets (bowl + raised rim), faceted pebbles and rock chips.
  * a tangent-space normal map and a cavity/AO channel (albedo alpha). The old
    set was albedo only, which is why the ground read as smooth painted cloth
    under a low sun: no micro-relief, no grain shadowing.
  * no craters larger than the tile. Craters are geometry (LunarRelief) and the
    macro normal map; a 25 m tile would repeat.

Scale: TextureScale 0.04 = one 25 m tile; 2048 px -> 1.2 cm per texel.

Output into Assets/_Project/Textures/Terrain/Lunar/:
  regolith_flat_clean.png   RGB albedo (mean printed) + A cavity
  regolith_flat_normal.png  linear tangent-space normal
  regolith_rock_clean.png   the same for scree / crater rims
  regolith_rock_normal.png
  regolith_mean.txt         linear mean of the albedo set (TerrainProfile.DetailAlbedoMean)

Run:  python Tools/Textures/GenerateRegolithTextures.py
"""

import os

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
from scipy.spatial import cKDTree

SIZE = 2048
OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "_Project", "Textures", "Terrain", "Lunar")

# Lunar regolith is slightly redder than the Sun (B-V 0.92 vs 0.65). The palette
# is already warm (0.5/0.49/0.47 sRGB); together this lands near 1 : 0.97 : 0.94,
# which is what a sunlit regolith looks like in RGB.
TINT = np.array([1.0, 0.968, 0.935], dtype=np.float64)


# --------------------------------------------------------------------------- #
# Tileable fields
# --------------------------------------------------------------------------- #
def periodic_noise(size, beta, seed):
    """Tileable 1/f^beta noise with zero mean and unit variance (FFT synthesis)."""
    rng = np.random.default_rng(seed)
    white = rng.normal(size=(size, size))
    spectrum = np.fft.fft2(white)
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spectrum = spectrum * np.power(f, -beta)
    value = np.real(np.fft.ifft2(spectrum))
    return (value - value.mean()) / value.std()


def smoothed_white(size, radius, seed):
    """Per-texel grain with a 1-2 px correlation (avoids 'TV static')."""
    rng = np.random.default_rng(seed)
    white = rng.normal(size=(size, size))
    kernel = np.outer(
        np.hanning(radius * 2 + 1),
        np.hanning(radius * 2 + 1))
    kernel /= kernel.sum()
    # Wrap-around convolution (the texture tiles).
    padded = np.pad(white, radius, mode="wrap")
    out = np.zeros_like(white)
    for oy in range(radius * 2 + 1):
        for ox in range(radius * 2 + 1):
            w = kernel[oy, ox]
            if w != 0.0:
                out += w * padded[oy:oy + size, ox:ox + size]
    return (out - out.mean()) / out.std()


def ridged(field):
    """Creased, angular relief in [-1, 1]: 1 - |n| has a sharp ridge at every zero."""
    return 1.0 - 2.0 * np.abs(field)


def voronoi_fields(size, cells, seed, jitter=0.42):
    """Tileable jittered Voronoi.

    Returns (edge, cellId):
      edge   = F2 - F1, normalised to [0,1] — 0 on a cell boundary, 1 in the centre;
      cellId = the nearest cell index (for per-grain tone / tilt).
    A jittered grid wraps exactly, so the texture tiles.
    """
    rng = np.random.default_rng(seed)
    step = size / cells
    gx, gy = np.meshgrid(np.arange(cells), np.arange(cells), indexing="xy")
    px = (gx + 0.5 + rng.uniform(-jitter, jitter, (cells, cells))) * step
    py = (gy + 0.5 + rng.uniform(-jitter, jitter, (cells, cells))) * step
    points = np.stack([px.ravel(), py.ravel()], axis=1)

    # Duplicate the point set with +-size offsets so distances wrap correctly.
    offsets = np.array([[-size, -size], [-size, 0], [-size, size],
                        [0, -size], [0, 0], [0, size],
                        [size, -size], [size, 0], [size, size]], dtype=np.float64)
    tiled = np.concatenate([points + o for o in offsets], axis=0)
    tiled_ids = np.tile(np.arange(points.shape[0]), len(offsets))

    yy, xx = np.meshgrid(np.arange(size), np.arange(size), indexing="ij")
    query = np.stack([xx.ravel(), yy.ravel()], axis=1).astype(np.float64)
    k = min(3, tiled.shape[0])
    dist, idx = cKDTree(tiled).query(query, k=k, workers=-1)
    d1 = dist[:, 0]
    d2 = dist[:, 1]
    edge = np.clip((d2 - d1) / step, 0.0, 1.0).reshape(size, size)
    cell = tiled_ids[idx[:, 0]].reshape(size, size)
    return edge, cell


def stamp_polygon(field, tone, cx, cy, radius, sides, tilt, height, seed):
    """A faceted rock chip: convex polygon filled with a tilted plane + a rim."""
    rng = np.random.default_rng(seed)
    angles = np.sort(rng.uniform(0.0, 2.0 * np.pi, sides))
    radii = radius * rng.uniform(0.55, 1.0, sides)
    verts = np.stack([np.cos(angles) * radii, np.sin(angles) * radii], axis=1)

    reach = int(np.ceil(radius * 1.35)) + 2
    ys = (np.arange(cy - reach, cy + reach + 1)) % SIZE
    xs = (np.arange(cx - reach, cx + reach + 1)) % SIZE
    dy = (np.arange(cy - reach, cy + reach + 1) - cy).astype(np.float64)
    dx = (np.arange(cx - reach, cx + reach + 1) - cx).astype(np.float64)

    # Half-plane test for a convex polygon: inside if the point is on the same
    # side of every edge. This gives genuinely straight, angular outlines.
    inside = np.ones((dy.size, dx.size), dtype=bool)
    for i in range(sides):
        ax, ay = verts[i]
        bx, by = verts[(i + 1) % sides]
        ex, ey = bx - ax, by - ay
        # Cross product of edge and (point - a); sign depends on winding, so
        # test the polygon's own centre first.
        cross = (ex * (dy[:, None] - ay)) - (ey * (dx[None, :] - ax))
        centre = (ex * (0.0 - ay)) - (ey * (0.0 - ax))
        inside &= (cross * np.sign(centre)) >= 0.0

    if not inside.any():
        return

    # Facet: one tilted plane per rock, so a single rock catches one sun facet.
    local = (tilt[0] * dx[None, :]) + (tilt[1] * dy[:, None])
    local = local - local[inside].mean()
    local = np.clip(local, -radius * 0.35, radius * 0.35)

    block = field[np.ix_(ys, xs)]
    block[inside] += height * (1.0 + (local[inside] / max(radius, 1.0)))
    # A thin raised rim / shadowed contact line around the chip.
    rim = np.zeros_like(inside)
    for i in range(sides):
        ax, ay = verts[i]
        bx, by = verts[(i + 1) % sides]
        ex, ey = bx - ax, by - ay
        cross = np.abs((ex * (dy[:, None] - ay)) - (ey * (dx[None, :] - ax))) / max(
            np.hypot(ex, ey), 1e-6)
        rim |= inside & (cross < 1.6)
    block[rim] += height * 0.25

    if tone is not None:
        tone_block = tone[np.ix_(ys, xs)]
        tone_block[inside] += rng.uniform(-0.35, 0.35)
        tone[np.ix_(ys, xs)] = tone_block

    field[np.ix_(ys, xs)] = block


def stamp_craterlet(field, cx, cy, radius, depth, seed):
    """A small bowl with a raised rim — the dominant lunar micro-relief."""
    rng = np.random.default_rng(seed)
    reach = int(np.ceil(radius * 2.4)) + 2
    ys = (np.arange(cy - reach, cy + reach + 1)) % SIZE
    xs = (np.arange(cx - reach, cx + reach + 1)) % SIZE
    dy = (np.arange(cy - reach, cy + reach + 1) - cy).astype(np.float64)
    dx = (np.arange(cx - reach, cx + reach + 1) - cx).astype(np.float64)
    r = np.sqrt(dx[None, :] ** 2 + dy[:, None] ** 2) / max(radius, 1.0)

    # Parabolic bowl inside, a Gaussian rim just outside r=1.
    bowl = np.where(r < 1.0, (r * r) - 1.0, 0.0)
    rim = 0.35 * np.exp(-np.square((r - 1.05) / 0.22))
    shape = bowl + rim
    # Irregular outline: warp the radius by a low-frequency angular noise.
    theta = np.arctan2(dy[:, None], dx[None, :])
    warp = 1.0 + 0.16 * np.sin((theta * 3.0) + rng.uniform(0.0, 6.28)) \
        + 0.09 * np.sin((theta * 5.0) + rng.uniform(0.0, 6.28))
    shape *= warp

    block = field[np.ix_(ys, xs)]
    block += depth * shape
    field[np.ix_(ys, xs)] = block


def normal_from_height(height, strength):
    """Tangent-space normal (OpenGL +Y) from a tileable height field."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx = -dx * strength
    ny = -dy * strength
    nz = np.ones_like(height)
    length = np.sqrt((nx * nx) + (ny * ny) + (nz * nz))
    n = np.stack([nx / length, ny / length, nz / length], axis=2)
    return np.clip((n * 0.5) + 0.5, 0.0, 1.0)


def cavity(height, radius=2, strength=1.0):
    """Local occlusion: a texel lower than its neighbourhood is in a pit."""
    kernel = np.ones((radius * 2 + 1, radius * 2 + 1), dtype=np.float64)
    kernel /= kernel.sum()
    padded = np.pad(height, radius, mode="wrap")
    blur = np.zeros_like(height)
    for oy in range(radius * 2 + 1):
        for ox in range(radius * 2 + 1):
            weight = kernel[oy, ox]
            if weight != 0.0:
                blur += weight * padded[oy:oy + height.shape[0], ox:ox + height.shape[1]]
    pit = np.clip(blur - height, 0.0, None)
    pit /= max(pit.max(), 1e-9)
    return np.clip(1.0 - (strength * pit), 0.55, 1.0)


# --------------------------------------------------------------------------- #
# Albedo / normal set
# --------------------------------------------------------------------------- #
def build(seed, kind):
    """Return (height, albedoLinear, normal, cavity)."""
    rng = np.random.default_rng(seed)

    # 1. Angular micro-relief. RIDGED noise is the key: its creases are sharp
    #    straight-ish edges, not the soft rounded bumps of 1/f fBm. The coarse
    #    term is deliberately weak — a 25 m tile must not contain dunes.
    coarse = ridged(periodic_noise(SIZE, 0.55, seed + 1))
    mid = ridged(periodic_noise(SIZE, 0.35, seed + 2))
    fine = ridged(smoothed_white(SIZE, 3, seed + 3))

    # 2. Voronoi grain: every cell is one crushed grain with a hard edge. Two
    #    scales: ~50 cm clods and ~15 cm chips. 1-px static is NOT grain at this
    #    texel size (1.2 cm) — it only aliases into fizz and disappears in mips.
    edge, cell = voronoi_fields(SIZE, 48 if kind == "flat" else 40, seed + 4)
    edge2, cell2 = voronoi_fields(SIZE, 160 if kind == "flat" else 128, seed + 5)
    # A crack is 2-3 cm wide, not one texel: a razor-thin F2-F1 boundary turns
    # into Nyquist fizz as soon as the sun is low. Widening it also makes the
    # grain read as clods instead of TV static.
    edge = gaussian_filter(edge, sigma=1.6 if kind == "flat" else 1.1, mode="wrap")
    edge2 = gaussian_filter(edge2, sigma=1.2 if kind == "flat" else 0.9, mode="wrap")
    grain_edge = 1.0 - edge   # 1 on a boundary, 0 in a cell centre
    chip_edge = 1.0 - edge2
    cell_tone = (cell % 97) / 96.0 - 0.5   # per-clod tone, repeatable
    chip_tone = (cell2 % 89) / 88.0 - 0.5

    height = (0.22 * coarse) + (0.22 * mid) + (0.10 * fine)
    height += (0.22 if kind == "flat" else 0.34) * grain_edge
    height += (0.10 if kind == "flat" else 0.16) * chip_edge
    height += 0.08 * (cell_tone * grain_edge)
    height += 0.05 * (chip_tone * chip_edge)

    # 3. Faceted pebbles / rock chips. A mare plain is DUST with sparse pebbles;
    #    the dense clast field belongs on the scree set (crater rims, steep slopes).
    #    The first flat set had 900 clasts per 25 m tile and read as a gravel pit.
    count = 260 if kind == "flat" else 2200
    rmin, rmax = (3.0, 30.0) if kind == "flat" else (3.0, 55.0)
    rock_tone = np.zeros_like(height)
    for i in range(count):
        radius = rmin * np.power(rng.uniform(0.05, 1.0), -1.0 / 1.7)
        radius = min(radius, rmax)
        cx = int(rng.integers(0, SIZE))
        cy = int(rng.integers(0, SIZE))
        sides = int(rng.integers(5, 8))
        tilt = rng.uniform(-0.60, 0.60, 2)
        amp = rng.uniform(0.25, 0.80) * (1.0 if kind == "flat" else 1.25)
        stamp_polygon(height, rock_tone, cx, cy, radius, sides, tilt, amp, seed + 100 + i)

    # A handful of boulders per tile: 0.4-1.0 m, too large to be hidden by grain.
    for i in range(4 if kind == "flat" else 6):
        radius = rng.uniform(30.0, 60.0)
        cx = int(rng.integers(0, SIZE))
        cy = int(rng.integers(0, SIZE))
        sides = int(rng.integers(5, 9))
        tilt = rng.uniform(-0.45, 0.45, 2)
        stamp_polygon(height, rock_tone, cx, cy, radius, sides, tilt,
            rng.uniform(0.4, 0.8), seed + 7000 + i)

    # 4. Micro-craterlets (bowl + rim), power-law radii: many small, few large.
    #    Radii reach ~2 m so a 25 m tile contains landforms, not just powder.
    craters = 150 if kind == "flat" else 240
    for i in range(craters):
        radius = 180.0 * np.power(rng.uniform(0.04, 1.0), 1.0 / 2.2)
        radius = max(radius * (1.25 if kind == "rock" else 1.0), 7.0)
        cx = int(rng.integers(0, SIZE))
        cy = int(rng.integers(0, SIZE))
        depth = -rng.uniform(0.12, 0.34) * (radius / 180.0)
        stamp_craterlet(height, cx, cy, radius, depth, seed + 5000 + i)

    # 5. Grain at the texel scale: crushed dust, no smooth dunes anywhere.
    height += 0.035 * smoothed_white(SIZE, 2, seed + 9)

    # Normalise the height so the normal strength is independent of the seed.
    height = (height - height.mean()) / max(height.std(), 1e-9)

    # --- albedo ------------------------------------------------------------
    # Slope facets catch light differently, plus a per-grain tone. This is the
    # same physical cause as the normal map; it keeps the two consistent.
    slope = np.sqrt(
        np.square(np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1))
        + np.square(np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)))
    facet = np.clip(slope / max(np.percentile(slope, 99), 1e-9), 0.0, 1.0)

    contrast = 0.18 if kind == "flat" else 0.27
    albedo = np.exp(contrast * (
        0.50 * height + 0.34 * facet + 0.28 * cell_tone * grain_edge
        + 0.18 * chip_tone * chip_edge + 0.25 * rock_tone))

    # Glass beads / agglutinates: sparse, tiny and bright — a handful of metres
    # apart, never a "starfield". Round, unlike the angular chips.
    bead_count = 80 if kind == "flat" else 140
    for i in range(bead_count):
        cx = int(rng.integers(0, SIZE))
        cy = int(rng.integers(0, SIZE))
        radius = rng.uniform(1.0, 2.4)
        reach = int(np.ceil(radius * 2.0)) + 1
        ys = (np.arange(cy - reach, cy + reach + 1)) % SIZE
        xs = (np.arange(cx - reach, cx + reach + 1)) % SIZE
        dy = (np.arange(cy - reach, cy + reach + 1) - cy).astype(np.float64)
        dx = (np.arange(cx - reach, cx + reach + 1) - cx).astype(np.float64)
        d2 = dx[None, :] ** 2 + dy[:, None] ** 2
        block = albedo[np.ix_(ys, xs)]
        block *= 1.0 + (rng.uniform(0.25, 0.65) * np.exp(-d2 / (2.0 * radius * radius)))
        albedo[np.ix_(ys, xs)] = block

    # A few dark, glassy impact-melt splashes (shiny in reality, dark in albedo).
    for i in range(40 if kind == "flat" else 60):
        cx = int(rng.integers(0, SIZE))
        cy = int(rng.integers(0, SIZE))
        radius = rng.uniform(2.0, 7.0)
        reach = int(np.ceil(radius * 2.0)) + 1
        ys = (np.arange(cy - reach, cy + reach + 1)) % SIZE
        xs = (np.arange(cx - reach, cx + reach + 1)) % SIZE
        dy = (np.arange(cy - reach, cy + reach + 1) - cy).astype(np.float64)
        dx = (np.arange(cx - reach, cx + reach + 1) - cx).astype(np.float64)
        d2 = dx[None, :] ** 2 + dy[:, None] ** 2
        block = albedo[np.ix_(ys, xs)]
        block *= 1.0 - (0.35 * np.exp(-d2 / (2.0 * radius * radius)))
        albedo[np.ix_(ys, xs)] = block

    albedo = np.clip(albedo, 0.05, 4.0)
    albedo /= albedo.mean()

    normal = normal_from_height(
        gaussian_filter(height, sigma=0.7 if kind == "flat" else 0.5, mode="wrap"),
        strength=0.65 if kind == "flat" else 1.05)
    ao = cavity(height, radius=2, strength=0.85 if kind == "flat" else 1.0)
    return height, albedo, normal, ao


def to_srgb(linear):
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(
        linear <= 0.0031308,
        linear * 12.92,
        1.055 * np.power(linear, 1.0 / 2.4) - 0.055)


def save_albedo(name, value, ao, target_mean):
    """sRGB RGB + A cavity; report the LINEAR mean of the stored RGB pixels."""
    scale = target_mean / value.mean()
    rgb = value[:, :, None] * TINT[None, None, :] * scale
    srgb = to_srgb(rgb)
    data = np.empty((SIZE, SIZE, 4), dtype=np.uint8)
    data[:, :, :3] = np.clip(np.round(srgb * 255.0), 0, 255).astype(np.uint8)
    data[:, :, 3] = np.clip(np.round(ao * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(data, mode="RGBA").save(os.path.join(OUT_DIR, name), optimize=True)

    # Measure what the GPU will actually read (sRGB decode of the stored bytes).
    stored = data[:, :, :3].astype(np.float64) / 255.0
    linear = np.where(
        stored <= 0.04045,
        stored / 12.92,
        np.power((stored + 0.055) / 1.055, 2.4))
    per_channel = linear.reshape(-1, 3).mean(axis=0)
    print(f"{name}: mean linear RGB = {per_channel[0]:.4f}, {per_channel[1]:.4f}, "
          f"{per_channel[2]:.4f}  -> average {per_channel.mean():.4f}  "
          f"std/mean = {linear.mean(axis=2).std() / linear.mean(axis=2).mean():.3f}  "
          f"ao mean = {ao.mean():.3f}")
    return float(per_channel.mean())


def save_normal(name, normal):
    data = np.clip(np.round(normal * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(data, mode="RGB").save(os.path.join(OUT_DIR, name), optimize=True)
    print(f"{name}: normal map written")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    target = 0.30

    flat_h, flat_a, flat_n, flat_ao = build(seed=20261124, kind="flat")
    rock_h, rock_a, rock_n, rock_ao = build(seed=20261125, kind="rock")

    means = [
        save_albedo("regolith_flat_clean.png", flat_a, flat_ao, target),
        save_albedo("regolith_rock_clean.png", rock_a, rock_ao, target),
    ]
    save_normal("regolith_flat_normal.png", flat_n)
    save_normal("regolith_rock_normal.png", rock_n)

    mean = float(np.mean(means))
    # The editor tool (TerrainTextureSetup.AssignLunar) reads this and writes it into
    # TerrainProfile.DetailAlbedoMean, so a regenerated set cannot silently shift the
    # absolute albedo: the shader divides the sample by exactly this number.
    with open(os.path.join(OUT_DIR, "regolith_mean.txt"), "w", encoding="ascii") as handle:
        handle.write(f"{mean:.4f}\n")
    print(f"\nDetailAlbedoMean = {mean:.4f}  (written to regolith_mean.txt)")


if __name__ == "__main__":
    main()
