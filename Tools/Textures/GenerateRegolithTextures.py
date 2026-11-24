"""Generate the lunar regolith detail maps (tileable, grey, physical mean).

Why a separate set: the terrain detail textures (`terrain_*_clean.jpg`) are the
EARTH photo set (green grass, wet rock). On an airless body the shader now uses
them as a RELATIVE modulation (TerrainProfile.DetailAlbedoMean > 0): the palette
and the baked macro albedo (maria/highlands) stay in charge of the absolute
colour, the detail set only adds sub-metre grain. So these maps are neutral grey
with a slight lunar red slope, and their mean is written into the profile.

Scale: TextureScale 0.04 = one 25 m tile; 1024 px -> 2.4 cm per texel. The maps
carry DUST, PEBBLES and SMALL ROCKS only (5-50 cm). Craters are geometry (the
lunar relief generator) and the macro normal map; baking them here would repeat
every 25 m.

Output: 8-bit sRGB PNGs into Assets/_Project/Textures/Terrain/Lunar/.
The mean printed at the end is the value for TerrainProfile.DetailAlbedoMean.

Run:  python Tools/Textures/GenerateRegolithTextures.py
"""

import os

import numpy as np
from PIL import Image

SIZE = 1024
OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "_Project", "Textures", "Terrain", "Lunar")

# Lunar regolith is slightly redder than the Sun (B-V 0.92 vs 0.65). The palette
# is already warm (0.5/0.49/0.47 sRGB); together this lands near 1 : 0.96 : 0.89,
# which is what a sunlit regolith looks like in RGB.
TINT = np.array([1.0, 0.975, 0.945], dtype=np.float64)


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


def stamp(field, x, y, radius, amplitude, softness=1.0):
    """Add a soft round blob at (x, y) on a tileable field."""
    reach = int(np.ceil(radius * 2.5)) + 1
    yy = (np.arange(y - reach, y + reach + 1)) % SIZE
    xx = (np.arange(x - reach, x + reach + 1)) % SIZE
    dy = (np.arange(y - reach, y + reach + 1) - y).astype(np.float64)
    dx = (np.arange(x - reach, x + reach + 1) - x).astype(np.float64)
    d2 = dy[:, None] ** 2 + dx[None, :] ** 2
    field[np.ix_(yy, xx)] += amplitude * np.exp(-d2 / (2.0 * radius * radius * softness))


def scatter(field, count, radius_range, amplitude, seed):
    """Pebbles and stones. Bright AND dark: one-sided stamps read as a starfield."""
    rng = np.random.default_rng(seed)
    for _ in range(count):
        r = rng.uniform(*radius_range)
        sign = 1.0 if rng.random() < 0.5 else -0.75
        stamp(field, rng.integers(0, SIZE), rng.integers(0, SIZE), r,
              sign * amplitude * rng.uniform(0.45, 1.0), softness=rng.uniform(0.6, 1.5))


def build(seed, beta_large, beta_mid, grain, pebbles, rocks, contrast):
    """Return a linear albedo field normalised to mean 1."""
    height = (
        0.62 * periodic_noise(SIZE, beta_large, seed + 1)
        + 0.28 * periodic_noise(SIZE, beta_mid, seed + 2)
        + 0.10 * smoothed_white(SIZE, 1, seed + 3))
    height += grain * smoothed_white(SIZE, 1, seed + 4)
    scatter(height, pebbles, (1.4, 3.6), 0.35, seed + 5)
    scatter(height, rocks, (4.0, 11.0), 0.55, seed + 6)

    # Multiplicative variation: always positive, controlled contrast.
    value = np.exp(contrast * height)
    return value / value.mean()


def to_srgb(linear):
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(
        linear <= 0.0031308,
        linear * 12.92,
        1.055 * np.power(linear, 1.0 / 2.4) - 0.055)


def save(name, value, target_mean):
    """Encode to sRGB 8 bit and report the LINEAR mean of the stored pixels."""
    # Scale so each channel's mean equals the target (tint preserved: one factor).
    scale = target_mean / value.mean()
    rgb = value[:, :, None] * TINT[None, None, :] * scale
    srgb = to_srgb(rgb)
    data = np.clip(np.round(srgb * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(data, mode="RGB").save(os.path.join(OUT_DIR, name), optimize=True)

    # Measure what the GPU will actually read (sRGB decode of the stored bytes).
    stored = data.astype(np.float64) / 255.0
    linear = np.where(
        stored <= 0.04045,
        stored / 12.92,
        np.power((stored + 0.055) / 1.055, 2.4))
    per_channel = linear.reshape(-1, 3).mean(axis=0)
    print(f"{name}: mean linear RGB = {per_channel[0]:.4f}, {per_channel[1]:.4f}, "
          f"{per_channel[2]:.4f}  -> average {per_channel.mean():.4f}  "
          f"std/mean = {linear.mean(axis=2).std() / linear.mean(axis=2).mean():.3f}")
    return float(per_channel.mean())


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    target = 0.30

    flat = build(seed=20261124, beta_large=1.40, beta_mid=1.10, grain=0.12,
                 pebbles=900, rocks=120, contrast=0.20)
    rock = build(seed=20261125, beta_large=1.10, beta_mid=0.85, grain=0.25,
                 pebbles=700, rocks=420, contrast=0.27)

    means = [
        save("regolith_flat_clean.png", flat, target),
        save("regolith_rock_clean.png", rock, target),
    ]
    mean = float(np.mean(means))
    # The editor tool (TerrainTextureSetup.AssignLunar) reads this and writes it into
    # TerrainProfile.DetailAlbedoMean, so a regenerated set cannot silently shift the
    # absolute albedo: the shader divides the sample by exactly this number.
    with open(os.path.join(OUT_DIR, "regolith_mean.txt"), "w", encoding="ascii") as handle:
        handle.write(f"{mean:.4f}\n")
    print(f"\nDetailAlbedoMean = {mean:.4f}  (written to regolith_mean.txt)")


if __name__ == "__main__":
    main()
