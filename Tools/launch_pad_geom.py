"""Geometry for a flat stone launch pad with a painted square marking.

    python -c "import launch_pad_geom as g; m = g.build(); print(m.stats())"

A plain 40 x 40 m slab, 1.5 m thick, with one painted square inset from the
edge. No paving joints, no steps, no ramp: the top is a single flat surface and
the only thing on it is the paint.

Colours are per corner so the square's edge lands on the grid instead of
staircasing across it. The face grid is built from coordinates snapped to every
edge of the marking, which is what keeps that edge straight.
"""

from __future__ import annotations

import numpy as np

# --------------------------------------------------------------------------
# dimensions (metres)
# --------------------------------------------------------------------------

SIZE = 40.0               # footprint, square
HALF = SIZE / 2.0
THICKNESS = 1.5           # slab depth below the top surface
TOP_Z = THICKNESS         # walking surface height

STEP = 0.5                # top surface grid pitch

PAINT_INSET = 1.0         # stone left between the painted line and the interior
PAINT_WIDTH = 1.0         # width of the painted line itself

# The painted square, as coordinates measured from the centre of the slab. Both
# figures above are distances from the edge, so they subtract from HALF - reading
# them as centre coordinates puts the marking in the middle of the pad instead of
# along its edge. PAINT_TO is the edge itself: the line runs right along it, which
# is what makes the four bands meet at the corners into one unbroken square.
PAINT_FROM = HALF - PAINT_INSET - PAINT_WIDTH
PAINT_TO = HALF
PAINT_LIFT = 0.03         # paint sits this far proud of the stone

# material slot indices
STONE, PAINT = 0, 1

BASE_STONE = (0.300, 0.301, 0.305)     # slate grey, the surface itself
PAINT_WHITE = (0.900, 0.898, 0.870)


def _grey(value):
    """A neutral RGB triple, a touch cool like stone."""
    return (value, value, value + 0.010)


def _hash01(*values):
    """Deterministic pseudo-random floats in [0, 1) from numeric inputs."""
    acc = np.zeros_like(np.asarray(values[0], dtype=np.float64))
    for index, value in enumerate(values):
        acc = acc + np.asarray(value, dtype=np.float64) * (127.1 + index * 61.7)
    return np.mod(np.sin(acc) * 43758.5453, 1.0)


class Mesh:
    """Accumulates quads with a material and colours.

    Two colour channels, because two kinds of surface need them:

    * ``cols``          - one colour per face, for walls that are flat.
    * ``corner_colors`` - one colour per corner, keyed by face index, for the
                          slab top whose faces straddle the paint edge.

    A face with an entry in ``corner_colors`` uses it; every other face uses its
    single ``cols`` entry.
    """

    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []
        self.cols = []
        self.corner_colors = {}

    def add(self, points, faces, mat, color):
        """Append faces that all share one material and one flat colour."""
        base = len(self.verts)
        self.verts.extend(points)
        for face in faces:
            self.faces.append(tuple(base + i for i in face))
        self.mats.extend([int(mat)] * len(faces))
        self.cols.extend([tuple(float(c) for c in color)] * len(faces))

    def add_cornered(self, points, faces, mats, corner_colors):
        """Append quads with a material per face and four colours per quad."""
        if any(len(face) != 4 for face in faces):
            raise ValueError("corner colours are only supported on quads")
        if len(mats) != len(faces):
            raise ValueError("need one material per face")
        if len(corner_colors) != 4 * len(faces):
            raise ValueError(
                "need %d corner colours for %d quads, got %d"
                % (4 * len(faces), len(faces), len(corner_colors))
            )

        first = len(self.faces)
        base = len(self.verts)
        self.verts.extend(points)
        for offset, face in enumerate(faces):
            self.faces.append(tuple(base + i for i in face))
            self.corner_colors[first + offset] = [
                tuple(float(c) for c in corner_colors[4 * offset + i])
                for i in range(4)
            ]
        self.mats.extend(int(mat) for mat in mats)
        self.cols.extend([None] * len(faces))

    def loops(self):
        """Per-loop colours: one entry per corner, in face order."""
        out = []
        for index, face in enumerate(self.faces):
            corners = self.corner_colors.get(index)
            if corners is not None:
                out.extend(corners)
            else:
                color = self.cols[index]
                out.extend([(float(color[0]), float(color[1]), float(color[2]))]
                           * len(face))
        return out

    def stats(self):
        return {
            "verts": len(self.verts),
            "faces": len(self.faces),
            "tris": sum(len(f) - 2 for f in self.faces),
            "loops": sum(len(f) for f in self.faces),
        }


# --------------------------------------------------------------------------
# top surface
# --------------------------------------------------------------------------

def _grid_coords():
    """Top-surface coordinates, snapped to both edges of the painted square."""
    values = np.arange(-HALF, HALF + 1e-9, STEP)
    edges = []
    for edge in (PAINT_FROM, PAINT_TO):
        for sign in (-1.0, 1.0):
            edges.append(sign * edge)

    keep = [v for v in edges if -HALF - 1e-9 <= v <= HALF + 1e-9]
    return np.unique(np.round(np.concatenate([values, np.array(keep)]), 4))


def _stone_colors(xx, yy):
    """Smooth slate grey, varied slowly so the slab is not a dead flat fill."""
    broad = _hash01(np.floor(xx / 6.0), np.floor(yy / 6.0))
    fine = _hash01(np.round(xx, 3) * 7.0, np.round(yy, 3) * 7.0)
    tone = 0.94 + 0.10 * broad + 0.03 * fine
    return np.stack([BASE_STONE[0] * tone,
                     BASE_STONE[1] * tone,
                     BASE_STONE[2] * tone], axis=-1)


def _paint_mask(xx, yy):
    """True where the point falls on the painted line along the slab edge.

    One continuous figure: the square band between PAINT_FROM and PAINT_TO, plus
    the corners that the four bands leave open.
    """
    ax, ay = np.abs(xx), np.abs(yy)
    in_x = (ax >= PAINT_FROM) & (ax <= PAINT_TO)
    in_y = (ay >= PAINT_FROM) & (ay <= PAINT_TO)
    return in_x | in_y


def _top(mesh):
    coords = _grid_coords()
    xx, yy = np.meshgrid(coords, coords, indexing="ij")
    paint = _paint_mask(xx, yy)
    stone = _stone_colors(xx, yy)

    flat = np.repeat(np.asarray(PAINT_WHITE)[None, None, :],
                     xx.shape[0], axis=0).repeat(xx.shape[1], axis=1)
    colors = np.where(paint[..., None], flat, stone)

    n = len(coords)
    zz = np.where(paint, TOP_Z + PAINT_LIFT, TOP_Z)
    top_verts = np.stack([xx.ravel(), yy.ravel(), zz.ravel()], axis=-1)

    grid = np.arange(n * n).reshape(n, n)
    quads = np.stack([
        grid[:-1, :-1].ravel(),
        grid[1:, :-1].ravel(),
        grid[1:, 1:].ravel(),
        grid[:-1, 1:].ravel(),
    ], axis=-1)

    centre_x = (xx[:-1, :-1] + xx[1:, 1:]) * 0.5
    centre_y = (yy[:-1, :-1] + yy[1:, 1:]) * 0.5
    face_paint = _paint_mask(centre_x, centre_y).ravel()
    mats = np.where(face_paint, PAINT, STONE)

    # one colour per grid point; each quad takes its four corners' colours
    point_colors = colors.reshape(-1, 3)
    mesh.add_cornered([tuple(map(float, p)) for p in top_verts],
                      [tuple(int(i) for i in quad) for quad in quads],
                      mats.tolist(),
                      [point_colors[index] for index in quads.reshape(-1)])


# --------------------------------------------------------------------------
# slab sides
# --------------------------------------------------------------------------

def _sides(mesh):
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    for index, (ax, ay) in enumerate(corners):
        bx, by = corners[(index + 1) % 4]
        quad = [
            (ax * HALF, ay * HALF, 0.0),
            (bx * HALF, by * HALF, 0.0),
            (bx * HALF, by * HALF, TOP_Z),
            (ax * HALF, ay * HALF, TOP_Z),
        ]
        # each wall its own shade, so the corners stay readable
        shade = 0.30 + 0.02 * index
        mesh.add(quad, [(0, 1, 2, 3)], STONE, _grey(shade))


def build():
    mesh = Mesh()
    _top(mesh)
    _sides(mesh)
    return mesh
