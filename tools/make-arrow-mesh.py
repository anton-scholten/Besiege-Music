#!/usr/bin/env python3
"""Builds the MIDI loader block's mesh and texture: a download arrow.

    ./tools/make-arrow-mesh.py              write the mesh and its texture
    ./tools/make-arrow-mesh.py --preview    also render it as the toolbar sees it

Unlike the nine instruments this shape is not a model anybody made: it is a
download arrow -- a shaft, a head pointing down, and a bar under the tip -- built
out of boxes here. The conventions are the instruments': the block's up is +z,
the model is centred and scaled to SPAN, and each flat colour gets a patch of a
tiny palette texture rather than an atlas. tools/make-block-meshes.py is where
all of that is explained, and this borrows its palette, its PNG writer and its
preview renderer rather than restating them.

The bar sits under the tip, the way a download icon draws it, rather than at the
arrow's tail: `--bar top` puts it at the tail instead, and the block XML's icon
pose is the only other thing that would have to change.
"""

import importlib.util
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
OUT = os.path.join(REPO, "Orchestra", "Resources", "Instruments")

BLOCK = "Loader"

# How the toolbar sees the block, as its <Icon><Rotation>. Read as a camera by
# make-block-meshes.icon_camera: the arrow is a flat sign, so it is shown nearly
# face on -- a three-quarter view of a plate is a line -- and turned a little to
# catch the toolbar's light, which comes from beside the camera and to the right.
#
# XmlCheck reads this table out of this file and holds Loader.xml to it, exactly
# as it does for the instruments.
ICON = {
    # The instruments' own pose -- front, from above, turned into the toolbar's
    # light -- rolled 15 degrees back in the picture plane, which is what stands
    # the shaft upright without changing where it is lit from. Read it as a camera
    # rather than as three turns; `make-block-meshes.rolled` is what produced the
    # numbers, from DEFAULT_ICON and -15.
    "Loader": (-68.5, 175.2, 152.6),
}

# Linear colour, as glTF gives it and as the palette expects: the arrow in
# Besiege's own signal orange, the bar it lands on in dark steel.
ARROW = (0.78, 0.30, 0.03)
BAR = (0.10, 0.10, 0.12)


def _mesh_tool():
    """make-block-meshes.py, imported despite the hyphen in its name."""
    path = os.path.join(HERE, "make-block-meshes.py")
    spec = importlib.util.spec_from_file_location("make_block_meshes", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# ---- solids ----------------------------------------------------------------

def quad(a, b, c, d, colour):
    """Two triangles, flat shaded: the normal is the face's own."""
    return [_flat([a, b, c], colour), _flat([a, c, d], colour)]


def _flat(points, colour):
    """A triangle with one normal, worked out from the order its points are in:
    counter-clockwise seen from outside, which is what Unity draws."""
    u = [points[1][k] - points[0][k] for k in range(3)]
    v = [points[2][k] - points[0][k] for k in range(3)]
    n = (u[1] * v[2] - u[2] * v[1],
         u[2] * v[0] - u[0] * v[2],
         u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(c * c for c in n)) or 1.0
    n = tuple(c / length for c in n)
    return ([tuple(p) for p in points], [n, n, n], colour)


def outward(tris, centre, what):
    """Holds a solid to faces that point out of it.

    `make-block-meshes.wind` cannot catch a mistake here: it turns a triangle
    whose winding disagrees with its *shading* normal, and these normals are
    worked out from the winding, so the two always agree and a face wound the
    wrong way round stays wrong. In Besiege that face is culled -- you see
    straight through the block, and the lighting on what is left is nonsense.
    The whole arrowhead shipped that way once, so the check is written down.
    """
    for points, _, _ in tris:
        n = _flat(points, None)[1][0]
        mid = [sum(p[k] for p in points) / 3.0 for k in range(3)]
        if sum(n[k] * (mid[k] - centre[k]) for k in range(3)) <= 0.0:
            raise SystemExit("%s has a face wound inside out: %s" % (what, points))
    return tris


def middle(bounds):
    return [(lo + hi) / 2.0 for lo, hi in bounds]


def box(bounds, colour):
    """An axis-aligned box, as ((x0, x1), (y0, y1), (z0, z1))."""
    (x0, x1), (y0, y1), (z0, z1) = bounds
    p = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7),        # -z, +z
             (0, 1, 5, 4), (2, 3, 7, 6),        # -y, +y
             (1, 2, 6, 5), (3, 0, 4, 7)]        # +x, -x
    out = []
    for a, b, c, d in faces:
        out += quad(p[a], p[b], p[c], p[d], colour)
    return outward(out, middle(bounds), "the box at " + str(bounds))


def wedge(bounds, tip_z, colour):
    """The arrowhead: a rectangle that closes to a point at `tip_z`.

    A pyramid rather than a flat triangle, so the head reads as solid from the
    side as well as from the front -- the block is looked at from every angle
    while a machine is built.
    """
    (x0, x1), (y0, y1), (z0, _) = bounds
    base = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]
    tip = ((x0 + x1) / 2.0, (y0 + y1) / 2.0, tip_z)
    # The base looks away from the tip, and each side has the tip last with its
    # two base corners the other way round from the base's own winding.
    out = quad(base[0], base[1], base[2], base[3], colour)
    for i in range(4):
        out.append(_flat([base[(i + 1) % 4], base[i], tip], colour))
    return outward(out, [(x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + tip_z) / 2.0],
                   "the arrowhead")


def arrow(bar_at_top):
    """The whole shape, in the block's own frame: +z is up, the plate faces y."""
    thick = (-0.11, 0.11)
    tris = []
    # The shaft, from the head up to the tail.
    tris += box(((-0.17, 0.17), thick, (-0.10, 0.62)), ARROW)
    # The head, pointing down.
    tris += wedge(((-0.42, 0.42), thick, (-0.10, -0.10)), -0.52, ARROW)
    # The line it lands on -- or, with --bar top, the one it hangs from.
    if bar_at_top:
        tris += box(((-0.50, 0.50), thick, (0.62, 0.78)), BAR)
    else:
        tris += box(((-0.50, 0.50), thick, (-0.78, -0.62)), BAR)
    return tris


def centred(tris, span):
    """Centred on the block's middle and scaled to `span`, as `stand` leaves the
    instruments -- so the arrow is the size of a block, not of its own numbers."""
    lo = [min(p[k] for ps, _, _ in tris for p in ps) for k in range(3)]
    hi = [max(p[k] for ps, _, _ in tris for p in ps) for k in range(3)]
    mid = [(lo[k] + hi[k]) / 2.0 for k in range(3)]
    scale = span / max(hi[k] - lo[k] for k in range(3))
    out = [([tuple((p[k] - mid[k]) * scale for k in range(3)) for p in ps], ns, c)
           for ps, ns, c in tris]
    return out, [(hi[k] - lo[k]) * scale for k in range(3)]


def write_obj(path, tris, uv):
    """The instruments' writer without its Poly Pizza credit line: nothing here
    came from anywhere."""
    verts, norms, faces = [], [], []
    seen_v, seen_n = {}, {}

    def index(store, seen, key):
        if key not in seen:
            store.append(key)
            seen[key] = len(store)
        return seen[key]

    texels = sorted(set(uv.values()))
    texel_index = dict((t, i + 1) for i, t in enumerate(texels))
    for ps, ns, colour in tris:
        face = []
        for p, n in zip(ps, ns):
            face.append((index(verts, seen_v, tuple(round(c, 5) for c in p)),
                         texel_index[uv[colour]],
                         index(norms, seen_n, tuple(round(c, 4) for c in n))))
        faces.append(face)

    with open(path, "w") as f:
        f.write("# Generated by tools/make-arrow-mesh.py -- edit that, not this.\n")
        for v in verts:
            f.write("v %.5f %.5f %.5f\n" % v)
        for t in texels:
            f.write("vt %.5f %.5f\n" % t)
        for n in norms:
            f.write("vn %.4f %.4f %.4f\n" % n)
        for face in faces:
            f.write("f " + " ".join("%d/%d/%d" % v for v in face) + "\n")
    return len(verts), len(faces)


def main():
    mesh = _mesh_tool()
    bar_at_top = "--bar" in sys.argv and sys.argv[sys.argv.index("--bar") + 1] == "top"

    tris, size = centred(arrow(bar_at_top), mesh.SPAN)
    tris = mesh.wind(tris)

    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    colours = sorted(set(c for _, _, c in tris))
    rows, uv = mesh.palette(colours)
    mesh.write_png(os.path.join(OUT, BLOCK + ".png"), rows)
    verts, faces = write_obj(os.path.join(OUT, BLOCK + ".obj"), tris, uv)
    print("  %-9s %5d faces, %d colour(s), %s"
          % (BLOCK, faces, len(colours), " x ".join("%.2f" % s for s in size)))

    if "--preview" in sys.argv:
        shots = os.path.join(HERE, "models", "preview")
        if not os.path.isdir(shots):
            os.makedirs(shots)
        # The preview renderer reads the pose out of the mesh tool's own table,
        # so this block is added to it for the length of the render.
        mesh.ICON[BLOCK] = ICON[BLOCK]
        mesh.preview(os.path.join(shots, BLOCK + ".png"), tris, BLOCK)
        mesh.preview(os.path.join(shots, BLOCK + "-icon.png"), tris, BLOCK, icon=True)
        print("previews in", os.path.relpath(shots, REPO))


if __name__ == "__main__":
    main()
