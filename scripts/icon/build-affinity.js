// Builds the layered icon as a new Affinity document. Run through the Affinity MCP:
// globalThis.theme='dark'|'light'; require('<this file's absolute path>').
'use strict';
const { Colour, ColourStop, Gradient } = require('/colours.js');
const { CurveBuilder, PolyCurve, Transform } = require('/geometry.js');
const N = require('/nodes.js');
const { FillDescriptor, GradientFill, GradientFillType, BlendMode } = require('/fills.js');
const { AddChildNodesCommandBuilder, InsertionMode } = require('/commands.js');
const { GaussianBlurLayerEffect } = require('/layereffects.js');
const { Document, NewDocumentOptions, UnitType } = require('/document.js');
const { layers, TILE } = require('/Users/nikola/Desktop/DAW/assets/brand/jglancer/jglancer-glass.js');

const col = h => Colour.createRGBA8({ r: parseInt(h.slice(1, 3), 16), g: parseInt(h.slice(3, 5), 16), b: parseInt(h.slice(5, 7), 16), alpha: 255 });
function pc(curves) {
  const p = PolyCurve.create();
  for (const c of curves) {
    const b = CurveBuilder.create(); b.beginXY(c.s[0], c.s[1]);
    for (const g of c.g) { if (g.length === 2) b.lineToXY(g[0], g[1]); else b.addBezierXY(...g); }
    b.close(); p.addCurve(b.createCurve());
  }
  return p;
}
// Solid hex, or a linear gradient { from, to, stops: [[offset, hex]] } in canvas space.
// Transform(d0..d5) maps (x, y) to (d0 x + d1 y + d2, d3 x + d4 y + d5); the gradient runs along fill-space x 0..1.
function fillOf(f) {
  if (typeof f === 'string') return FillDescriptor.createSolid(col(f));
  const g = Gradient.create(f.stops.map(([o, c]) => new ColourStop(col(c), o)));
  const dx = f.to[0] - f.from[0], dy = f.to[1] - f.from[1];
  return FillDescriptor.create(GradientFill.create(g, GradientFillType.Linear), false, new Transform(dx, -dy, f.from[0], dy, dx, f.from[1]), BlendMode.Normal, true);
}
// SVG stdDeviation = Affinity radius * this.
const SIGMA_PER_RADIUS = globalThis.sigmaPerRadius || .5;
const GRAIN = globalThis.grain || .5;

const opts = NewDocumentOptions.createDefault();
opts.units = UnitType.Pixel; opts.width = 1024; opts.height = 1024; opts.dpi = 72; opts.isTransparentBackground = true;
const doc = Document.createFromOptions(opts);

const L = layers(theme, false);
const body = L.find(l => l.name === 'Glass body');
doc.addNode(N.PolyCurveNodeDefinition.create(pc([TILE]), FillDescriptor.createSolid(col(body.fill))));
const root = doc.layers.last;
doc.setLayerDescription('Glass body (clip)', doc.selection);

// grained: true (full colour noise) or { amount, mono }.
function closeGroup(g, grained) {
  if (!g || !grained) return;
  const pr = N.AddNoiseFilterParameters.create();
  pr.intensity = GRAIN * (grained === true ? 1 : grained.amount); pr.isMonochromatic = grained !== true && !!grained.mono;
  const b = AddChildNodesCommandBuilder.create(); b.setInsertionTarget(g); b.setInsertionMode(InsertionMode.Inside_AtBack);
  b.addAddNoiseFilterRasterNode(N.AddNoiseFilterRasterNodeDefinition.create(pr));
  doc.executeCommand(b.createCommand(true, N.NodeChildType.Main));
}
let grp = null, cur = null, curGrain = false;
for (const l of L) {
  if (l === body) continue;
  if (l.grp !== cur) {
    closeGroup(grp, curGrain);
    const b = AddChildNodesCommandBuilder.create(); b.setInsertionTarget(root); b.setInsertionMode(InsertionMode.Inside_AtBack);
    b.addContainerNode(N.ContainerNodeDefinition.create(l.grp));
    doc.executeCommand(b.createCommand(true, N.NodeChildType.Main));
    grp = doc.selection.nodes.first; cur = l.grp; curGrain = l.grain;
    // Not passthrough, so the noise stops at this group and never reaches the black-and-white mark.
    if (l.grain) doc.setBlendMode(BlendMode.Normal, false, doc.selection);
  }
  const b = AddChildNodesCommandBuilder.create(); b.setInsertionTarget(grp); b.setInsertionMode(InsertionMode.Inside_AtBack);
  b.addNode(N.PolyCurveNodeDefinition.create(pc(l.cs), fillOf(l.fill)));
  doc.executeCommand(b.createCommand(true, N.NodeChildType.Main));
  const sel = doc.selection; doc.setLayerDescription(l.name, sel);
  if (l.blur) { const e = GaussianBlurLayerEffect.create(); e.radius = l.blur / SIGMA_PER_RADIUS; doc.setGaussianBlurLayerEffect(sel, e); }
  if (l.op < 1) doc.setOpacity(l.op, sel);
}
closeGroup(grp, curGrain);
console.log('built', theme, L.length, 'layers', doc.sessionUuid);
