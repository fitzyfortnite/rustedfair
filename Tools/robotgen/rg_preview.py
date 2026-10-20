"""Headless three.js preview renders of a generated robot (mirrors Unity's left-handed x)."""
import base64, json, os, shutil, asyncio
import numpy as np
from rg_core import rot_euler

THREE = "/tmp/claude-0/three/t/build/three.module.js"


def b64(a, dt):
    return base64.b64encode(np.ascontiguousarray(a).astype(dt).tobytes()).decode()


def world_with_pose(robot, pose):
    cache = {}

    def W(b):
        if b is None:
            return np.eye(3), np.zeros(3)
        if b in cache:
            return cache[b]
        parent, pos, eul = robot.bones[b]
        Rp, tp = W(parent)
        R = Rp @ rot_euler(*eul)
        if b in pose:
            R = R @ rot_euler(*pose[b])
        cache[b] = (R, tp + Rp @ pos)
        return cache[b]
    return W


def render(robot, meshes, tex_prefix, out_png, views, pose=None, eyes=(), size=(900, 1200), mode="game", root=(0, 0, 0), yaw=0.0):
    d = "/tmp/claude-0/rgprev"
    os.makedirs(d, exist_ok=True)
    shutil.copy(THREE, d + "/three.module.js")
    for suf in ("", "_n", "_ms"):
        shutil.copy(tex_prefix + suf + ".png", f"{d}/tex{suf}.png")
    W = world_with_pose(robot, pose or {})
    Ry = rot_euler(0, yaw, 0)
    objs = []
    for b, (P, N, UV, subs) in meshes.items():
        R, t = W(b)
        R = Ry @ R; t = Ry @ t + np.asarray(root)
        Pw = P @ R.T + t
        Nw = N @ R.T
        Pw[:, 0] *= -1; Nw[:, 0] *= -1
        T = np.concatenate([subs[0], subs[1]])
        objs.append(dict(p=b64(Pw, "<f4"), n=b64(Nw, "<f4"), uv=b64(UV, "<f4"), i=b64(T[:, [0, 2, 1]], "<u4"), no=len(subs[0]) * 3, nc=len(subs[1]) * 3))
    ey = []
    for (pt, r) in eyes:
        bone, lp = pt
        R, t = W(bone)
        R = Ry @ R; t = Ry @ t + np.asarray(root)
        w = R @ np.asarray(lp) + t
        ey.append([-w[0], w[1], w[2], r])
    data = dict(objs=objs, eyes=ey, views=views, mode=mode, w=size[0], h=size[1])
    with open(d + "/data.js", "w") as f:
        f.write("window.DATA=" + json.dumps(data) + ";")
    html = r"""<!doctype html><html><body style="margin:0;background:#000">
<script src="data.js"></script>
<script type="module">
import * as THREE from './three.module.js';
const D = window.DATA;
const r = new THREE.WebGLRenderer({antialias:true, preserveDrawingBuffer:true});
r.setSize(D.w, D.h); r.shadowMap.enabled = true; r.shadowMap.type = THREE.PCFSoftShadowMap;
r.toneMapping = THREE.NeutralToneMapping; r.toneMappingExposure = 1.0;
r.outputColorSpace = THREE.SRGBColorSpace;
document.body.appendChild(r.domElement);
const L = new THREE.TextureLoader();
const load = (u, srgb) => new Promise(res => L.load(u, t => { if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.flipY = true; t.anisotropy = 8; res(t); }));
const [alb, nor, ms] = await Promise.all([load('tex.png', true), load('tex_n.png', false), load('tex_ms.png', false)]);
const dec = (s, T) => { const b = atob(s); const u = new Uint8Array(b.length); for (let i=0;i<b.length;i++) u[i]=b.charCodeAt(i); return new T(u.buffer); };
// metallic in R, smoothness in A -> three wants roughness in G, metalness in B
const c = document.createElement('canvas'); const img = ms.image; c.width = img.width; c.height = img.height;
const g = c.getContext('2d'); g.drawImage(img, 0, 0); const id = g.getImageData(0,0,c.width,c.height);
for (let i=0;i<id.data.length;i+=4){ const m=id.data[i], s=id.data[i+3]; id.data[i]=255; id.data[i+1]=255-s; id.data[i+2]=m; id.data[i+3]=255; }
g.putImageData(id,0,0); const rm = new THREE.CanvasTexture(c); rm.flipY = true;
const mat = new THREE.MeshStandardMaterial({map: alb, normalMap: nor, roughnessMap: rm, metalnessMap: rm, roughness: 1, metalness: 1});
const matC = new THREE.MeshStandardMaterial({map: alb, normalMap: nor, roughnessMap: rm, metalnessMap: rm, roughness: 1, metalness: 1, alphaTest: 0.5});
const scene = new THREE.Scene();
for (const o of D.objs) {
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(dec(o.p, Float32Array), 3));
  geo.setAttribute('normal', new THREE.BufferAttribute(dec(o.n, Float32Array), 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(dec(o.uv, Float32Array), 2));
  geo.setIndex(new THREE.BufferAttribute(dec(o.i, Uint32Array), 1));
  geo.addGroup(0, o.no, 0); geo.addGroup(o.no, o.nc, 1);
  const m = new THREE.Mesh(geo, [mat, matC]); m.castShadow = true; m.receiveShadow = true; scene.add(m);
}
const eyeMat = new THREE.MeshBasicMaterial({color: new THREE.Color(4, 0.25, 0.1)});
for (const e of D.eyes) {
  const s = new THREE.Mesh(new THREE.SphereGeometry(e[3], 16, 12), eyeMat); s.position.set(e[0], e[1], e[2]); scene.add(s);
  if (D.mode === 'game') { const pl = new THREE.PointLight(0xff3010, 0.6, 1.6, 2); pl.position.set(e[0], e[1], e[2] + 0.05); scene.add(pl); }
}
const floor = new THREE.Mesh(new THREE.PlaneGeometry(20, 20), new THREE.MeshStandardMaterial({color: 0x2a2826, roughness: 0.55}));
floor.rotation.x = -Math.PI/2; floor.receiveShadow = true; scene.add(floor);
const wall = new THREE.Mesh(new THREE.PlaneGeometry(20, 8), new THREE.MeshStandardMaterial({color: 0x3a3634, roughness: 0.9}));
wall.position.set(0, 4, -1.6); wall.receiveShadow = true; scene.add(wall);
const cam = new THREE.PerspectiveCamera(50, D.w / D.h, 0.05, 50);
let lights = [];
function setup(v) {
  for (const l of lights) scene.remove(l); lights = [];
  cam.fov = v.fov || 50; cam.position.set(...v.pos); cam.lookAt(...v.at); cam.updateProjectionMatrix();
  if (D.mode === 'game') {
    scene.background = new THREE.Color(0x010102);
    const amb = new THREE.HemisphereLight(0x1a2030, 0x080604, 0.35); lights.push(amb);
    const sp = new THREE.SpotLight(0xfff1dc, 9, 25, 0.42, 0.55, 2);
    sp.position.set(v.pos[0] + 0.25, v.pos[1] - 0.15, v.pos[2]); sp.target.position.set(...v.at);
    sp.castShadow = true; sp.shadow.mapSize.set(2048, 2048); sp.shadow.bias = -0.0004; sp.shadow.normalBias = 0.02;
    lights.push(sp, sp.target);
  } else {
    scene.background = new THREE.Color(0x2b2d31);
    lights.push(new THREE.HemisphereLight(0xdfe6ff, 0x3a3028, 1.4));
    const k = new THREE.DirectionalLight(0xffffff, 2.2); k.position.set(2, 4, 3); k.castShadow = true;
    k.shadow.mapSize.set(2048, 2048); k.shadow.camera.left = -3; k.shadow.camera.right = 3; k.shadow.camera.top = 4; k.shadow.camera.bottom = -1; k.shadow.bias = -0.0005;
    lights.push(k);
    const rim = new THREE.DirectionalLight(0x99aaff, 1.2); rim.position.set(-3, 2, -3); lights.push(rim);
  }
  for (const l of lights) scene.add(l);
}
window.shots = [];
for (const v of D.views) { setup(v); r.render(scene, cam); window.shots.push(r.domElement.toDataURL('image/png')); }
window.done = true;
</script></body></html>"""
    with open(d + "/index.html", "w") as f:
        f.write(html)
    asyncio.run(_shoot(d, out_png, len(views)))


async def _shoot(d, out_png, n):
    from playwright.async_api import async_playwright
    import http.server, threading, functools, socketserver
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=d)
    http.server.SimpleHTTPRequestHandler.log_message = lambda *a, **k: None

    class S(socketserver.TCPServer):
        allow_reuse_address = True
    srv = S(("127.0.0.1", 0), handler)
    port = srv.server_address[1]
    th = threading.Thread(target=srv.serve_forever, daemon=True); th.start()
    async with async_playwright() as p:
        b = await p.chromium.launch(args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"])
        pg = await b.new_page()
        logs = []
        pg.on("console", lambda m: logs.append(m.text))
        pg.on("pageerror", lambda e: logs.append("ERR " + str(e)))
        await pg.goto(f"http://127.0.0.1:{port}/index.html")
        try:
            await pg.wait_for_function("window.done === true", timeout=240000)
        except Exception:
            print("\n".join(logs)); raise
        shots = await pg.evaluate("window.shots")
        await b.close()
    srv.shutdown()
    import cv2
    ims = []
    for s in shots:
        a = np.frombuffer(base64.b64decode(s.split(",")[1]), np.uint8)
        ims.append(cv2.imdecode(a, cv2.IMREAD_COLOR))
    cv2.imwrite(out_png, np.hstack(ims))
    if logs:
        print("\n".join(l for l in logs if "ERR" in l or "rror" in l))
