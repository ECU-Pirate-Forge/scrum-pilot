// aii/aii-dashboard.js
// Generates a self-contained, team-scoped HTML dashboard from aii-scores.json.
// The file embeds all data inline — no server required, opens from anywhere.

'use strict';

const RADAR_LABELS_JS = JSON.stringify([
  ['Individuals &', 'Interactions'],
  ['Working', 'Software'],
  'Customer Collab.',
  ['Responding', 'to Change'],
]);

const AP_KEYS_JS = JSON.stringify([
  { key: 'zombie_scrum',              label: 'Zombie Scrum',            max: 5 },
  { key: 'no_real_dod',               label: 'No Real DoD',             max: 4 },
  { key: 'carryover_work',            label: 'Carryover Work',          max: 3 },
  { key: 'status_reporting_standup',  label: 'Status-Rpt Standup',      max: 3 },
  { key: 'overloaded_sprint',         label: 'Overloaded Sprint',       max: 2 },
  { key: 'multitasking',              label: 'Multitasking',            max: 2 },
  { key: 'po_as_project_manager',     label: 'PO as Project Mgr',       max: 1 },
]);

/**
 * @param {Array}  scores    Full aii-scores.json contents, pre-filtered to one team
 * @param {string} teamName  The team this dashboard is for
 * @param {string} [defaultSprint]  Sprint name to pre-select (optional)
 * @returns {string}  Complete HTML document
 */
function generateDashboardHTML(scores, teamName, defaultSprint = null) {
  // Filter to this team only — hard line, never expose other teams
  const teamScores = scores
    .filter(e => e.team === teamName)
    .sort((a, b) => a.start.localeCompare(b.start));

  const dataJSON    = JSON.stringify(teamScores);
  const defaultSel  = JSON.stringify(defaultSprint || teamScores[teamScores.length - 1]?.sprint || null);
  const generated   = new Date().toLocaleString('en-US', {
    timeZone: 'America/New_York', timeZoneName: 'short',
  });

  return /* html */`<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>${teamName} — AII Dashboard</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@300;500;700&family=JetBrains+Mono:wght@400;700&display=swap" rel="stylesheet">
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.2/dist/chart.umd.min.js"></script>
<style>
:root{--bg0:#08090c;--bg1:#0e1016;--bg2:#151821;--bg3:#1c2030;--border:rgba(0,212,255,.12);--border-hi:rgba(0,212,255,.35);--cyan:#00d4ff;--green:#00ff88;--yellow:#ffd700;--red:#ff4060;--purple:#7f77dd;--teal:#1d9e75;--amber:#ba7517;--text:#c8d0e0;--text-dim:#5a6480;--text-hi:#eef0f8;--font-head:'Barlow Condensed',sans-serif;--font-mono:'JetBrains Mono',monospace;--r:6px}
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
body{background:var(--bg0);color:var(--text);font-family:var(--font-mono);font-size:13px;line-height:1.6;min-height:100vh}
body::before{content:'';position:fixed;inset:0;background:repeating-linear-gradient(0deg,transparent,transparent 2px,rgba(0,0,0,.06) 2px,rgba(0,0,0,.06) 4px);pointer-events:none;z-index:0}
.wrap{position:relative;z-index:1;max-width:1400px;margin:0 auto;padding:24px 20px 56px}
.header{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;margin-bottom:24px;padding-bottom:18px;border-bottom:1px solid var(--border);flex-wrap:wrap}
.logo-eye{font-size:10px;font-weight:700;letter-spacing:.2em;text-transform:uppercase;color:var(--cyan);margin-bottom:2px}
.logo-h1{font-family:var(--font-head);font-size:36px;font-weight:700;color:var(--text-hi);line-height:1;letter-spacing:.02em}
.logo-sub{font-size:11px;color:var(--text-dim);margin-top:4px}
.filter-group{display:flex;flex-direction:column;gap:4px}
.filter-group label{font-size:10px;font-weight:700;letter-spacing:.15em;text-transform:uppercase;color:var(--text-dim)}
select{appearance:none;background:var(--bg2);border:1px solid var(--border);border-radius:var(--r);color:var(--text-hi);font-family:var(--font-mono);font-size:12px;padding:7px 32px 7px 12px;cursor:pointer;outline:none;min-width:180px;background-image:url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='7' viewBox='0 0 10 7'%3E%3Cpath fill='%2300d4ff' d='M0 0l5 7 5-7z'/%3E%3C/svg%3E");background-repeat:no-repeat;background-position:right 10px center;transition:border-color .2s}
select:hover,select:focus{border-color:var(--border-hi)}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin-bottom:20px}
.card{background:var(--bg2);border:1px solid var(--border);border-radius:var(--r);padding:14px 16px;position:relative;overflow:hidden;transition:border-color .2s}
.card::before{content:'';position:absolute;top:0;left:0;right:0;height:2px}
.card:hover{border-color:var(--border-hi)}
.card-label{font-size:10px;font-weight:700;letter-spacing:.15em;text-transform:uppercase;color:var(--text-dim);margin-bottom:5px}
.card-score{font-family:var(--font-head);font-size:40px;font-weight:700;line-height:1;color:var(--text-hi)}
.card-score .dn{font-size:18px;font-weight:300;color:var(--text-dim)}
.card-bar{height:3px;background:var(--bg3);border-radius:2px;margin:7px 0 5px;overflow:hidden}
.card-bar div{height:100%;border-radius:2px;transition:width .5s ease}
.card-sub{font-size:10px;color:var(--text-dim)}
.sec-title{font-family:var(--font-head);font-size:11px;font-weight:700;letter-spacing:.2em;text-transform:uppercase;color:var(--cyan);margin-bottom:10px;padding-bottom:6px;border-bottom:1px solid var(--border)}
.chart-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-bottom:18px}
.chart-card{background:var(--bg2);border:1px solid var(--border);border-radius:var(--r);padding:18px}
.chart-card h3{font-family:var(--font-head);font-size:12px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--text-dim);margin-bottom:14px}
.hm-wrap{background:var(--bg2);border:1px solid var(--border);border-radius:var(--r);padding:18px;margin-bottom:18px;overflow-x:auto}
.hm-wrap h3{font-family:var(--font-head);font-size:12px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--text-dim);margin-bottom:14px}
.hm-cell{height:26px;border-radius:3px;display:flex;align-items:center;justify-content:center;font-size:10px;font-weight:700;cursor:default;transition:transform .1s}
.hm-cell:hover{transform:scale(1.08)}
.hm0{background:rgba(0,255,136,.1);color:rgba(0,255,136,.55)}
.hm1{background:rgba(186,117,23,.16);color:#ba7517}
.hm2{background:rgba(162,45,45,.2);color:#e26060}
.hm3{background:rgba(255,64,96,.28);color:#ff4060}
.dt-wrap{background:var(--bg2);border:1px solid var(--border);border-radius:var(--r);overflow:hidden}
.dt-wrap h3{font-family:var(--font-head);font-size:12px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--text-dim);padding:14px 18px;border-bottom:1px solid var(--border)}
.dt{width:100%;border-collapse:collapse}
.dt th{font-size:10px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--text-dim);padding:9px 14px;text-align:left;border-bottom:1px solid var(--border);background:var(--bg1)}
.dt td{padding:9px 14px;border-bottom:1px solid rgba(255,255,255,.04);font-size:12px}
.dt tbody tr:hover td{background:var(--bg3)}
.pill{display:inline-block;padding:2px 8px;border-radius:3px;font-size:11px;font-weight:700}
.pg{background:rgba(0,255,136,.12);color:#00ff88}
.py{background:rgba(255,215,0,.12);color:#ffd700}
.pr{background:rgba(255,64,96,.14);color:#ff4060}
.footer{margin-top:28px;padding-top:14px;border-top:1px solid var(--border);font-size:10px;color:var(--text-dim);display:flex;justify-content:space-between;flex-wrap:wrap;gap:6px}
@media(max-width:780px){.chart-grid{grid-template-columns:1fr}.cards{grid-template-columns:1fr 1fr}}
@media(max-width:480px){.cards{grid-template-columns:1fr}.logo-h1{font-size:26px}}
</style>
</head>
<body>
<div class="wrap">

<header class="header">
  <div>
    <div class="logo-eye">ECU Pirate Forge &middot; Scrumlord</div>
    <h1 class="logo-h1">${teamName}</h1>
    <div class="logo-sub">Agile Integrity Index &mdash; generated ${generated}</div>
  </div>
  <div class="filter-group">
    <label for="sSel">Sprint</label>
    <select id="sSel">
      <option value="__all__">All sprints</option>
    </select>
  </div>
</header>

<div class="cards">
  <div class="card" style="--top-color:var(--cyan)" id="cAII">
    <div class="card-label">AII Total</div>
    <div class="card-score" id="vTotal">&mdash;<span class="dn">/100</span></div>
    <div class="card-bar"><div id="bTotal"></div></div>
    <div class="card-sub" id="vGrade">&mdash;</div>
  </div>
  <div class="card">
    <div class="card-label">A &middot; Manifesto</div>
    <div class="card-score" id="vA">&mdash;<span class="dn">/40</span></div>
    <div class="card-bar"><div id="bA" style="background:var(--purple)"></div></div>
    <div class="card-sub">values alignment</div>
  </div>
  <div class="card">
    <div class="card-label">B &middot; Practices</div>
    <div class="card-score" id="vB">&mdash;<span class="dn">/40</span></div>
    <div class="card-bar"><div id="bB" style="background:var(--teal)"></div></div>
    <div class="card-sub">ceremony adherence</div>
  </div>
  <div class="card">
    <div class="card-label">C &middot; Anti-patterns</div>
    <div class="card-score" id="vC">&mdash;<span class="dn">/20</span></div>
    <div class="card-bar"><div id="bC" style="background:var(--amber)"></div></div>
    <div class="card-sub" id="vAP">&mdash;</div>
  </div>
  <div class="card">
    <div class="card-label">Cumulative avg</div>
    <div class="card-score" id="vAvg">&mdash;<span class="dn">/100</span></div>
    <div class="card-bar"><div id="bAvg" style="background:var(--cyan)"></div></div>
    <div class="card-sub">across all sprints</div>
  </div>
</div>

<p class="sec-title">Trend &amp; Composition</p>
<div class="chart-grid">
  <div class="chart-card">
    <h3>AII trend &mdash; all sprints</h3>
    <div style="position:relative;width:100%;height:200px">
      <canvas id="cTrend" role="img" aria-label="Line chart of AII total score per sprint">Sprint trend.</canvas>
    </div>
  </div>
  <div class="chart-card">
    <h3>Score composition by sprint</h3>
    <div style="position:relative;width:100%;height:200px">
      <canvas id="cStack" role="img" aria-label="Stacked bar of A, B, C scores per sprint">Score stack.</canvas>
    </div>
  </div>
</div>

<p class="sec-title">Sprint Detail &mdash; <span id="detailLbl">latest sprint</span></p>
<div class="chart-grid">
  <div class="chart-card">
    <h3>Section A &middot; Manifesto value alignment</h3>
    <div style="position:relative;width:100%;height:230px">
      <canvas id="cRadar" role="img" aria-label="Radar chart of four Agile Manifesto value scores">Section A radar.</canvas>
    </div>
  </div>
  <div class="chart-card">
    <h3>Section B &middot; Scrum practice adherence</h3>
    <div style="position:relative;width:100%;height:230px">
      <canvas id="cBar" role="img" aria-label="Bar chart of five Scrum ceremony scores">Section B bar.</canvas>
    </div>
  </div>
</div>

<p class="sec-title">Anti-Pattern Heatmap &mdash; All Sprints</p>
<div class="hm-wrap">
  <h3>Detection log by sprint</h3>
  <table id="hmTable" style="width:100%;border-collapse:collapse"></table>
</div>

<p class="sec-title">Full Sprint Record</p>
<div class="dt-wrap">
  <h3>All scored sprints</h3>
  <div style="overflow-x:auto">
    <table class="dt"><thead><tr>
      <th>Sprint</th><th>Period</th>
      <th>A /40</th><th>B /40</th><th>C /20</th>
      <th>Total</th><th>Grade</th><th>Confidence</th><th>Anti-patterns</th>
    </tr></thead><tbody id="dtBody"></tbody></table>
  </div>
</div>

<footer class="footer">
  <span>Agile Integrity Index &middot; Scrumlord AII Engine &middot; ${teamName}</span>
  <span>${teamScores.length} sprint(s) scored</span>
</footer>

</div>
<script>
const DATA   = ${dataJSON};
const AKEYS  = ${AP_KEYS_JS};
const RLBLS  = ${RADAR_LABELS_JS};
const DEFSEL = ${defaultSel};

const isDark = true; // dashboard always uses dark theme
const tx  = '#5a6480';
const grd = 'rgba(255,255,255,.06)';

function sc(v,mx){const p=v/mx;return p>=.75?'#00ff88':p>=.5?'#ffd700':'#ff4060'}
function grade(t){return t>=90?'A':t>=80?'B':t>=70?'C':t>=60?'D':'F'}
function gradeColor(g){return{A:'#00ff88',B:'#57f287',C:'#ffd700',D:'#ff8c00',F:'#ff4060'}[g]||'#ccc'}
function pillCls(v,mx){const p=v/mx;return p>=.75?'pg':p>=.5?'py':'pr'}

let charts={};
function kill(k){if(charts[k]){charts[k].destroy();charts[k]=null;}}

function detail(){
  const s=document.getElementById('sSel').value;
  if(s!=='__all__') return DATA.find(e=>e.sprint===s)||DATA[DATA.length-1];
  return DATA[DATA.length-1];
}

function updateCards(){
  const e=detail();
  if(!e) return;
  const tot=e.aii_total, A=e.section_a.subtotal, B=e.section_b.subtotal, C=e.section_c.subtotal;
  const g=grade(tot), gc=sc(tot,100);
  document.getElementById('vTotal').innerHTML=tot+'<span class="dn">/100</span>';
  document.getElementById('bTotal').style.cssText='background:'+gc+';width:'+tot+'%';
  document.getElementById('vGrade').innerHTML='<span style="font-family:var(--font-head);font-size:20px;font-weight:700;color:'+gc+'">'+g+'</span>&nbsp;&nbsp;conf: '+e.data_confidence;
  document.getElementById('vA').innerHTML=A+'<span class="dn">/40</span>';
  document.getElementById('bA').style.width=(A/40*100)+'%';
  document.getElementById('vB').innerHTML=B+'<span class="dn">/40</span>';
  document.getElementById('bB').style.width=(B/40*100)+'%';
  document.getElementById('vC').innerHTML=C+'<span class="dn">/20</span>';
  document.getElementById('bC').style.width=(C/20*100)+'%';
  const aps=e.section_c.antipatterns||[];
  document.getElementById('vAP').textContent=aps.length?aps.length+' detected':'\u2713 none detected';
  const avg=Math.round(DATA.reduce((a,d)=>a+d.aii_total,0)/DATA.length);
  document.getElementById('vAvg').innerHTML=avg+'<span class="dn">/100</span>';
  document.getElementById('bAvg').style.width=avg+'%';
  document.getElementById('detailLbl').textContent=e.sprint+' \u00b7 '+e.start+' \u2192 '+e.end;
}

function buildTrend(){
  kill('trend');
  const totals=DATA.map(e=>e.aii_total);
  const avg=Math.round(totals.reduce((a,b)=>a+b,0)/totals.length);
  charts.trend=new Chart(document.getElementById('cTrend'),{
    type:'line',
    data:{labels:DATA.map(e=>e.sprint),datasets:[
      {label:'AII Total',data:totals,borderColor:'#00d4ff',backgroundColor:'rgba(0,212,255,.07)',
       tension:.3,fill:true,pointRadius:5,pointHoverRadius:7},
      {label:'Avg ('+avg+')',data:DATA.map(()=>avg),
       borderColor:'rgba(255,255,255,.2)',borderDash:[5,4],pointRadius:0}
    ]},
    options:{responsive:true,maintainAspectRatio:false,animation:false,
      scales:{y:{min:0,max:100,grid:{color:grd},ticks:{color:tx,stepSize:20}},
              x:{grid:{display:false},ticks:{color:tx}}},
      plugins:{legend:{labels:{color:tx,boxWidth:10,font:{size:10},padding:12}}}}
  });
}

function buildStack(){
  kill('stack');
  charts.stack=new Chart(document.getElementById('cStack'),{
    type:'bar',
    data:{labels:DATA.map(e=>e.sprint),datasets:[
      {label:'A \u00b7 Manifesto',  data:DATA.map(e=>e.section_a.subtotal),backgroundColor:'rgba(127,119,221,.75)',stack:'s',borderRadius:2},
      {label:'B \u00b7 Practices',  data:DATA.map(e=>e.section_b.subtotal),backgroundColor:'rgba(29,158,117,.65)',stack:'s',borderRadius:2},
      {label:'C \u00b7 Anti-pat.',  data:DATA.map(e=>e.section_c.subtotal),backgroundColor:'rgba(186,117,23,.65)',stack:'s',borderRadius:2}
    ]},
    options:{responsive:true,maintainAspectRatio:false,animation:false,
      scales:{y:{stacked:true,min:0,max:100,grid:{color:grd},ticks:{color:tx}},
              x:{stacked:true,grid:{display:false},ticks:{color:tx}}},
      plugins:{legend:{position:'bottom',labels:{color:tx,boxWidth:10,font:{size:10},padding:12}}}}
  });
}

function buildRadar(){
  kill('radar');
  const e=detail(), a=e.section_a;
  charts.radar=new Chart(document.getElementById('cRadar'),{
    type:'radar',
    data:{labels:RLBLS,datasets:[
      {label:e.sprint,data:[a.individuals_interactions.score,a.working_software.score,
                             a.customer_collaboration.score,a.responding_to_change.score],
       backgroundColor:'rgba(88,101,242,.2)',borderColor:'#5865f2',
       pointBackgroundColor:'#5865f2',pointRadius:4},
      {label:'Max',data:[10,10,10,10],backgroundColor:'rgba(255,255,255,.02)',
       borderColor:'rgba(255,255,255,.1)',borderDash:[5,5],pointRadius:0}
    ]},
    options:{responsive:true,maintainAspectRatio:false,animation:false,
      scales:{r:{min:0,max:10,
        ticks:{stepSize:2,color:tx,backdropColor:'transparent',font:{size:9}},
        grid:{color:grd},angleLines:{color:grd},
        pointLabels:{color:'#b9bbbe',font:{size:9}}}},
      plugins:{legend:{display:false}}}
  });
}

function buildBar(){
  kill('bar');
  const e=detail(), b=e.section_b;
  const keys=['sprint_planning','daily_scrum','sprint_review','sprint_retrospective','backlog_refinement'];
  const labels=['Planning','Daily Scrum','Review','Retro','Refinement'];
  const scores=keys.map(k=>b[k].score);
  charts.bar=new Chart(document.getElementById('cBar'),{
    type:'bar',
    data:{labels,datasets:[
      {label:'Score',data:scores,backgroundColor:scores.map(s=>sc(s,8)+'bb'),borderRadius:3},
      {label:'Max (8)',data:[8,8,8,8,8],backgroundColor:'rgba(128,128,128,.07)',
       borderColor:'rgba(128,128,128,.2)',borderWidth:1,borderRadius:3}
    ]},
    options:{responsive:true,maintainAspectRatio:false,animation:false,
      scales:{y:{min:0,max:8,grid:{color:grd},ticks:{color:tx,stepSize:2}},
              x:{grid:{display:false},ticks:{color:tx}}},
      plugins:{legend:{display:false}}}
  });
}

function buildHeatmap(){
  let h='<thead><tr><th style="text-align:left;font-size:10px;padding:5px 10px;color:var(--text-dim);font-weight:700;text-transform:uppercase;letter-spacing:.08em;min-width:160px">Anti-pattern</th>';
  DATA.forEach(e=>{h+='<th style="font-size:10px;padding:5px 8px;color:var(--text-dim);font-weight:700;text-align:center">'+e.sprint+'</th>';});
  h+='</tr></thead><tbody>';
  AKEYS.forEach(({key,label})=>{
    h+='<tr><td style="font-size:11px;padding:4px 10px;color:var(--text)">'+label+'</td>';
    DATA.forEach(e=>{
      const ap=(e.section_c.antipatterns||[]).find(a=>a.key===key);
      const d=ap?ap.deduction:0;
      const cls=d===0?'hm0':d===1?'hm1':d<=3?'hm2':'hm3';
      h+='<td style="padding:3px 5px"><div class="hm-cell '+cls+'" title="'+(ap?ap.evidence||'':'')+'">'+(d===0?'&mdash;':'&minus;'+d)+'</div></td>';
    });
    h+='</tr>';
  });
  document.getElementById('hmTable').innerHTML=h+'</tbody>';
}

function buildTable(){
  let h='';
  DATA.forEach(e=>{
    const g=grade(e.aii_total), gc=gradeColor(g);
    const aps=(e.section_c.antipatterns||[]).map(a=>a.label).join(', ')||'&mdash;';
    h+='<tr>'
      +'<td style="font-weight:700">'+e.sprint+'</td>'
      +'<td style="color:var(--text-dim);font-size:11px">'+e.start+' &rarr; '+e.end+'</td>'
      +'<td><span class="pill '+pillCls(e.section_a.subtotal,40)+'">'+e.section_a.subtotal+'</span></td>'
      +'<td><span class="pill '+pillCls(e.section_b.subtotal,40)+'">'+e.section_b.subtotal+'</span></td>'
      +'<td><span class="pill '+pillCls(e.section_c.subtotal,20)+'">'+e.section_c.subtotal+'</span></td>'
      +'<td><span class="pill '+pillCls(e.aii_total,100)+'">'+e.aii_total+'</span></td>'
      +'<td style="font-family:var(--font-head);font-size:20px;font-weight:700;color:'+gc+'">'+g+'</td>'
      +'<td style="color:var(--text-dim)">'+e.data_confidence+'</td>'
      +'<td style="font-size:11px;color:var(--text-dim)">'+aps+'</td>'
      +'</tr>';
  });
  document.getElementById('dtBody').innerHTML=h;
}

// Card top-border colours via CSS custom property
document.querySelectorAll('.card').forEach((c,i)=>{
  const cols=['var(--cyan)','var(--purple)','var(--teal)','var(--amber)','rgba(255,255,255,.25)'];
  c.style.setProperty('--tc', cols[i]||cols[4]);
  c.style.borderTop='2px solid '+c.style.getPropertyValue('--tc');
  c.style.borderTopColor=cols[i]||cols[4];
});

function render(){
  updateCards(); buildTrend(); buildStack(); buildRadar(); buildBar(); buildHeatmap(); buildTable();
}

// Populate sprint selector
const sSel=document.getElementById('sSel');
DATA.forEach(e=>{
  const o=document.createElement('option');
  o.value=e.sprint; o.textContent=e.sprint;
  if(e.sprint===DEFSEL) o.selected=true;
  sSel.appendChild(o);
});
sSel.addEventListener('change', render);
render();
</script>
</body>
</html>`;
}

module.exports = { generateDashboardHTML };