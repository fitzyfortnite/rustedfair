"""Procedural surface materials baked into the robot atlases (albedo sRGB, metallic, smoothness, detail height in m)."""
import numpy as np
from rg_core import fbm, vnoise, worley, triplanar, luma, smoothstep, lerp, nrm


class Ctx:
    def __init__(self, **kw):
        self.__dict__.update(kw)


def C(rgb):
    return np.asarray(rgb, float)


def tint_tex(c, name, scale, color, mean=None, strength=1.0):
    t = triplanar(name, c.P, c.N, scale)
    m = t.reshape(-1, 3).mean(0) if mean is None else np.asarray(mean)
    rel = t / np.maximum(m, 1e-3)
    rel = 1 + (rel - 1) * strength
    return np.clip(C(color) * rel, 0, 1), t


def chips(c, amount=1.0, scale=38.0, seed=5):
    """Paint chipping on sharp convex edges and random spots: 0..1 mask."""
    e = smoothstep(45.0, 160.0, c.curv)
    n = fbm(c.P * scale, 4, seed=seed)
    m = smoothstep(0.62, 0.70, n + e * 0.28) * amount
    return np.clip(m, 0, 1)


# ----------------------------------------------------------------------------- materials
def m_plastic(c):
    mp = c.mp
    col = mp.get("color", (0.80, 0.74, 0.58))
    alb, t = tint_tex(c, mp.get("tex", "plastic_cracked"), mp.get("scale", 0.55), col, strength=mp.get("tex_str", 0.9))
    L = luma(t)
    crack = smoothstep(0.42, 0.18, L)  # dark crack lines of the texture
    # yellowing / discolouration
    y = fbm(c.P * 3.0, 3, seed=11)
    alb = lerp(alb, alb * C((0.92, 0.82, 0.6)), smoothstep(0.45, 0.75, y)[..., None] * mp.get("yellow", 0.7))
    ch = chips(c, mp.get("chips", 0.8))
    alb = lerp(alb, C(mp.get("under", (0.30, 0.29, 0.27))), ch[..., None])
    h = -crack * 0.0007 - ch * 0.00035 + (fbm(c.P * 160, 2, seed=2) - 0.5) * 0.00012
    sm = np.full(L.shape, mp.get("smooth", 0.55)) - crack * 0.3 - ch * 0.25
    return dict(albedo=alb, metal=np.zeros_like(L), smooth=sm, height=h)


def m_paint(c):
    mp = c.mp
    col = C(mp.get("color", (0.5, 0.05, 0.04)))
    n = fbm(c.P * 9, 3, seed=21)
    alb = col * (0.85 + 0.3 * n)[..., None]
    ch = chips(c, mp.get("chips", 0.9), seed=8)
    alb = lerp(alb, C(mp.get("under", (0.72, 0.68, 0.58))), ch[..., None])
    craz = smoothstep(0.08, 0.02, worley(c.P * 55, seed=4)[1] - worley(c.P * 55, seed=4)[0])  # paint crazing
    alb = alb * (1 - craz[..., None] * 0.35 * mp.get("craze", 1.0))
    h = -ch * 0.0003 - craz * 0.00015 * mp.get("craze", 1.0)
    sm = np.full(n.shape, mp.get("smooth", 0.6)) - ch * 0.3 - craz * 0.2
    return dict(albedo=alb, metal=np.zeros_like(n), smooth=sm, height=h)


def m_fur(c):
    mp = c.mp
    col = mp.get("color", (0.36, 0.25, 0.16))
    alb, t = tint_tex(c, "fur_dirty", mp.get("scale", 0.28), col, strength=0.8)
    L = luma(t)
    f1, f2 = worley(c.P * 22, seed=31)
    clump = smoothstep(0.0, 0.5, f2 - f1)  # matted clumps
    bald = smoothstep(0.66, 0.74, fbm(c.P * 4.5, 4, seed=33)) * mp.get("bald", 0.6)  # worn-through patches
    alb = alb * (0.86 + 0.2 * clump)[..., None]
    alb = lerp(alb, C(mp.get("skin", (0.22, 0.17, 0.13))), bald[..., None])
    h = (L - L.mean()) * 0.0025 + clump * 0.0012 - bald * 0.001
    sm = 0.06 + bald * 0.18
    return dict(albedo=alb, metal=np.zeros_like(L), smooth=np.full(L.shape, 1.0) * sm, height=h)


def m_fabric(c):
    mp = c.mp
    col = mp.get("color", (0.28, 0.12, 0.36))
    alb, t = tint_tex(c, "fabric_purple", mp.get("scale", 0.35), col, strength=1.0)
    L = luma(t)
    st = smoothstep(0.55, 0.75, fbm(c.P * 5, 4, seed=41))  # stains
    alb = lerp(alb, alb * C((0.55, 0.5, 0.42)), st[..., None] * 0.7)
    fade = smoothstep(0.3, 0.9, c.N[..., 1]) * 0.35  # sun-faded shoulders
    alb = lerp(alb, alb * 1.35 + 0.03, fade[..., None])
    h = (L - L.mean()) * 0.0015
    return dict(albedo=np.clip(alb, 0, 1), metal=np.zeros_like(L), smooth=np.full(L.shape, mp.get("smooth", 0.12)), height=h)


def m_metal(c):
    mp = c.mp
    steel = C(mp.get("color", (0.50, 0.49, 0.47)))
    t = triplanar("metal_rust", c.P, c.N, mp.get("scale", 0.4))
    rustTex = smoothstep(0.08, 0.25, t[..., 0] - t[..., 1])  # orange areas of the texture
    n = fbm(c.P * 12, 4, seed=51)
    rust = np.clip(rustTex * mp.get("rust", 0.8) + smoothstep(0.6, 0.8, n) * 0.5 * mp.get("rust", 0.8), 0, 1)
    scr = smoothstep(0.75, 0.9, fbm(c.P * C((300, 8, 300)) + 3.1, 2, seed=52))  # fine scratches along y
    base = steel * (0.8 + 0.35 * fbm(c.P * 30, 3, seed=53))[..., None]
    base = lerp(base, C((0.72, 0.71, 0.68)), scr[..., None] * 0.5)
    rcol = t * C((1.0, 0.8, 0.7)) * 0.85
    rcol = lerp(rcol, C((0.33, 0.15, 0.07)), 0.4)
    alb = lerp(base, rcol, rust[..., None])
    metal = lerp(np.full(n.shape, mp.get("metal", 0.9)), 0.1, rust)
    sm = lerp(np.full(n.shape, mp.get("smooth", 0.55)), 0.18, rust) + scr * 0.1
    h = rust * 0.0005 * (vnoise(c.P * 400, 3) - 0.3) - scr * 0.00008
    return dict(albedo=alb, metal=metal, smooth=sm, height=h)


def m_shiny(c):
    """chrome / gold / brass."""
    mp = c.mp
    col = C(mp.get("color", (0.76, 0.76, 0.77)))
    n = fbm(c.P * 14, 4, seed=61)
    tarn = smoothstep(0.5, 0.8, n) * mp.get("tarnish", 0.7)
    alb = lerp(col, col * C((0.45, 0.4, 0.3)), tarn[..., None])
    return dict(albedo=alb, metal=np.full(n.shape, 1.0), smooth=0.78 - tarn * 0.45, height=(n - 0.5) * 0.00015)


def m_flat(c):
    """black matte / rubber / socket / inner / felt / anything uniform."""
    mp = c.mp
    col = C(mp.get("color", (0.035, 0.035, 0.035)))
    n = fbm(c.P * mp.get("nscale", 40), 3, seed=mp.get("seed", 71))
    alb = col * (0.8 + 0.4 * n)[..., None]
    dust = smoothstep(0.4, 0.9, c.N[..., 1]) * mp.get("dust", 0.0)
    alb = lerp(alb, C((0.3, 0.28, 0.25)), (dust * smoothstep(0.35, 0.7, fbm(c.P * 20, 3, seed=72)))[..., None])
    h = (n - 0.5) * mp.get("bump", 0.0003)
    return dict(albedo=alb, metal=np.full(n.shape, mp.get("metal", 0.0)), smooth=np.full(n.shape, mp.get("smooth", 0.2)) - dust * 0.1, height=h)


def m_teeth(c):
    mp = c.mp
    n = fbm(c.P * 40, 3, seed=81)
    base = C(mp.get("color", (0.86, 0.80, 0.62)))
    alb = base * (0.75 + 0.35 * n)[..., None]
    stain = smoothstep(0.55, 0.75, fbm(c.P * 25, 3, seed=82))
    alb = lerp(alb, C((0.45, 0.33, 0.16)), stain[..., None] * 0.6)
    return dict(albedo=alb, metal=np.zeros_like(n), smooth=0.6 - stain * 0.3, height=(n - 0.5) * 0.0002)


def m_cloth(c):
    """white glove / shirt cloth."""
    mp = c.mp
    col = C(mp.get("color", (0.78, 0.76, 0.70)))
    t = triplanar("fabric_purple", c.P, c.N, 0.25)
    L = luma(t)
    alb = col * (0.8 + 0.6 * (L - L.mean()) / max(L.std(), 1e-3) * 0.12)[..., None]
    st = smoothstep(0.5, 0.75, fbm(c.P * 7, 4, seed=91))
    alb = lerp(alb, C((0.42, 0.36, 0.26)), st[..., None] * 0.65)
    return dict(albedo=np.clip(alb, 0, 1), metal=np.zeros_like(L), smooth=np.full(L.shape, 0.1), height=(L - L.mean()) * 0.001)


MATS = dict(plastic=m_plastic, paint=m_paint, fur=m_fur, fabric=m_fabric, metal=m_metal, shiny=m_shiny,
            flat=m_flat, teeth=m_teeth, cloth=m_cloth)


# ----------------------------------------------------------------------------- shared grime pass
def grime(c, r):
    mp = c.mp
    alb, sm = r["albedo"], np.broadcast_to(r["smooth"], c.ao.shape).astype(float)
    ao = c.ao
    occ = mp.get("ao", 1.0)
    alb = alb * lerp(1.0, lerp(0.16, 1.0, ao ** 1.3), occ)[..., None]
    sm = sm * lerp(0.45, 1.0, ao)
    # dirt in crevices + blotches
    d = np.clip((1 - ao) * 1.3 + (fbm(c.P * 5.5, 4, seed=101) - 0.5) * 0.9 - 0.25, 0, 1) * mp.get("dirt", 1.0)
    dcol = C((0.14, 0.10, 0.07)) * (0.6 + 0.6 * luma(alb))[..., None]
    alb = lerp(alb, dcol, (d * 0.55)[..., None])
    sm = sm * (1 - d * 0.5)
    # vertical drips / grime streaks
    s = fbm(c.P * C((46, 2.2, 46)), 3, seed=102)
    side = 1 - np.abs(c.N[..., 1])
    drip = smoothstep(0.60, 0.72, s) * side * mp.get("drip", 1.0)
    alb = lerp(alb, alb * C((0.42, 0.36, 0.30)), (drip * 0.65)[..., None])
    h = r["height"] - drip * 0.00008
    # blood
    for (pt, rad, amt) in c.robot.points.get("blood", []):
        dist = np.linalg.norm(c.P - np.asarray(pt), axis=-1)
        nz = fbm(c.P * 18, 4, seed=103) - 0.5
        m = smoothstep(rad, rad * 0.25, dist + nz * rad * 0.9) * amt * mp.get("blood", 1.0)
        dr = smoothstep(0.55, 0.65, fbm(c.P * C((60, 5, 60)), 3, seed=104)) * smoothstep(rad * 2.2, rad * 0.5, dist) * amt * 0.7 * side
        m = np.clip(m + dr * mp.get("blood", 1.0), 0, 1)
        alb = lerp(alb, C((0.13, 0.02, 0.012)) * (0.6 + 0.5 * (nz + 0.5))[..., None], (m * 0.85)[..., None])
        sm = lerp(sm, 0.5, m * 0.7)
        h = h + m * 0.00025
    # torn edges of holes: frayed / broken rim
    if np.any(c.rim > 0):
        rc = C(mp.get("rim_col", (0.62, 0.58, 0.5)))
        alb = lerp(alb, rc * (0.7 + 0.5 * luma(alb))[..., None], (c.rim * mp.get("rim", 0.5))[..., None])
    r["albedo"] = np.clip(alb, 0, 1)
    r["smooth"] = np.clip(sm, 0, 1)
    r["metal"] = np.clip(np.broadcast_to(r["metal"], c.ao.shape), 0, 1)
    r["height"] = h
    return r


def shade(mat, c):
    r = MATS[mat](c)
    # painted / material zones: mp["zones"] = [(mask(Pl, N_local) -> 0..1, mat, mp), ...]
    for (mask, zm, zmp) in c.mp.get("zones", []):
        w = np.clip(mask(c.Pl, c.Nl), 0, 1)
        if not np.any(w > 0):
            continue
        c2 = Ctx(**c.__dict__); c2.mp = dict(zmp)
        z = MATS[zm](c2)
        for k in ("albedo", "metal", "smooth", "height"):
            a = np.broadcast_to(r[k], w.shape + ((3,) if k == "albedo" else ())).astype(float)
            b = np.broadcast_to(z[k], a.shape).astype(float)
            ww = w[..., None] if k == "albedo" else w
            r[k] = a + (b - a) * ww
    if c.part.inner:
        r["albedo"] = r["albedo"] * 0.35
        r["height"] = r["height"] * 0.5
    return grime(c, r)
