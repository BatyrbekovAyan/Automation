// The particle field: one gl.POINTS draw call, every motion computed on the GPU.
//
// Each vertex carries its target in all three shapes (aA/aB/aC) plus a palette slot per
// shape (aTone) and four random numbers (aRnd). The vertex shader interpolates between the
// shapes by uMorph with a per-particle delay, so a morph travels across the field like a
// wave instead of snapping. Nothing here is per-frame CPU work: the only per-frame writes
// are a handful of uniforms.
//
// Shape space is the unit square [-1,1]², y UP (see shapes.js). `resize(w, h, view)` maps it
// to the page: `view.s` is one shape unit in CSS px, `view.cx/cy` the shape centre; the step shapes
// (aB/aC) may have their own unit/centre, `view.ss`/`view.cys` (stage.js stepScale) — one uniform.
import { mulberry32 } from './shapes.js';

const FLOATS = 13; // aA(2) aB(2) aC(2) aTone(3) aRnd(4)
const MAX_DPR = 1.5;

const VS = `
precision highp float;
attribute vec2 aA; attribute vec2 aB; attribute vec2 aC;
attribute vec3 aTone; attribute vec4 aRnd;
uniform float uTime, uMorph, uIntro, uSweep, uDpr, uPt, uS;
uniform vec2 uCenter, uView, uTilt;
uniform vec3 uStep; /* step shapes: scale, y offset, point size */
uniform vec3 uMouse;
varying vec3 vCol; varying float vA; varying float vBlur;
vec3 pal(float t){
  if(t<.5) return vec3(.200,.340,.780);
  if(t<1.5) return vec3(.349,.506,.839);
  if(t<2.5) return vec3(.660,.770,1.00);
  if(t<3.5) return vec3(.620,.670,.740);
  if(t<4.5) return vec3(.145,.827,.400);
  if(t<5.5) return vec3(.165,.671,.933);
  return vec3(.450,.520,.620);
}
float palA(float t){ return t<.5?.56: t<1.5?.68: t<2.5?.98: t<3.5?.72: t<5.5?.70: .18; }
float palS(float t){ return t<.5?.85: t<1.5?.85: t<2.5?1.15: t<3.5?.90: t<5.5?1.00: .80; }
float bright(float t){ return 1. - step(.5, abs(t - 2.)); }
/* per-particle progress: the wave crosses the field left to right (d comes from the x of
   the shape the particle starts in) */
float prog(float m, float k, float d){ float x = clamp((m - k) * 1.7 - d * .7, 0., 1.); return x * x * (3. - 2. * x); }
void main(){
  float d = aRnd.y, ph = aRnd.z * 6.2831853;
  float p0 = prog(uMorph, 0., d), p1 = prog(uMorph, 1., d);
  vec2 B = aB * uStep.x + vec2(0., uStep.y), C = aC * uStep.x + vec2(0., uStep.y);
  vec2 p = mix(mix(aA, B, p0), C, p1);
  vec3 col = mix(mix(pal(aTone.x), pal(aTone.y), p0), pal(aTone.z), p1);
  float al = mix(mix(palA(aTone.x), palA(aTone.y), p0), palA(aTone.z), p1);
  float sz = mix(mix(palS(aTone.x), palS(aTone.y), p0), palS(aTone.z), p1) * mix(1., uStep.z, p0);
  float br = mix(mix(bright(aTone.x), bright(aTone.y), p0), bright(aTone.z), p1);

  /* in flight: every particle arcs to the same side of its path, so the field turns like one
     liquid body instead of scattering; a little curl keeps it alive */
  float s0 = sin(p0 * 3.14159), s1 = sin(p1 * 3.14159), fl = s0 + s1;
  vec2 tv = (B - aA) * s0 + (C - B) * s1;
  p += vec2(-tv.y, tv.x) * (.28 + .3 * aRnd.w);
  p += vec2(sin(ph + uTime * .7 + p.y * 2.3), cos(ph * 1.3 + uTime * .6 + p.x * 2.1)) * fl * (.03 + .09 * aRnd.w);
  col = mix(col, vec3(.72,.82,1.), fl * .35); al *= 1. - fl * .34;
  /* breathing */
  p += .0045 * vec2(sin(uTime * .9 + ph * 3.), cos(uTime * .8 + ph * 2.));

  /* depth: a few particles float off the focal plane and render as soft bokeh */
  float z = (aRnd.w - .5) * .07 + step(.91, aRnd.x) * (aRnd.w - .5) * .7 + fl * (aRnd.x - .5) * .6;

  /* the chosen bar lights up once, swept left to right, and then a slow highlight keeps
     travelling through it */
  float inA = (1. - p0) * bright(aTone.x);
  float lit = 1. - smoothstep(uSweep - .02, uSweep + .22, p.x);
  float front = exp(-pow((p.x - uSweep) * 6., 2.));
  al *= mix(1., mix(.05, 1., lit), inA);
  al += inA * front * 1.1;
  float hx = mod(uTime * .42, 5.2) - 2.4;
  float band = exp(-pow((p.x - hx) * 3.4, 2.)) * br;
  al += band * .45; col = mix(col, vec3(.92,.95,1.), band * .55 + inA * front * .7);

  /* the cursor softly inflates the field */
  vec2 dm = p - uMouse.xy; float fm = exp(-dot(dm, dm) / .075) * uMouse.z;
  p += normalize(dm + 1e-4) * fm * .12; z += fm * .32; al += fm * .02;
  col = mix(col, vec3(.56,.68,.94), fm * .45);

  /* intro: spiral in from a ring just outside the mark — the SVG mark is still on screen while
     they fly (10-stage.css), so they visibly gather around it instead of spraying over the H1 */
  float qi = clamp(uIntro * 1.65 - d * .65, 0., 1.); qi = 1. - pow(1. - qi, 3.);
  float ang = ph + (1. - qi) * 2.3; vec2 st = vec2(cos(ang), sin(ang)) * (1.1 + aRnd.w * 1.2);
  p = mix(st, p, qi); z = mix((aRnd.x - .5) * 3., z, qi); al *= smoothstep(0., .35, qi);

  /* a small 3D tilt with perspective about the shape centre */
  float cy = cos(uTilt.x), sy = sin(uTilt.x), cx = cos(uTilt.y), sx = sin(uTilt.y);
  vec3 q = vec3(p.x * cy + z * sy, p.y, -p.x * sy + z * cy);
  q = vec3(q.x, q.y * cx - q.z * sx, q.y * sx + q.z * cx);
  float w = 1. / max(.3, 1. - q.z * .32);
  float blur = clamp(abs(q.z) * 2.2 - .05, 0., 1.);
  vec2 px = uCenter + vec2(q.x, -q.y) * uS * w;
  gl_Position = vec4(px.x / uView.x * 2. - 1., 1. - px.y / uView.y * 2., 0., 1.);
  gl_PointSize = uPt * uDpr * sz * (.55 + aRnd.x * .9) * w * (1. + blur * 2.6);
  vCol = col; vA = min(al, 1.5) / (1. + blur * 5.); vBlur = blur;
}`;

const FS = `
precision mediump float;
varying vec3 vCol; varying float vA; varying float vBlur;
void main(){
  vec2 c = gl_PointCoord - .5;
  float r = dot(c, c) * 4.;
  if (r > 1.) discard;
  float h = (1. - r) * (1. - r);
  float a = mix(exp(-r * 11.) + .26 * h, h * 1.1, vBlur);
  gl_FragColor = vec4(vCol, a * vA);
}`;

function compile(gl, type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) { console.warn('[field]', gl.getShaderInfoLog(s)); gl.deleteShader(s); return null; }
  return s;
}

export function createField(canvas, { count, dpr = 1, seed = 20260920 } = {}) {
  if (!canvas || !count) return null;
  let gl = null;
  try {
    gl = canvas.getContext('webgl', { alpha: true, antialias: false, depth: false, stencil: false, premultipliedAlpha: true, powerPreference: 'high-performance', failIfMajorPerformanceCaveat: false });
  } catch { gl = null; }
  if (!gl) return null;

  const vs = compile(gl, gl.VERTEX_SHADER, VS), fs = compile(gl, gl.FRAGMENT_SHADER, FS);
  if (!vs || !fs) return null;
  const prog = gl.createProgram();
  gl.attachShader(prog, vs); gl.attachShader(prog, fs); gl.linkProgram(prog);
  gl.deleteShader(vs); gl.deleteShader(fs);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { console.warn('[field]', gl.getProgramInfoLog(prog)); return null; }
  gl.useProgram(prog);

  const data = new Float32Array(count * FLOATS);
  const jitter = new Float32Array(count); // the raw random behind aRnd.y, kept so a re-upload stays stable
  const rand = mulberry32(seed);
  for (let i = 0; i < count; i++) {
    const o = i * FLOATS + 9;
    data[o] = rand(); jitter[i] = rand(); data[o + 1] = jitter[i]; data[o + 2] = rand(); data[o + 3] = rand();
  }
  const buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW); // пишется один раз в upload(), как и при setShapes
  const stride = FLOATS * 4;
  for (const [name, size, off] of [['aA', 2, 0], ['aB', 2, 8], ['aC', 2, 16], ['aTone', 3, 24], ['aRnd', 4, 36]]) {
    const loc = gl.getAttribLocation(prog, name);
    if (loc < 0) continue;
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride, off);
  }
  const U = {};
  for (const n of ['uTime', 'uMorph', 'uIntro', 'uSweep', 'uDpr', 'uPt', 'uS', 'uCenter', 'uView', 'uTilt', 'uMouse', 'uStep']) U[n] = gl.getUniformLocation(prog, n);
  gl.disable(gl.DEPTH_TEST);
  gl.enable(gl.BLEND);
  gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
  gl.clearColor(0, 0, 0, 0);

  let draw = count, ratio = Math.min(dpr, MAX_DPR);
  let W = 1, H = 1, cx = 0, cy = 0, S = 1, ptScale = 1, SS = 1, cys = 0, ptStep = 1;
  let morph = 0, t0 = 0, last = 0, dead = false;
  const mouse = { x: 9, y: 9, s: 0, tx: 9, ty: 9, ts: 0, nx: 0, ny: 0 };

  // Point size follows the mean spacing between the particles actually DRAWN (S / √draw),
  // so a thin field still reads as a solid shape instead of dust — and a 'degrade' that
  // halves the count costs frames, not legibility: √2 bigger points cover the same area.
  function pointScale() {
    const pt = (s) => Math.max(2, Math.min(3.9, 1.55 * s / Math.sqrt(Math.max(1, draw))));
    ptScale = pt(S); ptStep = pt(SS);
  }

  function upload(shapes, toneList) {
    const list = [0, 1, 2].map((k) => shapes[Math.min(k, shapes.length - 1)]);
    const tl = toneList ? [0, 1, 2].map((k) => toneList[Math.min(k, toneList.length - 1)]) : null;
    for (let i = 0; i < count; i++) {
      const o = i * FLOATS;
      for (let k = 0; k < 3; k++) {
        const pts = list[k];
        data[o + k * 2] = pts[2 * i]; data[o + k * 2 + 1] = pts[2 * i + 1];
        data[o + 6 + k] = tl ? tl[k][i] : 1;
      }
      // the morph wave crosses the field left to right: delay from the starting x
      data[o + 10] = Math.min(1, Math.max(0, 0.5 * jitter[i] + 0.5 * ((data[o] + 1) / 2)));
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
  }

  const api = {
    setShapes(shapes, toneList) { if (!dead && shapes && shapes.length) upload(shapes, toneList); },
    setMorph(t) { morph = t; },
    setPointer(x, y, active) {
      mouse.tx = x; mouse.ty = y; mouse.ts = active ? 1 : 0;
      mouse.nx = Math.max(-1, Math.min(1, x / 2)); mouse.ny = Math.max(-1, Math.min(1, y / 2));
    },
    setCount(n) { draw = Math.max(1, Math.min(count, Math.round(n))); pointScale(); },
    setDpr(d) { ratio = Math.min(d, MAX_DPR); api.resize(W, H, { cx, cy, s: S, ss: SS, cys }); },
    get count() { return draw; },
    get dpr() { return ratio; },
    resize(w, h, view) {
      if (dead) return;
      W = Math.max(1, w); H = Math.max(1, h);
      if (view) {
        cx = view.cx; cy = view.cy; S = Math.max(1, view.s);
        SS = view.ss > 0 ? view.ss : S; cys = Number.isFinite(view.cys) ? view.cys : cy;
      }
      const bw = Math.round(W * ratio), bh = Math.round(H * ratio);
      if (canvas.width !== bw || canvas.height !== bh) { canvas.width = bw; canvas.height = bh; }
      gl.viewport(0, 0, bw, bh);
      pointScale();
    },
    render(timeMs) {
      if (dead) return;
      if (!t0) { t0 = timeMs; last = timeMs; }
      const dt = Math.min(0.05, Math.max(0, (timeMs - last) / 1000)); last = timeMs;
      const t = (timeMs - t0) / 1000;
      const k = 1 - Math.exp(-dt * 7);
      mouse.x += (mouse.tx - mouse.x) * k; mouse.y += (mouse.ty - mouse.y) * k;
      mouse.s += (mouse.ts - mouse.s) * (1 - Math.exp(-dt * 3));
      const intro = Math.min(1, Math.max(0, (t - 0.05) / 1.6));
      const sw = Math.min(1, Math.max(0, (t - 1.15) / 1.0));
      const sweep = -1.35 + 2.8 * (1 - Math.pow(1 - sw, 3));
      // the tilt eases out as the mark leaves: a flat field reads better behind text
      const flat = 1 - Math.min(1, Math.max(0, morph / 0.6));
      const tx = (mouse.s * mouse.nx * 0.2 + Math.sin(t * 0.23) * 0.06) * flat;
      const ty = (mouse.s * mouse.ny * 0.12 + Math.cos(t * 0.19) * 0.04) * flat;
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.uniform1f(U.uTime, t); gl.uniform1f(U.uMorph, morph); gl.uniform1f(U.uIntro, intro);
      gl.uniform1f(U.uSweep, sweep); gl.uniform1f(U.uDpr, ratio); gl.uniform1f(U.uPt, ptScale); gl.uniform1f(U.uS, S);
      gl.uniform2f(U.uCenter, cx, cy); gl.uniform2f(U.uView, W, H); gl.uniform2f(U.uTilt, tx, ty);
      gl.uniform3f(U.uMouse, mouse.x, mouse.y, mouse.s);
      gl.uniform3f(U.uStep, SS / S, (cy - cys) / S, ptStep / ptScale);
      gl.drawArrays(gl.POINTS, 0, draw);
    },
    destroy() {
      if (dead) return;
      dead = true;
      try {
        gl.bindBuffer(gl.ARRAY_BUFFER, null);
        gl.deleteBuffer(buffer);
        gl.useProgram(null);
        gl.deleteProgram(prog);
        const lose = gl.getExtension('WEBGL_lose_context');
        if (lose) lose.loseContext();
      } catch {}
    },
  };
  return api;
}
