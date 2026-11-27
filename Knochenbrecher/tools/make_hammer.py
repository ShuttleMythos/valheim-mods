"""Modelliert den Knochenbrecher in Blender und exportiert ihn für den Mod.

Aufruf (im Ordner Knochenbrecher):

    blender -b -P tools/make_hammer.py -- <ausgabe-ordner-für-bilder>

Erzeugt:
  src/Resources/hammer.bin   3D-Modell, wird in die DLL eingebettet
  <ausgabe>/hammer_ganz.png  Vorschau ganzer Hammer
  <ausgabe>/hammer_kopf.png  Vorschau Hammerkopf
  <ausgabe>/hammer_icon.png  freigestelltes Bild für die Symbole

Blender-Koordinaten: Stiel entlang +Z, Knauf bei z = 0, Hammerkopf quer entlang X.
"""
import math
import os
import struct
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = os.path.abspath(ARGS[0]) if ARGS else None
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MESH_FILE = os.path.join(ROOT, "src", "Resources", "hammer.bin")

# Name -> (Farbe, Metallisch, Glanz, Leuchten)
MATERIALS = {
    "silver":   ((0.88, 0.89, 0.92), 1.0, 0.90, 0.0),
    "gold":     ((1.00, 0.74, 0.22), 1.0, 0.88, 0.0),
    "ruby":     ((0.85, 0.03, 0.08), 0.0, 0.97, 0.35),
    "sapphire": ((0.05, 0.22, 0.95), 0.0, 0.97, 0.35),
    "emerald":  ((0.02, 0.70, 0.22), 0.0, 0.97, 0.35),
    "amethyst": ((0.60, 0.15, 0.88), 0.0, 0.97, 0.35),
    "topaz":    ((1.00, 0.52, 0.04), 0.0, 0.97, 0.35),
    "diamond":  ((0.75, 0.95, 1.00), 0.0, 0.99, 0.45),
}
GEM_CYCLE = ["ruby", "emerald", "sapphire", "topaz", "amethyst", "diamond"]

SHAFT_TOP = 1.52       # oberes Ende des Stiels
HEAD_Z = SHAFT_TOP + 0.20  # Mitte des Hammerkopfs
HX, HY = 0.24, 0.13    # halbe Länge / halbe Dicke des Kopfs

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
parts = []


def material(name):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    color, metallic, gloss, emission = MATERIALS[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = 1.0 - gloss
    bsdf.inputs["Emission Color"].default_value = (*color, 1)
    bsdf.inputs["Emission Strength"].default_value = emission * 0.12
    return mat


def register(obj, mat, smooth=True):
    if obj.name not in scene.collection.objects:
        scene.collection.objects.link(obj)
    obj.data.materials.clear()
    obj.data.materials.append(material(mat))
    if obj.type == "MESH":
        for poly in obj.data.polygons:
            poly.use_smooth = smooth
        obj.data.use_auto_smooth = True
        obj.data.auto_smooth_angle = math.radians(40)
    parts.append(obj)
    return obj


def from_bmesh(bm, name, mat, smooth=True, matrix=None):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    if matrix is not None:
        obj.matrix_world = matrix
    return register(obj, mat, smooth)


def box(center, half, mat, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * half[0], v.co.y * half[1], v.co.z * half[2])) + Vector(center)
    obj = from_bmesh(bm, "box", mat)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    return obj


def frustum(z0, z1, r0, r1, mat, sides=20, axis="Z", offset=(0, 0, 0)):
    """Kegelstumpf entlang einer Achse, von z0 (Radius r0) bis z1 (Radius r1)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=r0, radius2=r1, depth=z1 - z0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, (z0 + z1) / 2))
    rot = Matrix.Identity(4) if axis == "Z" else Matrix.Rotation(math.radians(90), 4, "Y")
    return from_bmesh(bm, "frustum", mat, matrix=Matrix.Translation(offset) @ rot)


def sphere(center, radius, mat):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius)
    return from_bmesh(bm, "sphere", mat, matrix=Matrix.Translation(center))


def orient(center, normal):
    rot = Vector((0, 0, 1)).rotation_difference(Vector(normal).normalized()).to_matrix().to_4x4()
    return Matrix.Translation(center) @ rot


def torus(center, normal, major, minor, mat, seg=20, ring=6):
    bm = bmesh.new()
    for i in range(seg):
        a0 = 2 * math.pi * i / seg
        for j in range(ring):
            b = 2 * math.pi * j / ring
            r = major + minor * math.cos(b)
            bm.verts.new((r * math.cos(a0), r * math.sin(a0), minor * math.sin(b)))
    bm.verts.ensure_lookup_table()
    for i in range(seg):
        for j in range(ring):
            a, b = i * ring + j, i * ring + (j + 1) % ring
            c, d = ((i + 1) % seg) * ring + (j + 1) % ring, ((i + 1) % seg) * ring + j
            bm.faces.new((bm.verts[a], bm.verts[d], bm.verts[c], bm.verts[b]))
    return from_bmesh(bm, "torus", mat, matrix=orient(center, normal))


def gem(center, normal, radius, mat, bezel=True, sides=8):
    """Facettierter Edelstein: Tafel oben, Rundiste auf Höhe 0, Spitze unten (steckt in der Fassung)."""
    bm = bmesh.new()
    step = 2 * math.pi / sides
    girdle = [bm.verts.new((radius * math.cos(i * step), radius * math.sin(i * step), 0)) for i in range(sides)]
    table = [bm.verts.new((0.55 * radius * math.cos((i + 0.5) * step),
                           0.55 * radius * math.sin((i + 0.5) * step), 0.42 * radius)) for i in range(sides)]
    apex = bm.verts.new((0, 0, -0.6 * radius))
    bm.faces.new(table)
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new((girdle[i], girdle[j], table[i]))
        bm.faces.new((table[i], girdle[j], table[j]))
        bm.faces.new((girdle[j], girdle[i], apex))
    matrix = orient(center, normal)
    from_bmesh(bm, "gem", mat, smooth=False, matrix=matrix)
    if bezel:
        torus(center, normal, radius * 1.04, radius * 0.2, "gold", seg=14, ring=6)


def tube(points, radius, mat, resolution=1):
    curve = bpy.data.curves.new("tube", "CURVE")
    curve.dimensions = "3D"
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (co[0], co[1], co[2], 1)
    curve.bevel_depth = radius
    curve.bevel_resolution = resolution
    curve.use_fill_caps = True
    return register(bpy.data.objects.new("tube", curve), mat)


def helix(z0, z1, radius, pitch, phase, wire, mat):
    turns = (z1 - z0) / pitch
    count = max(2, int(turns * 14))
    pts = []
    for i in range(count + 1):
        t = i / count
        a = phase + 2 * math.pi * turns * t
        pts.append((radius * math.cos(a), radius * math.sin(a), z0 + (z1 - z0) * t))
    tube(pts, wire, mat)


def scroll_points(su, sv):
    """Schnörkel (Volute) in Flächenkoordinaten (u entlang des Kopfs, v quer dazu)."""
    cu, cv, big = 0.125, 0.062, 0.05
    # Anlauf: Bogen von der Fassung des Mittelsteins bis an den Rand der Spirale
    p0, p1, p2 = (0.054, 0.010), (0.076, 0.012), (cu - big, cv)
    pts = []
    for i in range(8):
        t = i / 8
        pts.append(((1 - t) ** 2 * p0[0] + 2 * t * (1 - t) * p1[0] + t * t * p2[0],
                    (1 - t) ** 2 * p0[1] + 2 * t * (1 - t) * p1[1] + t * t * p2[1]))
    turns, count = 2.3, 46
    start = math.pi
    for i in range(count + 1):
        t = i / count
        r = big * (1 - 0.86 * t)
        a = start - 2 * math.pi * turns * t
        pts.append((cu + r * math.cos(a), cv + r * math.sin(a)))
    return [(su * u, sv * v) for u, v in pts]


def decorate_face(to3d, normal, center_gem, gem_offset=0):
    """Schnörkel und Steine auf einer der vier Längsseiten des Kopfs."""
    index = gem_offset
    for su in (1, -1):
        for sv in (1, -1):
            tube([to3d(u, v, 0.006) for u, v in scroll_points(su, sv)], 0.007, "gold", resolution=2)
            gem(to3d(su * 0.125, sv * 0.062, 0.004), normal, 0.015, GEM_CYCLE[index % len(GEM_CYCLE)])
            index += 1
    if center_gem:
        gem(to3d(0, 0, 0.004), normal, 0.05, center_gem)


# ---------------------------------------------------------------- Stiel
frustum(0.09, SHAFT_TOP, 0.028, 0.028, "silver", sides=16)
for phase in (0.0, math.pi):
    helix(0.15, 0.58, 0.031, 0.085, phase, 0.006, "gold")
    helix(0.68, SHAFT_TOP - 0.05, 0.031, 0.085, phase, 0.006, "gold")
for z in (0.125, 0.60, 0.66, SHAFT_TOP - 0.03):
    torus((0, 0, z), (0, 0, 1), 0.037, 0.012, "gold")
torus((0, 0, 0.63), (0, 0, 1), 0.040, 0.018, "silver")
for i in range(4):
    a = math.pi / 4 + i * math.pi / 2
    d = Vector((math.cos(a), math.sin(a), 0))
    gem(d * 0.056 + Vector((0, 0, 0.63)), d, 0.013, GEM_CYCLE[i], bezel=False)

# ---------------------------------------------------------------- Knauf
sphere((0, 0, 0.058), 0.056, "gold")
gem((0, 0, 0.016), (0, 0, -1), 0.036, "amethyst")
for i in range(6):
    a = i * math.pi / 3
    d = Vector((math.cos(a), math.sin(a), 0))
    gem(d * 0.055 + Vector((0, 0, 0.062)), d, 0.011, GEM_CYCLE[(i + 2) % 6], bezel=False)

# ---------------------------------------------------------------- Übergang Stiel -> Kopf
frustum(SHAFT_TOP - 0.02, HEAD_Z - HY, 0.036, 0.088, "gold")
collar_z = (SHAFT_TOP - 0.02 + HEAD_Z - HY) / 2
for i in range(6):
    a = i * math.pi / 3
    d = Vector((math.cos(a), math.sin(a), 0))
    gem(d * 0.064 + Vector((0, 0, collar_z)), d + Vector((0, 0, -0.55)), 0.013, GEM_CYCLE[i], bezel=False)

# ---------------------------------------------------------------- Hammerkopf
box((0, 0, HEAD_Z), (HX, HY, HY), "silver", bevel=0.022)
for sx in (1, -1):
    box((sx * 0.212, 0, HEAD_Z), (0.013, HY + 0.012, HY + 0.012), "gold", bevel=0.006)
    box((sx * 0.268, 0, HEAD_Z), (0.036, HY + 0.026, HY + 0.026), "silver", bevel=0.016)
    face_x = sx * 0.304
    torus((face_x, 0, HEAD_Z), (sx, 0, 0), 0.088, 0.012, "gold")
    gem((face_x + sx * 0.003, 0, HEAD_Z), (sx, 0, 0), 0.052, "topaz" if sx > 0 else "amethyst")
    for sy in (1, -1):
        for sz in (1, -1):
            gem((face_x + sx * 0.002, sy * 0.108, HEAD_Z + sz * 0.108), (sx, 0, 0), 0.02, "diamond")

    # Wellenband rund um die Schlagplatte
    edge = HY + 0.026 + 0.004
    for axis in range(4):
        wave = []
        for i in range(33):
            t = i / 32
            w = -0.125 + 0.25 * t
            dx = sx * 0.268 + 0.017 * math.sin(t * 4 * 2 * math.pi)
            wave.append([(dx, edge, HEAD_Z + w), (dx, -edge, HEAD_Z + w),
                         (dx, w, HEAD_Z + edge), (dx, w, HEAD_Z - edge)][axis])
        tube(wave, 0.006, "gold")

decorate_face(lambda u, v, h: (u, HY + h, HEAD_Z + v), (0, 1, 0), "ruby", 0)
decorate_face(lambda u, v, h: (u, -HY - h, HEAD_Z + v), (0, -1, 0), "sapphire", 2)
decorate_face(lambda u, v, h: (u, v, HEAD_Z + HY + h), (0, 0, 1), None, 4)
decorate_face(lambda u, v, h: (u, v, HEAD_Z - HY - h), (0, 0, -1), None, 1)

# ---------------------------------------------------------------- Krone oben
top = HEAD_Z + HY
frustum(top, top + 0.05, 0.062, 0.040, "gold")
torus((0, 0, top + 0.05), (0, 0, 1), 0.042, 0.011, "gold")
gem((0, 0, top + 0.068), (0, 0, 1), 0.052, "emerald")


# ================================================================ Export
def export_mesh(path):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    groups = {}
    for obj in parts:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        mesh.calc_normals_split()
        world = obj.matrix_world
        normal_matrix = world.to_3x3().inverted().transposed()
        name = obj.data.materials[0].name
        verts, lookup, indices = groups.setdefault(name, ([], {}, []))
        for tri in mesh.loop_triangles:
            corner = []
            for vertex_index, normal in zip(tri.vertices, tri.split_normals):
                p = world @ mesh.vertices[vertex_index].co
                n = (normal_matrix @ Vector(normal)).normalized()
                # Blender (x, y, z) -> Unity (x, z, y)
                key = (round(p.x, 5), round(p.z, 5), round(p.y, 5), round(n.x, 3), round(n.z, 3), round(n.y, 3))
                index = lookup.get(key)
                if index is None:
                    index = lookup[key] = len(verts)
                    verts.append(key)
                corner.append(index)
            # Der Achsentausch spiegelt, deshalb die Dreiecke umdrehen
            indices.extend((corner[0], corner[2], corner[1]))
        evaluated.to_mesh_clear()

    os.makedirs(os.path.dirname(path), exist_ok=True)
    total_v = total_t = 0
    with open(path, "wb") as f:
        f.write(b"KBH1")
        f.write(struct.pack("<i", len(groups)))
        for name in MATERIALS:
            if name not in groups:
                continue
            verts, _, indices = groups[name]
            color, metallic, gloss, emission = MATERIALS[name]
            raw = name.encode("utf-8")
            f.write(struct.pack("<i", len(raw)) + raw)
            f.write(struct.pack("<6f", *color, metallic, gloss, emission))
            f.write(struct.pack("<i", len(verts)))
            for v in verts:
                f.write(struct.pack("<6f", *v))
            f.write(struct.pack("<i", len(indices)))
            f.write(struct.pack("<%di" % len(indices), *indices))
            total_v += len(verts)
            total_t += len(indices) // 3
    print("EXPORT: %d Materialien, %d Eckpunkte, %d Dreiecke -> %s" % (len(groups), total_v, total_t, path))


export_mesh(MESH_FILE)


# ================================================================ Vorschaubilder
def look_at(obj, target, roll=0.0):
    direction = Vector(target) - obj.location
    obj.rotation_euler = (direction.to_track_quat("-Z", "Y").to_matrix().to_4x4()
                          @ Matrix.Rotation(roll, 4, "Z")).to_euler()


def setup_render():
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 160
    scene.cycles.use_denoising = False
    scene.view_settings.view_transform = "Filmic"
    scene.view_settings.look = "Medium High Contrast"

    world = bpy.data.worlds.new("world")
    world.use_nodes = True
    scene.world = world
    nodes, links = world.node_tree.nodes, world.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputWorld")
    mix = nodes.new("ShaderNodeMixShader")
    path = nodes.new("ShaderNodeLightPath")
    env = nodes.new("ShaderNodeBackground")
    sky = nodes.new("ShaderNodeTexSky")
    sky.sky_type = "PREETHAM"
    env.inputs["Strength"].default_value = 0.3
    backdrop = nodes.new("ShaderNodeBackground")
    backdrop.inputs["Color"].default_value = (0.022, 0.026, 0.022, 1)
    links.new(sky.outputs["Color"], env.inputs["Color"])
    links.new(path.outputs["Is Camera Ray"], mix.inputs["Fac"])
    links.new(env.outputs["Background"], mix.inputs[1])
    links.new(backdrop.outputs["Background"], mix.inputs[2])
    links.new(mix.outputs["Shader"], out.inputs["Surface"])

    for location, energy, size in [((2.5, -3.0, 3.2), 420, 2.5), ((-3.0, -1.5, 1.5), 160, 3.0),
                                   ((0.5, 3.0, 2.5), 250, 2.0), ((0.0, -2.5, 0.2), 90, 3.0)]:
        light = bpy.data.lights.new("light", "AREA")
        light.energy = energy
        light.size = size
        obj = bpy.data.objects.new("light", light)
        obj.location = location
        scene.collection.objects.link(obj)
        look_at(obj, (0, 0, 0.9))


def render(name, location, target, size, lens=50, ortho=None, roll=0.0, transparent=False):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = location
    scene.collection.objects.link(cam)
    look_at(cam, target, roll)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = transparent
    scene.render.image_settings.color_mode = "RGBA" if transparent else "RGB"
    scene.render.filepath = os.path.join(RENDER_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)


if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    setup_render()
    mid = (HEAD_Z + HY + 0.1) / 2
    render("hammer_ganz", (2.6, -3.7, mid + 0.55), (0, 0, mid), (900, 1500), lens=58)
    render("hammer_kopf", (0.85, -1.15, HEAD_Z + 0.36), (0, 0, HEAD_Z - 0.04), (1400, 1050), lens=50)
    render("hammer_icon", (1.2, -4.0, mid), (0, 0, mid), (512, 512), ortho=1.9,
           roll=math.radians(40), transparent=True)
