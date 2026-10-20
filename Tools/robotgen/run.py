import sys, time, importlib, numpy as np
import rg_core as G
G.TEX_DIR = "/home/claude/RustedFair/Assets/RustedFair/Resources/RF/Textures"
from rg_bake import bake, build_meshes, tri_count, export_bytes
from rg_preview import render
name = sys.argv[1]
S = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
OUT = "/tmp/claude-0/rgout"
import os; os.makedirs(OUT, exist_ok=True)
mod = importlib.import_module(name)
r = mod.build()
t = time.time()
bake(r, S=S, out_prefix=f"{OUT}/robot_{name}")
print("bake s", round(time.time() - t, 1))
for lod in (0, 1):
    m = build_meshes(r, lod, S)
    print("lod", lod, "tris", tri_count(m), "verts", sum(len(x[0]) for x in m.values()), "bones", len(m))
    export_bytes(m, f"{OUT}/robot_{name}_lod{lod}.bytes")
m0 = build_meshes(r, 0, S)
views = getattr(mod, "VIEWS", None) or [dict(pos=[1.3, 2.0, 3.6], at=[0, 1.3, 0], fov=45), dict(pos=[0.5, 2.3, 1.2], at=[0, 2.2, 0], fov=40)]
pose = getattr(mod, "PREVIEW_POSE", {})
render(r, m0, f"{OUT}/robot_{name}", f"{OUT}/prev_{name}_studio.png", views, pose=pose, eyes=r.eyes, mode="studio", size=(700, 900))
render(r, m0, f"{OUT}/robot_{name}", f"{OUT}/prev_{name}_game.png", views, pose=pose, eyes=r.eyes, mode="game", size=(700, 900))
