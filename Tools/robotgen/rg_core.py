"""Rusted Fair robot generator - core toolkit.

Parametric surface parts (u around, v along) in bone-local Unity coordinates (x right, y up, z forward).
Each part produces a mesh (two LODs) and, after atlas packing, its own rectangle in one texture atlas per
robot (albedo, tangent-space normal, metallic/smoothness).  Ambient occlusion is baked from a signed
distance volume built out of per-part proxy shapes.
"""
import math
import numpy as np
import cv2
from scipy import ndimage

TEX_DIR = None  # set by caller: folder with the game's tiling textures


# ----------------------------------------------------------------------------- math
def rot_euler(ex, ey, ez):
    """Unity Quaternion.Euler(ex,ey,ez) as a 3x3 matrix (R = Ry * Rx * Rz)."""
    x, y, z = math.radians(ex), math.radians(ey), math.radians(ez)
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def nrm(v, axis=-1):
    l = np.linalg.norm(v, axis=axis, keepdims=True)
    return v / np.maximum(l, 1e-12)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def spow(x, e):
    return np.sign(x) * np.abs(x) ** e


# ----------------------------------------------------------------------------- noise
def _hash(ix, iy, iz, seed):
    h = (ix.astype(np.int64) * 73856093) ^ (iy.astype(np.int64) * 19349663) ^ (iz.astype(np.int64) * 83492791) ^ (seed * 2654435761)
    h = h & 0xFFFFFFFF
    h = ((h ^ (h >> 15)) * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 3266489917) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / 16777216.0


def vnoise(p, seed=0):
    """3D value noise in [0,1], quintic interpolation. p: (...,3)"""
    p = np.asarray(p, dtype=np.float64)
    pi = np.floor(p)
    f = p - pi
    w = f * f * f * (f * (f * 6 - 15) + 10)
    ix, iy, iz = pi[..., 0].astype(np.int64), pi[..., 1].astype(np.int64), pi[..., 2].astype(np.int64)
    res = 0.0
    for dx in (0, 1):
        wx = w[..., 0] if dx else 1 - w[..., 0]
        for dy in (0, 1):
            wy = w[..., 1] if dy else 1 - w[..., 1]
            for dz in (0, 1):
                wz = w[..., 2] if dz else 1 - w[..., 2]
                res = res + wx * wy * wz * _hash(ix + dx, iy + dy, iz + dz, seed)
    return res


def fbm(p, octaves=4, lac=2.03, gain=0.5, seed=0):
    amp, tot, s, q = 1.0, 0.0, 0.0, np.asarray(p, dtype=np.float64)
    for o in range(octaves):
        s = s + amp * vnoise(q, seed + o * 31)
        tot += amp
        amp *= gain
        q = q * lac + 17.17
    return s / tot


def worley(p, seed=0, jitter=0.9):
    """F1 and F2 cellular distances (cell size 1)."""
    p = np.asarray(p, dtype=np.float64)
    pi = np.floor(p)
    f1 = np.full(p.shape[:-1], 9.0)
    f2 = np.full(p.shape[:-1], 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = pi + np.array([dx, dy, dz])
                ix, iy, iz = c[..., 0].astype(np.int64), c[..., 1].astype(np.int64), c[..., 2].astype(np.int64)
                o = np.stack([_hash(ix, iy, iz, seed), _hash(ix, iy, iz, seed + 7), _hash(ix, iy, iz, seed + 13)], -1)
                d = np.linalg.norm(c + 0.5 + (o - 0.5) * jitter - p, axis=-1)
                f2 = np.where(d < f1, f1, np.minimum(f2, d))
                f1 = np.minimum(f1, d)
    return f1, f2


# ----------------------------------------------------------------------------- textures
_tex_cache = {}


def tex(name):
    if name not in _tex_cache:
        im = cv2.imread(f"{TEX_DIR}/{name}.png", cv2.IMREAD_COLOR)
        _tex_cache[name] = cv2.cvtColor(im, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    return _tex_cache[name]


def sample_tex(name, uv):
    """Bilinear repeat sampling. uv: (...,2) in texture repeats."""
    t = tex(name)
    h, w = t.shape[:2]
    sh = uv.shape[:-1]
    x = (np.mod(uv[..., 0], 1.0) * w - 0.5).astype(np.float32).reshape(-1)
    y = (np.mod(-uv[..., 1], 1.0) * h - 0.5).astype(np.float32).reshape(-1)
    n = x.size
    cols = 4096
    rows = (n + cols - 1) // cols
    xp = np.zeros(rows * cols, np.float32); xp[:n] = x
    yp = np.zeros(rows * cols, np.float32); yp[:n] = y
    out = cv2.remap(t, xp.reshape(rows, cols), yp.reshape(rows, cols), cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)
    return out.reshape(-1, 3)[:n].reshape(sh + (3,)).astype(np.float64)


def triplanar(name, P, N, scale, sharp=4.0):
    w = np.abs(N) ** sharp
    w = w / np.maximum(w.sum(-1, keepdims=True), 1e-9)
    q = P / scale
    a = sample_tex(name, q[..., [2, 1]] + 0.31)
    b = sample_tex(name, q[..., [0, 2]] + 0.57)
    c = sample_tex(name, q[..., [0, 1]] + 0.13)
    return a * w[..., 0:1] + b * w[..., 1:2] + c * w[..., 2:3]


def luma(c):
    return c[..., 0] * 0.3 + c[..., 1] * 0.59 + c[..., 2] * 0.11


# ----------------------------------------------------------------------------- proxy SDFs (bone-local)
def sdf_ellipsoid(c, r):
    c, r = np.asarray(c, float), np.asarray(r, float)

    def f(p):
        q = p - c
        k0 = np.linalg.norm(q / r, axis=-1)
        k1 = np.maximum(np.linalg.norm(q / (r * r), axis=-1), 1e-9)
        return k0 * (k0 - 1.0) / k1   # IQ's ellipsoid bound (close to the true distance near the surface)
    return f


def sdf_rbox(c, half, rr):
    c, half = np.asarray(c, float), np.asarray(half, float)

    def f(p):
        q = np.abs(p - c) - (half - rr)
        return np.linalg.norm(np.maximum(q, 0), axis=-1) + np.minimum(q.max(-1), 0) - rr
    return f


def sdf_polyline(pts, rad):
    pts = np.asarray(pts, float)
    rad = np.broadcast_to(np.asarray(rad, float), (len(pts),))

    def f(p):
        d = np.full(p.shape[:-1], 9.0)
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            ba = b - a
            h = np.clip(((p - a) @ ba) / max(ba @ ba, 1e-12), 0, 1)
            r = rad[i] + (rad[i + 1] - rad[i]) * h
            d = np.minimum(d, np.linalg.norm(p - a - h[..., None] * ba, axis=-1) - r)
        return d
    return f


def sdf_torus(c, R, r, Rm):
    c = np.asarray(c, float)
    Rt = np.asarray(Rm).T

    def f(p):
        q = (p - c) @ Rt.T  # into torus frame (torus in xz plane)
        qx = np.sqrt(q[..., 0] ** 2 + q[..., 2] ** 2) - R
        return np.sqrt(qx * qx + q[..., 1] ** 2) - r
    return f


def transform_sdf(f, c=(0, 0, 0), Rm=None):
    """Proxy defined around origin in a rotated frame -> bone-local."""
    c = np.asarray(c, float)
    if Rm is None:
        return lambda p: f(p - c)
    Rm = np.asarray(Rm)
    return lambda p: f((p - c) @ Rm)  # (Rm^T (p-c)) row-vector form


# ----------------------------------------------------------------------------- Part
class Part:
    """A parametric surface. fn(u,v) -> (...,3) bone-local; u in [0,1] around (wrap) or across, v in [0,1] along."""

    def __init__(self, name, bone, fn, nu, nv, wrap_u=True, mat="plastic", mp=None, sdf=None, keep=None,
                 inner=False, lod1=None, texel_scale=1.0, shadow=True, uv_aspect=None, cut=None):
        self.name, self.bone, self.fn = name, bone, fn
        self.cut = cut  # soft hole mask: cut(P) > 0 keeps the surface (alpha-tested in the game)
        self.rim_cut = None  # optional mask for the torn-edge colour (defaults to cut)
        if cut is not None and sdf is not None:
            # hollow shell proxy with the same holes, so whatever sits inside is not fully occluded
            base_sdf = sdf
            sdf = lambda p, b=base_sdf, c=cut: np.maximum(np.maximum(b(p), -b(p) - 0.02), -c(p))
        self.nu, self.nv, self.wrap_u = nu, nv, wrap_u
        self.mat, self.mp = mat, (mp or {})
        self.sdf, self.keep, self.inner = sdf, keep, inner
        self.lod1 = lod1 or (max(6, nu // 2), max(3, nv // 2))
        self.texel_scale = texel_scale
        self.shadow = shadow
        self.flip = False
        self.rect = None
        self._orient()

    # analytic normal by central differences (v clamped away from poles)
    def eval(self, u, v):
        P = self.fn(1.0 - u if self.flip else u, v)
        e = 1e-3
        vv = np.clip(v, 0.004, 0.996)
        uu = u
        f = (lambda a, b: self.fn(1.0 - a if self.flip else a, b))
        dU = f(uu + e, vv) - f(uu - e, vv)
        dV = f(uu, np.minimum(vv + e, 1.0)) - f(uu, np.maximum(vv - e, 0.0))
        N = nrm(np.cross(dU, dV))
        if self.inner:
            N = -N
        return P, N

    def _orient(self):
        u, v = np.meshgrid(np.linspace(0.02, 0.98, 24), np.linspace(0.08, 0.92, 16))
        P, N = self.eval(u, v)
        c = P.reshape(-1, 3).mean(0)
        s = np.sum(N * (P - c))
        want = -1 if self.inner else 1
        if s * want < 0:
            self.flip = True

    def mesh(self, lod=0):
        nu, nv = (self.nu, self.nv) if lod == 0 else self.lod1
        us = np.linspace(0, 1, nu + 1)
        vs = np.linspace(0, 1, nv + 1)
        U, V = np.meshgrid(us, vs)  # (nv+1, nu+1)
        P, N = self.eval(U, V)
        if self.inner:
            pass
        idx = np.arange((nv + 1) * (nu + 1)).reshape(nv + 1, nu + 1)
        a = idx[:-1, :-1]; b = idx[:-1, 1:]; c = idx[1:, 1:]; d = idx[1:, :-1]
        quads = np.stack([a, b, c, d], -1).reshape(-1, 4)
        Pf = P.reshape(-1, 3)
        sel = np.ones(len(quads), bool)
        if self.keep is not None or self.cut is not None:
            uc = (U[:-1, :-1] + U[:-1, 1:]) * 0.5
            vc = (V[:-1, :-1] + V[1:, :-1]) * 0.5
            Pc, _ = self.eval(uc, vc)
            if self.keep is not None:
                sel &= self.keep(Pc, uc, vc).reshape(-1)
            if self.cut is not None:
                # drop quads entirely inside a hole (corners and centre); the rest is alpha-tested
                cv = self.cut(Pf).reshape(-1)
                cc = self.cut(Pc).reshape(-1)
                sel &= np.maximum(cv[quads].max(1), cc) > -0.004
        quads = quads[sel]
        tris = np.concatenate([quads[:, [0, 1, 2]], quads[:, [0, 2, 3]]], 0)
        if self.flip != self.inner:
            pass
        # orientation: front face = cross(b-a, c-a) along normal
        A, B, C = Pf[tris[:, 0]], Pf[tris[:, 1]], Pf[tris[:, 2]]
        fn_ = np.cross(B - A, C - A)
        area = np.linalg.norm(fn_, axis=-1)
        tris = tris[area > 1e-10]
        A, B, C = Pf[tris[:, 0]], Pf[tris[:, 1]], Pf[tris[:, 2]]
        fn_ = np.cross(B - A, C - A)
        Nf = N.reshape(-1, 3)
        nav = Nf[tris[:, 0]] + Nf[tris[:, 1]] + Nf[tris[:, 2]]
        bad = np.sum(fn_ * nav, -1) < 0
        tris[bad] = tris[bad][:, [0, 2, 1]]
        uv = np.stack([U.reshape(-1), V.reshape(-1)], -1)
        used = np.unique(tris)
        remap = -np.ones(len(Pf), int)
        remap[used] = np.arange(len(used))
        return Pf[used], Nf[used], uv[used], remap[tris]

    def size_uv(self):
        """Mean physical length along u and v (m)."""
        u, v = np.meshgrid(np.linspace(0, 1, 33), np.linspace(0.0, 1.0, 17))
        P, _ = self.eval(u, v)
        lu = np.linalg.norm(np.diff(P, axis=1), axis=-1).sum(1).mean()
        lv = np.linalg.norm(np.diff(P, axis=0), axis=-1).sum(0).mean()
        return max(lu, 1e-3), max(lv, 1e-3)


# ----------------------------------------------------------------------------- generators
def ellip(name, bone, c, r, rot=(0, 0, 0), res=(32, 16), disp=None, warp=None, **kw):
    """Ellipsoid. disp(Pdir_unit, P) -> radial offset in metres (applied before warp)."""
    c, r = np.asarray(c, float), np.asarray(r, float)
    Rm = rot_euler(*rot)

    def fn(u, v):
        ph = 2 * np.pi * u
        th = np.pi * (v - 0.5)
        d = np.stack([np.cos(th) * np.cos(ph), np.sin(th), np.cos(th) * np.sin(ph)], -1)
        p = d * r
        if disp is not None:
            p = p + nrm(d / r) * disp(d, p)[..., None]
        p = p @ Rm.T
        p = p + c
        if warp is not None:
            p = warp(p)
        return p
    kw.setdefault("sdf", transform_sdf(sdf_ellipsoid((0, 0, 0), r), c, Rm))
    return Part(name, bone, fn, res[0], res[1], True, **kw)


def sell(name, bone, c, half, e=(0.3, 0.3), rot=(0, 0, 0), res=(32, 16), warp=None, disp=None, **kw):
    """Superellipsoid (rounded box for small e). half = half extents. e = (vertical exponent, around exponent)."""
    c, half = np.asarray(c, float), np.asarray(half, float)
    Rm = rot_euler(*rot)

    def fn(u, v):
        ph = 2 * np.pi * u - np.pi
        th = np.pi * (v - 0.5)
        ct, st = spow(np.cos(th), e[0]), spow(np.sin(th), e[0])
        cp, sp_ = spow(np.cos(ph), e[1]), spow(np.sin(ph), e[1])
        p = np.stack([half[0] * ct * cp, half[1] * st, half[2] * ct * sp_], -1)
        if disp is not None:
            d = nrm(p / half)
            p = p + d * disp(d, p)[..., None]
        p = p @ Rm.T + c
        if warp is not None:
            p = warp(p)
        return p
    rr = float(min(half) * min(1.0, max(e) * 0.9))
    kw.setdefault("sdf", transform_sdf(sdf_rbox((0, 0, 0), half, rr), c, Rm))
    return Part(name, bone, fn, res[0], res[1], True, **kw)


def catmull(pts, n):
    pts = np.asarray(pts, float)
    P = np.vstack([2 * pts[0] - pts[1], pts, 2 * pts[-1] - pts[-2]])
    seg = len(pts) - 1
    t = np.linspace(0, seg, n)
    i = np.minimum(t.astype(int), seg - 1)
    f = (t - i)[:, None]
    p0, p1, p2, p3 = P[i], P[i + 1], P[i + 2], P[i + 3]
    return 0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f ** 2 + (-p0 + 3 * p1 - 3 * p2 + p3) * f ** 3)


class Curve:
    """Densely sampled spline with parallel-transport frames, arc-length parametrised."""

    def __init__(self, pts, up=(0, 0, 1), n=200):
        C = catmull(pts, n) if len(pts) > 2 else np.linspace(pts[0], pts[1], n)
        seg = np.linalg.norm(np.diff(C, axis=0), axis=-1)
        s = np.concatenate([[0], np.cumsum(seg)])
        self.L = s[-1]
        self.s = s / s[-1]
        self.C = C
        T = nrm(np.gradient(C, axis=0))
        up = np.asarray(up, float)
        if abs(np.dot(up, T[0])) > 0.95:
            up = np.array([1.0, 0, 0]) if abs(T[0][0]) < 0.9 else np.array([0, 1.0, 0])
        Nn = [nrm(np.cross(np.cross(T[0], up), T[0]))]
        for k in range(1, len(T)):
            b = np.cross(T[k - 1], T[k])
            nb = np.linalg.norm(b)
            n_ = Nn[-1]
            if nb > 1e-9:
                b /= nb
                ang = math.acos(np.clip(np.dot(T[k - 1], T[k]), -1, 1))
                n_ = n_ * math.cos(ang) + np.cross(b, n_) * math.sin(ang) + b * np.dot(b, n_) * (1 - math.cos(ang))
            Nn.append(nrm(n_ - T[k] * np.dot(n_, T[k])))
        self.T, self.N = T, np.array(Nn)
        self.B = np.cross(self.T, self.N)

    def at(self, t):
        t = np.clip(t, 0, 1)
        idx = np.interp(t, self.s, np.arange(len(self.s)))
        i0 = np.minimum(idx.astype(int), len(self.s) - 2)
        f = (idx - i0)[..., None]
        g = lambda A: A[i0] * (1 - f) + A[i0 + 1] * f
        return g(self.C), nrm(g(self.T)), nrm(g(self.N)), nrm(g(self.B))


def tube(name, bone, pts, radius, res=(16, 24), rxz=(1.0, 1.0), e=1.0, caps=(True, True), up=(0, 0, 1),
         rfn=None, disp=None, twist=0.0, cap_len=1.0, **kw):
    """Swept tube along a spline. radius: float or list (interpolated along length) ; rfn(t, ang) multiplier.
    e: superellipse exponent of the cross-section (1 round, <1 boxy). Caps are hemispherical (scaled by cap_len)."""
    cur = Curve(pts, up)
    rad = np.atleast_1d(np.asarray(radius, float))
    rt = np.linspace(0, 1, len(rad))
    capf = 0.12

    def R_at(t):
        return np.interp(t, rt, rad)

    def fn(u, v):
        ang = 2 * np.pi * u
        c0, c1 = caps
        a0 = capf if c0 else 0.0
        a1 = capf if c1 else 0.0
        # map v -> (t along curve, cap angle factor)
        t = np.clip((v - a0) / max(1e-6, 1 - a0 - a1), 0, 1)
        k = np.ones_like(v)
        off = np.zeros_like(v)
        if c0:
            m = v < a0
            al = np.where(m, (a0 - v) / a0, 0) * (np.pi / 2)
            k = np.where(m, np.cos(al), k)
            off = np.where(m, -np.sin(al), off)
        if c1:
            m = v > 1 - a1
            al = np.where(m, (v - (1 - a1)) / a1, 0) * (np.pi / 2)
            k = np.where(m, np.cos(al), k)
            off = np.where(m, np.sin(al), off)
        C, T, N, B = cur.at(t)
        r = R_at(t)
        if rfn is not None:
            r = r * rfn(t, ang)
        a2 = ang + twist * t
        cx, sx = spow(np.cos(a2), e), spow(np.sin(a2), e)
        p = C + (N * (cx * rxz[0])[..., None] + B * (sx * rxz[1])[..., None]) * (r * k)[..., None] + T * (off * r * cap_len)[..., None]
        if disp is not None:
            d = nrm(N * cx[..., None] + B * sx[..., None])
            p = p + d * disp(t, ang, p)[..., None]
        return p
    kw.setdefault("sdf", sdf_polyline(cur.C[::8].tolist() + [cur.C[-1].tolist()],
                                       np.interp(np.append(cur.s[::8], 1.0), rt, rad) * max(rxz)))
    return Part(name, bone, fn, res[0], res[1], True, **kw)


def lathe(name, bone, prof, c=(0, 0, 0), rot=(0, 0, 0), res=(32, 16), rfn=None, warp=None, **kw):
    """Surface of revolution around local +y. prof: list of (radius, y) from bottom to top (use r=0 at the ends to close)."""
    prof = np.asarray(prof, float)
    c = np.asarray(c, float)
    Rm = rot_euler(*rot)
    # one equal v-interval per profile segment (corners land on grid lines when nv is a multiple)
    s = np.linspace(0, 1, len(prof))

    def fn(u, v):
        r = np.interp(v, s, prof[:, 0])
        y = np.interp(v, s, prof[:, 1])
        ang = 2 * np.pi * u
        if rfn is not None:
            r = r * rfn(v, ang)
        p = np.stack([r * np.cos(ang), y, r * np.sin(ang)], -1)
        p = p @ Rm.T + c
        if warp is not None:
            p = warp(p)
        return p

    def sd(p):
        q = (p - c) @ Rm  # into lathe frame
        rr = np.sqrt(q[..., 0] ** 2 + q[..., 2] ** 2)
        y = q[..., 1]
        # 2D distance to the profile polyline, sign via winding against the axis-closed polygon
        poly = np.vstack([[0, prof[0, 1]], prof, [0, prof[-1, 1]]])
        d = np.full(rr.shape, 9.0)
        inside = np.zeros(rr.shape, bool)
        for i in range(len(poly) - 1):
            a, b = poly[i], poly[i + 1]
            ba = b - a
            pa0, pa1 = rr - a[0], y - a[1]
            h = np.clip((pa0 * ba[0] + pa1 * ba[1]) / max(ba @ ba, 1e-12), 0, 1)
            d = np.minimum(d, np.hypot(pa0 - ba[0] * h, pa1 - ba[1] * h))
            cond = ((a[1] > y) != (b[1] > y)) & (rr < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1] + 1e-12) + a[0])
            inside ^= cond
        return np.where(inside, -d, d)
    kw.setdefault("sdf", sd)
    return Part(name, bone, fn, res[0], res[1], True, **kw)


def torus(name, bone, c, R, r, rot=(0, 0, 0), res=(16, 8), Rm=None, stretch=1.0, **kw):
    c = np.asarray(c, float)
    Rm = rot_euler(*rot) if Rm is None else np.asarray(Rm, float)

    def fn(u, v):
        a = 2 * np.pi * u
        b = 2 * np.pi * v
        p = np.stack([(R * stretch + r * np.cos(b)) * np.cos(a), r * np.sin(b), (R + r * np.cos(b)) * np.sin(a)], -1)
        return p @ Rm.T + c
    kw.setdefault("sdf", sdf_torus(c, R, r, Rm))
    return Part(name, bone, fn, res[0], res[1], True, **kw)


def inner_of(part, offset, name=None, mat="inner", mp=None):
    """Inside-facing copy of a shell part, pushed inwards by offset (keeps the same hole mask)."""
    base = part

    def fn(u, v):
        P, N = base.eval(u, v)
        return P - N * offset
    nu, nv = max(8, part.nu // 2), max(4, part.nv // 2)
    p = Part(name or part.name + "_in", part.bone, fn, nu, nv, part.wrap_u, mat=mat, mp=mp or {},
             sdf=None, keep=None, inner=True, lod1=(max(6, nu // 2), max(3, nv // 2)), texel_scale=0.3, shadow=False,
             cut=part.cut)
    # same hole mask (masks are functions of position)
    if base.keep is not None:
        p.keep = base.keep
    return p


def loft(name, bone, secs, res=(32, 24), close=(True, True), rot=(0, 0, 0), c=(0, 0, 0), rfn=None, disp=None, warp=None, **kw):
    """Lofted closed surface along local +y. secs: rows (y, rx, rz, e, cx, cz) (cx/cz: section centre offset,
    e superellipse exponent: 1 round, <1 boxy). Section parameters are interpolated with Catmull-Rom;
    close collapses the ends to a pole half a radius beyond the end sections."""
    S = np.asarray(secs, float)
    if S.shape[1] < 6:
        S = np.hstack([S, np.zeros((len(S), 6 - S.shape[1]))])
        S[:, 3] = np.where(S[:, 3] == 0, 1.0, S[:, 3])
    rows = [S[i] for i in range(len(S))]
    if close[0]:
        p = S[0].copy(); p[0] -= 0.55 * min(S[0, 1], S[0, 2]); p[1] = p[2] = 0.0; rows.insert(0, p)
    if close[1]:
        p = S[-1].copy(); p[0] += 0.55 * min(S[-1, 1], S[-1, 2]); p[1] = p[2] = 0.0; rows.append(p)
    S = np.array(rows)
    n = len(S)
    dense = catmull(S, 400)
    # keep radii non-negative and poles exact
    dense[:, 1:3] = np.maximum(dense[:, 1:3], 0)
    tt = np.linspace(0, 1, 400)
    Rm = rot_euler(*rot); c = np.asarray(c, float)

    def fn(u, v):
        vv = np.clip(v, 0, 1)
        g = lambda k: np.interp(vv, tt, dense[:, k])
        y, rx, rz, e, cx, cz = g(0), g(1), g(2), g(3), g(4), g(5)
        if close[0]:
            m = vv < 1.0 / (n - 1)
            f = np.clip(vv * (n - 1), 0, 1)
            k = np.sin(f * np.pi / 2)
            y0, y1 = S[0, 0], S[1, 0]
            rx = np.where(m, S[1, 1] * k, rx); rz = np.where(m, S[1, 2] * k, rz)
            y = np.where(m, y1 + (y0 - y1) * np.cos(f * np.pi / 2), y)
            cx = np.where(m, S[1, 4], cx); cz = np.where(m, S[1, 5], cz)
        if close[1]:
            m = vv > 1 - 1.0 / (n - 1)
            f = np.clip((1 - vv) * (n - 1), 0, 1)
            k = np.sin(f * np.pi / 2)
            y0, y1 = S[-1, 0], S[-2, 0]
            rx = np.where(m, S[-2, 1] * k, rx); rz = np.where(m, S[-2, 2] * k, rz)
            y = np.where(m, y1 + (y0 - y1) * np.cos(f * np.pi / 2), y)
            cx = np.where(m, S[-2, 4], cx); cz = np.where(m, S[-2, 5], cz)
        a = 2 * np.pi * u
        if rfn is not None:
            mul = rfn(vv, a)
            rx = rx * mul; rz = rz * mul
        ca, sa = spow(np.cos(a), e), spow(np.sin(a), e)
        p = np.stack([cx + rx * ca, y, cz + rz * sa], -1)
        if disp is not None:
            d = nrm(np.stack([ca / np.maximum(rx, 1e-4), np.zeros_like(y), sa / np.maximum(rz, 1e-4)], -1))
            p = p + d * disp(vv, a, p)[..., None]
        p = p @ Rm.T + c
        if warp is not None:
            p = warp(p)
        return p

    if "sdf" not in kw:
        ys = dense[:, 0]
        order = np.argsort(ys)
        ys_s = ys[order]
        y0, y1 = ys.min(), ys.max()

        def base(p):
            y = p[..., 1]
            g = lambda k: np.interp(np.clip(y, y0, y1), ys_s, dense[order, k])
            rx, rz = np.maximum(g(1), 0.004), np.maximum(g(2), 0.004)
            cx, cz = g(4), g(5)
            qx, qz = p[..., 0] - cx, p[..., 2] - cz
            k0 = np.sqrt((qx / rx) ** 2 + (qz / rz) ** 2)
            k1 = np.maximum(np.sqrt((qx / rx ** 2) ** 2 + (qz / rz ** 2) ** 2), 1e-9)
            dxz = k0 * (k0 - 1) / k1
            dy = np.maximum(y0 - y, y - y1)
            return np.maximum(dxz, dy)
        kw["sdf"] = transform_sdf(base, c, Rm)
    return Part(name, bone, fn, res[0], res[1], True, **kw)
