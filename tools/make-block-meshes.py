#!/usr/bin/env python3
"""Builds the nine instrument blocks' meshes and textures.

    ./tools/make-block-meshes.py              convert (downloading what is missing)
    ./tools/make-block-meshes.py --preview    also render each block to a PNG

The models are low-poly instruments from Poly Pizza, seven of the nine by the
same author, all CC-BY 3.0. They are *not* in the repository: this fetches them
into tools/models/ by id and converts, which keeps the shipped folder to what
Besiege loads and leaves the licences with their source. Credit is in the README.

What conversion means here:

  * glTF is Y-up and right-handed; a Besiege block mesh is Z-up -- see the
    sibling synth mod, whose generator says so in as many words -- and Unity is
    left-handed, so the axes are swapped and one is negated to keep the winding
    honest.

  * These models carry no texture at all, only a flat colour per material. A
    block needs a texture, so the colours are collected into a palette a few
    pixels across and every triangle is pointed at its own patch. Point-sampled
    by Besiege, so nothing bleeds between patches; and it means nine blocks cost
    nine tiny PNGs rather than nine atlases.

  * Each instrument is centred, scaled to the block, and turned upright by the
    table below, which is the only part of this that is taste rather than
    arithmetic. Run with --preview to see what it did.
"""

import io, json, math, os, struct, subprocess, sys, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CACHE = os.path.join(HERE, "models")
OUT = os.path.join(REPO, "Orchestra", "Resources", "Instruments")

# block -> (poly.pizza id, asset uuid, title, author, licence)
SOURCES = {
    "Piano":    ("7U-93vxPOER", "c217645d-d51f-4b36-8237-7bffe0e49029", "Piano", "jeremy", "CC-BY 3.0"),
    "Guitar":   ("0hg94uOO-sS", "6e20ad06-f528-41d5-a233-22d49b963858", "Electric guitar", "jeremy", "CC-BY 3.0"),
    "Bass":     ("afr6GCpce_I", "53d5d033-e2e3-4cd8-a399-4f6cdb737b27", "Acoustic guitar", "jeremy", "CC-BY 3.0"),
    "Strings":  ("fhj0GK-0kJu", "0a39deee-25d5-47b4-8a1d-305e635c7abe", "Violin", "jeremy", "CC-BY 3.0"),
    "Brass":    ("0Mj5XgeGtKJ", "18e3c8ce-1e7a-47c0-9fb3-4d27898d00bd", "Trumpet", "jeremy", "CC-BY 3.0"),
    "Woodwind": ("6A2UAKdCNy7", "27a428f2-ffb5-466e-87c5-5cb6e81b3ae4", "Saxophone", "jeremy", "CC-BY 3.0"),
    "Drums":    ("5Wp2emwd7xw", "58bd1985-0f3e-47f3-8c36-c19f958eea74", "Drum", "jeremy", "CC-BY 3.0"),
    "Mallets":  ("a-OYg3WVXfV", "f3f55659-b705-4877-ae87-27a5426d4b3e", "Xylophone", "Daniel Melchior", "CC-BY 3.0"),
    "Cymbals":  ("f8SdBE98BXE", "2d44ab30-ffe7-4ad4-8b5b-7f4ff66e1aac", "Cymbal", "Poly by Google", "CC-BY 3.0"),
}

# How each model is stood up on its block, after the axis swap: degrees about the
# block's own up, then about its front. Taste, and the only knob here.
POSE = {
    # All nine models are authored the way glTF says they should be, so the swap
    # above is the whole of it. They were once turned over on the strength of a
    # preview whose camera was upside down -- see `preview` -- which put five of
    # them into the game standing on their heads. Judge a change here against the
    # game, or against a preview that has been checked against the game.
    # Yaw is about the block's own up, so a quarter turn either way changes which
    # way an instrument faces without tipping it over. Negative is clockwise seen
    # from above the block.
    #
    # These face the block's +y, which is the side a machine is usually looked at
    # from: a piano shows its keyboard, a guitar its face rather than its back.
    # The signs turned over when the handedness above was fixed: a reflection does
    # not commute with the yaw, so mirroring the model reverses which way a given
    # turn carries its face.
    "Piano":    (-90.0, 0.0),
    "Guitar":   (-90.0, 0.0),
    "Bass":     (-90.0, 0.0),
    "Strings":  (-90.0, 0.0),
    "Brass":    (-90.0, 0.0),
    "Woodwind": (-90.0, 0.0),
    "Drums":    (0.0, 0.0),
    "Mallets":  (0.0, 0.0),
    "Cymbals":  (0.0, 0.0),
}

# What each block XML gives its <Icon> rotation, kept here so `--preview` can
# render from where the toolbar will look. These must be copied into the XMLs by
# hand; XmlCheck holds the two together.
#
# Read them as a camera, which `icon_camera` works out exactly: x sets how far
# above the block it sits, y and the block's own turn set which side, and z spins
# the block under it. At -65 the camera is a third of the way up from the block's
# waist -- Besiege's own three-quarter -- and at -25 it is half above it, looking
# down at something whose face is its top.
#
# z is where the instrument ends up looking, and the toolbar lights a block from
# beside the camera and to the right -- the drum icon's right-hand shell facet is
# its brightest. So every instrument is turned to that side: at z=115 a face sits
# about 37 degrees right of the camera, lit rather than shadowed, and still open
# enough to be read as a piano or a guitar. Turning further would only present the
# edge of something flat. The horns take z=180, which is the same turn measured
# from a different starting angle: they lie across the block rather than facing
# out of it, and 180 is what points a bell to the right.
DEFAULT_ICON = (-65.0, 210.0, 115.0)

ICON = {
    # Front, from above: everything with a face worth showing.
    "Piano":    DEFAULT_ICON,
    "Guitar":   DEFAULT_ICON,
    "Bass":     DEFAULT_ICON,
    "Strings":  DEFAULT_ICON,
    # A trumpet and a saxophone are a profile from either side; this is the side
    # that puts the bell to the right, with the light on it. The trumpet carries
    # 20 degrees of roll on top, which lifts the bell off the horizontal:
    # `--pose -65 210 180 20`.
    "Brass":    (-51.2, -125.7, 151.8),
    "Woodwind": (-65.0, 210.0, 180.0),
    # Struck from above, so drawn from above: bars, drum head, cymbal. All three
    # are rolled as well -- from `-25,210,115`, which showed their faces leaning
    # left, by -80 for the round two (`--pose -25 210 115 -80`) and by -65 for the
    # xylophone, which needs to lie along the tile as well as face right.
    "Drums":    (-31.3, 156.7, -151.6),
    "Mallets":  (-36.1, 166.3, -168.7),
    "Cymbals":  (-31.3, 156.7, -151.6),
}

# The mesh is worn at half scale by the block XML, as the synth block's is, so a
# model drawn to this reaches four fifths of the block.
SPAN = 1.6


# ---- glTF -----------------------------------------------------------------

def read_glb(path):
    d = open(path, "rb").read()
    if d[:4] != b"glTF":
        raise SystemExit("%s is not a GLB" % path)
    length = struct.unpack_from("<I", d, 12)[0]
    js = json.loads(d[20:20 + length])
    at = 20 + length
    blob = b""
    while at < len(d):
        size, kind = struct.unpack_from("<II", d, at)
        if kind == 0x004E4942:
            blob = d[at + 8:at + 8 + size]
        at += 8 + size + (-size % 4)
    return js, blob


COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2),
             5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def accessor(js, blob, index):
    acc = js["accessors"][index]
    fmt, size = COMPONENT[acc["componentType"]]
    n = COUNT[acc["type"]]
    view = js["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or size * n
    out = []
    for i in range(acc["count"]):
        at = start + i * stride
        out.append(struct.unpack_from("<" + fmt * n, blob, at))
    return out


def node_matrix(node):
    if "matrix" in node:
        m = node["matrix"]
        return [m[0:4], m[4:8], m[8:12], m[12:16]]     # column-major
    t = node.get("translation", [0, 0, 0])
    r = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    x, y, z, w = r
    rot = [[1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)],
           [2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)],
           [2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)]]
    cols = [[rot[c][r_] * s[c] for r_ in range(3)] + [0.0] for c in range(3)]
    return cols + [[t[0], t[1], t[2], 1.0]]


def multiply(a, b):
    """Column-major 4x4, a applied after b."""
    out = []
    for c in range(4):
        col = []
        for r in range(4):
            col.append(sum(a[k][r] * b[c][k] for k in range(4)))
        out.append(col)
    return out


def apply(m, v, point=True):
    w = 1.0 if point else 0.0
    return tuple(sum(m[k][r] * (list(v) + [w])[k] for k in range(4)) for r in range(3))


def triangles(path):
    """Every triangle in the file, as (three positions, three normals, colour)."""
    js, blob = read_glb(path)
    colours = []
    for mat in js.get("materials", [{}]):
        base = mat.get("pbrMetallicRoughness", {}).get("baseColorFactor", [0.8, 0.8, 0.8, 1])
        colours.append(tuple(base[:3]))
    if not colours:
        colours = [(0.8, 0.8, 0.8)]

    scene = js.get("scenes", [{}])[js.get("scene", 0)]
    out = []

    def walk(index, parent):
        node = js["nodes"][index]
        world = multiply(parent, node_matrix(node))
        if "mesh" in node:
            for prim in js["meshes"][node["mesh"]]["primitives"]:
                if prim.get("mode", 4) != 4:
                    continue
                pos = accessor(js, blob, prim["attributes"]["POSITION"])
                nrm = (accessor(js, blob, prim["attributes"]["NORMAL"])
                       if "NORMAL" in prim["attributes"] else None)
                idx = ([i[0] for i in accessor(js, blob, prim["indices"])]
                       if "indices" in prim else list(range(len(pos))))
                colour = colours[prim.get("material", 0) % len(colours)]
                for t in range(0, len(idx) - 2, 3):
                    tri = [idx[t], idx[t + 1], idx[t + 2]]
                    ps = [apply(world, pos[i]) for i in tri]
                    ns = ([apply(world, nrm[i], False) for i in tri] if nrm
                          else [face_normal(ps)] * 3)
                    out.append((ps, ns, colour))
        for child in node.get("children", []):
            walk(child, world)

    unit = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
    for root in scene.get("nodes", range(len(js.get("nodes", [])))):
        walk(root, unit)
    return out


def face_normal(p):
    u = [p[1][i] - p[0][i] for i in range(3)]
    v = [p[2][i] - p[0][i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    return normalise(n)


def normalise(v):
    d = math.sqrt(sum(c * c for c in v)) or 1.0
    return (v[0] / d, v[1] / d, v[2] / d)


# ---- placing it on the block ----------------------------------------------

def stand(tris, yaw, roll):
    """glTF's frame to the block's: Z up, centred, scaled, then posed."""
    placed = []
    for ps, ns, colour in tris:
        # glTF is right-handed and Unity is left-handed, so the map between them
        # has to *flip* the handedness: a swap that preserves it lands a model
        # that is correct in every measurement and mirrored, which is invisible
        # on a drum and plain on a piano. This one negates x as well as z, so it
        # reflects, and `wind` puts the winding back from the shading normals.
        # A piano's keyboard runs the right way along it because of this line.
        ps = [(-p[0], -p[2], p[1]) for p in ps]
        ns = [(-n[0], -n[2], n[1]) for n in ns]
        placed.append((ps, ns, colour))

    a, b = math.radians(yaw), math.radians(roll)
    for angle, axis in ((a, 2), (b, 0)):
        if abs(angle) < 1e-9:
            continue
        co, si = math.cos(angle), math.sin(angle)
        i, j = (0, 1) if axis == 2 else (1, 2)
        def turn(v):
            out = list(v)
            out[i] = v[i] * co - v[j] * si
            out[j] = v[i] * si + v[j] * co
            return tuple(out)
        placed = [([turn(p) for p in ps], [turn(n) for n in ns], c) for ps, ns, c in placed]

    lo = [min(p[k] for ps, _, _ in placed for p in ps) for k in range(3)]
    hi = [max(p[k] for ps, _, _ in placed for p in ps) for k in range(3)]
    mid = [(lo[k] + hi[k]) / 2.0 for k in range(3)]
    scale = SPAN / max(hi[k] - lo[k] for k in range(3))
    return [([tuple((p[k] - mid[k]) * scale for k in range(3)) for p in ps], ns, c)
            for ps, ns, c in placed], [(hi[k] - lo[k]) * scale for k in range(3)]


def wind(tris):
    """Turns any triangle whose face disagrees with its own shading normal."""
    out = []
    for ps, ns, colour in tris:
        avg = [sum(n[k] for n in ns) / 3.0 for k in range(3)]
        f = face_normal(ps)
        if sum(f[k] * avg[k] for k in range(3)) < 0.0:
            ps = [ps[0], ps[2], ps[1]]
            ns = [ns[0], ns[2], ns[1]]
        out.append((ps, ns, colour))
    return out


# ---- the palette ----------------------------------------------------------

CELL = 8            # pixels per colour, so point sampling cannot reach a neighbour


def palette(colours):
    """A square of flat patches, and where the middle of each one is in uv."""
    side = 1
    while side * side < len(colours):
        side += 1
    size = side * CELL
    rows = [[(0, 0, 0)] * size for _ in range(size)]
    uv = {}
    for i, c in enumerate(colours):
        cx, cy = i % side, i // side
        rgb = tuple(int(round(255 * srgb(v))) for v in c)
        for y in range(cy * CELL, (cy + 1) * CELL):
            for x in range(cx * CELL, (cx + 1) * CELL):
                rows[y][x] = rgb
        # v counts up from the bottom in a texture, down in the image.
        uv[c] = ((cx + 0.5) * CELL / size, 1.0 - (cy + 0.5) * CELL / size)
    return rows, uv


def srgb(linear):
    """glTF factors are linear; a texture is read as sRGB."""
    if linear <= 0.0031308:
        return max(0.0, 12.92 * linear)
    return min(1.0, 1.055 * (linear ** (1 / 2.4)) - 0.055)


def write_png(path, rows):
    height, width = len(rows), len(rows[0])
    raw = b"".join(b"\0" + b"".join(struct.pack("BBB", *px) for px in row) for row in rows)
    def chunk(kind, body):
        c = kind + body
        return struct.pack(">I", len(body)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


# ---- output ---------------------------------------------------------------

def write_obj(path, tris, uv, source):
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
        f.write("# Generated by tools/make-block-meshes.py -- edit that, not this.\n")
        f.write("# %s by %s, %s, from https://poly.pizza/m/%s\n" % source)
        for v in verts:
            f.write("v %.5f %.5f %.5f\n" % v)
        for t in texels:
            f.write("vt %.5f %.5f\n" % t)
        for n in norms:
            f.write("vn %.4f %.4f %.4f\n" % n)
        for face in faces:
            f.write("f " + " ".join("%d/%d/%d" % v for v in face) + "\n")
    return len(verts), len(faces)


# ---- a look at what came out ----------------------------------------------

def preview(path, tris, block, size=340, icon=False):
    """A flat-shaded render, so a pose can be judged without starting the game.
    Nothing here ships.

    Two views: three-quarters from the front, which is how a block is seen while
    it is being built with, and the toolbar's, worked out from the same rotation
    the block XMLs give their icon."""
    if icon:
        eye, up = icon_camera(block)
    else:
        # From the block's +y, which is the side the instruments face and the side
        # a machine is looked at from -- the same view as the toolbar's, further
        # round and less steep.
        eye = normalise((0.85, 1.0, 0.65))
        right = normalise((eye[1], -eye[0], 0.0))
        # right x eye, not eye x right: the other way round puts the block's up at
        # the bottom of the picture, which is a preview that lies about the one
        # thing it is for. Blocks were shipped standing on their heads on the
        # strength of it.
        up = (right[1] * eye[2] - right[2] * eye[1],
              right[2] * eye[0] - right[0] * eye[2],
              right[0] * eye[1] - right[1] * eye[0])
    # `cross(up, eye)` is the screen's right in a right-handed frame, and these
    # coordinates are Unity's, which is left-handed -- so it points left, and every
    # render this drew was a mirror of the game. Proven on the trumpet: the game
    # draws its bell to the left of the tile, and this drew it to the right until
    # the arguments were swapped. It cost a round of poses turned the wrong way,
    # "right" in the preview being left in the toolbar.
    right = normalise(cross(eye, up))
    up = normalise(cross(right, eye))
    # Beside the camera and to the right, which is where the toolbar's own light
    # is: the drum icon's right-hand shell facet is its brightest.
    light = normalise((eye[0] + right[0] * 0.9 + 0.1,
                       eye[1] + right[1] * 0.9 + 0.1,
                       eye[2] + right[2] * 0.9 + 0.5))


    pixels = [[(24, 24, 28)] * size for _ in range(size)]
    depth = [[1e9] * size for _ in range(size)]
    span = SPAN * 1.25

    def project(p):
        x = sum(p[k] * right[k] for k in range(3))
        y = sum(p[k] * up[k] for k in range(3))
        z = sum(p[k] * eye[k] for k in range(3))
        return ((x / span + 0.5) * size, (0.5 - y / span) * size, -z)

    for ps, ns, colour in tris:
        pts = [project(p) for p in ps]
        n = face_normal(ps)
        shade = 0.35 + 0.65 * max(0.0, sum(n[k] * light[k] for k in range(3)))
        rgb = tuple(min(255, int(255 * srgb(c) * shade)) for c in colour)
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        x0, x1 = max(0, int(min(xs))), min(size - 1, int(max(xs)) + 1)
        y0, y1 = max(0, int(min(ys))), min(size - 1, int(max(ys)) + 1)
        ax, ay, az = pts[0]; bx, by, bz = pts[1]; cx, cy, cz = pts[2]
        area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)
        if abs(area) < 1e-9:
            continue
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                px, py = x + 0.5, y + 0.5
                w0 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) / area
                w1 = ((px - ax) * (cy - ay) - (py - ay) * (cx - ax)) / area
                if w0 < 0 or w1 < 0 or w0 + w1 > 1:
                    continue
                z = az + w1 * (bz - az) + w0 * (cz - az)
                if z < depth[y][x]:
                    depth[y][x] = z
                    pixels[y][x] = rgb
    write_png(path, pixels)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def icon_camera(block):
    """Where the toolbar looks at a block from, in the block's own frame.

    The toolbar camera is fixed and the block is turned in front of it by the
    icon rotation, so the camera in the block's frame is that rotation undone.

    It looks along **+z**, not the -z a Unity camera looks along by default. That
    sign is not a guess: at -115,210,0 this returned a camera in front of and
    above the block, and the game drew all nine from behind and below -- the
    piano showing its legs and underside, the drum its bottom head. Screenshot
    20260822200251_1.jpg, against the render this same function produced.
    Whatever the toolbar does with the block after it has turned it, the two are
    opposite, and everything here is written to reproduce the game rather than
    to explain it. Judge a change to these numbers against a preview, and the
    preview against the game.
    """
    x, y, z = (math.radians(a) for a in ICON[block])

    # Unity turns Z, then X, then Y, so undoing it is Y, then X, then Z.
    def unturn(v):
        cy, sy = math.cos(-y), math.sin(-y)
        v = (v[0] * cy + v[2] * sy, v[1], -v[0] * sy + v[2] * cy)
        cx, sx = math.cos(-x), math.sin(-x)
        v = (v[0], v[1] * cx - v[2] * sx, v[1] * sx + v[2] * cx)
        cz, sz = math.cos(-z), math.sin(-z)
        return (v[0] * cz - v[1] * sz, v[0] * sz + v[1] * cz, v[2])

    return normalise(unturn((0.0, 0.0, -1.0))), normalise(unturn((0.0, 1.0, 0.0)))


def rolled(pose, degrees):
    """One icon pose turned in the picture plane, about the camera's own axis.

    Neither of the block's own turns can do this: the toolbar camera looks along
    world +z, and an icon rotation is Y, then X, then Z of the *block*. So the
    roll is applied to the whole rotation and the result decomposed back into the
    order Unity reads, which is why some entries in ICON are not round numbers.
    `--pose x y z roll` prints one.

    It is what tilts a trumpet's bell up off the horizontal, and what turns a drum
    head or a set of bars to face right rather than left -- a round thing has no
    other way of facing anywhere.
    """
    x, y, z = (math.radians(v) for v in pose)

    def mul(A, B):
        return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)]
                for i in range(3)]

    def Rx(a):
        c, s = math.cos(a), math.sin(a)
        return [[1, 0, 0], [0, c, -s], [0, s, c]]

    def Ry(a):
        c, s = math.cos(a), math.sin(a)
        return [[c, 0, s], [0, 1, 0], [-s, 0, c]]

    def Rz(a):
        c, s = math.cos(a), math.sin(a)
        return [[c, -s, 0], [s, c, 0], [0, 0, 1]]

    R = mul(Rz(math.radians(degrees)), mul(Ry(y), mul(Rx(x), Rz(z))))
    out = [math.degrees(v) for v in (math.asin(max(-1.0, min(1.0, -R[1][2]))),
                                     math.atan2(R[0][2], R[2][2]),
                                     math.atan2(R[1][0], R[1][1]))]
    # The decomposition has to rebuild what it came from.
    x2, y2, z2 = (math.radians(v) for v in out)
    back = mul(Ry(y2), mul(Rx(x2), Rz(z2)))
    assert max(abs(back[i][j] - R[i][j])
               for i in range(3) for j in range(3)) < 1e-9, pose
    return [round(v, 1) for v in out]


def fetch(block):
    pid, uuid = SOURCES[block][0], SOURCES[block][1]
    path = os.path.join(CACHE, block + ".glb")
    if not os.path.exists(path):
        if not os.path.isdir(CACHE):
            os.makedirs(CACHE)
        url = "https://static.poly.pizza/%s.glb" % uuid
        print("  fetching %s (%s)" % (block, pid))
        subprocess.check_call(["curl", "-sL", "-o", path, url])
    return path


def main():
    if "--pose" in sys.argv:
        at = sys.argv.index("--pose")
        x, y, z, turn = (float(v) for v in sys.argv[at + 1:at + 5])
        print("%s rolled %+.0f -> %s"
              % ((x, y, z), turn, tuple(rolled((x, y, z), turn))))
        return

    want_preview = "--preview" in sys.argv
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    shots = os.path.join(CACHE, "preview")
    if want_preview and not os.path.isdir(shots):
        os.makedirs(shots)

    for block in sorted(SOURCES):
        pid, uuid, title, author, licence = SOURCES[block]
        tris = triangles(fetch(block))
        yaw, roll = POSE[block]
        tris, size = stand(tris, yaw, roll)
        tris = wind(tris)

        colours = sorted(set(c for _, _, c in tris))
        rows, uv = palette(colours)
        write_png(os.path.join(OUT, block + ".png"), rows)
        verts, faces = write_obj(os.path.join(OUT, block + ".obj"), tris, uv,
                                 (title, author, licence, pid))
        if want_preview:
            preview(os.path.join(shots, block + ".png"), tris, block)
            preview(os.path.join(shots, block + "-icon.png"), tris, block, icon=True)
        print("  %-9s %-16s %5d faces, %2d colour(s), %s"
              % (block, title, faces, len(colours),
                 " x ".join("%.2f" % s for s in size)))
    if want_preview:
        print("previews in", os.path.relpath(shots, REPO))


if __name__ == "__main__":
    main()
