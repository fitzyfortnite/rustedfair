"""Brutus - hulking rotten bear: matted fur, ripped belly with ribs and spilling wires, chains, one empty eye socket."""
import numpy as np
import rg_core as G
from rg_core import fbm, smoothstep, nrm
from rg_bake import Robot
import rg_parts as K
from rg_parts import METAL, CHROME, BLACK, RUBBER, INNER

FUR = dict(color=(0.36, 0.24, 0.15), scale=0.3, rim_col=(0.55, 0.47, 0.3), rim=0.7)
FUR_D = dict(color=(0.22, 0.15, 0.1), scale=0.3, rim_col=(0.5, 0.42, 0.28), rim=0.7)
BELLY = dict(color=(0.55, 0.42, 0.28), scale=0.25)
MUZZLE = dict(color=(0.62, 0.5, 0.36), yellow=0.5, smooth=0.45, chips=0.7, rim=0.8, rim_col=(0.8, 0.74, 0.6))
FOAM = dict(color=(0.42, 0.34, 0.16), smooth=0.03, bump=0.0012, nscale=120)
NOSE = dict(color=(0.02, 0.018, 0.018), smooth=0.75)
PAD = dict(color=(0.06, 0.045, 0.04), smooth=0.35, bump=0.0006)
BONE = dict(color=(0.78, 0.72, 0.58))

EYE_L = np.array([-0.15, 0.28, 0.30])
EYE_R = np.array([0.16, 0.27, 0.25])


def lumps(amp, scale, seed):
    def disp(d, p):
        return amp * (fbm(p * scale, 3, seed=seed) - 0.5) * 2
    return disp


def tube_lumps(amp, scale, seed):
    def disp(t, a, p):
        return amp * (fbm(p * scale, 3, seed=seed) - 0.5) * 2
    return disp


def stitches(P, c, dirv, length, width=0.006, pitch=0.018):
    """Zone mask: a row of cross stitches along a straight seam on a surface."""
    c, dirv = np.asarray(c, float), nrm(np.asarray(dirv, float))
    q = P - c
    s = q @ dirv
    perp = np.linalg.norm(q - s[..., None] * dirv, axis=-1)
    seam = smoothstep(0.003, 0.0015, perp) * (np.abs(s) < length)
    m = np.mod(s, pitch) / pitch
    cross = smoothstep(0.25, 0.1, np.abs(m - 0.5)) * smoothstep(width * 1.6, width * 0.5, perp) * (np.abs(s) < length)
    return np.clip(seam * 0.8 + cross, 0, 1)


def build():
    r = Robot("brutus")
    r.bone("Pelvis", None, (0, 1.15, 0))
    r.bone("ThighL", "Pelvis", (-0.26, -0.12, 0)); r.bone("ThighR", "Pelvis", (0.26, -0.12, 0))
    r.bone("ShinL", "ThighL", (0, -0.52, 0)); r.bone("ShinR", "ThighR", (0, -0.52, 0))
    r.bone("Spine", "Pelvis", (0, 0.2, 0))
    r.bone("Chest", "Spine", (0, 0.38, 0))
    r.bone("ArmL", "Chest", (-0.58, 0.45, 0)); r.bone("ArmR", "Chest", (0.58, 0.45, 0))
    r.bone("ForeL", "ArmL", (0, -0.62, 0)); r.bone("ForeR", "ArmR", (0, -0.62, 0))
    r.bone("Neck", "Chest", (0, 0.62, 0.05))
    r.bone("Head", "Neck", (0, 0.22, 0.05))
    r.bone("Jaw", "Head", (0, -0.04, 0.12))

    # ------------------------------------------------------------------ pelvis + spine
    hip_cut = K.ragged_cut((0.36, 0.0, 0.12), 0.1, 0.035, 14, 31)
    pel = G.ellip("pelvis", "Pelvis", (0, 0, 0), (0.43, 0.27, 0.31), res=(32, 16), disp=lumps(0.018, 7, 1), mat="fur", mp=FUR, cut=hip_cut)
    r.add(pel, G.inner_of(pel, 0.02, mat="flat", mp=FOAM))
    r.add(K.ball("hipball", "Pelvis", (0.26, -0.1, 0.0), 0.08))
    r.add(K.rod("hipbar", "Pelvis", (-0.26, -0.08, 0), (0.26, -0.08, 0), 0.04))
    sp = G.loft("waist", "Spine", [(-0.12, 0.4, 0.29, 1.0), (0.1, 0.44, 0.32, 1.0), (0.3, 0.5, 0.36, 1.0)], res=(30, 8),
                close=(False, False), disp=tube_lumps(0.015, 6, 2), mat="fur", mp=FUR_D)
    r.add(sp)

    # ------------------------------------------------------------------ legs
    for side, th, sh in ((-1, "ThighL", "ShinL"), (1, "ThighR", "ShinR")):
        thigh = G.tube("thigh_" + th, th, [(0, 0.06, 0), (0, -0.25, 0.01), (0, -0.5, 0)], [0.19, 0.18, 0.155], res=(22, 14),
                       disp=tube_lumps(0.015, 8, 3 + side), mat="fur", mp=FUR)
        r.add(thigh)
        torn = side > 0
        shin_cut = K.ragged_cut((0.12, -0.25, 0.1), 0.1, 0.035, 15, 41) if torn else None
        shin = G.tube("shin_" + sh, sh, [(0, 0.04, 0), (0, -0.22, 0.01), (0, -0.47, 0.0)], [0.155, 0.14, 0.125], res=(20, 12),
                      disp=tube_lumps(0.013, 8, 5 + side), mat="fur", mp=FUR_D if torn else FUR, cut=shin_cut)
        r.add(shin)
        if torn:
            r.add(G.inner_of(shin, 0.018, mat="flat", mp=FOAM))
            r.add(K.rod("tibia_" + sh, sh, (0.05, -0.02, 0.04), (0.06, -0.46, 0.05), 0.03, e=0.7))
            r.add(K.piston("shp_" + sh, sh, (0.1, -0.08, 0.03), (0.09, -0.42, 0.06), 0.018))
            r.add(K.wire("shw_" + sh, sh, [(0.06, -0.15, 0.11), (0.13, -0.24, 0.17), (0.14, -0.33, 0.15), (0.16, -0.4, 0.17)], 0.007, 0))
        # paw foot with pads and claws
        def pad(P, N):
            return smoothstep(-0.2, -0.6, N[..., 1]) * smoothstep(0.0, 0.08, P[..., 2])
        r.add(G.sell("foot_" + sh, sh, (0, -0.545, 0.09), (0.17, 0.085, 0.24), e=(0.6, 0.8), res=(22, 12), disp=lumps(0.008, 10, 7 + side),
                     mat="fur", mp=dict(FUR_D, zones=[(pad, "flat", PAD)])))
        for k in range(4):
            x = -0.105 + k * 0.07
            base = np.array([x, -0.565, 0.3 - abs(k - 1.5) * 0.02])
            r.add(G.tube(f"toeclaw_{sh}{k}", sh, [base, base + [x * 0.15, -0.02, 0.06], base + [x * 0.2, -0.06, 0.09]], [0.022, 0.014, 0.002],
                         res=(7, 6), caps=(True, False), mat="teeth", mp=BONE, texel_scale=1.2, lod1=(5, 3)))

    # ------------------------------------------------------------------ chest: barrel torso, ripped belly, chains
    belly_c = np.array([0.13, -0.06, 0.44])

    def belly_zone(P, N):
        return smoothstep(0.03, -0.03, np.linalg.norm((P - np.array([0, 0.0, 0.3])) / np.array([0.34, 0.38, 0.4]), axis=-1) - 1.0) * (P[..., 2] > 0.05)

    def chest_stitch(P, N):
        return np.maximum(stitches(P, (-0.32, 0.3, 0.32), (0.4, -0.3, 0.1), 0.12), stitches(P, (0.3, 0.35, 0.3), (0.1, 1, 0.2), 0.1))

    hole = K.ragged_cut(belly_c, 0.15, 0.05, 11, 51)
    torso = G.loft("torso", "Chest", [(-0.28, 0.42, 0.33, 1.0), (-0.12, 0.52, 0.42, 1.0), (0.1, 0.58, 0.44, 1.0), (0.34, 0.57, 0.41, 1.0),
                                       (0.52, 0.5, 0.36, 1.0), (0.64, 0.3, 0.25, 1.0), (0.7, 0.14, 0.14, 1.0)], res=(44, 26),
                   disp=tube_lumps(0.02, 6, 9), close=(False, True), mat="fur",
                   mp=dict(FUR, zones=[(belly_zone, "fur", BELLY), (chest_stitch, "flat", dict(color=(0.06, 0.04, 0.03), smooth=0.2))]),
                   cut=hole)
    r.add(torso, G.inner_of(torso, 0.03, mat="flat", mp=FOAM))
    r.add(G.ellip("bellycav", "Chest", belly_c + [0, 0, -0.22], (0.16, 0.18, 0.08), res=(14, 10), mat="flat", mp=dict(BLACK, ao=0.5)))
    for i in range(5):
        y = -0.2 + i * 0.075
        r.add(G.tube(f"rib{i}", "Chest", [(0.0, y + 0.02, 0.27), (0.08, y, 0.36), (0.2, y - 0.01, 0.37), (0.28, y + 0.02, 0.3)], 0.014,
                     res=(7, 10), mat="metal", mp=METAL))
    r.add(K.rod("spine_in", "Chest", (0.12, -0.3, 0.2), (0.12, 0.2, 0.22), 0.03))
    # wires spilling out of the belly
    for k, (a, b, drop) in enumerate([((0.05, -0.05, 0.36), (0.02, -0.5, 0.48), 0.0), ((0.15, -0.1, 0.38), (0.16, -0.56, 0.5), 0.0),
                                      ((0.22, -0.02, 0.36), (0.3, -0.42, 0.5), 0.0), ((0.1, 0.05, 0.35), (0.12, -0.3, 0.54), 0.0)]):
        mid = (np.array(a) + np.array(b)) / 2 + [0.02 * (k - 1.5), 0.05, 0.08]
        r.add(K.wire(f"bw{k}", "Chest", [a, (a[0], a[1] - 0.05, a[2] + 0.06), mid, b], 0.008 - k * 0.0008, k))
    # chains draped across the chest with a padlock
    links = []
    for i in range(13):
        t = i / 12
        x = -0.48 + 0.96 * t
        y = 0.5 - np.sin(t * np.pi) * 0.42
        z = 0.43 * np.sqrt(max(0.0, 1 - (x / 0.6) ** 2)) + 0.07
        links.append((x, y, z))
    for i, p in enumerate(links):
        q = links[min(i + 1, len(links) - 1)]; o = links[max(i - 1, 0)]
        d = nrm(np.array(q) - np.array(o))
        n = nrm(np.array(p) - np.array([0, 0.15, 0]))
        side = nrm(np.cross(n, d))
        bvec = n if i % 2 else side            # alternate standing / lying links
        ax = nrm(np.cross(d, bvec))
        Rm = np.stack([d, ax, bvec], 1)
        r.add(G.torus(f"link{i}", "Chest", np.array(p) + n * 0.012, 0.03, 0.009, Rm=Rm, stretch=1.45, res=(12, 6), mat="metal",
                      mp=dict(METAL, rust=0.85), lod1=(8, 4)))
    lk = np.array(links[6]) + [0, -0.07, 0.03]
    r.add(G.sell("padlock", "Chest", lk, (0.045, 0.04, 0.018), e=(0.4, 0.4), res=(12, 8), mat="metal", mp=dict(METAL, color=(0.42, 0.36, 0.22))))
    r.add(G.torus("shackle", "Chest", lk + [0, 0.045, 0], 0.028, 0.007, rot=(90, 0, 0), res=(12, 6), mat="shiny", mp=CHROME))
    # shoulders
    for s in (-1, 1):
        r.add(G.ellip(f"shoulder{s}", "Chest", (0.52 * s, 0.42, 0), (0.2, 0.19, 0.2), res=(18, 12), disp=lumps(0.012, 9, 11 + s), mat="fur", mp=FUR))

    # ------------------------------------------------------------------ arms with long bone claws
    for side, ar, fo in ((-1, "ArmL", "ForeL"), (1, "ArmR", "ForeR")):
        r.add(G.tube("upper_" + ar, ar, [(0, 0.03, 0), (0, -0.3, 0.02), (0, -0.6, 0)], [0.175, 0.165, 0.145], res=(20, 12),
                     disp=tube_lumps(0.014, 8, 13 + side), mat="fur", mp=FUR))
        torn = side < 0
        fcut = K.ragged_cut((-0.1, -0.3, 0.06), 0.09, 0.03, 15, 61) if torn else None
        fore = G.tube("fore_" + fo, fo, [(0, 0.02, 0), (0, -0.28, 0.02), (0, -0.52, 0.02)], [0.145, 0.135, 0.12], res=(20, 12),
                      disp=tube_lumps(0.012, 8, 15 + side), mat="fur", mp=FUR_D if torn else FUR, cut=fcut)
        r.add(fore)
        if torn:
            r.add(G.inner_of(fore, 0.016, mat="flat", mp=FOAM))
            r.add(K.rod("radius_" + fo, fo, (-0.03, -0.05, 0.02), (-0.04, -0.5, 0.03), 0.025, e=0.7))
            r.add(K.wire("fw_" + fo, fo, [(-0.07, -0.22, 0.08), (-0.14, -0.3, 0.11), (-0.12, -0.4, 0.1)], 0.006, 2))
        r.add(G.ellip("paw_" + fo, fo, (0, -0.62, 0.03), (0.15, 0.125, 0.125), res=(18, 12), disp=lumps(0.01, 10, 17 + side), mat="fur",
                      mp=dict(FUR_D, zones=[(lambda P, N: smoothstep(0.2, 0.7, N[..., 2]) * smoothstep(-0.6, -0.68, P[..., 1]), "flat", PAD)])))
        for k in range(4):
            x = -0.075 + k * 0.05
            base = np.array([x, -0.71, 0.09])
            r.add(G.tube(f"claw_{fo}{k}", fo, [base, base + [x * 0.2, -0.08, 0.04], base + [x * 0.25, -0.15, 0.03]], [0.018, 0.011, 0.0015],
                         res=(7, 7), caps=(True, False), mat="teeth", mp=BONE, texel_scale=1.2, lod1=(5, 4)))

    # ------------------------------------------------------------------ neck + head
    r.add(G.tube("neck", "Neck", [(0, -0.06, -0.02), (0, 0.14, 0.02)], [0.24, 0.22], res=(20, 6), caps=(False, False), mat="fur", mp=FUR_D))
    socket_cut = K.ragged_cut(EYE_R + [0, 0, 0.05], 0.06, 0.012, 25, 71)
    ear_l = np.array([-0.31, 0.47, 0.0])

    def head_disp(d, p):
        q = p + [0, 0.15, 0]
        out = 0.03 * K.gauss(np.linalg.norm(q - np.array([0.0, 0.34, 0.24]), axis=-1), 0.1)          # heavy brow
        dd = np.linalg.norm(q - EYE_L, axis=-1)
        out += 0.025 * K.gauss(dd, 0.06) - 0.015 * K.gauss(dd, 0.03)
        out -= 0.05 * K.gauss(np.linalg.norm(q - EYE_R, axis=-1), 0.06)
        out += 0.02 * K.gauss(np.linalg.norm(q - np.array([-0.24, 0.1, 0.2]), axis=-1), 0.1)         # jowls
        out += 0.02 * K.gauss(np.linalg.norm(q - np.array([0.24, 0.1, 0.2]), axis=-1), 0.1)
        return out + 0.016 * (fbm(q * 7, 3, seed=81) - 0.5) * 2

    def head_stitch(P, N):
        return np.maximum(stitches(P, (0.05, 0.42, 0.25), (1, 0.15, -0.2), 0.12), stitches(P, (-0.3, 0.2, 0.2), (0.2, 1, 0.1), 0.09))

    head = G.ellip("head", "Head", (0, 0.15, 0), (0.39, 0.35, 0.35), res=(40, 26), disp=head_disp, mat="fur",
                   mp=dict(FUR, zones=[(head_stitch, "flat", dict(color=(0.06, 0.04, 0.03), smooth=0.2))]), cut=socket_cut)
    r.add(head, G.inner_of(head, 0.02, mat="flat", mp=dict(BLACK, ao=0.6)))
    r.add(G.tube("socketR_tube", "Head", [EYE_R + [0, 0, 0.06], EYE_R + [0, 0, -0.03]], [0.05, 0.03], res=(14, 4), caps=(False, True),
                 mat="flat", mp=dict(color=(0.025, 0.02, 0.02), smooth=0.3, ao=0.6), sdf=None))
    r.add(G.torus("socketL", "Head", EYE_L + [0, 0, -0.012], 0.044, 0.01, rot=(90, 0, 0), res=(16, 8), mat="flat", mp=dict(color=(0.03, 0.025, 0.025), smooth=0.4)))
    # muzzle, nose, upper teeth
    mz = np.array([0.0, 0.05, 0.3])
    r.add(G.ellip("muzzle", "Head", mz, (0.2, 0.15, 0.16), res=(28, 16), mat="plastic",
                  disp=lambda d, p: 0.012 * K.gauss(np.linalg.norm(p - [0.08, 0.04, 0.12], axis=-1), 0.06) + 0.012 * K.gauss(np.linalg.norm(p - [-0.08, 0.04, 0.12], axis=-1), 0.06),
                  mp=dict(MUZZLE, tex="plastic_cracked")))
    r.add(G.sell("nose", "Head", (0, 0.13, 0.445), (0.07, 0.042, 0.04), e=(0.6, 0.8), res=(16, 10), mat="paint", mp=dict(NOSE, chips=0.4)))
    for s in (-1, 1):
        r.add(G.ellip(f"nostril{s}", "Head", (0.028 * s, 0.115, 0.48), (0.013, 0.009, 0.006), res=(8, 6), mat="flat", mp=BLACK))
    for i in range(9):
        x = -0.15 + i * 0.0375
        z = mz[2] + 0.16 * np.sqrt(max(0.05, 1 - (x / 0.2) ** 2)) - 0.03
        tilt = (i % 2) * 8 - 4 + np.sin(i * 2.3) * 6
        r.add(K.tooth(f"tu{i}", "Head", (x, -0.03, z), (0.014, 0.034 + 0.008 * np.sin(i * 1.7), 0.011),
                      rot=(0, -np.degrees(np.arctan2(x, 0.2)), tilt), down=True, sharp=0.85))
    # ears: left furry round ear, right torn down to the metal frame
    r.add(G.ellip("earL", "Head", ear_l, (0.12, 0.12, 0.05), rot=(0, 0, 18), res=(18, 10), disp=lumps(0.008, 12, 91), mat="fur",
                  mp=dict(FUR, zones=[(lambda P, N: smoothstep(0.075, 0.06, np.linalg.norm(P - ear_l, axis=-1)) * (N[..., 2] > 0.3), "fur", BELLY)])))
    r.add(G.torus("earR_frame", "Head", (0.3, 0.45, 0.0), 0.085, 0.009, rot=(90, 0, -18), res=(16, 6), mat="metal", mp=METAL))
    r.add(K.rod("earR_rod", "Head", (0.24, 0.36, 0.0), (0.33, 0.52, 0.0), 0.012))
    rag = G.ellip("earR_rag", "Head", (0.29, 0.43, -0.005), (0.09, 0.09, 0.035), rot=(0, 0, -18), res=(14, 8), mat="fur", mp=FUR_D,
                  cut=lambda P: 0.37 - P[..., 1] + 0.03 * (fbm(P * 30, 3, seed=93) - 0.5))
    r.add(rag, G.inner_of(rag, 0.006, mat="flat", mp=FOAM))
    # jaw
    jaw = G.ellip("jaw", "Jaw", (0, -0.07, 0.17), (0.19, 0.07, 0.15), res=(26, 12), mat="plastic", mp=dict(MUZZLE, tex="plastic_cracked"),
                  disp=lambda d, p: -0.02 * smoothstep(0.3, 0.9, d[..., 1]))
    r.add(jaw)
    r.add(G.ellip("jawfur", "Jaw", (0, -0.1, 0.05), (0.27, 0.1, 0.17), res=(22, 10), disp=lumps(0.01, 10, 95), mat="fur", mp=FUR_D))
    r.add(G.ellip("tongue", "Jaw", (0, -0.035, 0.15), (0.13, 0.025, 0.11), res=(12, 8), mat="flat", mp=dict(color=(0.16, 0.04, 0.04), smooth=0.55)))
    for i in range(8):
        x = -0.13 + i * 0.037
        z = 0.17 + 0.15 * np.sqrt(max(0.05, 1 - (x / 0.19) ** 2)) - 0.03
        r.add(K.tooth(f"tl{i}", "Jaw", (x, -0.02, z), (0.012, 0.026, 0.01), rot=(0, -np.degrees(np.arctan2(x, 0.2)), np.sin(i * 1.9) * 8),
                      down=False, sharp=0.85))

    head_R, head_t = r.world("Head")
    jaw_R, jaw_t = r.world("Jaw")
    r.points["blood"] = [(head_t + head_R @ np.array([0.0, -0.03, 0.45]), 0.08, 0.8),
                         (jaw_t + jaw_R @ np.array([0.0, -0.03, 0.3]), 0.08, 0.8)]
    for fo in ("ForeL", "ForeR"):
        R, t = r.world(fo)
        r.points["blood"].append((t + R @ np.array([0, -0.82, 0.11]), 0.07, 0.9))
    r.eyes = [(("Head", EYE_L), 0.0375), (("Head", EYE_R), 0.0175)]
    return r


VIEWS = [dict(pos=[1.9, 1.9, 5.4], at=[0, 1.45, 0], fov=42), dict(pos=[0.9, 2.85, 1.8], at=[0, 2.68, 0.2], fov=38)]
