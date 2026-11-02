// Writes the true-vector SVGs: native clip, blur and live noise only, no rasters. Run: node build-svg.js [outDir]
'use strict';
const fs = require('fs'), path = require('path');
const { layers, TILE } = require('./jglancer-glass.js');
const out = process.argv[2] || __dirname;
const f = v => +v.toFixed(1);
const d = cs => cs.map(c => 'M' + f(c.s[0]) + ' ' + f(c.s[1]) + c.g.map(g => (g.length === 2 ? 'L' : 'C') + g.map(f).join(' ')).join('') + 'Z').join('');
const id = b => 'b' + String(b).replace('.', '_');
function svg(theme, clear) {
  const L = layers(theme, clear);
  const blurs = [...new Set(L.map(l => l.blur).filter(Boolean))];
  const defs = blurs.map(b => `<filter id="${id(b)}" filterUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="${f(b)}"/></filter>`).join('');
  // Noise strength follows the layer's alpha, so faint glow tails stay clean instead of turning to grey speckle.
  // Weaker grain scales the noise contrast around mid-grey; mono takes one noise channel (alpha) for all three.
  const grainFilter = (fid, amount = 1, mono = false) => {
    const k = +(1.5 * amount).toFixed(3), o = +(.5 - .75 * amount).toFixed(3);
    const m = mono ? `0 0 0 ${k} ${o} 0 0 0 ${k} ${o} 0 0 0 ${k} ${o} 0 0 0 0 1` : `${k} 0 0 0 ${o} 0 ${k} 0 0 ${o} 0 0 ${k} 0 ${o} 0 0 0 0 1`;
    return GRAIN_TPL.replace('id="grain"', `id="${fid}"`).replace('NOISE', m);
  };
  const GRAIN_TPL = '<filter id="grain" filterUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024" color-interpolation-filters="sRGB"><feColorMatrix in="SourceGraphic" values="1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 0 0 1" result="o"/><feTurbulence type="fractalNoise" baseFrequency="1.1" numOctaves="2" seed="11"/><feColorMatrix values="NOISE" result="n"/><feBlend in="n" in2="o" mode="overlay" result="m"/><feComposite in="m" in2="SourceGraphic" operator="in" result="g"/><feFlood flood-color="#fff"/><feComposite in2="SourceGraphic" operator="in" result="a"/><feComposite in="g" in2="a" operator="arithmetic" k1="1" result="x"/><feComposite in="SourceGraphic" in2="a" operator="arithmetic" k1="1" result="y"/><feComposite in="SourceGraphic" in2="y" operator="arithmetic" k2="1" k3="-1" result="z"/><feComposite in="z" in2="x" operator="arithmetic" k2="1" k3="1"/></filter>';
  const gid = g => g === true ? 'grain' : `grain-${g.amount}${g.mono ? 'm' : ''}`.replace('.', '_');
  const grains = [...new Map(L.filter(l => l.grain).map(l => [gid(l.grain), l.grain])).entries()]
    .map(([fid, g]) => g === true ? grainFilter(fid) : grainFilter(fid, g.amount, g.mono)).join('');
  // Gradient fills: { from, to, stops: [[offset, colour]] } in canvas space.
  const grads = L.filter(l => typeof l.fill === 'object').map((l, i) => { l.gid = 'g' + i; const [x1, y1] = l.fill.from.map(f), [x2, y2] = l.fill.to.map(f);
    return `<linearGradient id="${l.gid}" gradientUnits="userSpaceOnUse" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}">${l.fill.stops.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('')}</linearGradient>`; }).join('');
  const one = l => `<path id="${l.name.replace(/\s+/g, '-')}" fill="${l.gid ? `url(#${l.gid})` : l.fill}" d="${d(l.cs)}"${l.blur ? ` filter="url(#${id(l.blur)})"` : ''}${l.op < 1 ? ` opacity="${l.op}"` : ''}/>`;
  const parts = []; let open = null;
  for (const l of L) {
    if (l.grp !== open) {
      if (open) parts.push('</g>');
      if (l.grp) parts.push(`<g id="${l.grp.replace(/\s+/g, '-')}"${l.grain ? ` filter="url(#${gid(l.grain)})"` : ''}>`);
      open = l.grp;
    }
    parts.push(one(l));
  }
  if (open) parts.push('</g>');
  const body = parts.join('\n');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024"><title>jGlancer</title>\n<defs><clipPath id="tile"><path d="${d([TILE])}"/></clipPath>${defs}${grads}${grains}</defs>\n<g clip-path="url(#tile)">\n${body}\n</g></svg>\n`;
}
for (const [n, t, c] of [['jglancer-glass-dark', 'dark', false], ['jglancer-glass-light', 'light', false]]) {
  const s = svg(t, c); fs.writeFileSync(path.join(out, n + '.svg'), s); console.log(n, (s.length / 1024).toFixed(0) + ' KB');
}
