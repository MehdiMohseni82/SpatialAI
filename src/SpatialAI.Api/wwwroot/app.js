import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { TransformControls } from 'three/addons/controls/TransformControls.js';

// ── Renderer / scene / camera ──────────────────────────────────────────────
const app = document.getElementById('app');
// preserveDrawingBuffer lets us read the canvas back (toDataURL) for the vision gate's screenshots.
const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(window.devicePixelRatio);
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.shadowMap.enabled = true;
app.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x16181d);

const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 0.1, 200);
camera.position.set(7, 7, 9);

const orbit = new OrbitControls(camera, renderer.domElement);
orbit.enableDamping = true;
orbit.target.set(0, 0.5, 0);

// Lights — warm sun + soft sky/ground fill for an architectural feel.
scene.add(new THREE.HemisphereLight(0xcfe0ff, 0x33372e, 0.95));
const sun = new THREE.DirectionalLight(0xfff1dd, 2.0);
sun.position.set(9, 15, 7);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
sun.shadow.camera.left = -24; sun.shadow.camera.right = 24;
sun.shadow.camera.top = 24; sun.shadow.camera.bottom = -24;
sun.shadow.radius = 3; sun.shadow.bias = -0.0004;
scene.add(sun);
scene.add(new THREE.DirectionalLight(0x88a0c0, 0.35)); // cool fill from the opposite side

// Ground plane (terrain) + a faint grid on top for orientation.
const ground = new THREE.Mesh(
  new THREE.PlaneGeometry(120, 120),
  new THREE.MeshStandardMaterial({ color: 0x2b3528, roughness: 1.0 })
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = -0.02;
ground.receiveShadow = true;
scene.add(ground);
const grid = new THREE.GridHelper(60, 60, 0x39435a, 0x222a36);
grid.position.y = 0;
grid.material.opacity = 0.35; grid.material.transparent = true;
scene.add(grid);

// ── Transform gizmo ────────────────────────────────────────────────────────
const transform = new TransformControls(camera, renderer.domElement);
transform.setMode('translate');
transform.showY = false; // keep items on the floor while translating
scene.add(transform.getHelper());

let justDragged = false;
transform.addEventListener('dragging-changed', (e) => {
  orbit.enabled = !e.value;
  if (e.value) { justDragged = true; }
  else { commitTransform(); }
});

// ── Scene model <-> meshes ─────────────────────────────────────────────────
const sceneGroup = new THREE.Group();
scene.add(sceneGroup);
const itemMeshes = new Map(); // item id -> mesh
const roomMeshes = new Map(); // room id -> Object3D
const highlightsGroup = new THREE.Group();
sceneGroup.add(highlightsGroup);
let buildingRoofMesh = null;  // building-wide roof (Object3D or null)
let selectedId = null;
let dragging = false;
transform.addEventListener('dragging-changed', (e) => { dragging = e.value; });

const col = (c) => new THREE.Color().setRGB(c?.r ?? 0.8, c?.g ?? 0.8, c?.b ?? 0.8);

// Unit primitives (size 1) — scaled per part so non-uniform sizes work.
function unitGeometry(shape) {
  switch ((shape || 'Box').toLowerCase()) {
    case 'cylinder': return new THREE.CylinderGeometry(0.5, 0.5, 1, 24);
    case 'sphere': return new THREE.SphereGeometry(0.5, 20, 14);
    default: return new THREE.BoxGeometry(1, 1, 1);
  }
}

// An item is a group of primitive parts (offsets relative to the item center).
function buildItemMesh(item) {
  const group = new THREE.Group();
  const parts = (item.parts && item.parts.length)
    ? item.parts
    : [{ shape: item.shape, offset: { x: 0, y: 0, z: 0 }, size: item.size, rotationY: 0, color: item.color }];

  for (const part of parts) {
    const mat = new THREE.MeshStandardMaterial({ color: col(part.color), roughness: 0.72, metalness: 0.04 });
    const m = new THREE.Mesh(unitGeometry(part.shape), mat);
    m.castShadow = true;
    m.receiveShadow = true;
    m.position.set(part.offset.x, part.offset.y, part.offset.z);
    m.scale.set(part.size.x, part.size.y, part.size.z);
    m.rotation.y = THREE.MathUtils.degToRad(part.rotationY || 0);
    m.userData = { id: item.id };           // so a raycast on any part resolves to the item
    group.add(m);
  }

  group.position.set(item.position.x, item.position.y, item.position.z);
  group.rotation.y = THREE.MathUtils.degToRad(item.rotationY || 0);
  group.userData = { id: item.id, name: item.name, size: { ...item.size }, level: item.level || 0, baseY: item.position.y };
  return group;
}

// Two wall looks the user can toggle: a clean translucent "glass" massing model, and an opaque "solid" house.
const wallGlass = new THREE.MeshStandardMaterial({ color: 0xb9c4dc, transparent: true, opacity: 0.18, roughness: 0.6, side: THREE.DoubleSide });
const wallSolid = new THREE.MeshStandardMaterial({ color: 0xe8e2d6, roughness: 0.85, side: THREE.DoubleSide });
let renderStyle = 'glass';
const wallMat = () => (renderStyle === 'solid' ? wallSolid : wallGlass);
// The roof follows the same toggle: translucent in "glass" so you can see the rooms UNDER it (essential
// for attics/imported buildings where an opaque roof would hide everything), opaque in "solid".
const roofSolid = new THREE.MeshStandardMaterial({ color: 0x6f4636, roughness: 0.9, metalness: 0.0, side: THREE.DoubleSide });
const roofGlass = new THREE.MeshStandardMaterial({ color: 0x9a6a52, roughness: 0.4, metalness: 0.0, side: THREE.DoubleSide, transparent: true, opacity: 0.30 });

const ROOM = {
  t: 0.08,
  frame: new THREE.MeshStandardMaterial({ color: 0xd6d2cb, roughness: 0.6 }),
  glass: new THREE.MeshStandardMaterial({ color: 0x8ec5ff, transparent: true, opacity: 0.22, roughness: 0.1, metalness: 0.0, side: THREE.DoubleSide }),
  slab: new THREE.MeshStandardMaterial({ color: 0xc7c2b4, roughness: 0.9, side: THREE.DoubleSide }),
  get roof() { return renderStyle === 'solid' ? roofSolid : roofGlass; },
  rail: new THREE.MeshStandardMaterial({ color: 0xf97316, emissive: 0x331a06 })
};

// A wall running along an axis, segmented around its openings, with frames + glass.
// side: { axis:'x'|'z', len, wx, wz }   openings: [{ type, offset, width, height, sill }]
function buildWall(group, side, height, openings) {
  const t = ROOM.t;
  // Place a box aligned to the wall: `along` is the position along the wall axis from its center.
  const addBox = (along, y, lenAlong, h, thick, mat) => {
    const geom = side.axis === 'x'
      ? new THREE.BoxGeometry(lenAlong, h, thick)
      : new THREE.BoxGeometry(thick, h, lenAlong);
    const m = new THREE.Mesh(geom, mat);
    m.position.set(
      side.axis === 'x' ? side.wx + along : side.wx,
      y,
      side.axis === 'x' ? side.wz : side.wz + along);
    m.receiveShadow = true;
    group.add(m);
    return m;
  };

  const half = side.len / 2;
  const ops = [...openings]
    .map(o => ({
      type: (o.type || 'window').toLowerCase(),
      off: o.offset || 0,
      w: o.width || (o.type === 'door' ? 0.9 : 1.2),
      h: o.height || (o.type === 'door' ? 2.1 : 1.2),
      sill: (o.type === 'door') ? 0 : (o.sill ?? 0.9),
    }))
    .sort((a, b) => a.off - b.off);

  // Orange top rail (keeps the room outline accent).
  addBox(0, height - 0.015, side.len, 0.03, t * 1.1, ROOM.rail);

  if (ops.length === 0) { addBox(0, height / 2, side.len, height, t, wallMat()); return; }

  let cursor = -half;
  const fb = 0.06; // frame bar thickness
  for (const o of ops) {
    const gapL = o.off - o.w / 2, gapR = o.off + o.w / 2;
    if (gapL - cursor > 0.01) addBox((cursor + gapL) / 2, height / 2, gapL - cursor, height, t, wallMat());
    cursor = gapR;

    const top = o.sill + o.h;
    if (height - top > 0.01) addBox(o.off, (top + height) / 2, o.w, height - top, t, wallMat());   // above
    if (o.sill > 0.01) addBox(o.off, o.sill / 2, o.w, o.sill, t, wallMat());                        // below (window)

    // frame
    const ft = t * 1.4;
    addBox(o.off - o.w / 2 + fb / 2, o.sill + o.h / 2, fb, o.h, ft, ROOM.frame);
    addBox(o.off + o.w / 2 - fb / 2, o.sill + o.h / 2, fb, o.h, ft, ROOM.frame);
    addBox(o.off, o.sill + o.h - fb / 2, o.w, fb, ft, ROOM.frame);
    addBox(o.off, o.sill + fb / 2, o.w, fb, ft, ROOM.frame);
    if (o.type !== 'door') addBox(o.off, o.sill + o.h / 2, o.w - 2 * fb, o.h - 2 * fb, t * 0.4, ROOM.glass);
  }
  if (half - cursor > 0.01) addBox((cursor + half) / 2, height / 2, half - cursor, height, t, wallMat());
}

function buildRoom(room) {
  const g = new THREE.Group();
  const { center, width, depth, height } = room;
  const openings = room.openings || [];

  // Floor
  const floor = new THREE.Mesh(
    new THREE.BoxGeometry(width, 0.06, depth),
    new THREE.MeshStandardMaterial({ color: col(room.floorColor), roughness: 0.95 })
  );
  floor.position.set(center.x, -0.03, center.z);
  floor.receiveShadow = true;
  g.add(floor);

  // An attic storey lives INSIDE the building roof — only a short knee wall, no full masonry walls.
  const wallH = room.inRoof ? 0.5 : height;

  const sides = [
    { side: 'north', axis: 'x', len: width, wx: center.x, wz: center.z + depth / 2 },
    { side: 'south', axis: 'x', len: width, wx: center.x, wz: center.z - depth / 2 },
    { side: 'east', axis: 'z', len: depth, wx: center.x + width / 2, wz: center.z },
    { side: 'west', axis: 'z', len: depth, wx: center.x - width / 2, wz: center.z },
  ];
  for (const s of sides) {
    buildWall(g, s, wallH, room.inRoof ? [] : openings.filter(o => (o.wall || '').toLowerCase() === s.side));
  }

  // Interior partitions (full height except in the attic, where the roof is the enclosure)
  if (!room.inRoof) for (const p of room.partitions || []) {
    const door = p.doorWidth > 0 ? [{ type: 'door', offset: 0, width: p.doorWidth, height: Math.min(2.1, height - 0.1), sill: 0 }] : [];
    const side = p.axis === 'z'
      ? { axis: 'z', len: depth, wx: p.position, wz: center.z }
      : { axis: 'x', len: width, wx: center.x, wz: p.position };
    buildWall(g, side, height, door);
  }

  // Ceiling
  if (room.ceiling && !room.inRoof) {
    const c = new THREE.Mesh(new THREE.BoxGeometry(width, 0.06, depth), ROOM.slab);
    c.position.set(center.x, height + 0.03, center.z);
    g.add(c);
  }

  // Roof
  if (room.roof === 'flat') {
    const r = new THREE.Mesh(new THREE.BoxGeometry(width + 0.4, 0.12, depth + 0.4), ROOM.roof);
    r.position.set(center.x, height + 0.12, center.z);
    g.add(r);
  } else if (room.roof === 'gable') {
    const rise = Math.max(0.8, width * 0.28);
    const slopeLen = Math.sqrt((width / 2) * (width / 2) + rise * rise);
    const ang = Math.atan2(rise, width / 2);
    const baseY = room.ceiling ? height + 0.06 : height;
    for (const dir of [-1, 1]) {
      // Each panel runs from the eave (x = ±width/2, y = baseY) up to the ridge (x = 0, y = baseY+rise).
      const panel = new THREE.Mesh(new THREE.BoxGeometry(slopeLen + 0.2, 0.1, depth + 0.4), ROOM.roof);
      panel.position.set(center.x + dir * width / 4, baseY + rise / 2, center.z);
      panel.rotation.z = -dir * ang;
      g.add(panel);
    }
  }
  // Lift the whole storey to its floor elevation (everything above was built relative to Y=0).
  g.position.y = room.elevation || 0;
  g.userData = { level: room.level || 0, elevation: room.elevation || 0 };
  return g;
}

// A clean, solid building-wide roof seated on the top storey. Built from mitered trapezoidal faces
// (shared corner vertices) so there are no overlapping slabs or gaps.
function buildBuildingRoof(roof) {
  const g = new THREE.Group();
  const ov = 0.35; // eave overhang
  const x0 = roof.minX - ov, z0 = roof.minZ - ov, x1 = roof.maxX + ov, z1 = roof.maxZ + ov;
  const w = Math.max(0.5, x1 - x0), d = Math.max(0.5, z1 - z0);
  const baseY = roof.baseY || 0;
  // When the roof height comes from the elevation's exact ▽ markers (eave→ridge), trust it; otherwise clamp.
  const exact = roof.breakY && roof.breakY > baseY;
  const rise = exact ? Math.max(0.8, roof.height) : Math.min(Math.max(0.8, roof.height || 2.5), Math.min(w, d) * 0.55);
  const style = (roof.style || 'gable').toLowerCase();

  if (style === 'flat') {
    const slab = new THREE.Mesh(new THREE.BoxGeometry(w, 0.18, d), ROOM.roof);
    slab.position.set((x0 + x1) / 2, baseY + 0.09, (z0 + z1) / 2);
    slab.castShadow = slab.receiveShadow = true;
    g.add(slab);
    return g;
  }

  if (style === 'mansard') {
    // Steep lower skirt (eave→break) then a shallow hipped cap (break→ridge). Use the exact break height from
    // the elevation when present, else split the rise.
    const lowerRise = exact ? Math.max(0.4, roof.breakY - baseY) : rise * 0.62;
    const upperRise = Math.max(0.3, rise - lowerRise);
    const inX = w * 0.16, inZ = d * 0.16;
    const mx0 = x0 + inX, mz0 = z0 + inZ, mx1 = x1 - inX, mz1 = z1 - inZ;
    g.add(roofBand(x0, z0, x1, z1, mx0, mz0, mx1, mz1, baseY, baseY + lowerRise));        // steep skirt
    addHipTop(g, mx0, mz0, mx1, mz1, baseY + lowerRise, upperRise);                       // shallow cap
    g.add(eaveBoard(x0, z0, x1, z1, baseY));
    addDormers(g, x0, z0, x1, z1, baseY, lowerRise, roof.dormers);                        // windowed dormers
    return g;
  }

  if (style === 'hip') {
    addHipTop(g, x0, z0, x1, z1, baseY, rise);
    g.add(eaveBoard(x0, z0, x1, z1, baseY));
    return g;
  }

  // gable: ridge along the longer axis; two slopes + two end gable triangles.
  const ridgeAlongZ = d >= w;
  const ridgeY = baseY + rise;
  let a, b; // outer rect collapsed to the ridge line
  if (ridgeAlongZ) { const xm = (x0 + x1) / 2; a = [xm, z0, xm, z1]; }
  else { const zm = (z0 + z1) / 2; a = [x0, zm, x1, zm]; }
  g.add(roofBand(x0, z0, x1, z1, a[0], a[1], a[2], a[3], baseY, ridgeY));
  // gable end triangles
  if (ridgeAlongZ) {
    g.add(tri([x0, baseY, z0], [x1, baseY, z0], [(x0 + x1) / 2, ridgeY, z0]));
    g.add(tri([x0, baseY, z1], [x1, baseY, z1], [(x0 + x1) / 2, ridgeY, z1]));
  } else {
    g.add(tri([x0, baseY, z0], [x0, baseY, z1], [x0, ridgeY, (z0 + z1) / 2]));
    g.add(tri([x1, baseY, z0], [x1, baseY, z1], [x1, ridgeY, (z0 + z1) / 2]));
  }
  g.add(eaveBoard(x0, z0, x1, z1, baseY));
  return g;
}

// Four trapezoidal faces between an outer rectangle (at yLow) and an inner rectangle (at yHigh).
// Inner corners may coincide (a ridge line / apex), giving hips and gables from the same primitive.
function roofBand(ox0, oz0, ox1, oz1, ix0, iz0, ix1, iz1, yLow, yHigh) {
  const O = [[ox0, yLow, oz0], [ox1, yLow, oz0], [ox1, yLow, oz1], [ox0, yLow, oz1]];
  const I = [[ix0, yHigh, iz0], [ix1, yHigh, iz0], [ix1, yHigh, iz1], [ix0, yHigh, iz1]];
  const pos = [];
  for (const [p, q] of [[0, 1], [1, 2], [2, 3], [3, 0]]) {
    pos.push(...O[p], ...O[q], ...I[q], ...O[p], ...I[q], ...I[p]);
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.computeVertexNormals();
  const m = new THREE.Mesh(geo, ROOM.roof);
  m.castShadow = m.receiveShadow = true;
  return m;
}

// A shallow hipped cap: four slopes from a rectangle up to a short ridge (inset ~30%), plus a small top fill.
function addHipTop(g, x0, z0, x1, z1, baseY, rise) {
  const rx = (x1 - x0) * 0.32, rz = (z1 - z0) * 0.32;
  const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
  const rx0 = cx - rx, rx1 = cx + rx, rz0 = cz - rz, rz1 = cz + rz;
  g.add(roofBand(x0, z0, x1, z1, rx0, rz0, rx1, rz1, baseY, baseY + rise));
  const top = new THREE.Mesh(new THREE.BoxGeometry(rx1 - rx0, 0.1, rz1 - rz0), ROOM.roof);
  top.position.set(cx, baseY + rise + 0.05, cz); top.castShadow = top.receiveShadow = true;
  g.add(top);
}

// A single flat triangle (gable end fill).
function tri(a, b, c) {
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute([...a, ...b, ...c], 3));
  geo.computeVertexNormals();
  const m = new THREE.Mesh(geo, ROOM.roof);
  m.castShadow = m.receiveShadow = true;
  return m;
}

// A thin fascia board around the eaves for a finished edge.
function eaveBoard(x0, z0, x1, z1, y) {
  const grp = new THREE.Group();
  const t = 0.12, h = 0.16;
  const mk = (sx, sz, px, pz) => { const m = new THREE.Mesh(new THREE.BoxGeometry(sx, h, sz), ROOM.frame); m.position.set(px, y - h / 2 + 0.02, pz); grp.add(m); };
  mk(x1 - x0 + t, t, (x0 + x1) / 2, z0); mk(x1 - x0 + t, t, (x0 + x1) / 2, z1);
  mk(t, z1 - z0 + t, x0, (z0 + z1) / 2); mk(t, z1 - z0 + t, x1, (z0 + z1) / 2);
  return grp;
}

// Windowed gabled dormers on the mansard's steep front (-Z) and back (+Z) faces.
function addDormers(g, x0, z0, x1, z1, baseY, lowerRise, count) {
  const w = x1 - x0;
  const n = Math.max(1, Math.min(6, count || Math.round(w / 3.5)));
  const yBase = baseY + lowerRise * 0.18; // sit low on the steep slope
  for (let i = 0; i < n; i++) {
    const cx = x0 + (i + 0.5) * (w / n);
    g.add(dormer(cx, yBase, z0 + 0.28, -1)); // front (faces -Z)
    g.add(dormer(cx, yBase, z1 - 0.28, +1)); // back  (faces +Z)
  }
}

// One gabled dormer: little box + pitched roof + a glass window, facing outward (faceDir = -1 for -Z).
function dormer(cx, yBase, zFace, faceDir) {
  const grp = new THREE.Group();
  const w = 1.1, h = 1.15, d = 0.85;
  const zBody = zFace - faceDir * d / 2;        // body sits inward from the face
  const yc = yBase + h / 2;
  const walls = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), ROOM.slab);
  walls.position.set(cx, yc, zBody); walls.castShadow = walls.receiveShadow = true;
  grp.add(walls);
  // little gable roof over the dormer
  const rise = 0.45, span = w + 0.18;
  const slope = Math.sqrt((span / 2) * (span / 2) + rise * rise);
  const ang = Math.atan2(rise, span / 2);
  for (const dir of [-1, 1]) {
    const p = new THREE.Mesh(new THREE.BoxGeometry(slope, 0.08, d + 0.18), ROOM.roof);
    p.position.set(cx + dir * span / 4, yBase + h + rise / 2, zBody);
    p.rotation.z = -dir * ang; p.castShadow = true;
    grp.add(p);
  }
  // window on the outward face
  const win = new THREE.Mesh(new THREE.BoxGeometry(w * 0.62, h * 0.6, 0.05), ROOM.glass);
  win.position.set(cx, yBase + h * 0.52, zFace + faceDir * 0.03);
  grp.add(win);
  const frame = new THREE.Mesh(new THREE.BoxGeometry(w * 0.7, h * 0.68, 0.06), ROOM.frame);
  frame.position.set(cx, yBase + h * 0.52, zFace + faceDir * 0.01);
  grp.add(frame);
  return grp;
}

function buildHighlight(h) {
  const group = new THREE.Group();
  const c = new THREE.Color(0xfb923c); // Ember orange — matches the deck
  const slab = new THREE.Mesh(
    new THREE.BoxGeometry(h.size.x, 0.04, h.size.z),
    new THREE.MeshBasicMaterial({ color: c, transparent: true, opacity: 0.5 })
  );
  slab.position.set(h.position.x, 0.06, h.position.z);
  group.add(slab);
  // Bright outline so the region reads clearly on a projector.
  const edges = new THREE.LineSegments(
    new THREE.EdgesGeometry(slab.geometry),
    new THREE.LineBasicMaterial({ color: c })
  );
  edges.position.copy(slab.position);
  group.add(edges);
  return group;
}

// Recursively free GPU resources so add/remove churn doesn't leak at scale.
function disposeObject(obj) {
  obj.traverse((o) => {
    if (o.geometry) o.geometry.dispose();
    if (o.material) {
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) m.dispose();
    }
  });
}

function removeMesh(map, id, group) {
  const mesh = map.get(id);
  if (!mesh) return;
  group.remove(mesh);
  disposeObject(mesh);
  map.delete(id);
}

function upsertItem(item) {
  // While dragging, don't clobber the item currently on the gizmo (server echo of our own transform).
  if (dragging && transform.object && transform.object.userData.id === item.id) return;
  removeMesh(itemMeshes, item.id, sceneGroup);
  const mesh = buildItemMesh(item);
  itemMeshes.set(item.id, mesh);
  sceneGroup.add(mesh);
  if (item.id === selectedId) transform.attach(mesh);
}

function removeItem(id) {
  if (selectedId === id) { transform.detach(); selectedId = null; }
  removeMesh(itemMeshes, id, sceneGroup);
}

function upsertRoom(room) {
  removeMesh(roomMeshes, room.id, sceneGroup);
  const mesh = buildRoom(room);
  roomMeshes.set(room.id, mesh);
  sceneGroup.add(mesh);
}

function removeRoom(id) {
  removeMesh(roomMeshes, id, sceneGroup);
}

function setHighlights(list) {
  for (const child of [...highlightsGroup.children]) { highlightsGroup.remove(child); disposeObject(child); }
  for (const h of list || []) highlightsGroup.add(buildHighlight(h));
}

// Replace the building-wide roof mesh (null = remove it).
function setBuildingRoof(roof) {
  if (buildingRoofMesh) { sceneGroup.remove(buildingRoofMesh); disposeObject(buildingRoofMesh); buildingRoofMesh = null; }
  if (roof) { buildingRoofMesh = buildBuildingRoof(roof); sceneGroup.add(buildingRoofMesh); }
}

// Full replace (initial baseline, reset, opening a space).
function applyFull(scene) {
  for (const id of [...itemMeshes.keys()]) removeMesh(itemMeshes, id, sceneGroup);
  for (const id of [...roomMeshes.keys()]) removeMesh(roomMeshes, id, sceneGroup);
  for (const room of scene.rooms || []) upsertRoom(room);
  for (const item of scene.items || []) upsertItem(item);
  setHighlights(scene.highlights);
  setBuildingRoof(scene.roof || null);
  if (selectedId && !itemMeshes.has(selectedId)) { transform.detach(); selectedId = null; }
  populateStoreys();
  applyView();
}

// Incremental update — only the changed entities.
function applyPatch(p) {
  const rooms = p.rooms || {}, items = p.items || {};
  for (const id of rooms.remove || []) removeRoom(id);
  for (const room of rooms.upsert || []) upsertRoom(room);
  for (const id of items.remove || []) removeItem(id);
  for (const item of items.upsert || []) upsertItem(item);
  setHighlights(p.highlights);
  setBuildingRoof(p.roof || null);
  // groups are non-visual metadata (organization / LLM context) — nothing to render.
  populateStoreys();   // refresh the storey list as rooms stream in via patches (e.g. an import)
  applyView();
}

// ── Building view: Glass/Solid style, storey switcher, dollhouse ──────────────
const STOREY_GAP = 1.8;
let viewStorey = 'all';   // 'all' or an integer level
let dollhouse = false;

const levelLabel = (l) => ({ '-2': 'Sub-basement', '-1': 'Basement', '0': 'Ground', '1': '1st floor', '2': '2nd floor', '3': '3rd floor' }[String(l)] || `Level ${l}`);
const levelOffset = (level) => (dollhouse ? level * STOREY_GAP : 0);

function applyView() {
  for (const mesh of roomMeshes.values()) {
    const lvl = mesh.userData.level || 0;
    mesh.visible = (viewStorey === 'all' || lvl === viewStorey);
    mesh.position.y = (mesh.userData.elevation || 0) + levelOffset(lvl);
  }
  for (const mesh of itemMeshes.values()) {
    const lvl = mesh.userData.level || 0;
    mesh.visible = (viewStorey === 'all' || lvl === viewStorey);
    mesh.position.y = (mesh.userData.baseY || 0) + levelOffset(lvl);
  }
  if (buildingRoofMesh) buildingRoofMesh.visible = (viewStorey === 'all' && !dollhouse);
  ground.visible = !dollhouse; // reveal the basement when floors are exploded
}

// Rebuild the storey dropdown from the LIVE room meshes (so it stays correct after incremental patches,
// e.g. an import that streams rooms in — not just on a full replace).
function populateStoreys() {
  const levels = [...new Set([...roomMeshes.values()].map(m => m.userData.level || 0))].sort((a, b) => a - b);
  const sel = document.getElementById('sel-storey');
  const prev = sel.value;
  const next = '<option value="all">All floors</option>' +
    levels.map(l => `<option value="${l}">${levelLabel(l)}</option>`).join('');
  if (sel.innerHTML === next) return; // no change → keep current selection untouched
  sel.innerHTML = next;
  sel.value = [...sel.options].some(o => o.value === prev) ? prev : 'all';
  viewStorey = sel.value === 'all' ? 'all' : parseInt(sel.value, 10);
  sel.style.display = levels.length > 1 ? '' : 'none';
}

// Re-fetch + re-render the scene (used when the wall material changes — walls are baked at build time).
async function rerenderScene() {
  try { const s = await (await fetch('/api/scene')).json(); applyFull(s); } catch {}
}

// ── Vision gate (opt-in) ─────────────────────────────────────────────────────
// When enabled server-side, each turn sends a BEFORE screenshot (context) and, after the scene renders,
// an AFTER screenshot for a one-shot correction pass. `visionEnabled` comes from /api/me on load.
let visionEnabled = false;

// Capture the current canvas as a downscaled JPEG data URL (~1024px wide) to keep the payload small.
function captureScene(maxW = 1024) {
  try {
    renderer.render(scene, camera);              // ensure the latest frame is in the (preserved) buffer
    const src = renderer.domElement;
    const scale = Math.min(1, maxW / src.width);
    const c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(src.width * scale));
    c.height = Math.max(1, Math.round(src.height * scale));
    c.getContext('2d').drawImage(src, 0, 0, c.width, c.height);
    return c.toDataURL('image/jpeg', 0.85);
  } catch { return null; }
}

// Wait for the just-mutated scene to actually paint, then capture the AFTER frame and run the gate.
async function runVisionGate(message) {
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  const afterImage = captureScene();
  if (!afterImage) return;
  const data = await jsonFetch('/api/chat/verify', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ afterImage, request: message })
  });
  const acts = (data && data.actions) || [];
  for (const a of acts) addMessage('tool', '🔍 ' + a);   // corrections the vision gate applied
}

const btnStyle = document.getElementById('btn-style');
btnStyle.onclick = () => {
  renderStyle = (renderStyle === 'solid') ? 'glass' : 'solid';
  btnStyle.textContent = (renderStyle === 'solid') ? 'Solid' : 'Glass';
  btnStyle.classList.toggle('active', renderStyle === 'solid');
  rerenderScene();
};

document.getElementById('sel-storey').onchange = (e) => {
  viewStorey = e.target.value === 'all' ? 'all' : parseInt(e.target.value, 10);
  applyView();
};

const btnDoll = document.getElementById('btn-dollhouse');
btnDoll.onclick = () => {
  dollhouse = !dollhouse;
  btnDoll.classList.toggle('active', dollhouse);
  applyView();
};

// ── Selection (raycast) ────────────────────────────────────────────────────
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

renderer.domElement.addEventListener('click', (e) => {
  if (justDragged) { justDragged = false; return; }
  pointer.x = (e.clientX / window.innerWidth) * 2 - 1;
  pointer.y = -(e.clientY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const hits = raycaster.intersectObjects([...itemMeshes.values()], true); // recurse into part meshes
  if (hits.length > 0) {
    const id = hits[0].object.userData.id;
    const group = itemMeshes.get(id);
    if (group) {
      selectedId = id;
      transform.attach(group);
    }
  } else {
    selectedId = null;
    transform.detach();
  }
});

// ── Commit a manual transform back to the server ───────────────────────────
async function commitTransform() {
  const mesh = transform.object;
  if (!mesh) return;
  const base = mesh.userData.size;
  const size = { x: base.x * mesh.scale.x, y: base.y * mesh.scale.y, z: base.z * mesh.scale.z };
  mesh.scale.set(1, 1, 1);
  // Keep the item's bottom anchored (works for floor- and surface-placed items)
  // even after a scale, instead of snapping back to the floor.
  const bottom = mesh.position.y - base.y / 2;
  mesh.position.y = bottom + size.y / 2;

  await fetch(`/api/items/${mesh.userData.id}/transform`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      positionX: mesh.position.x, positionY: mesh.position.y, positionZ: mesh.position.z,
      rotationY: THREE.MathUtils.radToDeg(mesh.rotation.y),
      sizeX: size.x, sizeY: size.y, sizeZ: size.z
    })
  });
}

// ── Live scene stream (SSE) ────────────────────────────────────────────────
// The stream is bound to the caller's tenant by cookie (personal id, or the room when collaborating),
// so switching into/out of a room is just a reconnect — the server sends a fresh full baseline.
let sceneES = null;
let lastStreamMsgAt = 0;
let streamWatchdog = null;
function connectStream() {
  if (sceneES) { try { sceneES.close(); } catch {} }
  const es = new EventSource('/api/stream?mode=patch');
  sceneES = es;
  lastStreamMsgAt = Date.now();
  es.onmessage = (e) => {
    lastStreamMsgAt = Date.now();
    try {
      const msg = JSON.parse(e.data);
      if (msg.type === 'ping') return; // heartbeat — just proves the stream is alive
      if (msg.type === 'patch') applyPatch(msg);
      else if (msg.type === 'full') applyFull(msg.scene);
      else applyFull(msg); // legacy full-scene payload (no envelope)
      scheduleAutosave(); // persist the current space if it has been saved before
    } catch (err) {
      console.error('[scene] failed to render update:', err, e.data);
    }
  };
  es.onerror = () => { es.close(); if (sceneES === es) sceneES = null; setTimeout(connectStream, 1500); };
}
// The server heartbeats every 20s. If nothing arrives for 50s the socket is dead even when 'error'
// never fires (a stale half-open connection a server restart can leave) — force a clean reconnect.
if (!streamWatchdog) streamWatchdog = setInterval(() => {
  if (sceneES && Date.now() - lastStreamMsgAt > 30000) {
    try { sceneES.close(); } catch {}
    sceneES = null;
    connectStream();
  }
}, 8000);
connectStream();

// A backgrounded tab pauses requestAnimationFrame (no repaint) AND can stall EventSource — so while you
// drive the scene from Claude Desktop, the viewer falls behind. On return to the tab, reconnect the live
// stream AND pull the current scene immediately (don't wait on the reconnect) — so it catches up with no
// manual refresh. Bound to both visibilitychange and window focus to cover every switch-back path.
function refreshLiveScene() { connectStream(); rerenderScene(); }
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') refreshLiveScene(); });
window.addEventListener('focus', refreshLiveScene);

// ── Live collaboration (shared rooms + presence) ─────────────────────────────
let inRoom = false;
let roomCode = null;
let myUserId = null;
let presenceES = null;
let presenceTimer = null;
let myPointer = null;                 // [x, z] on the floor plane
const groundPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
const presenceGroup = new THREE.Group();
scene.add(presenceGroup);
const remotes = new Map();            // userId -> { color, cursor, cam, box, labelEl }
const labelsEl = document.getElementById('presence-labels');
const rosterEl = document.getElementById('room-roster');
const roomWrapEl = document.getElementById('room-wrap');
const shareBtn = document.getElementById('btn-share');
const toastEl = document.getElementById('collab-toast');

function showToast(html, ms) {
  toastEl.innerHTML = html;
  toastEl.classList.remove('collab-hidden');
  if (ms) setTimeout(() => toastEl.classList.add('collab-hidden'), ms);
}

async function shareRoom() {
  try {
    const r = await (await fetch('/api/rooms', { method: 'POST' })).json();
    if (!r.code) return;
    try { await navigator.clipboard.writeText(r.url); } catch {}
    enterRoom(r.code);
    showToast(`You're collaborating — link copied. Share it: <b>${r.url}</b>`, 7000);
  } catch {}
}

async function leaveRoom() {
  try { await fetch('/api/rooms/leave', { method: 'POST' }); } catch {}
  exitRoomUi();
  switchContext();
}

function enterRoom(code) {
  inRoom = true; roomCode = code;
  shareBtn.classList.add('collab-hidden');
  roomWrapEl.classList.remove('collab-hidden');
  switchContext();            // reconnect the stream → the room's shared scene baseline
  startPresence(code);
}

function exitRoomUi() {
  inRoom = false; roomCode = null;
  shareBtn.classList.remove('collab-hidden');
  roomWrapEl.classList.add('collab-hidden');
  stopPresence();
  clearRemotes();
  rosterEl.innerHTML = '';
}

// Reconnecting the SSE delivers the new tenant's full baseline; also reload chat + reset selection.
function switchContext() {
  selectedId = null;
  try { transform.detach(); } catch {}
  connectStream();
  if (typeof loadChat === 'function') loadChat();
  if (typeof refreshCurrentSpace === 'function') refreshCurrentSpace();
}

function startPresence(code) {
  stopPresence();
  presenceES = new EventSource(`/api/rooms/${code}/presence/stream`);
  presenceES.onmessage = (e) => { try { applyPresence(JSON.parse(e.data)); } catch {} };
  presenceES.onerror = () => { if (presenceES) presenceES.close(); };
  presenceTimer = setInterval(sendPresence, 100);   // ~10Hz
}

function stopPresence() {
  if (presenceES) { try { presenceES.close(); } catch {} presenceES = null; }
  if (presenceTimer) { clearInterval(presenceTimer); presenceTimer = null; }
}

async function sendPresence() {
  if (!inRoom || !roomCode) return;
  try {
    await fetch(`/api/rooms/${roomCode}/presence`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        selectedItemId: selectedId,
        pointer: myPointer,
        camera: [camera.position.x, camera.position.y, camera.position.z,
                 orbit.target.x, orbit.target.y, orbit.target.z],
      }),
    });
  } catch {}
}

function applyPresence(msg) {
  if (msg.type !== 'presence') return;
  const players = msg.players || [];
  renderRoster(players);
  const seen = new Set();
  for (const p of players) {
    if (p.userId === myUserId) continue;
    seen.add(p.userId);
    updateRemote(p);
  }
  for (const [uid, r] of remotes) if (!seen.has(uid)) { disposeRemote(r); remotes.delete(uid); }
}

function renderRoster(players) {
  rosterEl.innerHTML = '';
  for (const p of players) {
    const chip = document.createElement('span');
    chip.className = 'roster-chip';
    chip.style.background = p.color;
    chip.textContent = p.name + (p.userId === myUserId ? ' (you)' : '');
    rosterEl.appendChild(chip);
  }
}

function updateRemote(p) {
  let r = remotes.get(p.userId);
  if (!r) {
    const col = new THREE.Color(p.color);
    const cursor = new THREE.Mesh(new THREE.ConeGeometry(0.13, 0.4, 16),
      new THREE.MeshBasicMaterial({ color: col }));
    cursor.rotation.x = Math.PI;   // point the cone tip down at the floor
    const cam = new THREE.Mesh(new THREE.OctahedronGeometry(0.32),
      new THREE.MeshBasicMaterial({ color: col, transparent: true, opacity: 0.5, wireframe: true }));
    const labelEl = document.createElement('div');
    labelEl.className = 'presence-label';
    labelEl.style.background = p.color;
    labelEl.textContent = p.name;
    labelsEl.appendChild(labelEl);
    presenceGroup.add(cursor); presenceGroup.add(cam);
    r = { color: col, cursor, cam, box: null, labelEl };
    remotes.set(p.userId, r);
  }
  if (p.pointer) { r.cursor.visible = true; r.cursor.position.set(p.pointer[0], 0.25, p.pointer[1]); }
  else r.cursor.visible = false;
  if (p.camera && p.camera.length === 6) { r.cam.visible = true; r.cam.position.set(p.camera[0], p.camera[1], p.camera[2]); }
  else r.cam.visible = false;
  const selMesh = p.selectedItemId ? itemMeshes.get(p.selectedItemId) : null;
  if (selMesh) {
    if (!r.box) { r.box = new THREE.BoxHelper(selMesh, r.color); scene.add(r.box); }
    else { r.box.setFromObject(selMesh); }
    r.box.visible = true;
  } else if (r.box) { r.box.visible = false; }
}

function disposeRemote(r) {
  if (r.cursor) presenceGroup.remove(r.cursor);
  if (r.cam) presenceGroup.remove(r.cam);
  if (r.box) scene.remove(r.box);
  if (r.labelEl) r.labelEl.remove();
}

function clearRemotes() {
  for (const r of remotes.values()) disposeRemote(r);
  remotes.clear();
  labelsEl.innerHTML = '';
}

// Project each remote cursor's world position to a screen-space name label (called each frame).
const _projV = new THREE.Vector3();
function updatePresenceLabels() {
  if (!inRoom) return;
  for (const r of remotes.values()) {
    if (!r.cursor.visible) { r.labelEl.style.display = 'none'; continue; }
    _projV.copy(r.cursor.position).project(camera);
    if (_projV.z > 1) { r.labelEl.style.display = 'none'; continue; }
    r.labelEl.style.display = 'block';
    r.labelEl.style.left = ((_projV.x * 0.5 + 0.5) * window.innerWidth) + 'px';
    r.labelEl.style.top = ((-_projV.y * 0.5 + 0.5) * window.innerHeight) + 'px';
  }
}

// Track my own pointer on the floor so others can see my cursor.
renderer.domElement.addEventListener('mousemove', (e) => {
  if (!inRoom) return;
  const rect = renderer.domElement.getBoundingClientRect();
  pointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const hit = new THREE.Vector3();
  if (raycaster.ray.intersectPlane(groundPlane, hit)) myPointer = [hit.x, hit.z];
});

shareBtn.addEventListener('click', shareRoom);
document.getElementById('btn-leave').addEventListener('click', leaveRoom);

// ── Toolbar ────────────────────────────────────────────────────────────────
const modeButtons = {
  translate: document.getElementById('mode-translate'),
  rotate: document.getElementById('mode-rotate'),
  scale: document.getElementById('mode-scale'),
};
function setMode(mode) {
  transform.setMode(mode);
  transform.showY = (mode !== 'translate'); // lock Y only while moving
  for (const [m, btn] of Object.entries(modeButtons)) btn.classList.toggle('active', m === mode);
}
modeButtons.translate.onclick = () => setMode('translate');
modeButtons.rotate.onclick = () => setMode('rotate');
modeButtons.scale.onclick = () => setMode('scale');
window.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT') return;
  if (e.key === 'w') setMode('translate');
  if (e.key === 'e') setMode('rotate');
  if (e.key === 'r') setMode('scale');
});

document.getElementById('btn-reset').onclick = () =>
  fetch('/api/reset', { method: 'POST' }).then(() => refreshSuggestions());

// ── Spaces manager (gallery + autosave) ──────────────────────────────────────
const spaceCurrentEl = document.getElementById('space-current');
const spaceStatusEl = document.getElementById('space-status');
const spacesModal = document.getElementById('spaces-modal');
const galleryEl = document.getElementById('sp-gallery');
const searchEl = document.getElementById('sp-search');
const sortEl = document.getElementById('sp-sort');
const countEl = document.getElementById('sp-count');
let currentSpace = null;
let allSpaces = [];

const jsonFetch = (url, opts) => fetch(url, opts).then(r => (r.ok ? r.json().catch(() => null) : null));
const escapeHtml = (s) => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function setStatus(state) { // 'saved' | 'saving' | 'unsaved'
  spaceStatusEl.className = state;
  spaceStatusEl.textContent = state === 'saved' ? '✓ Saved' : state === 'saving' ? 'Saving…' : 'Unsaved';
}

async function refreshCurrentSpace() {
  currentSpace = await jsonFetch('/api/spaces/current');
  if (currentSpace) {
    spaceCurrentEl.textContent = currentSpace.name;
    setStatus(currentSpace.saved ? 'saved' : 'unsaved');
  }
}

function updatedAgo(iso) {
  const t = new Date(iso).getTime();
  if (!t) return '';
  const s = Math.max(0, (Date.now() - t) / 1000);
  if (s < 60) return 'just now';
  const m = s / 60; if (m < 60) return `${Math.floor(m)}m ago`;
  const h = m / 60; if (h < 24) return `${Math.floor(h)}h ago`;
  const d = h / 24; if (d < 7) return `${Math.floor(d)}d ago`;
  return new Date(iso).toLocaleDateString();
}

// Top-down SVG thumbnail from compact preview geometry (rooms + colored item dots).
function spaceThumbnail(p) {
  const hasGeo = p && ((p.rooms && p.rooms.length) || (p.items && p.items.length));
  if (!hasGeo)
    return `<svg viewBox="0 0 100 62"><text x="50" y="34" text-anchor="middle" fill="#a8a29e" font-size="8">empty</text></svg>`;
  const pad = 0.6;
  const minX = p.minX - pad, minZ = p.minZ - pad;
  const w = Math.max(0.01, (p.maxX + pad) - minX), h = Math.max(0.01, (p.maxZ + pad) - minZ);
  const span = Math.max(w, h);
  const parts = [];
  for (const r of (p.rooms || []))
    parts.push(`<rect x="${r.cx - r.w / 2}" y="${r.cz - r.d / 2}" width="${r.w}" height="${r.d}" fill="rgba(255,255,255,0.05)" stroke="#a8a29e" stroke-width="${span * 0.008}"/>`);
  const rDot = span * 0.014;
  for (const it of (p.items || []))
    parts.push(`<circle cx="${it.x}" cy="${it.z}" r="${rDot}" fill="${it.color}"/>`);
  return `<svg viewBox="${minX} ${minZ} ${w} ${h}" preserveAspectRatio="xMidYMid meet">${parts.join('')}</svg>`;
}

async function loadSpaces() {
  allSpaces = (await jsonFetch('/api/spaces')) || [];
  renderGallery();
}

function renderGallery() {
  const q = searchEl.value.trim().toLowerCase();
  let list = allSpaces.filter(s => !q || s.name.toLowerCase().includes(q));
  if (sortEl.value === 'name') list = list.slice().sort((a, b) => a.name.localeCompare(b.name));
  countEl.textContent = `${list.length} space${list.length === 1 ? '' : 's'}`;
  galleryEl.innerHTML = '';
  if (!list.length) {
    galleryEl.innerHTML = `<div class="gallery-empty">${q ? 'No spaces match your search.' : 'No spaces saved yet. Build a scene, then “Save As…”.'}</div>`;
    return;
  }
  for (const s of list) {
    const isCurrent = currentSpace && s.id === currentSpace.id;
    const card = document.createElement('div');
    card.className = 'space-card' + (isCurrent ? ' current' : '');
    card.innerHTML = `
      <div class="thumb">${spaceThumbnail(s.preview)}${isCurrent ? '<span class="tag">Current</span>' : ''}</div>
      <div class="body">
        <div class="nm" title="${escapeHtml(s.name)}">${escapeHtml(s.name)}</div>
        <div class="meta">${s.roomCount} room${s.roomCount === 1 ? '' : 's'} · ${s.itemCount} item${s.itemCount === 1 ? '' : 's'} · ${updatedAgo(s.updatedAt)}</div>
        <div class="actions">
          <button class="open">Open</button>
          <button class="mini ren" title="Rename">✎</button>
          <button class="mini dup" title="Duplicate">⧉</button>
          <button class="mini danger del" title="Delete">🗑</button>
        </div>
      </div>`;
    card.querySelector('.open').onclick = () => openSpace(s.id);
    card.querySelector('.ren').onclick = () => startInlineRename(card, s);
    card.querySelector('.dup').onclick = () => duplicateSpace(s.id);
    card.querySelector('.del').onclick = () => deleteSpace(s);
    galleryEl.appendChild(card);
  }
}

function startInlineRename(card, s) {
  const nm = card.querySelector('.nm');
  const input = document.createElement('input');
  input.className = 'nm-edit'; input.value = s.name;
  nm.replaceWith(input);
  input.focus(); input.select();
  let done = false;
  const commit = async (save) => {
    if (done) return; done = true;
    const name = input.value.trim();
    if (save && name && name !== s.name) {
      await fetch(`/api/spaces/${s.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) });
      await refreshCurrentSpace();
    }
    await loadSpaces();
  };
  input.onkeydown = (e) => { if (e.key === 'Enter') commit(true); if (e.key === 'Escape') commit(false); };
  input.onblur = () => commit(true);
}

async function openSpace(id) {
  await fetch(`/api/spaces/${id}/open`, { method: 'POST' });
  await refreshCurrentSpace();
  await loadChat();          // swap the chat panel to this space's conversation
  await loadSpaces();
  refreshSuggestions();      // suggestions follow the newly-opened scene
  closeSpacesModal();
}

async function duplicateSpace(id) {
  await fetch(`/api/spaces/${id}/duplicate`, { method: 'POST' });
  await loadSpaces();
}

async function deleteSpace(s) {
  if (!(await confirmDialog('Delete space', `Delete “${s.name}”? This cannot be undone.`, 'Delete', true))) return;
  await fetch(`/api/spaces/${s.id}`, { method: 'DELETE' });
  await loadSpaces();
}

async function newSpace() {
  const name = await promptDialog('New space', 'Start a fresh, empty space.', 'Untitled');
  if (name === null) return;
  await fetch('/api/spaces', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: name || 'Untitled' }) });
  await refreshCurrentSpace();
  await loadChat();          // fresh space → empty chat panel
  await loadSpaces();
  refreshSuggestions();      // fresh empty scene → opener suggestions
}

async function saveAsSpace() {
  const name = await promptDialog('Save space as', 'Save the current scene as a new space.', currentSpace ? currentSpace.name : 'Untitled');
  if (name === null) return;
  setStatus('saving');
  await fetch('/api/spaces/save', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: name || 'Untitled' }) });
  await refreshCurrentSpace();
  await loadSpaces();
}

// Autosave the current space (once it has been saved at least once), debounced.
let autosaveTimer = null;
function scheduleAutosave() {
  if (inRoom) return;   // don't let N collaborators thrash-save the shared room scene
  if (!(currentSpace && currentSpace.saved)) return;
  setStatus('saving');
  clearTimeout(autosaveTimer);
  autosaveTimer = setTimeout(async () => {
    await fetch('/api/spaces/save', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    setStatus('saved');
    if (spacesModal.classList.contains('open')) loadSpaces();
  }, 800);
}

function openSpacesModal() { searchEl.value = ''; spacesModal.classList.add('open'); loadSpaces(); }
function closeSpacesModal() { spacesModal.classList.remove('open'); }

document.getElementById('btn-spaces').onclick = openSpacesModal;
document.getElementById('sp-close').onclick = closeSpacesModal;
document.getElementById('sp-new').onclick = newSpace;
document.getElementById('sp-saveas').onclick = saveAsSpace;
searchEl.oninput = renderGallery;
sortEl.onchange = renderGallery;
spacesModal.onclick = (e) => { if (e.target === spacesModal) closeSpacesModal(); };

// ── Connect Claude Desktop (MCP) ─────────────────────────────────────────────
// Signed-in users get a personal token so an MCP client (Claude Desktop) can edit THEIR space on this
// (remote) API. We render a ready-to-paste claude_desktop_config.json with the token + this origin.
function setupConnect(token) {
  const btn = document.getElementById('btn-connect');
  const modal = document.getElementById('connect-modal');
  if (!btn || !modal) return;
  const origin = location.origin;
  // Prebuilt, self-contained connector per OS (served from /download/). command = the downloaded file.
  const bins = [
    { id: 'macos-arm64', label: 'macOS · Apple Silicon', file: 'spatialai-mcp-macos-arm64', cmd: '/Users/you/Downloads/spatialai-mcp-macos-arm64' },
    { id: 'macos-x64',   label: 'macOS · Intel',         file: 'spatialai-mcp-macos-x64',   cmd: '/Users/you/Downloads/spatialai-mcp-macos-x64' },
    { id: 'win-x64',     label: 'Windows',               file: 'spatialai-mcp-win-x64.exe', cmd: 'C:\\Users\\you\\Downloads\\spatialai-mcp-win-x64.exe' },
    { id: 'linux-x64',   label: 'Linux',                 file: 'spatialai-mcp-linux-x64',   cmd: '/home/you/Downloads/spatialai-mcp-linux-x64' },
  ];
  const ua = (navigator.userAgent || '') + ' ' + (navigator.platform || '');
  const pick = /Win/i.test(ua) ? 'win-x64' : /Mac/i.test(ua) ? 'macos-arm64' : /Linux|X11/i.test(ua) ? 'linux-x64' : 'macos-arm64';
  const ordered = bins.slice().sort((a, b) => (b.id === pick) - (a.id === pick));  // detected OS first

  const cfgEl = document.getElementById('cn-config');
  const configFor = (b) => JSON.stringify({
    mcpServers: { spatialai: { command: b.cmd, env: { SpatialApi: origin, SpatialApiKey: token } } }
  }, null, 2);
  let config = configFor(bins.find(b => b.id === pick));
  cfgEl.textContent = config;

  const dl = document.getElementById('cn-downloads');
  dl.innerHTML = '';
  ordered.forEach(b => {
    const a = document.createElement('a');
    a.className = 'cn-dl' + (b.id === pick ? ' rec active' : '');
    a.href = origin + '/download/' + b.file;
    a.setAttribute('download', '');
    a.innerHTML = '<span>' + b.label + '</span>' + (b.id === pick ? '<span class="cn-dl-tag">detected</span>' : '');
    a.addEventListener('click', () => {   // picking an OS also retargets the config's command path
      config = configFor(b); cfgEl.textContent = config;
      dl.querySelectorAll('.cn-dl').forEach(x => x.classList.remove('active'));
      a.classList.add('active');
    });
    dl.appendChild(a);
  });

  btn.hidden = false;
  const close = () => modal.classList.remove('open');
  btn.onclick = () => modal.classList.add('open');
  document.getElementById('cn-close').onclick = close;
  modal.onclick = (e) => { if (e.target === modal) close(); };
  document.getElementById('cn-copy').onclick = async () => {
    const note = document.getElementById('cn-copied');
    try { await navigator.clipboard.writeText(config); note.textContent = 'Copied ✓'; }
    catch { note.textContent = 'Select the text and press Ctrl+C'; }
    setTimeout(() => { note.textContent = ''; }, 2200);
  };
}

// ── Reusable prompt / confirm dialog ─────────────────────────────────────────
const dialogEl = document.getElementById('dialog');
const dInput = document.getElementById('d-input');
const dOk = document.getElementById('d-ok');
const dCancel = document.getElementById('d-cancel');
let dialogResolve = null, dialogMode = 'prompt';

function closeDialog(value) {
  if (!dialogResolve) return;
  const resolve = dialogResolve; dialogResolve = null;
  dialogEl.classList.remove('open');
  resolve(value);
}
function promptDialog(title, message, def = '') {
  return new Promise((resolve) => {
    dialogMode = 'prompt'; dialogResolve = resolve;
    document.getElementById('d-title').textContent = title;
    document.getElementById('d-msg').textContent = message;
    dInput.style.display = ''; dInput.value = def;
    dOk.textContent = 'OK'; dOk.classList.remove('danger');
    dialogEl.classList.add('open');
    dInput.focus(); dInput.select();
  });
}
function confirmDialog(title, message, okLabel = 'OK', danger = false) {
  return new Promise((resolve) => {
    dialogMode = 'confirm'; dialogResolve = resolve;
    document.getElementById('d-title').textContent = title;
    document.getElementById('d-msg').textContent = message;
    dInput.style.display = 'none';
    dOk.textContent = okLabel; dOk.classList.toggle('danger', danger);
    dialogEl.classList.add('open');
    dOk.focus();
  });
}
dOk.onclick = () => closeDialog(dialogMode === 'prompt' ? dInput.value : true);
dCancel.onclick = () => closeDialog(dialogMode === 'prompt' ? null : false);
dialogEl.onclick = (e) => { if (e.target === dialogEl) dCancel.onclick(); };
dInput.onkeydown = (e) => { if (e.key === 'Enter') dOk.onclick(); if (e.key === 'Escape') dCancel.onclick(); };

// ── Import building from plans ───────────────────────────────────────────────
const importModal = document.getElementById('import-modal');
const impDrop = document.getElementById('imp-drop');
const impFile = document.getElementById('imp-file');
const impThumbs = document.getElementById('imp-thumbs');
const impRun = document.getElementById('imp-run');
const impStatus = document.getElementById('imp-status');
const impResult = document.getElementById('imp-result');
const impAttic = document.getElementById('imp-attic');
let importFiles = [];

function renderImpThumbs() {
  impThumbs.innerHTML = '';
  importFiles.forEach((f, i) => {
    const t = document.createElement('div'); t.className = 't';
    const img = document.createElement('img'); img.src = URL.createObjectURL(f); t.appendChild(img);
    const rm = document.createElement('button'); rm.className = 'rm'; rm.textContent = '✕';
    rm.onclick = (e) => { e.stopPropagation(); importFiles.splice(i, 1); renderImpThumbs(); };
    t.appendChild(rm);
    impThumbs.appendChild(t);
  });
  impRun.disabled = importFiles.length === 0;
}
function addImportFiles(files) {
  for (const f of files) if (f.type.startsWith('image/')) importFiles.push(f);
  renderImpThumbs();
}
impDrop.onclick = () => impFile.click();
impFile.onchange = () => { addImportFiles(impFile.files); impFile.value = ''; };
impDrop.ondragover = (e) => { e.preventDefault(); impDrop.classList.add('active'); };
impDrop.ondragleave = () => impDrop.classList.remove('active');
impDrop.ondrop = (e) => { e.preventDefault(); impDrop.classList.remove('active'); addImportFiles(e.dataTransfer.files); };

impRun.onclick = async () => {
  if (!importFiles.length) return;
  impRun.disabled = true; impStatus.textContent = 'Reading plans… (vision extraction can take a minute)'; impResult.innerHTML = '';
  const fd = new FormData();
  for (const f of importFiles) fd.append('files', f);
  fd.append('attic', impAttic && impAttic.checked ? 'true' : 'false');
  try {
    const res = await fetch('/api/import/plans', { method: 'POST', body: fd });
    if (!res.ok) {
      let msg = res.status === 403 ? 'Plan import is disabled on this demo.' : 'Failed: ' + res.status;
      if (res.status !== 403) { try { const e = await res.json(); if (e && e.error) msg = e.error; } catch {} }
      impStatus.textContent = msg;   // only parse on success — 403 bodies are empty
      impRun.disabled = false;
      return;
    }
    const data = await res.json();
    renderPlan(data.plan || data);
    impStatus.textContent = `Built ${data.roomsBuilt ?? 0} room(s). Close to view — refine with chat or drag.`;
    if (typeof data.messagesRemaining === 'number') { messagesRemaining = data.messagesRemaining; renderStatus(); }
    importFiles = []; renderImpThumbs();
    setTimeout(closeImportModal, 1400); // reveal the reconstructed building
  } catch (err) { impStatus.textContent = 'Failed: ' + err; }
  impRun.disabled = false;
};

function renderPlan(plan) {
  const floors = plan.floors || [];
  impResult.innerHTML = '';
  const head = document.createElement('div'); head.className = 'floor';
  head.innerHTML = `<b>Building</b> — ${floors.length} floor(s), footprint ${(plan.width || 0).toFixed(1)}×${(plan.depth || 0).toFixed(1)} m, roof: ${plan.roof ? plan.roof.style : 'none'}`;
  impResult.appendChild(head);
  for (const f of floors.slice().sort((a, b) => a.level - b.level)) {
    const rooms = f.rooms || [];
    const furn = rooms.reduce((n, r) => n + ((r.furniture || []).length), 0);
    const div = document.createElement('div'); div.className = 'floor';
    div.innerHTML = `<b>${f.name || ('Level ' + f.level)}</b> (level ${f.level}, elev ${(f.elevation || 0).toFixed(1)} m) — ${rooms.length} room(s), ${furn} item(s): ` +
      rooms.map(r => `${r.name} ${(r.width || 0).toFixed(1)}×${(r.depth || 0).toFixed(1)}`).join(', ');
    impResult.appendChild(div);
  }
}

function openImportModal() { importModal.classList.add('open'); }
function closeImportModal() { importModal.classList.remove('open'); }
document.getElementById('btn-import').onclick = openImportModal;
document.getElementById('imp-close').onclick = closeImportModal;
importModal.onclick = (e) => { if (e.target === importModal) closeImportModal(); };

refreshCurrentSpace();
loadChat();
document.getElementById('btn-unused').onclick = async () => {
  const r = await (await fetch('/api/analysis/unused')).json();
  addMessage('tool', r.message);
};
document.getElementById('btn-ergo').onclick = async () => {
  const r = await (await fetch('/api/analysis/ergonomics')).json();
  addMessage('tool', r.message);
};

// ── Chat (transcript is owned by the active space, server-side) ──────────────
const log = document.getElementById('log');
const promptEl = document.getElementById('prompt');

// Minimal, safe markdown for assistant replies: HTML-escape first (reusing escapeHtml above — no
// injection), then a small subset — **bold**, *italic*, `code`, bullet/numbered lists, links, paragraphs.
function inlineMd(s) {
  return s
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*\n]+)\*/g, '$1<em>$2</em>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
}
function renderMarkdown(text) {
  const lines = escapeHtml(text).split(/\r?\n/);
  let html = '', list = null, para = [];
  const closeList = () => { if (list) { html += `</${list}>`; list = null; } };
  const flushPara = () => { if (para.length) { html += `<p>${inlineMd(para.join(' '))}</p>`; para = []; } };
  for (const raw of lines) {
    const line = raw.trimEnd();
    let m;
    if (/^\s*$/.test(line)) { flushPara(); closeList(); }
    else if ((m = line.match(/^\s*[-*]\s+(.*)$/))) {
      flushPara();
      if (list !== 'ul') { closeList(); html += '<ul>'; list = 'ul'; }
      html += `<li>${inlineMd(m[1])}</li>`;
    } else if ((m = line.match(/^\s*\d+\.\s+(.*)$/))) {
      flushPara();
      if (list !== 'ol') { closeList(); html += '<ol>'; list = 'ol'; }
      html += `<li>${inlineMd(m[1])}</li>`;
    } else if ((m = line.match(/^\s*#{1,4}\s+(.*)$/))) {
      flushPara(); closeList();
      html += `<div class="md-h">${inlineMd(m[1])}</div>`;
    } else { closeList(); para.push(line); }
  }
  flushPara(); closeList();
  return html;
}

function addMessage(kind, text) {
  const div = document.createElement('div');
  div.className = `msg ${kind}`;
  if (kind === 'ai') div.innerHTML = renderMarkdown(text);
  else div.textContent = text;   // user + tool stay plain (textContent escapes)
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
  return div;
}

// Repopulate the panel from the active space's transcript (called on load and when switching spaces).
async function loadChat() {
  const msgs = (await jsonFetch('/api/chat/history')) || [];
  log.innerHTML = '';
  for (const m of msgs) addMessage(m.kind, m.text);
}

async function send() {
  const message = promptEl.value.trim();
  if (!message) return;
  promptEl.value = '';
  document.getElementById('chat').classList.add('expanded');   // mobile: reveal the conversation on send
  addMessage('user', message);
  const pending = addMessage('ai', '…');

  // BEFORE screenshot (vision gate on): the canvas still shows the pre-prompt scene — capture it now.
  const beforeImage = visionEnabled ? captureScene() : null;

  try {
    const res = await fetch('/api/chat', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(beforeImage ? { message, beforeImage } : { message })
    });
    const data = await res.json();
    pending.remove();
    for (const action of data.actions || []) addMessage('tool', action);
    if (data.assistant) addMessage('ai', data.assistant);
    if (typeof data.messagesRemaining === 'number') { messagesRemaining = data.messagesRemaining; renderStatus(); }
    // Instant deterministic chips from the reply; then quietly upgrade to LLM-refined ones (non-blocking).
    if (data.budgetExhausted) renderSuggestions([]);
    else { renderSuggestions(data.suggestions); refreshSuggestions(); }
    await rerenderScene();   // pull the final scene + redraw — mobile SSE/rAF can stall while the keyboard is up
    // AFTER screenshot correcting gate — non-blocking, only when the server says vision is on.
    if (data.visionCheck) runVisionGate(message).catch(() => {});
  } catch (err) {
    pending.textContent = 'Request failed: ' + err;
  }
}

document.getElementById('send').onclick = send;
promptEl.addEventListener('keydown', (e) => { if (e.key === 'Enter') send(); });

// ── Smart follow-up suggestions ──────────────────────────────────────────────
// The chip row is dynamic: after each turn (and on load/reset/open) it shows context-aware next steps.
// One delegated click handler survives re-renders — clicking a chip fills the prompt and sends it.
const chipsEl = document.querySelector('.chips');
if (chipsEl) chipsEl.addEventListener('click', (e) => {
  const chip = e.target.closest('.chip');
  if (!chip) return;
  promptEl.value = chip.textContent;
  send();
});

function renderSuggestions(list) {
  if (!chipsEl) return;
  const items = Array.isArray(list) ? list.filter(Boolean) : [];
  if (!items.length) { chipsEl.style.display = 'none'; return; }
  chipsEl.style.display = '';
  chipsEl.replaceChildren(...items.map((text) => {
    const span = document.createElement('span');
    span.className = 'chip';
    span.textContent = text;
    return span;
  }));
}

// Pull suggestions for the current scene, upgrading to LLM-refined ones when the server allows it.
async function refreshSuggestions() {
  const data = await jsonFetch('/api/suggestions?refine=1');
  if (data && Array.isArray(data.suggestions)) renderSuggestions(data.suggestions);
}

// Mobile: the chat is a bottom sheet — tap its header (the drag handle) to expand/collapse the log.
(() => {
  const chatPanel = document.getElementById('chat');
  const h = chatPanel && chatPanel.querySelector('header');
  if (h) h.addEventListener('click', (e) => {
    if (e.target.closest('input, button, .chip')) return;
    chatPanel.classList.toggle('expanded');
  });
})();

// ── Identity gate + AI status + budget indicator ────────────────────────────
let aiConfigured = false;
let messagesRemaining = null;
function renderStatus() {
  const dot = document.getElementById('ai-dot');
  const txt = document.getElementById('ai-text');
  if (dot) dot.classList.toggle('on', aiConfigured);
  if (txt) {
    let s = aiConfigured ? 'Claude ready' : 'Claude not configured';
    if (messagesRemaining != null) s += ` · ${messagesRemaining} msgs left`;
    txt.textContent = s;
  }
}
(async () => {
  const roomParam = new URLSearchParams(location.search).get('room');
  try {
    const me = await (await fetch('/api/me')).json();
    // In public mode, an unregistered visitor is sent to the sign-up page (preserving any room link).
    if (me.authRequired && !me.authenticated) {
      location.replace('/register.html' + (roomParam ? '?room=' + encodeURIComponent(roomParam) : ''));
      return;
    }
    myUserId = me.userId || null;
    if (me.importEnabled === false) {
      const ib = document.getElementById('btn-import');
      if (ib) ib.style.display = 'none';   // plan import is disabled on the public demo — don't offer it
    }
    if (me.mcpToken) setupConnect(me.mcpToken);   // signed-in: offer the "Connect Claude Desktop" panel
    if (typeof me.messagesRemaining === 'number') messagesRemaining = me.messagesRemaining;
    visionEnabled = me.visionEnabled === true;    // capture/send screenshots only when the gate is on
  } catch { /* open/dev mode or offline — carry on */ }
  try {
    const { configured } = await (await fetch('/api/configured')).json();
    aiConfigured = !!configured;
  } catch { /* ignore */ }
  renderStatus();

  // Collaboration: join a room from a shared link, or resume an existing room cookie.
  try {
    if (roomParam) {
      const r = await fetch(`/api/rooms/${encodeURIComponent(roomParam)}/join`, { method: 'POST' });
      if (r.ok) { history.replaceState(null, '', location.pathname); enterRoom(roomParam); }
    } else {
      const cur = await (await fetch('/api/rooms/current')).json();
      if (cur.inRoom) enterRoom(cur.code);
    }
  } catch { /* ignore */ }
  refreshSuggestions();   // seed the chip row from the current scene (openers when empty)
})();

// ── Render loop ────────────────────────────────────────────────────────────
function animate() {
  requestAnimationFrame(animate);
  orbit.update();
  updatePresenceLabels();
  renderer.render(scene, camera);
}
animate();

window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});
