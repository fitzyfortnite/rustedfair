"""Rattle - the rabbit mascot ripped in half, dragging itself on long arms (spine and wires trailing behind)."""
import numpy as np
import rg_core as G
from rg_core import fbm, smoothstep, nrm
from rg_bake import Robot
import rg_parts as K
from rg_parts import METAL, CHROME, BLACK, RUBBER, INNER

SHELL = dict(color=(0.72, 0.66, 0.62), yellow=0.5, smooth=0.55, chips=0.9, rim=0.85, rim_col=(0.95, 0.93, 0.88), scale=0.45, tex_str=0.45)
SHELL_D = dict(SHELL, color=(0.62, 0.55, 0.52))
PINK = dict(color=(0.78, 0.42, 0.45), smooth=0.5, chips=0.6)
BLUE = dict(color=(0.12, 0.2, 0.42), smooth=0.5, chips=0.6)
FOAM = dict(color=(0.4, 0.34, 0.2), smooth=0.03, bump=0.0012, nscale=120)

EYE_L = np.array([-0.1, 0.12, 0.205])
EYE_R = np.array([0.1, 0.10, 0.20])


def build():
    r = Robot("rattle")
    r.bone("Torso", None, (0, 0.48, -0.2))
    r.bone("Spine", "Torso", (0, -0.05, -0.35))
    r.bone("Stump", "Spine", (0.1, -0.1, -0.3))
    r.bone("Chest", "Torso", (0, 0.05, 0.42))
    r.bone("ArmL", "Chest", (-0.27, 0.05, 0.05)); r.bone("ArmR", "Chest", (0.27, 0.05, 0.05))
    r.bone("ForeL", "ArmL", (0, -0.6, 0)); r.bone("ForeR", "ArmR", (0, -0.6, 0))
    r.bone("Neck", "Chest", (0, 0.12, 0.2))
    r.bone("Head", "Neck", (0, 0.08, 0.12), (-20, 0, 28))
    r.bone("Jaw", "Head", (0, -0.06, 0.05))

    # ------------------------------------------------------------------ torso: lying belly-down, torn open at the waist (-z)
    waist = lambda P: (P[..., 2] + 0.3) + 0.06 * (fbm(P * 13, 3, seed=3) - 0.4)

    def belly(P, N):
        return smoothstep(-0.2, -0.6, N[..., 1]) * 0.6

    tor = G.loft("torso", "Torso", [(-0.36, 0.2, 0.17, 1.0), (-0.2, 0.23, 0.2, 1.0), (0.0, 0.25, 0.21, 1.0), (0.2, 0.25, 0.21, 1.0), (0.36, 0.22, 0.19, 1.0)],
                 rot=(90, 0, 0), c=(0, 0, 0.06), res=(32, 20), close=(True, True), mat="plastic",
                 mp=dict(SHELL, zones=[(belly, "plastic", dict(SHELL, color=(0.7, 0.6, 0.55)))]), cut=waist)
    r.add(tor, G.inner_of(tor, 0.012, mat="flat", mp=FOAM))
    r.add(G.ellip("waistcav", "Torso", (0.0, 0.0, -0.18), (0.17, 0.15, 0.06), res=(12, 8), mat="flat", mp=dict(BLACK, ao=0.6)))
    # loose belly-panel crack
    for i in range(3):
        r.add(K.wire(f"tw{i}", "Torso", [(-0.08 + i * 0.07, -0.05, -0.25), (-0.1 + i * 0.08, -0.15, -0.38), (-0.12 + i * 0.09, -0.36, -0.5)], 0.007, i))

    # ------------------------------------------------------------------ spine trailing behind + stump leg
    for i in range(5):
        c = np.array([0, -0.05 * i, -0.11 * i])
        r.add(K.disc(f"vert{i}", "Spine", c, 0.05 - i * 0.004, 0.035, rot=(80, 0, 0)))
        r.add(G.sell(f"vproc{i}", "Spine", c + [0, 0.045, 0], (0.012, 0.025, 0.014), e=(0.4, 0.4), res=(6, 4), mat="metal", mp=METAL))
    r.add(G.tube("spinecore", "Spine", [(0, 0.0, 0.05), (0, -0.1, -0.22), (0, -0.2, -0.46)], 0.02, res=(8, 8), mat="metal", mp=METAL))
    for i in range(5):
        r.add(K.wire(f"sw{i}", "Spine", [(-0.06 + i * 0.03, 0.0, 0.0), (-0.1 + i * 0.05, -0.12, -0.2), (-0.14 + i * 0.07, -0.33, -0.42 - i * 0.04)],
                     0.006, i))
    r.add(K.ball("hip", "Stump", (0, 0, 0), 0.045))
    r.add(K.rod("femur", "Stump", (0, -0.02, -0.02), (0, -0.2, -0.27), 0.022, e=0.7))
    r.add(G.sell("femur_end", "Stump", (0, -0.21, -0.29), (0.03, 0.025, 0.03), e=(0.5, 0.5), res=(8, 6), mat="metal", mp=METAL))

    # ------------------------------------------------------------------ chest + blue bow tie
    ch = G.ellip("chest", "Chest", (0, 0, 0), (0.26, 0.21, 0.21), res=(28, 16), mat="plastic", mp=SHELL,
                 disp=lambda d, p: 0.012 * K.gauss(d[..., 1] - 0.7, 0.4))
    r.add(ch)
    p, n = K.surface_at(ch, 0.0, 0.1)
    bc = p + n * 0.01
    r.add(G.ellip("bow_l", "Chest", bc + [-0.045, 0, 0], (0.045, 0.03, 0.015), rot=(0, 0, 10), res=(12, 8), mat="paint", mp=BLUE,
                  disp=lambda d, p: -0.016 * K.gauss(d[..., 0] - 1.0, 0.45)))
    r.add(G.ellip("bow_r", "Chest", bc + [0.045, 0, 0], (0.045, 0.03, 0.015), rot=(0, 0, -10), res=(12, 8), mat="paint", mp=BLUE,
                  disp=lambda d, p: -0.016 * K.gauss(d[..., 0] + 1.0, 0.45)))
    r.add(G.ellip("bow_k", "Chest", bc + [0, 0, 0.005], (0.017, 0.019, 0.014), res=(8, 6), mat="paint", mp=BLUE))
    for s in (-1, 1):
        r.add(K.ball(f"shoulder{s}", "Chest", (0.25 * s, 0.05, 0.05), 0.05))

    # ------------------------------------------------------------------ long arms
    for side, ar, fo in ((-1, "ArmL", "ForeL"), (1, "ArmR", "ForeR")):
        r.add(G.tube("upper_" + ar, ar, [(0, -0.03, 0), (0, -0.3, 0.01), (0, -0.56, 0)], [0.058, 0.052, 0.046], res=(14, 10), mat="plastic",
                     mp=SHELL_D if side > 0 else SHELL,
                     rfn=lambda t, a: 1 + 0.08 * K.gauss(t - 0.0, 0.08) + 0.08 * K.gauss(t - 1.0, 0.08)))
        r.add(K.ball("elbow_" + fo, fo, (0, 0, 0), 0.04))
        r.add(K.rod("ulna_" + fo, fo, (0.012, -0.03, 0), (0.012, -0.62, 0), 0.016, e=0.7))
        r.add(K.rod("radius_" + fo, fo, (-0.016, -0.03, 0.006), (-0.016, -0.6, 0.006), 0.011))
        r.add(K.piston("fp_" + fo, fo, (0.0, -0.06, -0.03), (0.0, -0.55, -0.025), 0.013))
        r.add(K.wire("fw_" + fo, fo, [(0.03, -0.03, 0.02), (0.04, -0.3, 0.035), (0.02, -0.6, 0.02)], 0.005, 0 if side < 0 else 3))
        r.add(K.ball("wrist_" + fo, fo, (0, -0.63, 0), 0.025))
        r.add(G.sell("hand_" + fo, fo, (0, -0.67, 0.025), (0.055, 0.03, 0.07), e=(0.5, 0.6), res=(14, 8), mat="plastic", mp=SHELL_D))
        for k in range(4):
            x = -0.04 + k * 0.027
            pts = [(x, -0.68, 0.08), (x * 1.2, -0.7, 0.14), (x * 1.3, -0.74, 0.19)]
            r.add(K.finger(f"fing_{fo}{k}", fo, pts, [0.011, 0.009, 0.006], mat="metal", mp=METAL))
            tip = np.array(pts[-1]); d = nrm(tip - np.array(pts[1]))
            r.add(G.tube(f"claw_{fo}{k}", fo, [tip, tip + d * 0.035 + [0, -0.012, 0]], [0.006, 0.0006], res=(6, 3), caps=(True, False),
                         mat="shiny", mp=CHROME, texel_scale=0.6, lod1=(4, 2)))

    # ------------------------------------------------------------------ neck
    r.add(K.rod("neck", "Neck", (0, -0.05, -0.04), (0, 0.08, 0.1), 0.035))
    r.add(K.wire("nw", "Neck", [(0.04, -0.04, -0.02), (0.06, 0.04, 0.05), (0.04, 0.08, 0.1)], 0.005, 1))

    # ------------------------------------------------------------------ head
    hc = np.array([0.0, 0.05, 0.0])

    def head_disp(d, p):
        q = p + hc
        out = 0.03 * K.gauss(np.linalg.norm(q - np.array([-0.09, -0.02, 0.17]), axis=-1), 0.07)    # cheeks / muzzle
        out += 0.03 * K.gauss(np.linalg.norm(q - np.array([0.09, -0.02, 0.17]), axis=-1), 0.07)
        out += 0.035 * K.gauss(np.linalg.norm(q - np.array([0.0, 0.0, 0.22]), axis=-1), 0.06)      # snout
        for e in (EYE_L, EYE_R):  # raised sockets that seat the eyeballs
            dd = np.linalg.norm(q - e, axis=-1)
            out += 0.03 * K.gauss(dd, 0.05) - 0.012 * K.gauss(dd, 0.022)
        return out

    def cheeks(P, N):
        return 0.6 * np.maximum(smoothstep(0.05, 0.02, np.linalg.norm(P - np.array([-0.12, 0.0, 0.18]), axis=-1)),
                                smoothstep(0.05, 0.02, np.linalg.norm(P - np.array([0.12, -0.01, 0.17]), axis=-1)))

    def sockets(P, N):
        return np.maximum(smoothstep(0.042, 0.03, np.linalg.norm(P - EYE_L, axis=-1)), smoothstep(0.04, 0.028, np.linalg.norm(P - EYE_R, axis=-1)))

    crack = K.ragged_cut((0.13, 0.17, 0.12), 0.045, 0.02, 25, 21)
    head = G.ellip("head", "Head", hc, (0.21, 0.19, 0.22), res=(36, 24), disp=head_disp, mat="plastic",
                   mp=dict(SHELL, zones=[(cheeks, "paint", PINK), (sockets, "flat", dict(color=(0.02, 0.018, 0.018), smooth=0.3))]),
                   cut=K.combine_cuts(crack, lambda P: np.maximum((P[..., 1] + 0.035), 0.06 - P[..., 2])))
    head.rim_cut = crack
    r.add(head, G.inner_of(head, 0.008, mat="flat", mp=INNER))
    r.add(G.ellip("headendo", "Head", (0.07, 0.1, 0.06), (0.1, 0.09, 0.1), res=(14, 10), mat="metal", mp=METAL))
    r.add(G.ellip("muzzle", "Head", (0.0, 0.0, 0.19), (0.12, 0.075, 0.07), res=(20, 12), mat="plastic", mp=SHELL,
                  disp=lambda d, p: 0.015 * K.gauss(np.abs(p[..., 0]) - 0.055, 0.035) - 0.012 * K.gauss(p[..., 0], 0.012) * (p[..., 1] < 0)))
    r.add(G.ellip("nose", "Head", (0.0, 0.05, 0.255), (0.03, 0.022, 0.02), res=(12, 8), mat="paint", mp=PINK))
    for s in (-1, 1):
        for k in range(3):
            r.add(G.ellip(f"whisk{s}{k}", "Head", (0.06 * s, 0.01 - k * 0.014, 0.252), (0.0045, 0.0045, 0.003), res=(6, 4),
                          mat="flat", mp=BLACK))
    # buck teeth + rows
    for s in (-1, 1):
        r.add(K.tooth(f"buck{s}", "Head", (0.015 * s, -0.08, 0.235), (0.014, 0.032, 0.008), down=True, sharp=0.2))
    for i in range(8):
        x = -0.12 + i * 0.034
        if abs(x) < 0.03:
            continue
        z = hc[2] + 0.22 * np.sqrt(max(0.05, 1 - (x / 0.21) ** 2)) - 0.02
        r.add(K.tooth(f"tu{i}", "Head", (x, -0.07, z - 0.01), (0.011, 0.02, 0.008), rot=(0, -np.degrees(np.arctan2(x, 0.2)), 0), down=True))
    r.add(G.ellip("mouth", "Head", (0, -0.03, 0.12), (0.13, 0.04, 0.09), res=(12, 8), mat="flat", mp=dict(color=(0.04, 0.01, 0.01), smooth=0.5, ao=0.4)))
    # ears: one tall, one snapped and dangling on wires
    def inner_ear(P, N):
        return smoothstep(0.55, 0.8, N[..., 2]) * 0.9
    r.add(G.loft("earL", "Head", [(0.0, 0.04, 0.025, 1.0), (0.12, 0.055, 0.03, 1.0), (0.3, 0.06, 0.028, 1.0), (0.46, 0.04, 0.02, 1.0)], res=(16, 14),
                 rot=(-10, 0, 10), c=(-0.09, 0.2, -0.03), mat="plastic",
                 disp=lambda t, a, p: -0.012 * smoothstep(0.3, 0.9, np.sin(a)) * smoothstep(0.1, 0.3, t),
                 mp=dict(SHELL, zones=[(inner_ear, "paint", PINK)])))
    ear2 = G.loft("earR", "Head", [(0.0, 0.04, 0.025, 1.0), (0.14, 0.055, 0.028, 1.0), (0.28, 0.045, 0.022, 1.0)], res=(14, 10),
                  rot=(10, 0, -125), c=(0.2, 0.1, -0.04), mat="plastic", mp=dict(SHELL_D, zones=[(inner_ear, "paint", PINK)]))
    r.add(ear2)
    r.add(K.wire("earw0", "Head", [(0.11, 0.21, -0.03), (0.19, 0.18, -0.02), (0.21, 0.11, -0.04)], 0.005, 0))
    r.add(K.wire("earw1", "Head", [(0.1, 0.2, -0.05), (0.17, 0.2, -0.07), (0.2, 0.12, -0.06)], 0.004, 2))
    r.add(K.rod("earstub", "Head", (0.08, 0.18, -0.03), (0.12, 0.23, -0.03), 0.012))
    # jaw
    def jaw_cut(P):
        return np.minimum(-(P[..., 1] - 0.06 + 0.035) - 0.0, P[..., 2] + 0.05 - 0.06)
    jaw = G.ellip("jaw", "Jaw", hc - [0, -0.06, 0.05], (0.205, 0.185, 0.215), res=(28, 14), disp=head_disp,
                  mat="plastic", mp=SHELL, cut=jaw_cut)
    jaw.rim_cut = lambda P: np.full(P.shape[:-1], 1.0)
    r.add(jaw, G.inner_of(jaw, 0.008, mat="flat", mp=INNER))
    r.add(G.ellip("tongue", "Jaw", (0, -0.03, 0.1), (0.1, 0.02, 0.08), res=(10, 6), mat="flat", mp=dict(color=(0.16, 0.05, 0.05), smooth=0.5)))
    for i in range(7):
        x = -0.1 + i * 0.033
        z = 0.22 * np.sqrt(max(0.05, 1 - (x / 0.21) ** 2)) - 0.075
        r.add(K.tooth(f"tl{i}", "Jaw", (x, -0.005, z), (0.01, 0.016, 0.008), rot=(0, -np.degrees(np.arctan2(x, 0.2)), 0), down=False))

    head_R, head_t = r.world("Head")
    r.points["blood"] = [(head_t + head_R @ np.array([0.0, -0.05, 0.22]), 0.06, 0.8)]
    for fo in ("ForeL", "ForeR"):
        R, t = r.world(fo)
        r.points["blood"].append((t + R @ np.array([0, -0.73, 0.18]), 0.06, 0.9))
    r.points["blood"].append((np.array([0, 0.4, -0.55]), 0.12, 0.7))
    r.eyes = [(("Head", EYE_L), 0.0225), (("Head", EYE_R), 0.0175)]
    return r


VIEWS = [dict(pos=[1.6, 1.3, 2.4], at=[0, 0.4, -0.1], fov=45), dict(pos=[0.4, 0.95, 1.5], at=[0.05, 0.75, 0.55], fov=38)]

PREVIEW_POSE = {"ArmL": (-70, 0, -10), "ArmR": (-70, 0, 10), "ForeL": (-20, 0, 0), "ForeR": (-40, 0, 0)}
