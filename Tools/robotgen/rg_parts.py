"""Reusable animatronic components."""
import numpy as np
import rg_core as G
from rg_core import fbm, vnoise, nrm, smoothstep

METAL = dict(color=(0.48, 0.47, 0.45), rust=0.75)
STEEL_CLEAN = dict(color=(0.55, 0.55, 0.54), rust=0.35, smooth=0.6)
CHROME = dict(color=(0.72, 0.72, 0.73), tarnish=0.6)
GOLD = dict(color=(0.80, 0.58, 0.24), tarnish=0.55)
BLACK = dict(color=(0.03, 0.03, 0.03), smooth=0.25)
RUBBER = dict(color=(0.045, 0.043, 0.04), smooth=0.35, bump=0.0004)
INNER = dict(color=(0.07, 0.055, 0.045), smooth=0.05, bump=0.0006, nscale=90)
WIRE_COLS = [(0.42, 0.04, 0.03), (0.05, 0.05, 0.05), (0.55, 0.42, 0.06), (0.08, 0.16, 0.38)]


def ball(name, bone, c, r, mp=METAL, mat="metal", res=(12, 8)):
    return G.ellip(name, bone, c, (r, r, r), res=res, mat=mat, mp=mp)


def rod(name, bone, a, b, r, mp=METAL, mat="metal", res=(8, 5), e=1.0, caps=(True, True)):
    return G.tube(name, bone, [a, b], r, res=res, mat=mat, mp=mp, e=e, caps=caps)


def piston(name, bone, a, b, r_house, frac=0.55, res=(8, 4)):
    a, b = np.asarray(a, float), np.asarray(b, float)
    m = a + (b - a) * frac
    return [G.tube(name + "_h", bone, [a, m], r_house, res=res, mat="metal", mp=METAL),
            G.tube(name + "_r", bone, [m - (b - a) * 0.05, b], r_house * 0.55, res=(6, 3), mat="shiny", mp=CHROME),
            G.ellip(name + "_e", bone, b, (r_house * 0.8,) * 3, res=(6, 4), mat="metal", mp=METAL)]


def wire(name, bone, pts, r=0.006, k=0, res=(5, 12)):
    return G.tube(name, bone, pts, r, res=res, mat="flat",
                  mp=dict(color=WIRE_COLS[k % len(WIRE_COLS)], smooth=0.45, bump=0.0001, dirt=0.6), shadow=False,
                  texel_scale=0.5)


def sag(a, b, drop, n=5, side=(0, 0, 0)):
    a, b, side = np.asarray(a, float), np.asarray(b, float), np.asarray(side, float)
    pts = []
    for i in range(n):
        t = i / (n - 1)
        p = a + (b - a) * t
        p[1] -= drop * 4 * t * (1 - t)
        p = p + side * 4 * t * (1 - t)
        pts.append(p)
    return pts


def disc(name, bone, c, r, h, rot=(0, 0, 0), mp=METAL, mat="metal", res=(14, 5)):
    """Bevelled disc / short cylinder centred at c (axis = local y after rot)."""
    hh = h * 0.5
    b = min(r * 0.2, hh * 0.6)
    prof = [(0, -hh), (r - b, -hh), (r, -hh + b), (r, hh - b), (r - b, hh), (0, hh)]
    return G.lathe(name, bone, prof, c=c, rot=rot, res=(res[0], res[1]), mat=mat, mp=mp)


def tooth(name, bone, c, half, rot=(0, 0, 0), down=True, mp=None, sharp=0.65):
    """Tapered tooth: rounded box narrowing towards the tip (-y when down)."""
    c = np.asarray(c, float)
    s = -1 if down else 1
    Rm = G.rot_euler(*rot)
    hy = half[1]

    def warp(p):
        q = (p - c) @ Rm  # back into tooth frame
        t = np.clip((q[..., 1] * s) / hy * 0.5 + 0.5, 0, 1)  # 0 root .. 1 tip
        k = 1 - sharp * t ** 1.5
        q = q * np.stack([k, np.ones_like(k), k], -1)
        return q @ Rm.T + c
    return G.sell(name, bone, c, half, e=(0.55, 0.6), rot=rot, res=(6, 5), warp=warp, mat="teeth",
                  mp=mp or dict(), texel_scale=1.4, lod1=(4, 3))


def finger(name, bone, pts, rad, mat="cloth", mp=None, res=(7, 7)):
    return G.tube(name, bone, pts, rad, res=res, mat=mat, mp=mp or {}, texel_scale=1.2, lod1=(5, 4))


def noise_mask(P, scale, seed):
    return (fbm(P * scale, 3, seed=seed) - 0.5) * 2.0


def ragged_keep(center, radius, amp, scale=14.0, seed=1, outside=True):
    """keep(P) True outside an irregular hole (outside=True) or inside an irregular blob."""
    c = np.asarray(center, float)

    def keep(P, u, v):
        d = np.linalg.norm(P - c, axis=-1) + noise_mask(P, scale, seed) * amp
        return d > radius if outside else d < radius
    return keep


def gauss(d, s):
    return np.exp(-(d / s) ** 2)


def ragged_cut(center, radius, amp, scale=14.0, seed=1):
    """Soft hole mask (>0 keeps the surface): an irregular round hole."""
    c = np.asarray(center, float)

    def cut(P):
        d = np.linalg.norm(P - c, axis=-1) + noise_mask(P, scale, seed) * amp
        return d - radius
    return cut


def hem_cut(y0, amp, scale=16.0, seed=7, above=True):
    """Ragged hem: keeps y above (or below) y0 with a noisy edge."""
    def cut(P):
        e = y0 - amp * (fbm(P * scale, 3, seed=seed) - 0.3)
        return (P[..., 1] - e) if above else (e - P[..., 1])
    return cut


def combine_cuts(*cs):
    def cut(P):
        return np.minimum.reduce([c(P) for c in cs])
    return cut


def surface_at(part, x, y, front=True, res=(160, 120)):
    """Point and normal of a part's surface closest to (x, y) on its front (+z) or back side."""
    u, v = np.meshgrid(np.linspace(0, 1, res[0]), np.linspace(0, 1, res[1]))
    P, N = part.eval(u, v)
    P = P.reshape(-1, 3); N = N.reshape(-1, 3)
    ok = (N[:, 2] > 0.2) if front else (N[:, 2] < -0.2)
    d = np.hypot(P[:, 0] - x, P[:, 1] - y) + (~ok) * 9
    i = np.argmin(d)
    return P[i], N[i]
