"""Robot assembly, SDF volume, atlas packing, texture baking and export."""
import math
import struct
import numpy as np
import cv2
from scipy import ndimage
from rg_core import rot_euler, nrm, smoothstep, lerp, fbm, vnoise
import rg_mats


class Robot:
    def __init__(self, name):
        self.name = name
        self.bones = {}   # name -> (parent, pos, euler, scale)
        self.order = []
        self.parts = []
        self.points = {}  # named world points for material effects (mouth, hands...)

    def bone(self, name, parent, pos, euler=(0, 0, 0)):
        self.bones[name] = (parent, np.asarray(pos, float), euler)
        self.order.append(name)
        return name

    def add(self, *parts):
        for p in parts:
            if isinstance(p, (list, tuple)):
                self.add(*p)
            else:
                assert p.bone in self.bones, p.bone
                self.parts.append(p)

    def world(self, b):
        if b is None:
            return np.eye(3), np.zeros(3)
        parent, pos, eul = self.bones[b]
        Rp, tp = self.world(parent)
        return Rp @ rot_euler(*eul), tp + Rp @ pos


# ----------------------------------------------------------------------------- SDF volume
class Volume:
    def __init__(self, robot, vox=0.012):
        pts = []
        for p in robot.parts:
            u, v = np.meshgrid(np.linspace(0, 1, 9), np.linspace(0, 1, 7))
            P, _ = p.eval(u, v)
            R, t = robot.world(p.bone)
            pts.append(P.reshape(-1, 3) @ R.T + t)
        pts = np.concatenate(pts)
        lo, hi = pts.min(0) - 0.25, pts.max(0) + 0.25
        self.lo, self.vox = lo, vox
        dims = np.ceil((hi - lo) / vox).astype(int) + 1
        self.dims = dims
        g = np.full(tuple(dims), 1.0, np.float32)
        for p in robot.parts:
            if p.sdf is None:
                continue
            R, t = robot.world(p.bone)
            u, v = np.meshgrid(np.linspace(0, 1, 17), np.linspace(0, 1, 11))
            P, _ = p.eval(u, v)
            Pw = P.reshape(-1, 3) @ R.T + t
            a = np.maximum(np.floor((Pw.min(0) - 0.2 - lo) / vox).astype(int), 0)
            b = np.minimum(np.ceil((Pw.max(0) + 0.2 - lo) / vox).astype(int) + 1, dims)
            xs = [lo[k] + np.arange(a[k], b[k]) * vox for k in range(3)]
            X, Y, Z = np.meshgrid(*xs, indexing="ij")
            W = np.stack([X, Y, Z], -1)
            L = (W - t) @ R  # world -> bone local
            d = p.sdf(L).astype(np.float32)
            sl = tuple(slice(a[k], b[k]) for k in range(3))
            g[sl] = np.minimum(g[sl], d)
        self.g = g

    def sample(self, P):
        c = ((P - self.lo) / self.vox).reshape(-1, 3).T
        return ndimage.map_coordinates(self.g, c, order=1, mode="nearest").reshape(P.shape[:-1])


def hemi_dirs(n=12, seed=3):
    rng = np.random.default_rng(seed)
    d = []
    for i in range(n):
        # cosine-weighted, stratified around z
        a = 2 * np.pi * (i + rng.random() * 0.5) / n
        r = math.sqrt((i % 3 + 0.5) / 3.0)
        d.append([r * math.cos(a), r * math.sin(a), math.sqrt(max(0, 1 - r * r))])
    return np.array(d)


def bake_ao(vol, P, N, dist=(0.02, 0.045, 0.08, 0.14, 0.22), ndir=12):
    """SDF cone-ish ambient occlusion (1 = open)."""
    sh = P.shape[:-1]
    P = P.reshape(-1, 3); N = N.reshape(-1, 3)
    t1 = nrm(np.cross(N, np.where(np.abs(N[:, 1:2]) < 0.9, [[0, 1, 0]], [[1, 0, 0]])))
    t2 = np.cross(N, t1)
    occ = np.zeros(len(P)); wsum = 0.0
    for d in hemi_dirs(ndir):
        D = t1 * d[0] + t2 * d[1] + N * d[2]
        for k, s in enumerate(dist):
            q = P + N * 0.006 + D * s
            v = vol.sample(q)
            w = 1.0 / (1 + k * 0.6)
            occ += w * np.clip((s * 0.7 - v) / (s * 0.7), 0, 1)   # tolerant of proxy inaccuracy
            wsum += w
    ao = 1 - np.clip(occ / wsum * 1.8, 0, 1)
    return ao.reshape(sh)


# ----------------------------------------------------------------------------- atlas
def pack(parts, S=2048, pad=4, fill=0.80):
    area = sum(lu * lv * p.texel_scale ** 2 for p in parts for lu, lv in [p.size_uv()])
    dens = math.sqrt(fill * S * S / area)
    sizes = [p.size_uv() for p in parts]
    for attempt in range(60):
        rects = []
        for p, (lu, lv) in zip(parts, sizes):
            w = int(np.clip(math.ceil(lu * dens * p.texel_scale), 6, S - 2 * pad))
            h = int(np.clip(math.ceil(lv * dens * p.texel_scale), 6, S - 2 * pad))
            rects.append([w, h])
        order = sorted(range(len(parts)), key=lambda i: -rects[i][1])
        x = y = shelf = 0
        ok = True
        pos = [None] * len(parts)
        for i in order:
            w, h = rects[i]
            if x + w + 2 * pad > S:
                x = 0; y += shelf; shelf = 0
            if y + h + 2 * pad > S:
                ok = False; break
            pos[i] = (x + pad, y + pad, w, h)
            x += w + 2 * pad
            shelf = max(shelf, h + 2 * pad)
        if ok:
            for p, r in zip(parts, pos):
                p.rect = r
            return dens
        dens *= 0.96
    raise RuntimeError("atlas packing failed")


def dilate_fill(img, mask, iters=12):
    """Push valid texels outwards into empty space (mip/bilinear padding)."""
    img = img.copy()
    m = mask.astype(np.float32)
    for _ in range(iters):
        k = np.ones((3, 3), np.float32)
        s = cv2.filter2D(img * m[..., None], -1, k, borderType=cv2.BORDER_CONSTANT)
        c = cv2.filter2D(m, -1, k, borderType=cv2.BORDER_CONSTANT)
        new = (m == 0) & (c > 0)
        img[new] = s[new] / c[new][:, None]
        m[new] = 1
    # remaining empty texels -> mean colour
    rest = m == 0
    if rest.any():
        img[rest] = img[~rest].mean(0)
    return img


# ----------------------------------------------------------------------------- bake
def bake(robot, S=2048, out_prefix="out", vox=0.012, ao_scale=0.5):
    dens = pack(robot.parts, S)
    print(f"[{robot.name}] atlas density {dens:.0f} px/m, {len(robot.parts)} parts")
    vol = Volume(robot, vox)
    print(f"[{robot.name}] volume {vol.dims}")
    alb = np.zeros((S, S, 3), np.float32)
    alpha = np.ones((S, S), np.float32)
    nor = np.zeros((S, S, 3), np.float32); nor[..., 2] = 1
    msk = np.zeros((S, S, 4), np.float32)
    used = np.zeros((S, S), bool)
    for p in robot.parts:
        x0, y0, w, h = p.rect
        R, t = robot.world(p.bone)
        u, v = np.meshgrid((np.arange(w) + 0.5) / w, (np.arange(h) + 0.5) / h)
        Pl, Nl = p.eval(u, v)
        Pw = Pl @ R.T + t
        Nw = Nl @ R.T
        # AO at reduced resolution
        aw, ah = max(4, int(w * ao_scale)), max(4, int(h * ao_scale))
        ua, va = np.meshgrid((np.arange(aw) + 0.5) / aw, (np.arange(ah) + 0.5) / ah)
        Pa, Na = p.eval(ua, va)
        if p.inner:
            ao = np.full((h, w), 0.35, np.float32)
        else:
            ao_s = bake_ao(vol, Pa @ R.T + t, Na @ R.T).astype(np.float32)
            ao = cv2.resize(ao_s, (w, h), interpolation=cv2.INTER_LINEAR)
        # physical texel spacing & curvature from the surface itself
        dPu = np.gradient(Pw, axis=1) * w  # dP/du
        dPv = np.gradient(Pw, axis=0) * h
        lu = np.maximum(np.linalg.norm(dPu, axis=-1), 1e-5)
        lv = np.maximum(np.linalg.norm(dPv, axis=-1), 1e-5)
        dNu = np.linalg.norm(np.gradient(Nw, axis=1) * w, axis=-1)
        dNv = np.linalg.norm(np.gradient(Nw, axis=0) * h, axis=-1)
        curv = dNu / lu + dNv / lv
        if p.cut is not None:
            cv = p.cut(Pl)
            a_ = smoothstep(-0.0012, 0.0012, cv)
            rv = (p.rim_cut or p.cut)(Pl)
            rim = smoothstep(0.012, 0.0, rv) * (rv > -0.002)
        else:
            a_ = np.ones_like(ao); rim = np.zeros_like(ao)
        ctx = rg_mats.Ctx(rim=rim, P=Pw, N=Nw, Pl=Pl, Nl=Nl, u=u, v=v, ao=ao, curv=curv, mp=p.mp, robot=robot, part=p,
                          texel_m=1.0 / dens)
        res = rg_mats.shade(p.mat, ctx)
        H = res["height"]
        # tangent-space normal from detail height (x along +u, y along +v)
        dhu = np.gradient(H, axis=1) * w / lu
        dhv = np.gradient(H, axis=0) * h / lv
        n = nrm(np.stack([-dhu, -dhv, np.ones_like(H)], -1))
        sl = (slice(y0, y0 + h), slice(x0, x0 + w))
        alb[sl] = res["albedo"]
        alpha[sl] = a_
        nor[sl] = n
        msk[sl] = np.dstack([res["metal"], ao, np.zeros_like(ao), res["smooth"]])
        used[sl] = True
    alb = dilate_fill(alb, used)
    nor = dilate_fill(nor, used)
    nor = nrm(nor)
    msk = dilate_fill(msk, used)
    # alpha: holes stay transparent, padding copies the nearest texel
    alpha = dilate_fill(alpha[..., None].repeat(3, -1), used)[..., 0]
    to8 = lambda a: np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8)
    rgba = np.dstack([alb, alpha])
    cv2.imwrite(out_prefix + ".png", cv2.cvtColor(to8(np.flipud(rgba)), cv2.COLOR_RGBA2BGRA))
    cv2.imwrite(out_prefix + "_n.png", cv2.cvtColor(to8(np.flipud(nor * 0.5 + 0.5)), cv2.COLOR_RGB2BGR))
    cv2.imwrite(out_prefix + "_ms.png", cv2.cvtColor(to8(np.flipud(msk)), cv2.COLOR_RGBA2BGRA))
    return alb, nor, msk


# ----------------------------------------------------------------------------- meshes
def build_meshes(robot, lod, S=2048):
    """Merged mesh per bone (bone-local). Returns {bone: (P, N, UV, [opaque_tris, cutout_tris])}."""
    out = {}
    for b in robot.order:
        Ps, Ns, UVs, To, Tc = [], [], [], [], []
        base = 0
        for p in robot.parts:
            if p.bone != b:
                continue
            P, N, uv, tris = p.mesh(lod)
            x0, y0, w, h = p.rect
            U = np.stack([(x0 + uv[:, 0] * w) / S, (y0 + uv[:, 1] * h) / S], -1)
            Ps.append(P); Ns.append(N); UVs.append(U)
            (Tc if p.cut is not None else To).append(tris + base)
            base += len(P)
        if not Ps:
            continue
        cat = lambda L: np.concatenate(L) if L else np.zeros((0, 3), int)
        out[b] = (np.concatenate(Ps), np.concatenate(Ns), np.concatenate(UVs), [cat(To), cat(Tc)])
    return out


def export_bytes(meshes, path):
    """RFM2: per bone: name, vertices (pos, normal, uv), then 2 submeshes (opaque, alpha-cut) as int32 triangle lists."""
    with open(path, "wb") as f:
        f.write(b"RFM2")
        f.write(struct.pack("<i", len(meshes)))
        for b, (P, N, UV, subs) in meshes.items():
            nb = b.encode()
            f.write(struct.pack("<i", len(nb))); f.write(nb)
            f.write(struct.pack("<i", len(P)))
            f.write(P.astype("<f4").tobytes()); f.write(N.astype("<f4").tobytes()); f.write(UV.astype("<f4").tobytes())
            f.write(struct.pack("<i", len(subs)))
            for T in subs:
                f.write(struct.pack("<i", T.size)); f.write(T.astype("<i4").tobytes())


def tri_count(meshes):
    return sum(len(t) for m in meshes.values() for t in m[3])
