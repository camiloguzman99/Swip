"""
Gatos de Swip en 3D: modelo + esqueleto + biblioteca de poses + escenas, renderizado a pixel art.

Se ejecuta con el módulo oficial de Blender para Python (sin interfaz):
    pip install bpy==5.0.1            # requiere Python 3.11
    python tools/blender/swip_cats.py -- <carpeta_salida> [escena ...]
Por cada escena escribe tres pasadas (color plano, luz e identificadores de pieza) que
tools/blender/toonify.py combina en pixel art con sombreado por bandas y contorno.

Ejes: el gato mira hacia +X, arriba es +Z; la cámara lo ve de lado (desde -Y).
Ángulos de las articulaciones en grados. "pitch" positivo = la pieza baja la punta (morro abajo).
"""
import math
import os
import sys

import bpy

TURN = 25   # cada gato gira hacia la cámara: vista 3/4 con la cara visible mire a donde mire

# ------------------------------------------------------------------ pelajes
COATS = {
    "orange": dict(fur=(0.98, 0.50, 0.12), stripe=(0.80, 0.30, 0.05), belly=(1.0, 0.92, 0.80),
                   pink=(1.0, 0.62, 0.66), nose=(0.95, 0.45, 0.52)),
    "gray": dict(fur=(0.45, 0.38, 0.32), stripe=(0.20, 0.16, 0.13), belly=(0.80, 0.74, 0.66),
                 pink=(0.92, 0.58, 0.60), nose=(0.80, 0.45, 0.48)),
}
EYE = (0.10, 0.07, 0.06)
WHITE = (1.0, 1.0, 1.0)
CARDBOARD = (0.80, 0.55, 0.28)
CARDBOARD_IN = (0.55, 0.36, 0.18)
TAPE = (0.86, 0.72, 0.48)

# Grupos de pieza (para las líneas interiores del pixel art): un color de identificador por grupo.
GROUPS = ["head", "body", "leg_fl", "leg_fr", "leg_bl", "leg_br", "tail", "box", "box_flap"]


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ------------------------------------------------------------------ materiales
_mats = {}


def flat(name, rgb, stripes_of=None, stripe_rgb=None):
    """Material de color plano (emisión). Con stripes_of = objeto raíz del gato, añade rayas atigradas."""
    key = (name, rgb, stripes_of.name if stripes_of else None)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(emit.outputs[0], out.inputs[0])
    if stripes_of is None:
        emit.inputs["Color"].default_value = (*rgb, 1)
    else:
        coord = nt.nodes.new("ShaderNodeTexCoord")
        coord.object = stripes_of
        wave = nt.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "X"
        wave.inputs["Scale"].default_value = 2.2
        wave.inputs["Distortion"].default_value = 4.0
        wave.inputs["Detail"].default_value = 1.0
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = (*stripe_rgb, 1)
        ramp.color_ramp.elements[1].position = 0.38
        ramp.color_ramp.elements[1].color = (*rgb, 1)
        nt.links.new(coord.outputs["Object"], wave.inputs["Vector"])
        nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], emit.inputs["Color"])
    _mats[key] = m
    return m


def override_materials():
    """Materiales que sustituyen a todos en las pasadas de luz y de identificadores."""
    light = bpy.data.materials.new("light_pass")
    light.use_nodes = True
    bsdf = light.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1)
    bsdf.inputs["Roughness"].default_value = 1.0
    bsdf.inputs["Specular IOR Level"].default_value = 0.0

    ids = bpy.data.materials.new("id_pass")
    ids.use_nodes = True
    nt = ids.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    info = nt.nodes.new("ShaderNodeObjectInfo")
    nt.links.new(info.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs[0])
    return light, ids


# ------------------------------------------------------------------ geometría
def blob(name, parent, loc, scale, mat, group, cat_id, rot=(0, 0, 0), kind="sphere", subdiv=2):
    if kind == "sphere":
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16)
    elif kind == "cone":
        bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=1, radius2=0.08, depth=2)
    elif kind == "cube":
        bpy.ops.mesh.primitive_cube_add()
    elif kind == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=24)
    o = bpy.context.active_object
    o.name = name
    o.parent = parent
    o.location = loc
    o.scale = scale
    o.rotation_euler = [math.radians(a) for a in rot]
    o.data.materials.append(mat)
    if kind in ("sphere", "cone", "cyl") and subdiv:
        bpy.ops.object.shade_smooth()
    # Color de identificador: grupo de pieza (R) + gato (G) para las líneas interiores.
    o.color = ((GROUPS.index(group) + 1) / 16, (cat_id + 1) / 8, 0.5, 1)
    return o


def joint(name, parent, loc):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    e.location = loc
    e.rotation_mode = "XYZ"
    return e


class Cat:
    """Un gato articulado. Proporciones de los dibujos de Swip: cabeza grande, cuerpo redondo, patas cortas."""

    def __init__(self, name, coat, cat_id, location=(0, 0, 0), facing=1):
        c = COATS[coat]
        self.name, self.facing = name, facing
        self.root = joint(f"{name}_root", None, location)
        self.root.rotation_euler = (0, 0, self.yaw())
        fur = flat(f"{name}_fur", c["fur"], stripes_of=self.root, stripe_rgb=c["stripe"])
        plain = flat(f"{name}_plain", c["fur"])
        belly = flat(f"{name}_belly", c["belly"])
        pink = flat(f"{name}_pink", c["pink"])
        nose = flat(f"{name}_nose", c["nose"])
        eye = flat("eye", EYE)
        white = flat("white", WHITE)
        cid = cat_id

        # Columna: pelvis (cadera) -> pecho -> cuello -> cabeza
        self.pelvis = joint(f"{name}_pelvis", self.root, (-0.28, 0, 0.46))
        blob(f"{name}_hips", self.pelvis, (0, 0, 0), (0.36, 0.32, 0.30), fur, "body", cid)
        blob(f"{name}_hipsbelly", self.pelvis, (0.02, 0, -0.10), (0.27, 0.25, 0.2), belly, "body", cid)
        self.chest = joint(f"{name}_chest", self.pelvis, (0.40, 0, 0.04))
        blob(f"{name}_chestb", self.chest, (0, 0, 0), (0.37, 0.33, 0.33), fur, "body", cid)
        blob(f"{name}_chestbelly", self.chest, (0.13, 0, -0.07), (0.25, 0.26, 0.25), belly, "body", cid)
        self.neck = joint(f"{name}_neck", self.chest, (0.24, 0, 0.18))
        blob(f"{name}_neckb", self.neck, (0.06, 0, 0.10), (0.22, 0.24, 0.24), fur, "body", cid)
        self.head = joint(f"{name}_head", self.neck, (0.14, 0, 0.22))
        h = self.head
        blob(f"{name}_skull", h, (0, 0, 0), (0.42, 0.44, 0.38), fur, "head", cid)
        blob(f"{name}_cheeks", h, (0.20, 0, -0.12), (0.24, 0.34, 0.18), belly, "head", cid)
        blob(f"{name}_muzzle", h, (0.37, 0, -0.10), (0.09, 0.15, 0.07), belly, "head", cid)
        self.nose = blob(f"{name}_nose", h, (0.43, 0, -0.05), (0.04, 0.055, 0.035), nose, "head", cid)
        self.tongue = blob(f"{name}_tongue", h, (0.42, 0, -0.18), (0.045, 0.055, 0.04), pink, "head", cid)
        self.ears = []
        for s in (-1, 1):
            ear = joint(f"{name}_ear{s}", h, (-0.02, 0.22 * s, 0.30))
            blob(f"{name}_earout{s}", ear, (0, 0, 0.09), (0.14, 0.07, 0.13), plain, "head", cid, rot=(-12 * s, 0, 0), kind="cone")
            blob(f"{name}_earin{s}", ear, (0.04, 0, 0.08), (0.09, 0.035, 0.09), pink, "head", cid, rot=(-12 * s, 0, 0), kind="cone")
            self.ears.append(ear)
        self.eyes_open, self.eyes_closed, self.shines = [], [], []
        for s in (-1, 1):
            self.eyes_open.append(blob(f"{name}_eye{s}", h, (0.34, 0.19 * s, 0.04), (0.065, 0.07, 0.09), eye, "head", cid))
            self.shines.append(blob(f"{name}_shine{s}", h, (0.395, 0.17 * s, 0.08), (0.025, 0.025, 0.03), white, "head", cid))
            self.eyes_closed.append(blob(f"{name}_lid{s}", h, (0.37, 0.19 * s, 0.01), (0.05, 0.07, 0.014), eye, "head", cid))

        # Patas: dos segmentos (muslo/brazo y pierna) + patita
        self.legs = {}
        for key, base, x, y, upper, lower in (
            ("fl", self.chest, 0.10, 0.17, 0.20, 0.18), ("fr", self.chest, 0.10, -0.17, 0.20, 0.18),
            ("bl", self.pelvis, -0.02, 0.18, 0.22, 0.18), ("br", self.pelvis, -0.02, -0.18, 0.22, 0.18),
        ):
            grp = f"leg_{key}"
            mat = fur if key[0] == "b" else plain
            top = joint(f"{name}_{key}_top", base, (x, y, -0.08))
            blob(f"{name}_{key}_u", top, (0, 0, -upper / 2), (0.10, 0.10, upper / 2 + 0.05), mat, grp, cid)
            mid = joint(f"{name}_{key}_mid", top, (0, 0, -upper))
            blob(f"{name}_{key}_l", mid, (0, 0, -lower / 2), (0.085, 0.085, lower / 2 + 0.04), mat, grp, cid)
            blob(f"{name}_{key}_paw", mid, (0.03, 0, -lower), (0.10, 0.09, 0.055), belly, grp, cid)
            self.legs[key] = (top, mid)

        # Cola: 6 segmentos encadenados
        self.tail = []
        prev = joint(f"{name}_tail0", self.pelvis, (-0.30, 0, 0.10))
        for i in range(6):
            seg = joint(f"{name}_tail{i + 1}", prev, (-0.09, 0, 0) if i else (0, 0, 0))
            blob(f"{name}_tailseg{i}", seg, (-0.05, 0, 0), (0.075 - i * 0.004, 0.075 - i * 0.004, 0.075),
                 fur, "tail", cid)
            self.tail.append(seg)
            prev = seg
        self.set_eyes(True)
        self.set_tongue(False)

    def yaw(self, extra=0.0):
        t = math.radians(TURN + extra)
        return -t if self.facing > 0 else math.pi + t

    def set_eyes(self, open_):
        for o in self.eyes_open + self.shines:
            o.hide_render = not open_
        for o in self.eyes_closed:
            o.hide_render = open_

    def set_tongue(self, out):
        self.tongue.hide_render = not out

    def pose(self, p):
        """Aplica una pose: diccionario de ángulos (grados) y opciones. Las claves que faltan valen 0."""
        r = math.radians
        g = lambda k, d=0.0: p.get(k, d)
        self.root.location.z = g("lift")
        self.root.location.x += g("shift", 0.0) * self.facing
        self.root.rotation_euler = (r(g("roll")), r(g("body_pitch")), self.yaw(g("turn")))
        self.pelvis.location.z = 0.46 - g("drop")
        self.pelvis.rotation_euler = (0, r(g("pelvis")), 0)
        self.chest.rotation_euler = (0, r(g("chest")), 0)
        self.neck.rotation_euler = (r(g("neck_roll")), r(g("neck")), r(g("neck_yaw")))
        self.head.rotation_euler = (r(g("head_roll")), r(g("head")), r(g("head_yaw")))
        for key, (top, mid) in self.legs.items():
            top.rotation_euler = (0, r(g(f"{key}_top")), 0)
            mid.rotation_euler = (0, r(g(f"{key}_mid")), 0)
        curve = p.get("tail", [-50, 10, 10, 10, 8, 6])
        for seg, a in zip(self.tail, curve):
            seg.rotation_euler = (0, r(a), 0)
        for ear in self.ears:
            ear.rotation_euler = (0, r(g("ears")), 0)
        for seg in self.tail:
            for part in seg.children:
                if part.type == "MESH":
                    part.hide_render = p.get("hide_tail", False)
        self.set_eyes(p.get("eyes", True))
        self.set_tongue(p.get("tongue", False))


class Box:
    """Caja de cartón con solapas (abierta o cerrada)."""

    def __init__(self, location=(0, 0, 0), size=(1.1, 0.95, 0.80), open_=False):
        w, d, h = size
        self.root = joint("box_root", None, location)
        out_m, in_m, tape = flat("cardboard", CARDBOARD), flat("cardboard_in", CARDBOARD_IN), flat("tape", TAPE)
        t = 0.025
        # Paredes (huecas, para que un gato quepa dentro) y fondo
        for nm, loc, sc in (
            ("front", (0, -d / 2, h / 2), (w / 2, t, h / 2)), ("back", (0, d / 2, h / 2), (w / 2, t, h / 2)),
            ("left", (-w / 2, 0, h / 2), (t, d / 2, h / 2)), ("right", (w / 2, 0, h / 2), (t, d / 2, h / 2)),
            ("bottom", (0, 0, t), (w / 2, d / 2, t)),
        ):
            blob(f"box_{nm}", self.root, loc, sc, out_m, "box", 7, kind="cube", subdiv=0)
        blob("box_inside", self.root, (0, d / 2 - 0.03, h / 2), (w / 2 - 0.03, 0.005, h / 2 - 0.02), in_m, "box", 7, kind="cube", subdiv=0)
        if open_:
            # Solapas abiertas hacia fuera, inclinadas.
            for nm, loc, rot, sc in (
                ("flap_l", (-w / 2 - 0.16, 0, h + 0.10), (0, -55, 0), (0.22, d / 2, t)),
                ("flap_r", (w / 2 + 0.16, 0, h + 0.10), (0, 55, 0), (0.22, d / 2, t)),
            ):
                blob(f"box_{nm}", self.root, loc, sc, out_m, "box_flap", 7, rot=rot, kind="cube", subdiv=0)
        else:
            blob("box_lid", self.root, (0, 0, h), (w / 2 + 0.01, d / 2 + 0.01, t), out_m, "box", 7, kind="cube", subdiv=0)
            blob("box_tape", self.root, (0, -d / 2 - 0.006, h - 0.06), (0.12, 0.004, 0.08), tape, "box", 7, kind="cube", subdiv=0)
        self.height = h


# ------------------------------------------------------------------ poses
WALK_A = dict(fl_top=28, fl_mid=-10, fr_top=-22, fr_mid=12, bl_top=-26, bl_mid=18, br_top=22, br_mid=-8,
              tail=[-70, 12, 12, 10, 8, -6])
WALK_B = dict(fl_top=-22, fl_mid=12, fr_top=28, fr_mid=-10, bl_top=22, bl_mid=-8, br_top=-26, br_mid=18,
              tail=[-66, 14, 12, 8, 4, -8])
SIT = dict(pelvis=-38, chest=-12, neck=8, head=38, drop=0.10,
           fl_top=48, fl_mid=0, fr_top=48, fr_mid=0,
           bl_top=-70, bl_mid=150, br_top=-70, br_mid=150, tail=[30, 25, 20, 15, 10, 5])
LOAF = dict(drop=0.15, pelvis=0, chest=4, neck=18, head=-12,
            fl_top=-85, fl_mid=170, fr_top=-85, fr_mid=170, bl_top=-80, bl_mid=165, br_top=-80, br_mid=165,
            tail=[20, 30, 30, 25, 15, 10], eyes=False, ears=-8)
CROUCH_PAW = dict(drop=0.12, pelvis=12, chest=8, neck=-10, head=-12,
                  fl_top=-70, fl_mid=40, fr_top=40, fr_mid=-20,
                  bl_top=-60, bl_mid=110, br_top=-60, br_mid=110, tail=[-10, 10, 5, 0, -5, -10])
BELLY_UP = dict(roll=180, lift=0.80, neck=-10, head=10,
                fl_top=-40, fl_mid=-60, fr_top=20, fr_mid=-80, bl_top=-60, bl_mid=-40, br_top=-30, br_mid=-60,
                tail=[30, 20, 10, 5, 0, 0])
RUN = dict(pelvis=4, chest=-4, neck=4, head=-6,
           fl_top=60, fl_mid=-20, fr_top=40, fr_mid=-10, bl_top=-70, bl_mid=30, br_top=-55, br_mid=20,
           tail=[-20, 0, 0, 0, 0, 0])
PUSH = dict(pelvis=10, chest=14, neck=18, head=-28, drop=0.03,
            fl_top=-10, fl_mid=10, fr_top=30, fr_mid=-10, bl_top=-40, bl_mid=25, br_top=-10, br_mid=10,
            tail=[-40, 10, 5, 0, 0, 0], eyes=False)
POUNCE = dict(body_pitch=-32, lift=0.16, neck=22, head=8,
              fl_top=-75, fl_mid=-35, fr_top=-55, fr_mid=-55, bl_top=30, bl_mid=0, br_top=42, br_mid=-10,
              tail=[-10, 5, 5, 5, 5, 5])
SNIFF = dict(WALK_A, neck=12, head=-4, tail=[-75, 15, 15, 10, 8, -10])


def mix(base, **over):
    p = dict(base)
    p.update(over)
    return p


# ------------------------------------------------------------------ escenas
CAM_YAW, CAM_PITCH = 0, 10


def scene_setup(width_units, height_units, px_per_unit, center_x, center_z):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(width_units, height_units)
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    yaw, pitch = math.radians(CAM_YAW), math.radians(CAM_PITCH)
    cam.location = (center_x + 8 * math.sin(yaw) * math.cos(pitch), -8 * math.cos(yaw) * math.cos(pitch),
                    center_z + 8 * math.sin(pitch))
    cam.rotation_euler = (math.radians(90) - pitch, 0, yaw)
    scene.camera = cam
    scene.render.resolution_x = round(width_units * px_per_unit)
    scene.render.resolution_y = round(height_units * px_per_unit)
    if width_units < height_units:
        cam_data.sensor_fit = "VERTICAL"
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(42), math.radians(-18), math.radians(-32))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(2)
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.max_bounces = 0
    scene.render.film_transparent = True
    scene.render.filter_size = 0.01      # sin suavizado: píxeles nítidos
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    return scene


def render_passes(scene, out_path):
    light, ids = override_materials()
    layer = scene.view_layers[0]
    for name, override, samples in (("albedo", None, 4), ("light", light, 24), ("ids", ids, 4)):
        layer.material_override = override
        scene.cycles.samples = samples
        scene.render.filepath = f"{out_path}_{name}.png"
        bpy.ops.render.render(write_still=True)
    layer.material_override = None


PX = 96 / 1.15  # píxeles por unidad: un gato de pie mide ~1.15 unidades -> ~96 px


def S_greet():
    a, b = Cat("naranja", "orange", 0, (-0.88, 0, 0), 1), Cat("gris", "gray", 1, (0.88, 0.1, 0), -1)
    a.pose(mix(SNIFF, neck=16, head=-2)); b.pose(mix(SNIFF, fl_top=-22, fl_mid=12, fr_top=28, fr_mid=-10))
    return dict(w=3.6, h=1.8, cx=0, cz=0.85)


def S_groom():
    a, b = Cat("naranja", "orange", 0, (-0.30, 0.25, 0), 1), Cat("gris", "gray", 1, (0.62, -0.15, 0), -1)
    a.pose(mix(SIT, eyes=False, head=30, ears=-10))
    b.pose(mix(SIT, neck=30, head=10, tongue=True, eyes=False, fl_top=40))
    return dict(w=2.8, h=1.7, cx=0.1, cz=0.8)


def S_play():
    a, b = Cat("naranja", "orange", 0, (-0.95, 0, 0), 1), Cat("gris", "gray", 1, (0.85, 0, 0), -1)
    a.pose(CROUCH_PAW); b.pose(POUNCE)
    return dict(w=3.0, h=1.5, cx=-0.05, cz=0.7)


def S_sleep_together():
    a, b = Cat("naranja", "orange", 0, (-0.90, 0.25, 0), 1), Cat("gris", "gray", 1, (0.92, -0.15, 0), -1)
    a.pose(LOAF); b.pose(mix(LOAF, head=-18, neck=12))
    return dict(w=2.8, h=1.2, cx=0.03, cz=0.55)


def S_push_box():
    Box((0.75, 0, 0), open_=False)
    a = Cat("naranja", "orange", 0, (-0.92, 0, 0), 1)
    a.pose(PUSH)
    return dict(w=3.0, h=1.5, cx=0.05, cz=0.7)


def S_peek_box():
    box = Box((0.25, 0, 0), open_=True)
    b = Cat("gris", "gray", 1, (0.25, 0.05, 0.18), -1)
    b.pose(mix(SIT, drop=0.10, head=30, fl_top=20, fl_mid=-40, fr_top=20, fr_mid=-40, hide_tail=True))
    a = Cat("naranja", "orange", 0, (-1.05, 0, 0), 1)
    a.pose(mix(SIT, neck=10, head=10))
    return dict(w=3.2, h=1.8, cx=-0.35, cz=0.9)


def S_on_box():
    box = Box((0.2, 0, 0), open_=False)
    a = Cat("naranja", "orange", 0, (0.2, 0, box.height + 0.02), 1)
    a.pose(mix(SIT, lift=box.height + 0.03, tail=[-20, -25, -20, -15, -10, -5]))
    b = Cat("gris", "gray", 1, (-1.35, -0.1, 0), 1)
    b.pose(mix(SIT, neck=-8, head=-6, chest=-8))
    return dict(w=3.0, h=2.3, cx=-0.35, cz=1.15)


def S_chase():
    a, b = Cat("naranja", "orange", 0, (-1.15, 0, 0), 1), Cat("gris", "gray", 1, (0.95, 0, 0), 1)
    a.pose(mix(RUN, lift=0.08)); b.pose(mix(RUN, lift=0.14, fl_top=40, fr_top=65, bl_top=-50, br_top=-75))
    return dict(w=3.6, h=1.4, cx=0, cz=0.68)


SCENES = {"saludo": S_greet, "acicalado": S_groom, "juego": S_play, "dormir_juntos": S_sleep_together,
          "empujar_caja": S_push_box, "dentro_caja": S_peek_box, "encima_caja": S_on_box, "persecucion": S_chase}


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = args[0] if args else "."
    names = args[1:] or list(SCENES)
    os.makedirs(out_dir, exist_ok=True)
    for name in names:
        reset()
        _mats.clear()
        view = SCENES[name]()
        scene = scene_setup(view["w"] + 1.0, view["h"] + 1.0, PX, view["cx"], view["cz"])
        render_passes(scene, os.path.join(out_dir, name))
        print("escena", name)


if __name__ == "__main__":
    main()
