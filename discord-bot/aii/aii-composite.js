// aii/aii-composite.js
// Generates the 3-panel Discord-embeddable composite PNG.
// Requires: npm install @napi-rs/canvas chartjs-node-canvas chart.js
//
// Panel layout (1200 × 420):
//   [ Radar chart  |  Score + grade  |  Breakdown + anti-patterns ]

'use strict';

const fs   = require('fs');
const path = require('path');

let createCanvas, loadImage, ChartJSNodeCanvas;
let available = false;

try {
  ({ createCanvas, loadImage } = require('@napi-rs/canvas'));
  ({ ChartJSNodeCanvas }       = require('chartjs-node-canvas'));
  available = true;
} catch {
  console.warn('[AII Composite] Missing deps — run: npm install @napi-rs/canvas chartjs-node-canvas chart.js');
}

// ── Layout ────────────────────────────────────────────────────────────────────
const W      = 1200;
const H      = 420;
const HDR_H  = 38;
const BODY_H = H - HDR_H;
const P1_W   = 430;   // radar
const P2_W   = 210;   // score
const P3_W   = W - P1_W - P2_W; // breakdown

// ── Palette (Discord dark) ────────────────────────────────────────────────────
const C = {
  bg:    '#2b2d31',
  bgDim: '#1e2124',
  div:   'rgba(255,255,255,0.07)',
  hi:    '#dcddde',
  md:    '#b9bbbe',
  lo:    '#8a8f97',
};

function scoreColor(v, mx) {
  const p = v / mx;
  if (p >= 0.75) return '#57f287';
  if (p >= 0.50) return '#fee75c';
  return '#ed4245';
}

function grade(t) {
  if (t >= 90) return 'A';
  if (t >= 80) return 'B';
  if (t >= 70) return 'C';
  if (t >= 60) return 'D';
  return 'F';
}

// ── Canvas helpers ────────────────────────────────────────────────────────────

function text(ctx, str, x, y, {font='11px Arial', color=C.lo, align='left', baseline='top'} = {}) {
  ctx.font         = font;
  ctx.fillStyle    = color;
  ctx.textAlign    = align;
  ctx.textBaseline = baseline;
  ctx.fillText(str, x, y);
}

function hline(ctx, y, x1, x2) {
  ctx.strokeStyle = C.div;
  ctx.lineWidth   = 1;
  ctx.beginPath();
  ctx.moveTo(x1, y);
  ctx.lineTo(x2, y);
  ctx.stroke();
}

function vline(ctx, x, y1, y2) {
  ctx.strokeStyle = C.div;
  ctx.lineWidth   = 1;
  ctx.beginPath();
  ctx.moveTo(x, y1);
  ctx.lineTo(x, y2);
  ctx.stroke();
}

function roundRect(ctx, x, y, w, h, r = 3) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y,     x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h,     x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y,         x + r, y);
  ctx.closePath();
}

function progressBar(ctx, x, y, value, max, color, w = 200, h = 5) {
  const fill = Math.max(0, Math.round((value / max) * w));
  ctx.fillStyle = 'rgba(255,255,255,0.09)';
  roundRect(ctx, x, y, w, h, 2);
  ctx.fill();
  if (fill > 0) {
    ctx.fillStyle = color;
    roundRect(ctx, x, y, fill, h, 2);
    ctx.fill();
  }
}

// ── Radar chart config ────────────────────────────────────────────────────────
function radarCfg(result) {
  const a = result.section_a;
  return {
    type: 'radar',
    data: {
      labels: [
        ['Individuals &', 'Interactions'],
        ['Working', 'Software'],
        'Customer Collab.',
        ['Responding', 'to Change'],
      ],
      datasets: [
        {
          label: result.sprint || result.start,
          data: [
            a.individuals_interactions.score,
            a.working_software.score,
            a.customer_collaboration.score,
            a.responding_to_change.score,
          ],
          backgroundColor:    'rgba(88,101,242,0.22)',
          borderColor:        '#5865f2',
          pointBackgroundColor: '#5865f2',
          pointRadius: 4,
        },
        {
          label: 'Max',
          data:  [10, 10, 10, 10],
          backgroundColor: 'rgba(255,255,255,0.02)',
          borderColor:     'rgba(255,255,255,0.11)',
          borderDash: [5, 5],
          pointRadius: 0,
        },
      ],
    },
    options: {
      responsive: false,
      animation:  false,
      scales: {
        r: {
          min: 0, max: 10,
          ticks:       { stepSize: 2, color: C.lo, backdropColor: 'transparent', font: { size: 9 } },
          grid:        { color: 'rgba(255,255,255,0.07)' },
          angleLines:  { color: 'rgba(255,255,255,0.07)' },
          pointLabels: { color: C.md, font: { size: 9 } },
        },
      },
      plugins: { legend: { display: false } },
    },
  };
}

// ── Panel drawers ─────────────────────────────────────────────────────────────

function drawHeader(ctx, result) {
  ctx.fillStyle = C.bgDim;
  ctx.fillRect(0, 0, W, HDR_H);
  hline(ctx, HDR_H, 0, W);

  text(ctx, '\u2605 Agile Integrity Index', 16, HDR_H / 2, {
    font: 'bold 13px Arial', color: C.hi, baseline: 'middle',
  });
  const rhs = `${result.team || 'Team'}  \u00b7  ${result.sprint || result.start}  \u00b7  ${result.start} \u2192 ${result.end}`;
  text(ctx, rhs, W - 16, HDR_H / 2, {
    font: '11px Arial', color: C.lo, align: 'right', baseline: 'middle',
  });
}

function drawScorePanel(ctx, result) {
  const cx  = P1_W + P2_W / 2;
  const top = HDR_H;
  const tot = result.aii_total;
  const g   = grade(tot);
  const gc  = scoreColor(tot, 100);

  // Label
  text(ctx, 'AII SCORE', cx, top + 18, { font: 'bold 9px Arial', color: C.lo, align: 'center' });

  // Big number
  ctx.font         = 'bold 66px Arial';
  ctx.fillStyle    = C.hi;
  ctx.textAlign    = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText(String(tot), cx, top + 34);

  // /100
  text(ctx, '/ 100', cx, top + 108, { font: '13px Arial', color: C.lo, align: 'center' });

  // Progress bar
  const bx = cx - 54;
  progressBar(ctx, bx, top + 130, tot, 100, gc, 108, 4);

  // Grade badge
  const by = top + 148;
  ctx.fillStyle = gc + '28';
  roundRect(ctx, cx - 26, by, 52, 32, 4);
  ctx.fill();
  ctx.strokeStyle = gc + '55';
  ctx.lineWidth   = 1;
  roundRect(ctx, cx - 26, by, 52, 32, 4);
  ctx.stroke();
  ctx.font         = 'bold 22px Arial';
  ctx.fillStyle    = gc;
  ctx.textAlign    = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(g, cx, by + 16);

  // Confidence
  text(ctx, `confidence: ${result.data_confidence || '\u2014'}`, cx, top + 194, {
    font: '10px Arial', color: C.lo, align: 'center',
  });

  // Delta vs previous sprint
  if (result._delta != null) {
    const sign  = result._delta >= 0 ? '+' : '';
    const arrow = result._delta >= 0 ? '\u2191' : '\u2193';
    const dc    = result._delta >= 0 ? '#57f287' : '#ed4245';
    text(ctx, `${arrow} ${sign}${result._delta} vs last sprint`, cx, top + 213, {
      font: '11px Arial', color: dc, align: 'center',
    });
  }
}

function drawBreakdownPanel(ctx, result) {
  const x0   = P1_W + P2_W + 18;
  const barW = P3_W - 38;
  let   y    = HDR_H + 18;

  // Header
  text(ctx, 'SCORE BREAKDOWN', x0, y, { font: 'bold 9px Arial', color: C.lo });
  y += 22;

  const sections = [
    { label: 'A \u00b7 Manifesto',   val: result.section_a.subtotal, max: 40, color: '#5865f2' },
    { label: 'B \u00b7 Practices',   val: result.section_b.subtotal, max: 40, color: '#57f287' },
    { label: 'C \u00b7 Anti-patterns', val: result.section_c.subtotal, max: 20, color: '#fee75c' },
  ];

  for (const s of sections) {
    text(ctx, s.label, x0, y, { font: '11px Arial', color: C.md });
    text(ctx, `${s.val}/${s.max}`, x0 + barW, y, {
      font: 'bold 11px Arial', color: C.hi, align: 'right',
    });
    progressBar(ctx, x0, y + 16, s.val, s.max, s.color, barW, 5);
    y += 40;
  }

  // Divider
  hline(ctx, y - 4, x0, x0 + barW);
  y += 10;

  // Anti-patterns
  text(ctx, 'ANTI-PATTERNS DETECTED', x0, y, { font: 'bold 9px Arial', color: C.lo });
  y += 18;

  const aps = result.section_c.antipatterns || [];
  if (aps.length === 0) {
    text(ctx, '\u2713  None detected', x0, y, { font: '11px Arial', color: '#57f287' });
  } else {
    for (const ap of aps.slice(0, 4)) {
      text(ctx, '\u26a0', x0, y, { font: '11px Arial', color: '#fee75c' });
      text(ctx, ap.label, x0 + 18, y, { font: '11px Arial', color: C.hi });
      text(ctx, `\u2212${ap.deduction}`, x0 + barW, y, {
        font: '11px Arial', color: C.lo, align: 'right',
      });
      y += 20;
    }
  }
}

// ── Main export ───────────────────────────────────────────────────────────────

async function generateCompositePNG(result, history, outputPath) {
  if (!available) {
    console.warn('[AII Composite] Skipping PNG — run: npm install @napi-rs/canvas chartjs-node-canvas chart.js');
    return null;
  }

  // Attach delta vs previous sprint for same team
  const prev = [...history]
    .filter(e => e.team === result.team && e.start < result.start)
    .sort((a, b) => b.start.localeCompare(a.start))[0];
  result._delta = prev ? result.aii_total - prev.aii_total : null;

  // Render radar into a buffer using chartjs-node-canvas
  const radarCanvas = new ChartJSNodeCanvas({
    width: P1_W, height: BODY_H, backgroundColour: 'transparent',
  });
  const radarBuf = await radarCanvas.renderToBuffer(radarCfg(result));

  // Main canvas
  const canvas = createCanvas(W, H);
  const ctx    = canvas.getContext('2d');

  // Background
  ctx.fillStyle = C.bg;
  ctx.fillRect(0, 0, W, H);

  // Header strip
  drawHeader(ctx, result);

  // Radar panel
  const radarImg = await loadImage(radarBuf);
  ctx.drawImage(radarImg, 0, HDR_H, P1_W, BODY_H);

  // Panel dividers
  vline(ctx, P1_W,         HDR_H, H);
  vline(ctx, P1_W + P2_W,  HDR_H, H);

  // Score panel
  drawScorePanel(ctx, result);

  // Breakdown panel
  drawBreakdownPanel(ctx, result);

  // Write file
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, canvas.toBuffer('image/png'));

  return outputPath;
}

module.exports = { generateCompositePNG };