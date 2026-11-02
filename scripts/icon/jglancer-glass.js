// jGlancer liquid-glass icon: pure geometry and layer list, shared by the SVG writer (Node) and the Affinity builder.
// Same glass, rim and liquid as assets/brand/glass/jdaw-glass.js; the mark is a photo card in perspective.
// Every layer is a filled path with optional blur and opacity, painted back to front with normal blending.
'use strict';

const A = 100, B = 924, TILE_R = 186, SMOOTH = .6;
const MARK_R = 46, OPEN_R = 26; // card corners and window corners, measured after perspective

const sub = (a, b) => [a[0] - b[0], a[1] - b[1]];
const add = (a, b, k = 1) => [a[0] + b[0] * k, a[1] + b[1] * k];
const len = v => Math.hypot(v[0], v[1]);
const unit = v => { const l = len(v) || 1; return [v[0] / l, v[1] / l]; };
const rot = (v, a) => [v[0] * Math.cos(a) - v[1] * Math.sin(a), v[0] * Math.sin(a) + v[1] * Math.cos(a)];
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };

// Continuous corner (Apple-style curvature ramp) for any angle: returns cubic segments from entry to exit.
function corner(prev, v, next, r, sm = SMOOTH) {
  const e1 = unit(sub(prev, v)), e2 = unit(sub(next, v));
  const th = Math.acos(Math.max(-1, Math.min(1, e1[0] * e2[0] + e1[1] * e2[1]))), phi = Math.PI - th;
  const t = r * Math.tan(phi / 2), bis = unit(add(e1, e2)), c = add(v, bis, r / Math.sin(th / 2));
  const dir = Math.sign(e1[0] * e2[1] - e1[1] * e2[0]) * -1;
  const n1 = unit(sub(add(v, e1, t), c));
  const at = a => add(c, rot(n1, dir * a), r), tan = a => rot(rot(n1, dir * a), dir * Math.PI / 2);
  const a1 = sm * phi / 2, a2 = phi - a1;
  const S1 = add(v, e1, t * (1 + sm)), S2p = add(v, e2, t * (1 + sm));
  const P1 = at(a1), P2 = at(a2), k = 4 / 3 * Math.tan((a2 - a1) / 4) * r;
  // Handles stop short of where the edge and the arc tangent meet, so the ramp never overshoots the edge.
  const h2 = r * (1 - Math.cos(a1)) / Math.sin(a1), h1 = t * sm + r * Math.sin(a1) - h2 * Math.cos(a1);
  const c1 = add(S1, e1, -h1 * 2 / 3), c2 = add(P1, tan(a1), -h2);
  const c3 = add(P1, tan(a1), k), c4 = add(P2, tan(a2), -k);
  const c5 = add(P2, tan(a2), h2), c6 = add(S2p, e2, -h1 * 2 / 3);
  return { start: S1, end: S2p, segs: [[...c1, ...c2, ...P1], [...c3, ...c4, ...P2], [...c5, ...c6, ...S2p]] };
}

// r: one radius, or one per vertex.
function roundedPoly(V, r, sm) {
  const n = V.length, g = []; let s = null;
  for (let i = 0; i < n; i++) {
    const c = corner(V[(i + n - 1) % n], V[i], V[(i + 1) % n], Array.isArray(r) ? r[i] : r, sm);
    if (!s) s = c.start; else g.push(c.start);
    g.push(...c.segs);
  }
  return { s, g };
}

function flatten(c, n = 16) {
  const out = [c.s]; let p = c.s;
  for (const g of c.g) {
    if (g.length === 2) { out.push(g); p = g; continue; }
    for (let i = 1; i <= n; i++) {
      const t = i / n, u = 1 - t;
      out.push([u * u * u * p[0] + 3 * u * u * t * g[0] + 3 * u * t * t * g[2] + t * t * t * g[4],
                u * u * u * p[1] + 3 * u * u * t * g[1] + 3 * u * t * t * g[3] + t * t * t * g[5]]);
    }
    p = g.slice(-2);
  }
  return out;
}

// Tile outline resampled by arc length, clockwise from the top edge.
const TILE = roundedPoly([[A, A], [B, A], [B, B], [A, B]], TILE_R);
const RIM = (() => {
  const pts = flatten(TILE, 24), out = []; const step = 4; let d = 0;
  pts.push(pts[0]);
  out.push(pts[0]);
  for (let i = 1; i < pts.length; i++) {
    let a = pts[i - 1]; const b = pts[i]; let l = len(sub(b, a));
    while (d + l >= step) { const q = add(a, unit(sub(b, a)), step - d); out.push(q); a = q; l = len(sub(b, a)); d = 0; }
    d += l;
  }
  return out;
})();
const RL = RIM.length;
const rimAt = i => RIM[((Math.round(i) % RL) + RL) % RL];
function rimFrame(i, inset) {
  const a = rimAt(i - 2), b = rimAt(i + 2), t = unit(sub(b, a)), n = [-t[1], t[0]]; // inward for a clockwise path on y-down
  return add(rimAt(i), n, inset);
}
const nearestRim = (x, y) => { let best = 1e12, k = 0; RIM.forEach((p, i) => { const d = (p[0] - x) ** 2 + (p[1] - y) ** 2; if (d < best) { best = d; k = i; } }); return k; };
const px = v => v / 4; // arc length in px to rim samples

// Crescent along the rim: centred at sample c, spanning back/fwd px, thickness h shaped by a soft taper.
function band(c, back, fwd, i0, h, { pw = 1.2, wob = 0, seed = 1 } = {}) {
  const n0 = Math.round(px(back)), n1 = Math.round(px(fwd)), inner = [], outer = [];
  for (let j = -n0; j <= n1; j += 2) {
    const u = j < 0 ? -j / n0 : j / n1, w = Math.pow(Math.cos(u * Math.PI / 2), pw);
    const flow = 1 + wob * (.6 * Math.sin(j * .05 + seed) + .4 * Math.sin(j * .13 + seed * 2.1));
    outer.push(rimFrame(c + j, i0));
    inner.unshift(rimFrame(c + j, i0 + h * w * flow));
  }
  const all = [...outer, ...inner];
  return [{ s: all[0], g: all.slice(1) }];
}
// Closed ring between two insets of the tile.
function ring(i0, i1) {
  const path = (ins, rev) => { const a = []; for (let i = 0; i < RL; i += 2) a.push(rimFrame(i, ins)); if (rev) a.reverse(); return { s: a[0], g: a.slice(1) }; };
  return [path(i0, false), path(i1, true)];
}

// Perspective: unit square (u right, v down) onto the tilted front card.
function homography(q) {
  const [p0, p1, p2, p3] = q; // TL TR BR BL
  const dx1 = p1[0] - p2[0], dx2 = p3[0] - p2[0], sx = p0[0] - p1[0] + p2[0] - p3[0];
  const dy1 = p1[1] - p2[1], dy2 = p3[1] - p2[1], sy = p0[1] - p1[1] + p2[1] - p3[1];
  const den = dx1 * dy2 - dx2 * dy1, g = (sx * dy2 - dx2 * sy) / den, h = (dx1 * sy - sx * dy1) / den;
  const a = p1[0] - p0[0] + g * p1[0], b = p3[0] - p0[0] + h * p3[0], c = p0[0];
  const d = p1[1] - p0[1] + g * p1[1], e = p3[1] - p0[1] + h * p3[1], f = p0[1];
  return (u, v) => { const w = g * u + h * v + 1; return [(a * u + b * v + c) / w, (d * u + e * v + f) / w]; };
}

// Liquid neck: starts inside the letter tangent to its edge and lands on the rim tangent to it, so no seam exists.
// `sink` pushes the start into the letter, so a blurred layer emerges from under the edge gradually.
function neck(k, along, m, span, fwd, sink = 0) {
  const e = unit(sub(along, k.p)); let n = [-e[1], e[0]];
  if ((512 - k.p[0]) * n[0] + (512 - k.p[1]) * n[1] < 0) n = [-n[0], -n[1]];
  const Pe = add(add(k.p, e, m), n, sink), j = k.c + (fwd ? 1 : -1) * px(span), Pr = rimFrame(j, 0);
  const dir = unit(sub(rimAt(j + (fwd ? -3 : 3)), rimAt(j)));
  const c1 = add(Pe, e, -m * .78), c2 = add(Pr, dir, span * .62);
  const back = [], cnt = Math.abs(j - k.c);
  for (let i = 1; i <= cnt; i += 2) back.push(rimFrame(j + (fwd ? -i : i), 0));
  return [{ s: add(k.p, n, sink), g: [Pe, [...c1, ...c2, ...Pr], ...back] }];
}


// Front card: near (left) side large, far side smaller. TL and BL sit just past the rim so the rounded corners reach it.
const QUAD = [[117, 199], [785, 266], [865, 772], [300, 926]];
const H = homography(QUAD);
const WIN = [[.14, .16], [.86, .16], [.86, .84], [.14, .84]].map(p => H(...p));
const MOUNT = [[.12, .86, 4], [.40, .40, 24], [.56, .62, 16], [.70, .49, 20], [.90, .86, 4], [.90, .94, 4], [.12, .94, 4]];
const SUN = [.71, .31, .075, .072]; // centre u, v and radii in u, v: round after perspective

const area = V => V.reduce((s, p, i) => { const q = V[(i + 1) % V.length]; return s + p[0] * q[1] - q[0] * p[1]; }, 0);
const wind = (V, solid) => (area(V) > 0) === solid ? V : [...V].reverse(); // solid = clockwise on y-down
// Convex polygon with every side moved d inward (negative grows it), shifted, wound like a hole or like a solid.
function poly(V0, d, r, dx = 0, dy = 0, solid = false) {
  const n = V0.length, c = V0.reduce((a, v) => [a[0] + v[0] / n, a[1] + v[1] / n], [0, 0]);
  const lines = V0.map((a, i) => {
    const b = V0[(i + 1) % n], t = unit(sub(b, a)); let m = [-t[1], t[0]];
    if ((c[0] - a[0]) * m[0] + (c[1] - a[1]) * m[1] < 0) m = [-m[0], -m[1]];
    return [add(a, m, d), t];
  });
  const V = lines.map((l, i) => {
    const [p, t] = lines[(i + n - 1) % n], [q, u] = l, den = t[0] * u[1] - t[1] * u[0], w = ((q[0] - p[0]) * u[1] - (q[1] - p[1]) * u[0]) / den;
    return add(add(p, t, w), [dx, dy]);
  });
  return roundedPoly(wind(V, solid), Math.max(8, r - d));
}
function mountains() {
  const V = MOUNT.map(([u, v]) => H(u, v)), r = MOUNT.map(m => m[2]);
  return area(V) > 0 ? roundedPoly(V, r) : roundedPoly([...V].reverse(), [...r].reverse());
}
function sun() {
  const [cu, cv, ru, rv] = SUN, n = 48, pts = [];
  for (let i = 0; i < n; i++) { const a = 2 * Math.PI * i / n; pts.push(H(cu + ru * Math.cos(a), cv + rv * Math.sin(a))); }
  const P = wind(pts, true), g = [];
  for (let i = 0; i < n; i++) {
    const p0 = P[(i + n - 1) % n], p1 = P[i], p2 = P[(i + 1) % n], p3 = P[(i + 2) % n];
    g.push([p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6, p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6, p2[0], p2[1]]);
  }
  return { s: P[0], g };
}
// The two rim contacts stay sharp: those corners are absorbed into the necks and clipped by the rim.
// Rounding them left a cut-away corner where the liquid behind showed through as a blot.
const CARD = roundedPoly(QUAD, [6, MARK_R, MARK_R, 6]);

const C = { deep: '#0716FF', blue: '#1537FF', cyan: '#12D4FF', ice: '#CFF6FF', yellow: '#FFE36E', orange: '#FF8A14', red: '#FF2436', pink: '#FF1E78', mag: '#D21CFF', white: '#FFFFFF', black: '#000000' };

function layers(theme, clear) {
  const dark = theme === 'dark', ink = dark ? C.white : '#06060A', glass = dark ? '#040406' : '#F7F8FA', out = [];
  let grp = null, grain = false;
  const group = (g, gr = false) => { grp = g; grain = gr; };
  const put = (name, cs, fill, blur = 0, op = 1) => out.push({ name, cs, fill, blur, op, grp, grain });
  const TLc = nearestRim(QUAD[0][0], QUAD[0][1]), BLc = nearestRim(QUAD[3][0], QUAD[3][1]);
  // Rim runs clockwise: at TL forward climbs the left edge to the top; at BL forward runs left along the bottom.
  const contacts = [
    { c: TLc, p: QUAD[0], back: 300, fwd: 560, s: 1, toFwd: QUAD[1], toBack: QUAD[3] },
    { c: BLc, p: QUAD[3], back: 540, fwd: 420, s: 3, toFwd: QUAD[0], toBack: QUAD[2] },
  ];
  const liquid = (nm, col, bl, op, h, kk, pw, m, sp) => {
    contacts.forEach((k, i) => put(`${nm} line ${i}`, band(k.c, k.back * kk, k.fwd * kk, 0, h, { pw, wob: .22, seed: k.s + bl }), col, bl, op));
    contacts.forEach((k, i) => {
      put(`${nm} neck ${i}a`, neck(k, k.toFwd, m, sp, true, bl * 2.4), col, bl, op);
      put(`${nm} neck ${i}b`, neck(k, k.toBack, m, sp, false, bl * 2.4), col, bl, op);
    });
  };

  if (!clear) { group(null); put('Glass body', [TILE], glass); }

  group('Glass wall');
  put('Wall shade', ring(0, 26), dark ? '#2A303C' : '#AEB6C6', 6, dark ? .2 : .25);
  if (!clear) put('Wall clear', ring(30, 46), dark ? '#000000' : '#FFFFFF', 7, dark ? .85 : .9);

  // Colour appears only where the card meets the rim.
  group('Liquid colour', true);
  if (dark) {
    liquid('Deep blue', C.deep, 22, .9, 86, 1, 1.3, 205, 420);
    liquid('Blue', C.blue, 11, 1, 62, .84, 1.3, 175, 350);
    liquid('Cyan', C.cyan, 8, 1, 46, .66, 1.3, 150, 295);
  } else {
    liquid('Yellow', '#FFD53D', 20, 1, 112, 1, 1.3, 225, 460);
    liquid('Orange', C.orange, 15, 1, 82, .94, 1.3, 195, 400);
    liquid('Red', C.red, 11, 1, 70, .86, 1.3, 178, 365);
    liquid('Magenta', C.mag, 8.5, .95, 60, .78, 1.3, 162, 335);
    liquid('Blue', C.blue, 6.5, 1, 50, .7, 1.3, 146, 305);
    liquid('Deep blue', C.deep, 5, 1, 42, .62, 1.3, 134, 280);
  }

  group('Liquid ink');
  liquid('Ink', ink, 3, 1, 34, .5, 1.3, 130, 250);

  // Cards behind: further is closer to the glass, so darker on dark glass and lighter on light glass.
  // Own group without grain: Affinity's noise filter would speckle the black card body inside the photo window.
  group('Front card');
  // Inset, so its edge stays under the white frame and no dark seam shows where the card melts into the rim.
  put('Card body', [poly(QUAD, 24, MARK_R, 0, 0, true)], glass);

  // Photo window: a blurred opening. Its edge is painted as rings, shifted per colour like a lens splits light.
  const WBIG = poly(WIN, -45, OPEN_R, 0, 0, true);
  const win = (d, dx, dy) => [WBIG, poly(WIN, d, OPEN_R, dx, dy)];
  group('Window light', true);
  if (dark) {
    put('Window deep blue', win(17, 14, 11), C.deep, 13, .95);
    put('Window blue', win(13, 11, 8.5), C.blue, 8, 1);
    put('Window cyan', win(8, 8, 6), C.cyan, 5, 1);
    put('Window red', win(8, -11, -8.5), C.red, 5.5, .95);
    put('Window orange', win(5, -7, -5.5), C.orange, 3.6, 1);
    put('Window yellow', win(2.5, -3.6, -2.8), C.yellow, 2.4, 1);
  } else {
    put('Window yellow', win(25, -5, -4), '#FFD53D', 10, 1);
    put('Window orange', win(18, -4, -3), C.orange, 8, 1);
    put('Window red', win(14, -3, -2.4), C.red, 6, 1);
    put('Window magenta', win(11, 0, 0), C.mag, 5, .95);
    put('Window blue', win(8, 3, 2.4), C.blue, 4, 1);
    put('Window deep blue', win(5, 4, 3), C.deep, 3.2, 1);
  }

  // Card, mountains and sun: pure ink with no fringe at all.
  group('Mark');
  put('Window edge', win(dark ? -1 : 1, 0, 0), ink, dark ? 5 : 4);
  put('Card', [CARD, poly(WIN, -15, OPEN_R)], ink);
  put('Mountains', [mountains()], ink);
  put('Sun', [sun()], ink);
  contacts.forEach((k, i) => {
    put(`Neck ${i}a`, neck(k, k.toFwd, 112, 200, true), ink);
    put(`Neck ${i}b`, neck(k, k.toBack, 112, 200, false), ink);
  });

  group('Rim spectrum', true);
  const SPECTRUM = dark ? [['Yellow', C.yellow], ['Orange', C.orange], ['Red', C.red], ['Magenta', C.mag]] : [['Deep blue', C.deep], ['Blue', C.blue], ['Cyan', C.cyan], ['Ice', C.ice]];
  contacts.forEach((k, i) => {
    put(`${SPECTRUM[0][0]} rim ${i}`, band(k.c, k.back * .62, k.fwd * .62, 0, 15, { pw: 1, wob: .2, seed: k.s + 3 }), SPECTRUM[0][1], 3.5, 1);
    put(`${SPECTRUM[1][0]} rim ${i}`, band(k.c, k.back * .82, k.fwd * .82, 0, 10, { pw: .9, wob: .2, seed: k.s + 4 }), SPECTRUM[1][1], 2.4, 1);
    put(`${SPECTRUM[2][0]} rim ${i}`, band(k.c, k.back * .96, k.fwd * .96, 0, 6, { pw: 1, wob: .2, seed: k.s + 5 }), SPECTRUM[2][1], 1.6, 1);
    put(`${SPECTRUM[3][0]} rim ${i}`, band(k.c, k.back * 1.1, k.fwd * 1.1, 0, 2.2, { pw: 1.2, seed: k.s + 6 }), SPECTRUM[3][1], .8, .75);
  });

  group('Speculars');
  const shade = dark ? '#000000' : '#59617A', hi = C.white;
  const SPEC = [[nearestRim(880, 880), 170, 240, 20, 5, 2.4, dark ? .55 : 1], [nearestRim(924, 560), 90, 120, 10, 3.5, 1.4, dark ? .35 : 1], [nearestRim(700, 100), 110, 160, 7, 2.6, 1.1, dark ? .3 : 1]];
  SPEC.forEach(([c, b, f, i, h, bl, op], k) => {
    put(`Spec shade ${k}`, band(c, b, f, i + h, h * .9, { pw: 1 }), shade, bl * 1.4, dark ? .55 : .25);
    put(`Spec ${k}`, band(c, b, f, i, h, { pw: 1 }), hi, bl, op);
  });
  [[nearestRim(860, 164), 130, 110, 5, C.cyan, .65], [nearestRim(860, 164), 110, 90, 8.5, C.pink, .45], [nearestRim(860, 860), 140, 120, 5, C.pink, .55], [nearestRim(860, 860), 130, 110, 8.5, C.cyan, .55]].forEach(([c, b, f, i, col, op], k) =>
    put(`Hairline ${k}`, band(c, b, f, i, 1.8, { pw: 1.2 }), col, .9, op));
  put('Edge line', ring(.4, 2.2), dark ? '#FFFFFF' : '#2B3142', .4, dark ? .26 : .2);
  put('Inner edge', ring(33, 34.4), dark ? '#FFFFFF' : '#2B3142', .8, .07);
  return out;
}

module.exports = { layers, TILE, CARD, MARK_R, flatten };
