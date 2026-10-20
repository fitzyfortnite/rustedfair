"""Mister Grin - towering ringmaster mascot, half of the face plate torn off showing the metal skull."""
import numpy as np
import rg_core as G
from rg_core import fbm, smoothstep, nrm
from rg_bake import Robot
import rg_parts as K
from rg_parts import METAL, CHROME, GOLD, BLACK, RUBBER, INNER

PURPLE = dict(color=(0.15, 0.055, 0.19), scale=0.35, rim_col=(0.12, 0.06, 0.12))
TROUSER = dict(color=(0.065, 0.055, 0.07), scale=0.3, rim_col=(0.06, 0.05, 0.06))
FACE = dict(color=(0.86, 0.80, 0.64), yellow=0.8, smooth=0.5, chips=0.9, rim=0.8, rim_col=(0.92, 0.9, 0.84))
RED = dict(color=(0.6, 0.06, 0.045), smooth=0.55, dirt=0.6)
GLOVE = dict(color=(0.7, 0.67, 0.6))
FELT = dict(color=(0.03, 0.028, 0.03), smooth=0.14, dust=0.25, bump=0.0006, nscale=70)
LEATHER = dict(color=(0.035, 0.03, 0.03), smooth=0.6, craze=1.0, under=(0.2, 0.17, 0.14))

EYE_L = np.array([-0.12, 0.22, 0.235])
EYE_R = np.array([0.14, 0.20, 0.215])


def smile_y(x):
    return 0.055 + 1.35 * (x + 0.03) ** 2


def build():
    r = Robot("grin")
    r.bone("Pelvis", None, (0, 1.18, 0))
    r.bone("ThighL", "Pelvis", (-0.14, -0.08, 0)); r.bone("ThighR", "Pelvis", (0.14, -0.08, 0))
    r.bone("ShinL", "ThighL", (0, -0.55, 0)); r.bone("ShinR", "ThighR", (0, -0.55, 0))
    r.bone("Spine", "Pelvis", (0, 0.12, 0))
    r.bone("Chest", "Spine", (0, 0.32, 0))
    r.bone("ArmL", "Chest", (-0.36, 0.48, 0)); r.bone("ArmR", "Chest", (0.36, 0.48, 0))
    r.bone("ForeL", "ArmL", (0, -0.52, 0)); r.bone("ForeR", "ArmR", (0, -0.52, 0))
    r.bone("Neck", "Chest", (0, 0.6, 0))
    r.bone("Head", "Neck", (0, 0.2, 0))
    r.bone("Jaw", "Head", (0, 0.02, 0.05))

    # ------------------------------------------------------------------ pelvis (trousers + belt)
    belt_zone = (lambda P, N: smoothstep(0.004, 0.0, np.abs(P[..., 1] - 0.095) - 0.025), "paint", LEATHER)
    r.add(G.loft("trousers_top", "Pelvis", [(-0.12, 0.2, 0.14, 0.8), (0.0, 0.215, 0.148, 0.8), (0.11, 0.205, 0.142, 0.8),
                                             (0.15, 0.165, 0.115, 0.8)], res=(36, 14),
                 mat="fabric", mp=dict(TROUSER, zones=[belt_zone])))
    r.add(G.sell("buckle", "Pelvis", (0, 0.095, 0.148), (0.045, 0.034, 0.012), e=(0.3, 0.3), res=(12, 8), mat="shiny", mp=GOLD))
    r.add(K.disc("hip_socket", "Pelvis", (0, 0.15, 0), 0.06, 0.03))

    # ------------------------------------------------------------------ legs
    for side, th, sh in ((-1, "ThighL", "ShinL"), (1, "ThighR", "ShinR")):
        cut = -0.40 if side < 0 else -0.33  # right trouser leg torn shorter


        wr = lambda t, a: 1 + 0.045 * np.sin(a * 3 + t * 11) * (0.3 + t) + 0.05 * (G.vnoise(np.stack([np.cos(a) * 3, np.sin(a) * 3, t * 6], -1), 5) - 0.5)
        leg = G.tube("trouser_" + th, th, [(0, 0.07, 0), (0, -0.2, 0.012), (0, -0.47, 0)], [0.1, 0.09, 0.078],
                     res=(18, 14), caps=(True, False), rfn=wr, mat="fabric", mp=TROUSER, cut=K.hem_cut(cut, 0.07, 16, 7 + side))
        r.add(leg, G.inner_of(leg, 0.006, mp=INNER, mat="flat"))
        r.add(K.rod("femur_" + th, th, (0, -0.22, 0), (0, -0.55, 0), 0.026, res=(10, 6), e=0.7))
        r.add(K.piston("thp_" + th, th, (0.045 * side, -0.25, -0.03), (0.04 * side, -0.53, -0.025), 0.016))
        # shin endoskeleton
        r.add(K.ball("knee_" + sh, sh, (0, 0, 0), 0.052))
        for s2 in (-1, 1):
            r.add(G.sell(f"kneeplate_{sh}{s2}", sh, (0.055 * s2, 0.0, 0), (0.008, 0.05, 0.045), e=(0.3, 0.4), res=(6, 4),
                         mat="metal", mp=METAL))
        r.add(K.rod("tibia_" + sh, sh, (0, -0.03, 0), (0, -0.52, 0), 0.027, e=0.65, res=(10, 6)))
        r.add(K.piston("shp_" + sh, sh, (-0.04 * side, -0.07, -0.03), (-0.035 * side, -0.47, -0.03), 0.017))
        r.add(K.wire("shw1_" + sh, sh, [(0.03 * side, -0.02, 0.03), (0.045 * side, -0.2, 0.04), (0.03 * side, -0.38, 0.035), (0.015 * side, -0.5, 0.03)], 0.006, 0))
        r.add(K.wire("shw2_" + sh, sh, [(0.02 * side, -0.02, -0.04), (0.035 * side, -0.25, -0.05), (0.02 * side, -0.5, -0.03)], 0.005, 1))
        r.add(K.ball("ankle_" + sh, sh, (0, -0.545, 0), 0.034))
        # pointed ringmaster boots
        def toe_up(p):
            z = p[..., 2]
            lift = 0.035 * smoothstep(0.08, 0.24, z) ** 2
            return p + np.stack([np.zeros_like(z), lift, np.zeros_like(z)], -1)
        r.add(G.sell("boot_" + sh, sh, (0, -0.565, 0.065), (0.068, 0.045, 0.165), e=(0.4, 0.75), res=(16, 10), warp=toe_up,
                     mat="paint", mp=LEATHER))
        r.add(G.sell("sole_" + sh, sh, (0, -0.602, 0.065), (0.072, 0.009, 0.168), e=(0.2, 0.75), res=(14, 5), warp=toe_up,
                     mat="flat", mp=RUBBER))
        r.add(G.loft("spat_" + sh, sh, [(-0.555, 0.06, 0.07), (-0.5, 0.05, 0.055), (-0.47, 0.042, 0.045)], res=(12, 5),
                     close=(False, True), mat="cloth", mp=GLOVE))

    # ------------------------------------------------------------------ spine (exposed waist)
    r.add(K.rod("spine_rod", "Spine", (0, -0.02, 0), (0, 0.34, 0), 0.03, res=(12, 6)))
    for i, y in enumerate((0.04, 0.12, 0.2, 0.28)):
        r.add(K.disc(f"vert{i}", "Spine", (0, y, -0.005), 0.056 - i * 0.003, 0.028, mp=METAL))
        r.add(G.sell(f"vproc{i}", "Spine", (0, y, -0.06), (0.012, 0.012, 0.03), e=(0.4, 0.4), res=(8, 6), mat="metal", mp=METAL))
    r.add(K.piston("spp_l", "Spine", (-0.07, 0.0, -0.03), (-0.075, 0.32, -0.035), 0.016))
    r.add(K.piston("spp_r", "Spine", (0.07, 0.0, -0.03), (0.075, 0.32, -0.035), 0.016))
    r.add(K.wire("spw0", "Spine", K.sag((-0.05, 0.0, 0.04), (-0.08, 0.34, 0.06), 0, side=(0, 0, 0.06)), 0.007, 0))
    r.add(K.wire("spw1", "Spine", K.sag((0.03, 0.0, 0.05), (0.06, 0.34, 0.05), 0, side=(0, 0, 0.08)), 0.006, 1))
    r.add(K.wire("spw2", "Spine", K.sag((0.0, 0.02, 0.05), (-0.01, 0.34, 0.07), 0, side=(0.02, 0, 0.1)), 0.005, 2))

    # ------------------------------------------------------------------ chest (ringmaster jacket)
    def shirt_w(y):
        return 0.012 + np.clip(y, 0, 0.55) * 0.13

    shirt = (lambda P, N: smoothstep(0.004, -0.004, np.abs(P[..., 0]) - shirt_w(P[..., 1])) * (P[..., 2] > 0.05) * (P[..., 1] > -0.02), "cloth", GLOVE)
    lapel = (lambda P, N: smoothstep(0.004, -0.004, np.abs(np.abs(P[..., 0]) - shirt_w(P[..., 1]) - 0.025) - 0.022)
             * (P[..., 2] > 0.05) * smoothstep(0.08, 0.16, P[..., 1]), "fabric", dict(color=(0.11, 0.045, 0.14), smooth=0.45))
    hole_c = np.array([-0.17, 0.25, 0.18])
    jk = G.loft("jacket", "Chest", [(-0.2, 0.225, 0.145, 0.8), (-0.08, 0.235, 0.15, 0.8), (0.05, 0.25, 0.16, 0.8), (0.2, 0.28, 0.175, 0.8), (0.38, 0.30, 0.18, 0.8),
                                     (0.5, 0.31, 0.165, 0.8), (0.565, 0.22, 0.13, 0.85), (0.6, 0.09, 0.08, 1.0)], res=(40, 26),
                rfn=lambda t, a: 1 + 0.012 * np.sin(a * 7 + t * 20) * smoothstep(0.0, 0.5, t),
                mat="fabric", mp=dict(PURPLE, zones=[shirt, lapel]), cut=K.ragged_cut(hole_c, 0.085, 0.03, 18, 3))
    r.add(jk, G.inner_of(jk, 0.007, mp=INNER, mat="flat"))
    r.add(G.ellip("ribcav", "Chest", (-0.17, 0.25, 0.08), (0.1, 0.12, 0.05), res=(14, 10), mat="flat", mp=BLACK))
    for i, y in enumerate((0.18, 0.25, 0.32)):
        r.add(G.tube(f"rib{i}", "Chest", [(-0.27, y + 0.02, 0.06), (-0.2, y, 0.135), (-0.1, y - 0.01, 0.13), (-0.06, y - 0.01, 0.08)], 0.011,
                     res=(8, 14), mat="metal", mp=METAL))
    r.add(K.rod("sternum", "Chest", (-0.07, 0.14, 0.11), (-0.07, 0.36, 0.115), 0.012))
    r.add(K.wire("chw0", "Chest", [(-0.13, 0.3, 0.14), (-0.16, 0.22, 0.19), (-0.15, 0.13, 0.2), (-0.17, 0.08, 0.17)], 0.006, 0))
    r.add(K.wire("chw1", "Chest", [(-0.2, 0.3, 0.12), (-0.21, 0.2, 0.17), (-0.23, 0.12, 0.18)], 0.005, 2))
    # buttons, bow tie (seated on the jacket surface)
    for i, y in enumerate((0.13, 0.25, 0.37)):
        p, n = K.surface_at(jk, 0.0, y)
        r.add(G.ellip(f"button{i}", "Chest", p + n * 0.004, (0.017, 0.017, 0.008), res=(10, 6), mat="shiny", mp=GOLD))
    p, n = K.surface_at(jk, 0.0, 0.52)
    bc = p + n * 0.012
    r.add(G.ellip("bow_l", "Chest", bc + [-0.05, 0, 0], (0.05, 0.034, 0.017), rot=(0, 0, 10), res=(14, 8), mat="paint", mp=dict(RED, chips=0.4),
                  disp=lambda d, p: -0.018 * K.gauss(d[..., 0] - 1.0, 0.45)))
    r.add(G.ellip("bow_r", "Chest", bc + [0.05, 0, 0], (0.05, 0.034, 0.017), rot=(0, 0, -10), res=(14, 8), mat="paint", mp=dict(RED, chips=0.4),
                  disp=lambda d, p: -0.018 * K.gauss(d[..., 0] + 1.0, 0.45)))
    r.add(G.ellip("bow_k", "Chest", bc + [0, 0, 0.006], (0.019, 0.021, 0.015), res=(10, 6), mat="paint", mp=dict(RED, chips=0.4)))
    # coat tails (ragged hems)
    for s in (-1, 1):
        cx = 0.12 * s

        def tail_warp(p, cx=cx, s=s):
            y = p[..., 1]
            low = smoothstep(-0.2, -0.6, y)
            rag = (fbm(np.stack([p[..., 0] * 22, y * 0 + s, p[..., 2] * 0], -1), 3, seed=12) - 0.3) * 0.09
            back = -0.04 * low  # flare backwards
            return p + np.stack([np.zeros_like(y), rag * low, back], -1)
        r.add(G.sell(f"tail{s}", "Chest", (cx, -0.3, -0.15), (0.1, 0.32, 0.012), rot=(8, 0, 4 * s), e=(0.15, 0.25),
                     res=(16, 20), warp=tail_warp, mat="fabric", mp=PURPLE))
    # epaulettes with gold fringe
    for s in (-1, 1):
        r.add(G.sell(f"epaul{s}", "Chest", (0.25 * s, 0.535, 0.0), (0.085, 0.022, 0.08), e=(0.35, 0.7), res=(16, 8), mat="shiny", mp=GOLD))
        for k in range(7):
            a = np.pi * (k / 6.0) - np.pi / 2
            x0 = 0.25 * s + s * 0.08 * np.cos(a); z0 = 0.075 * np.sin(a)
            r.add(G.tube(f"fringe{s}_{k}", "Chest", [(x0, 0.52, z0), (x0 + s * 0.008, 0.485, z0), (x0 + s * 0.012, 0.455, z0)], [0.007, 0.006, 0.004],
                         res=(6, 5), mat="shiny", mp=GOLD, texel_scale=0.6, lod1=(4, 3)))

    # ------------------------------------------------------------------ arms
    # left: full sleeve torn at the elbow
    r.add(G.ellip("shoulderL", "ArmL", (0.0, -0.01, 0), (0.105, 0.1, 0.1), res=(20, 12), mat="fabric", mp=PURPLE))
    slv = G.tube("sleeveL", "ArmL", [(0, -0.02, 0), (0, -0.26, 0.012), (0, -0.5, 0)], [0.088, 0.078, 0.07], res=(16, 14), caps=(True, False),
                 rfn=lambda t, a: 1 + 0.05 * np.sin(a * 4 + t * 14) * t, mat="fabric", mp=PURPLE, cut=K.hem_cut(-0.43, 0.06, 15, 21))
    r.add(slv, G.inner_of(slv, 0.006, mp=INNER, mat="flat"))
    r.add(K.rod("humerusL", "ArmL", (0, -0.2, 0), (0, -0.52, 0), 0.024))
    # right: sleeve ripped off, bare endoskeleton
    cap = G.ellip("shoulderR", "ArmR", (0.0, -0.0, 0), (0.105, 0.1, 0.1), res=(18, 12), mat="fabric", mp=PURPLE,
                  cut=K.hem_cut(-0.03, 0.05, 18, 23))
    r.add(cap, G.inner_of(cap, 0.006, mp=INNER, mat="flat"))
    r.add(K.ball("shballR", "ArmR", (0, -0.03, 0), 0.05))
    r.add(K.rod("humerusR", "ArmR", (0, -0.04, 0), (0, -0.5, 0), 0.026, e=0.7))
    r.add(K.piston("arp", "ArmR", (0.035, -0.06, 0.03), (0.032, -0.47, 0.025), 0.015))
    r.add(K.wire("arw0", "ArmR", [(-0.04, -0.06, 0.02), (-0.05, -0.2, 0.05), (-0.03, -0.35, 0.04), (-0.02, -0.48, 0.02)], 0.006, 0))
    r.add(K.wire("arw1", "ArmR", [(0.04, -0.05, -0.04), (0.06, -0.22, -0.05), (0.05, -0.3, -0.07), (0.06, -0.36, -0.05)], 0.005, 2))
    r.add(G.sell("rag", "ArmR", (0.02, -0.12, 0.07), (0.035, 0.07, 0.005), rot=(-15, 0, 10), e=(0.2, 0.3), res=(8, 10),
                 warp=lambda p: p + np.stack([0 * p[..., 0], (fbm(p * 30, 2, seed=5) - 0.5) * 0.05 * (p[..., 1] < -0.1), 0 * p[..., 0]], -1),
                 mat="fabric", mp=PURPLE))
    # forearms + white gloves
    for side, fo in ((-1, "ForeL"), (1, "ForeR")):
        r.add(K.ball("elbow_" + fo, fo, (0, 0, 0), 0.043))
        for s2 in (-1, 1):
            r.add(K.rod(f"ulna_{fo}{s2}", fo, (0.02 * s2, -0.03, 0), (0.018 * s2, -0.47, 0), 0.0145, res=(8, 5)))
        r.add(K.wire("fw_" + fo, fo, [(0.0, -0.03, 0.04), (0.01, -0.2, 0.035), (-0.005, -0.4, 0.03), (0, -0.5, 0.02)], 0.005, 1))
        r.add(K.ball("wrist_" + fo, fo, (0, -0.5, 0), 0.03))
        r.add(G.lathe("cuff_" + fo, fo, [(0.044, -0.47), (0.05, -0.5), (0.064, -0.545), (0.058, -0.55), (0.045, -0.51), (0.04, -0.47)],
                      res=(14, 5), mat="cloth", mp=GLOVE, sdf=None))
        r.add(G.sell("palm_" + fo, fo, (0, -0.6, 0.004), (0.05, 0.066, 0.026), e=(0.5, 0.55), res=(16, 10), mat="cloth", mp=GLOVE))
        sx = -side  # thumb towards the body
        for k in range(4):
            x = -0.036 + k * 0.024
            L = 1.0 - abs(k - 1.4) * 0.07
            pts = [(x, -0.655, 0.004), (x * 1.08, -0.655 - 0.065 * L, 0.018), (x * 1.12, -0.655 - 0.12 * L, 0.048)]
            r.add(K.finger(f"fing_{fo}{k}", fo, pts, [0.0135, 0.012, 0.0098], mp=GLOVE))
            if k in (1, 2):
                tip = np.array(pts[-1]); d = nrm(tip - np.array(pts[1]))
                r.add(G.tube(f"claw_{fo}{k}", fo, [tip, tip + d * 0.03], [0.006, 0.0008], res=(6, 4), caps=(True, False),
                             mat="shiny", mp=CHROME, texel_scale=0.6, lod1=(4, 2)))
        r.add(K.finger(f"thumb_{fo}", fo, [(sx * 0.035, -0.6, 0.02), (sx * 0.045, -0.64, 0.05), (sx * 0.04, -0.685, 0.07)],
                       [0.015, 0.013, 0.0105], mp=GLOVE))

    # ------------------------------------------------------------------ neck
    r.add(K.rod("neck_rod", "Neck", (0, -0.05, 0), (0, 0.19, 0), 0.036, res=(12, 6)))
    r.add(K.disc("neck_plate", "Neck", (0, 0.17, 0.0), 0.075, 0.022))
    r.add(K.piston("nkp_l", "Neck", (-0.05, -0.02, 0.025), (-0.045, 0.165, 0.03), 0.012))
    r.add(K.piston("nkp_r", "Neck", (0.05, -0.02, 0.025), (0.045, 0.165, 0.03), 0.012))
    r.add(K.wire("nkw0", "Neck", K.sag((-0.02, -0.04, -0.045), (-0.04, 0.17, -0.05), 0, side=(0, 0, -0.03)), 0.006, 0))
    r.add(K.wire("nkw1", "Neck", K.sag((0.03, -0.04, -0.04), (0.02, 0.17, -0.06), 0, side=(0.02, 0, -0.03)), 0.005, 3))

    # ------------------------------------------------------------------ head
    fc = np.array([-0.03, 0.16, 0.0])
    fr = np.array([0.245, 0.28, 0.24])
    J0 = np.array([0.0, 0.02, 0.05])        # jaw pivot in head space
    tear_c = np.array([0.155, 0.14, 0.17])

    def face_disp_at(off):
        def face_disp(d, p):
            q = p + off
            out = 0.034 * K.gauss(np.linalg.norm(q - np.array([-0.16, 0.07, 0.17]), axis=-1), 0.075)     # puffy cheeks
            out += 0.034 * K.gauss(np.linalg.norm(q - np.array([0.1, 0.07, 0.17]), axis=-1), 0.075)
            out += 0.018 * K.gauss(np.linalg.norm(q - np.array([-0.03, 0.3, 0.17]), axis=-1), 0.09)     # forehead
            out -= 0.02 * K.gauss(np.linalg.norm(q - np.array([-0.03, 0.2, 0.24]), axis=-1), 0.05)      # bridge
            dd = np.linalg.norm(q - EYE_L, axis=-1)
            out += 0.03 * K.gauss(dd, 0.055) - 0.014 * K.gauss(dd, 0.026)                               # raised socket
            out += 0.014 * K.gauss(np.linalg.norm(q - (EYE_L + [0.0, 0.055, -0.01]), axis=-1), 0.04)    # brow
            out += 0.012 * K.gauss(q[..., 1] + 0.04, 0.05) * smoothstep(0.0, 0.2, q[..., 2])             # chin
            out -= 0.012 * smoothstep(0.0, -0.2, q[..., 2]) * smoothstep(0.2, 0.3, q[..., 1])            # flatter back
            return out
        return face_disp

    def lip(P, N):
        d = P[..., 1] - smile_y(P[..., 0])
        return smoothstep(0.03, 0.022, d) * smoothstep(-0.016, -0.008, d) * smoothstep(0.1, 0.16, P[..., 2]) \
            * smoothstep(0.25, 0.21, np.abs(P[..., 0] + 0.03))

    def brow(P, N):
        x, y = P[..., 0] - EYE_L[0], P[..., 1] - EYE_L[1] - 0.06 - 0.25 * (P[..., 0] - EYE_L[0])
        return smoothstep(0.012, 0.006, np.abs(y)) * smoothstep(0.065, 0.05, np.abs(x)) * (P[..., 2] > 0.1)

    def blush(P, N):
        return 0.5 * smoothstep(0.06, 0.02, np.linalg.norm(P - np.array([-0.17, 0.1, 0.2]), axis=-1))

    def socket(P, N):
        return smoothstep(0.05, 0.035, np.linalg.norm(P - EYE_L, axis=-1))

    def mouth_cut(P):  # face shell gives way to the jaw below the grin line (front only)
        return np.maximum(P[..., 1] - (smile_y(P[..., 0]) - 0.012), 0.03 - P[..., 2])
    tear_cut = K.ragged_cut(tear_c, 0.165, 0.045, 13, 9)

    face = G.ellip("face", "Head", fc, fr, res=(44, 28), disp=face_disp_at(fc), mat="plastic",
                   mp=dict(FACE, zones=[(lip, "paint", RED), (brow, "flat", BLACK), (blush, "paint", dict(color=(0.6, 0.18, 0.15), chips=0.4)),
                                        (socket, "flat", dict(color=(0.02, 0.018, 0.017), smooth=0.3))]),
                   cut=K.combine_cuts(tear_cut, mouth_cut))
    face.rim_cut = tear_cut
    r.add(face, G.inner_of(face, 0.009, mp=INNER, mat="flat"))
    # metal skull under the torn half
    def skull_zone(P, N):
        return smoothstep(0.004, 0.0, np.abs(P[..., 1] - 0.24 - 0.03 * np.sin(P[..., 0] * 30)) - 0.002)
    sc = np.array([0.06, 0.145, 0.03])
    r.add(G.ellip("skull", "Head", sc, (0.165, 0.19, 0.18), res=(26, 18), mat="metal",
                  mp=dict(METAL, color=(0.58, 0.57, 0.55), rust=0.45, zones=[(skull_zone, "flat", BLACK)]),
                  disp=lambda d, p: -0.018 * K.gauss(np.linalg.norm(p + sc - EYE_R, axis=-1), 0.04)
                  + 0.012 * K.gauss(np.linalg.norm(p + sc - (EYE_R + [0.02, -0.07, -0.01]), axis=-1), 0.05)))
    r.add(G.torus("socketR", "Head", EYE_R + [0, 0, -0.008], 0.033, 0.0075, rot=(90, 0, 0), res=(16, 8), mat="metal", mp=METAL))
    r.add(K.disc("cheek_servo", "Head", (0.185, 0.07, 0.11), 0.03, 0.03, rot=(0, 0, 90)))
    r.add(K.rod("cheek_bar", "Head", (0.17, 0.07, 0.12), (0.12, 0.04, 0.2), 0.008))
    r.add(K.wire("hw0", "Head", [(0.2, 0.12, 0.08), (0.22, 0.05, 0.1), (0.19, -0.01, 0.12)], 0.005, 0))
    r.add(K.wire("hw1", "Head", [(0.13, 0.26, 0.15), (0.17, 0.22, 0.17), (0.19, 0.15, 0.15)], 0.004, 2))
    # eyelid over the mascot eye (half closed, droopy)
    lid = G.ellip("lidL", "Head", EYE_L + [0, 0.002, -0.004], (0.037, 0.037, 0.037), res=(16, 10), mat="plastic", mp=FACE,
                  cut=lambda P: (P[..., 1] - EYE_L[1]) - (0.004 - 0.15 * (P[..., 0] - EYE_L[0])))
    r.add(lid, G.inner_of(lid, 0.003, mp=INNER, mat="flat"))
    # nose + mustache (left half intact, right half snapped)
    r.add(G.ellip("nose", "Head", (-0.035, 0.135, 0.262), (0.032, 0.03, 0.03), res=(14, 10), mat="paint", mp=dict(RED, chips=0.5)))
    MUST = dict(color=(0.04, 0.035, 0.03), smooth=0.4, craze=0.3, chips=0.3)
    r.add(G.tube("mustL", "Head", [(-0.04, 0.105, 0.258), (-0.1, 0.098, 0.24), (-0.16, 0.11, 0.2), (-0.185, 0.145, 0.18), (-0.165, 0.165, 0.178)],
                 [0.012, 0.014, 0.01, 0.006, 0.003], res=(10, 16), mat="paint", mp=MUST))
    r.add(G.tube("mustR", "Head", [(-0.02, 0.105, 0.258), (0.02, 0.1, 0.25), (0.045, 0.08, 0.245)], [0.012, 0.012, 0.007],
                 res=(10, 8), mat="paint", mp=MUST))
    # mouth cavity + upper teeth along the grin
    r.add(G.ellip("mouth", "Head", (-0.03, 0.04, 0.1), (0.18, 0.07, 0.12), res=(16, 10), mat="flat",
                  mp=dict(color=(0.035, 0.012, 0.01), smooth=0.5, ao=0.4)))
    for i in range(13):
        x = -0.215 + i * 0.031
        y = smile_y(x) - 0.031
        z = fc[2] + fr[2] * np.sqrt(max(0.05, 1 - ((x - fc[0]) / fr[0]) ** 2 - ((y - fc[1]) / fr[1]) ** 2)) - 0.009
        yaw = -np.degrees(np.arctan2(x - fc[0], 0.3))
        r.add(K.tooth(f"tu{i}", "Head", (x, y, z), (0.0135, 0.023, 0.009), rot=(0, yaw, -np.degrees(np.arctan(2.7 * (x + 0.03)))), down=True))
    # top hat (tilted), dented crown with a torn hole and red band
    hat_c, hat_rot = np.array([-0.04, 0.42, -0.02]), (-8, 0, 14)
    Rh = G.rot_euler(*hat_rot)
    band = (lambda P, N: smoothstep(0.004, 0.0, np.abs(((P - hat_c) @ Rh)[..., 1] - 0.065) - 0.03), "paint", dict(RED, craze=0.5, chips=0.4))

    def brim_warp(p):
        q = (p - hat_c) @ Rh
        rr = np.hypot(q[..., 0], q[..., 2])
        q[..., 1] += 0.035 * (q[..., 0] / 0.24) ** 2 * smoothstep(0.15, 0.24, rr)
        return q @ Rh.T + hat_c

    def dent(v, a):
        return 1 - 0.07 * K.gauss(a - 2.2, 0.5) * smoothstep(0.4, 0.8, v) - 0.03 * np.sin(a * 2) * smoothstep(0.5, 1, v)

    hat = G.lathe("hat", "Head", [(0, 0), (0.2, 0.0), (0.236, 0.004), (0.242, 0.014), (0.228, 0.022), (0.162, 0.026), (0.153, 0.036),
                                  (0.148, 0.2), (0.156, 0.33), (0.159, 0.347), (0.15, 0.357), (0, 0.36)],
                  c=hat_c, rot=hat_rot, res=(40, 22), rfn=dent, warp=brim_warp, mat="flat", mp=dict(FELT, zones=[band], rim_col=(0.1, 0.09, 0.09)),
                  cut=K.ragged_cut(hat_c + Rh @ np.array([0.1, 0.25, 0.12]), 0.04, 0.02, 30, 4))
    r.add(hat, G.inner_of(hat, 0.006, mp=INNER, mat="flat"))

    # ------------------------------------------------------------------ jaw: lower part of the same face shell
    jc = fc - J0

    def lowlip(P, N):
        d = (smile_y(P[..., 0]) - 0.05) - (P[..., 1] + J0[1])
        return smoothstep(0.0, 0.006, d) * smoothstep(0.03, 0.022, d) * smoothstep(0.12, 0.17, P[..., 2] + J0[2])

    def jaw_cut(P):
        y, z = P[..., 1] + J0[1], P[..., 2] + J0[2]
        return np.minimum((smile_y(P[..., 0]) - 0.05) - y, z - 0.015)

    jaw = G.ellip("jaw", "Jaw", jc, fr - 0.007, res=(36, 20), disp=face_disp_at(fc), mat="plastic",
                  mp=dict(FACE, zones=[(lowlip, "paint", RED)]),
                  cut=K.combine_cuts(jaw_cut, K.ragged_cut(tear_c - J0 + [0.0, -0.08, 0.0], 0.09, 0.03, 18, 11)))
    jaw.rim_cut = K.ragged_cut(tear_c - J0 + [0.0, -0.08, 0.0], 0.09, 0.03, 18, 11)
    r.add(jaw, G.inner_of(jaw, 0.008, mp=INNER, mat="flat"))
    r.add(G.sell("jaw_metal", "Jaw", (0.12, -0.05, 0.08), (0.05, 0.022, 0.09), rot=(0, -20, 0), e=(0.3, 0.5), res=(12, 8), mat="metal", mp=METAL))
    r.add(G.ellip("tongue", "Jaw", (-0.03, -0.025, 0.06), (0.13, 0.025, 0.1), res=(14, 8), mat="flat", mp=dict(color=(0.14, 0.035, 0.035), smooth=0.5)))
    for i in range(12):
        x = -0.2 + i * 0.031
        yh = smile_y(x) - 0.045
        z = fc[2] + fr[2] * np.sqrt(max(0.05, 1 - ((x - fc[0]) / fr[0]) ** 2 - ((yh - fc[1]) / fr[1]) ** 2)) - 0.02
        yaw = -np.degrees(np.arctan2(x - fc[0], 0.3))
        r.add(K.tooth(f"tl{i}", "Jaw", (x, yh - J0[1], z - J0[2]), (0.012, 0.017, 0.008),
                      rot=(0, yaw, -np.degrees(np.arctan(2.7 * (x + 0.03)))), down=False))

    # effects
    head_R, head_t = r.world("Head")
    jaw_R, jaw_t = r.world("Jaw")
    r.points["blood"] = [
        (head_t + head_R @ np.array([0.02, 0.03, 0.24]), 0.06, 0.6),   # grin
        (jaw_t + jaw_R @ np.array([0.0, -0.02, 0.2]), 0.05, 0.7),
    ]
    for fo in ("ForeL", "ForeR"):
        R, t = r.world(fo)
        r.points["blood"].append((t + R @ np.array([0, -0.77, 0.045]), 0.045, 0.8))
    r.eyes = [(("Head", EYE_L), 0.03), (("Head", EYE_R), 0.025)]
    return r


VIEWS = [dict(pos=[1.7, 1.75, 5.0], at=[0, 1.6, 0], fov=42), dict(pos=[0.75, 2.62, 1.35], at=[0.0, 2.58, 0], fov=36)]
