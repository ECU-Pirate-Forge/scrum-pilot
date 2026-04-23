'use strict';
// aii/aii.js
// Agile Integrity Index — scoring engine, Discord embed, and !aii* command handlers.

const { EmbedBuilder, AttachmentBuilder } = require('discord.js');
const fs   = require('fs');
const path = require('path');

// AII scoring uses claude-sonnet-4-6 directly (intentionally bypasses callAI):
//   • Requires a different model than the summarization pipeline.
//   • Must produce raw JSON — no fallback to GPT-4o-mini which would need
//     its own JSON-enforcement logic.
const { anthropic }              = require('../ai');
const { downloadAttachment, estDateOf, isInRange } = require('../utils');
const {
  CHAT_SUMMARIES_CHANNEL,
  RECAP_CHANNEL,
  SPRINT_CHANNEL,
  SPRINT_EXCLUDE_TEAMS,
  SPRINT_SCHEDULE,
} = require('../config');

const STORE_PATH = path.join(__dirname, '../data/aii-scores.json');

// ── Optional visual generators (graceful degradation) ─────────────────────────

let generateCompositePNG  = null;
let generateDashboardHTML = null;

try {
  ({ generateCompositePNG  } = require('./aii-composite'));
  console.log('[AII] Composite PNG enabled.');
} catch (err) {
  console.warn('[AII] aii-composite unavailable (npm install @napi-rs/canvas chartjs-node-canvas chart.js):', err.message);
}

try {
  ({ generateDashboardHTML } = require('./aii-dashboard'));
  console.log('[AII] Dashboard HTML enabled.');
} catch (err) {
  console.warn('[AII] aii-dashboard unavailable:', err.message);
}

// ═══════════════════════════════════════════════════════════════════════════════
// STORE
// ═══════════════════════════════════════════════════════════════════════════════

function loadStore() {
  try {
    fs.mkdirSync(path.dirname(STORE_PATH), { recursive: true });
    return JSON.parse(fs.readFileSync(STORE_PATH, 'utf8'));
  } catch {
    return [];
  }
}

function saveStore(data) {
  fs.mkdirSync(path.dirname(STORE_PATH), { recursive: true });
  fs.writeFileSync(STORE_PATH, JSON.stringify(data, null, 2));
}

function appendScore(entry) {
  const store    = loadStore();
  const filtered = store.filter(
    e => !(e.team === entry.team && e.start === entry.start && e.end === entry.end)
  );
  filtered.push(entry);
  filtered.sort((a, b) =>
    a.start.localeCompare(b.start) || (a.team || '').localeCompare(b.team || '')
  );
  saveStore(filtered);
}

function removeScore(teamName, start, end) {
  const store = loadStore();
  saveStore(store.filter(
    e => !(e.team === teamName && e.start === start && e.end === end)
  ));
}

function getHistory(teamName) {
  return loadStore()
    .filter(e => !teamName || e.team === teamName)
    .sort((a, b) => a.start.localeCompare(b.start));
}

function isAlreadyScored(teamName, start, end) {
  return loadStore().some(e => e.team === teamName && e.start === start && e.end === end);
}

// ═══════════════════════════════════════════════════════════════════════════════
// SCORING PROMPT
// ═══════════════════════════════════════════════════════════════════════════════

function buildScoringPrompt(summaryText, teamName, sprintName, start, end, dailySummaries = []) {
  const dailyBlock = dailySummaries.length > 0
    ? `\n═══════════════════════════════════════════
DAILY ACTIVITY SUMMARIES (primary evidence source):
═══════════════════════════════════════════
These are day-by-day channel summaries for this team during the sprint.
Use these as your primary evidence for scoring. The sprint summary below
is a synthesis — these dailies are the raw record.

${dailySummaries.map(d => `--- ${d.label} ---\n${d.content.trim()}`).join('\n\n')}

`
    : '';

  return `You are an Agile Integrity Index (AII) evaluator. Analyze the inputs below and return ONLY a valid JSON object — no preamble, no markdown fences, no text outside the JSON braces.

Sprint : ${sprintName || `${start} → ${end}`}
Team   : ${teamName  || 'Unknown'}
Period : ${start} → ${end}

${dailyBlock}═══════════════════════════════════════════
SPRINT SUMMARY (synthesized roll-up):
═══════════════════════════════════════════
${summaryText}

═══════════════════════════════════════════
SCORING RULES — READ CAREFULLY BEFORE SCORING
═══════════════════════════════════════════

EVIDENCE STANDARD: Score only what is explicitly stated in the inputs above.
Do NOT infer, assume, or penalize for the absence of information.
If evidence is sparse, reflect that in data_confidence, not in lower scores.
A missing mention of a ceremony does NOT mean the ceremony did not happen.

SCORING CALIBRATION:
• 7–10 (or 6–8 for Section B): Clear positive evidence present.
• 5 (or 4 for Section B): Neutral — ceremony/value mentioned but quality unclear, OR
  no direct evidence either way. This is the correct default when data is thin.
• 0–4 (or 0–3 for Section B): Reserve for explicit negative evidence —
  e.g., standups described as status reports, no deliverables whatsoever,
  team explicitly unable to adapt to blockers.

═══════════════════════════════════════════
SECTION A — Agile Manifesto Alignment  (40 pts, 0–10 each)
═══════════════════════════════════════════

• individuals_interactions: Look for participation across multiple team members,
  async communication, peer collaboration on tasks, or interpersonal blockers
  noted and resolved. Multiple contributors = positive signal.

• working_software: Stories completed and accepted, demos mentioned, features
  shipped, DoD referenced. A sprint that shipped anything working scores at
  least 6. Only score below 5 if explicitly no working output exists.

• customer_collaboration: PO involvement, stakeholder feedback incorporated,
  requirement questions answered during the sprint. Score 5 if no evidence
  either way — do not penalize for absence.

• responding_to_change: Mid-sprint replanning, pivot on blockers, scope
  adjustments made in response to new information. If blockers were raised
  AND resolved within the sprint, that is evidence of adaptation.

═══════════════════════════════════════════
SECTION B — Scrum Practice Adherence  (40 pts, 0–8 each)
═══════════════════════════════════════════

• sprint_planning: Was a sprint goal or committed scope mentioned? Story
  readiness signals? Score 4 if planning happened but details are sparse.

• daily_scrum: Standups present in the daily summaries = strong positive.
  Count of standup entries is your primary signal. Multiple standups across
  the sprint with blockers/progress discussed = 6–8. Score 4 if standups
  occurred but format is unclear. Only score below 4 if standups are
  explicitly described as manager status reports.

• sprint_review: Demo, review meeting, or acceptance of work mentioned = 6+.
  Score 4 if sprint ended and deliverables existed but review isn't mentioned.
  Do NOT score 0 simply because the word "review" doesn't appear.

• sprint_retrospective: Retro explicitly mentioned = 6+. No mention = 4
  (absence of evidence). Only score below 4 if the team explicitly skipped
  or stated they don't do retros.

• backlog_refinement: Story sizing, backlog discussion, or prioritization
  mentioned = 6+. Score 4 if no evidence either way.

═══════════════════════════════════════════
SECTION C — Anti-Pattern Detection  (20 pts base, deductions)
═══════════════════════════════════════════

CRITICAL: These are DEDUCTIONS for confirmed dysfunctions, not checkboxes.
The default for every anti-pattern is NO DEDUCTION.
Only deduct when the evidence is unambiguous and specific.

ANTI-PATTERN DEFINITIONS AND EVIDENCE BARS:

  zombie_scrum (−5):
    REQUIRES: Ceremonies are explicitly described as going through the motions
    with no real product output and no team desire to improve. A low-output
    sprint alone does NOT qualify. Must see both mechanical process AND absence
    of improvement intent.

  no_real_dod (−4):
    REQUIRES: Stories explicitly accepted or closed without any criteria, OR
    the same work explicitly reopened/reworked in the same sprint due to
    quality failures. Forward-looking work or new stories ≠ rework.

  carryover_work (−3):
    REQUIRES: The sprint summary's "Carry-over Items" section explicitly names
    specific stories or tasks that were started this sprint and not finished,
    AND those same named items appeared in a prior sprint's carry-over section.
    General mentions of "future work," "next sprint," or "ongoing" items do NOT
    qualify. Planned future work is not carryover. A one-time slip of a single
    item is not a pattern.

  status_reporting_standup (−3):
    REQUIRES: Standups explicitly described as reporting status TO a manager,
    PO, or external stakeholder, rather than peer-to-peer synchronization.
    Standups where blockers are raised to the team do not qualify.

  overloaded_sprint (−2):
    REQUIRES: Explicit evidence that committed scope was far beyond capacity —
    e.g., more than half of planned work carries over, or team explicitly
    states they over-committed. A single item not finishing does not qualify.

  multitasking (−2):
    REQUIRES: Team members explicitly described as split across two or more
    unrelated projects simultaneously, causing context-switching harm.
    Working on multiple stories within the same project does not qualify.

  po_as_project_manager (−1):
    REQUIRES: PO explicitly assigning individual tasks to developers, acting
    as a deadline enforcer, or micromanaging implementation. PO participating
    in planning or clarifying requirements does not qualify.

═══════════════════════════════════════════
REQUIRED JSON — return exactly this structure:
═══════════════════════════════════════════
{
  "section_a": {
    "individuals_interactions": { "score": <0-10>, "reasoning": "<cite specific evidence from dailies or summary>" },
    "working_software":         { "score": <0-10>, "reasoning": "<cite specific evidence>" },
    "customer_collaboration":   { "score": <0-10>, "reasoning": "<cite specific evidence or note absence>" },
    "responding_to_change":     { "score": <0-10>, "reasoning": "<cite specific evidence>" },
    "subtotal": <sum of the four scores>
  },
  "section_b": {
    "sprint_planning":      { "score": <0-8>, "reasoning": "<cite specific evidence>" },
    "daily_scrum":          { "score": <0-8>, "reasoning": "<note standup count and quality>" },
    "sprint_review":        { "score": <0-8>, "reasoning": "<cite specific evidence>" },
    "sprint_retrospective": { "score": <0-8>, "reasoning": "<cite specific evidence or note absence>" },
    "backlog_refinement":   { "score": <0-8>, "reasoning": "<cite specific evidence>" },
    "subtotal": <sum of the five scores>
  },
  "section_c": {
    "antipatterns": [
      { "key": "<key>", "label": "<human label>", "deduction": <number>, "evidence": "<quote the specific text that triggered this deduction>" }
    ],
    "subtotal": <20 minus sum of deductions, minimum 0>
  },
  "aii_total": <section_a.subtotal + section_b.subtotal + section_c.subtotal>,
  "data_confidence": "<low|medium|high>",
  "scoring_notes": "<note input richness, any dimensions where evidence was thin, and anything that surprised you>"
}`;
}

// ═══════════════════════════════════════════════════════════════════════════════
// SCORING ENGINE
// ═══════════════════════════════════════════════════════════════════════════════

async function scoreSprintSummary(summaryText, teamName, sprintName, start, end, dailySummaries = []) {
  let raw;
  try {
    const res = await anthropic.messages.create({
      model:      'claude-sonnet-4-6',
      max_tokens: 4096,
      messages:   [{ role: 'user', content: buildScoringPrompt(summaryText, teamName, sprintName, start, end, dailySummaries) }],
    });
    raw = res.content[0].text.trim();
  } catch (err) {
    throw new Error(`AII Claude call failed: ${err.message}`);
  }

  const cleaned = raw.replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '').trim();
  let result;
  try {
    result = JSON.parse(cleaned);
  } catch (err) {
    throw new Error(`AII JSON parse failed: ${err.message}\nRaw (first 400): ${raw.slice(0, 400)}`);
  }

  result.team     = teamName   || null;
  result.sprint   = sprintName || null;
  result.start    = start;
  result.end      = end;
  result.scoredAt = new Date().toISOString();

  appendScore(result);
  return result;
}

// ═══════════════════════════════════════════════════════════════════════════════
// DISCORD EMBED
// ═══════════════════════════════════════════════════════════════════════════════

function grade(t) {
  if (t >= 90) return 'A'; if (t >= 80) return 'B';
  if (t >= 70) return 'C'; if (t >= 60) return 'D';
  return 'F';
}

function embedColor(t) {
  if (t >= 75) return 0x57f287;
  if (t >= 50) return 0xfee75c;
  return 0xed4245;
}

function trunc(str, n = 1020) {
  return str.length > n ? str.slice(0, n - 3) + '...' : str;
}

function sectionAText(a) {
  return trunc([
    `👥 **Individuals & Interactions** ${a.individuals_interactions.score}/10 — ${a.individuals_interactions.reasoning}`,
    `💻 **Working Software** ${a.working_software.score}/10 — ${a.working_software.reasoning}`,
    `🤝 **Customer Collaboration** ${a.customer_collaboration.score}/10 — ${a.customer_collaboration.reasoning}`,
    `🔄 **Responding to Change** ${a.responding_to_change.score}/10 — ${a.responding_to_change.reasoning}`,
  ].join('\n'));
}

function sectionBText(b) {
  return trunc([
    `📋 **Sprint Planning** ${b.sprint_planning.score}/8 — ${b.sprint_planning.reasoning}`,
    `🎯 **Daily Scrum** ${b.daily_scrum.score}/8 — ${b.daily_scrum.reasoning}`,
    `🎤 **Sprint Review** ${b.sprint_review.score}/8 — ${b.sprint_review.reasoning}`,
    `🔁 **Retrospective** ${b.sprint_retrospective.score}/8 — ${b.sprint_retrospective.reasoning}`,
    `📦 **Backlog Refinement** ${b.backlog_refinement.score}/8 — ${b.backlog_refinement.reasoning}`,
  ].join('\n'));
}

function antiPatternText(c) {
  if (!c.antipatterns?.length) return '✅ No anti-patterns detected.';
  return trunc(
    c.antipatterns.map(ap => `⚠️ **${ap.label}** (−${ap.deduction})\n└ ${ap.evidence}`).join('\n')
  );
}

function buildEmbed(result) {
  const g     = grade(result.aii_total);
  const color = embedColor(result.aii_total);
  const conf  = { low: '🔴', medium: '🟡', high: '🟢' }[result.data_confidence] || '⚪';
  const label = result.sprint || `${result.start} → ${result.end}`;
  const date  = new Date(result.scoredAt).toLocaleDateString('en-US', { timeZone: 'America/New_York' });

  return new EmbedBuilder()
    .setColor(color)
    .setTitle(`🏆 AII — ${label} · ${result.team || 'Unknown Team'}`)
    .setDescription(
      `**Score: ${result.aii_total}/100**   Grade: **${g}**\n` +
      `${conf} Data confidence: *${result.data_confidence}*\n\n` +
      `> ${result.scoring_notes || '—'}`
    )
    .addFields(
      { name: '📊 Score Summary', inline: false, value: [
          `📘 Manifesto Alignment (A)  \`${result.section_a.subtotal}/40\``,
          `📗 Scrum Practices (B)      \`${result.section_b.subtotal}/40\``,
          `📕 Anti-Pattern Resist. (C) \`${result.section_c.subtotal}/20\``,
          `───────────────────────────────`,
          `🏆 **Total  \`${result.aii_total}/100\`**`,
        ].join('\n') },
      { name: '📘 Section A — Manifesto Alignment',      value: sectionAText(result.section_a),  inline: false },
      { name: '📗 Section B — Scrum Practice Adherence', value: sectionBText(result.section_b),  inline: false },
      { name: '📕 Section C — Anti-Patterns',            value: antiPatternText(result.section_c), inline: false },
    )
    .setFooter({ text: `Scrumlord AII Engine · scored ${date}` });
}

// ═══════════════════════════════════════════════════════════════════════════════
// MAIN RUNNER — called after a sprint summary thread is created
// ═══════════════════════════════════════════════════════════════════════════════

async function runAIIForSprint(thread, summaryText, teamName, sprintName, start, end, dailySummaries = []) {
  console.log(`[AII] Scoring: ${sprintName || start + '→' + end} — ${teamName}`);

  let result;
  try {
    result = await scoreSprintSummary(summaryText, teamName, sprintName, start, end, dailySummaries);
  } catch (err) {
    console.error('[AII] Scoring failed:', err.message);
    await thread.send(`⚠️ AII scoring failed: ${err.message}`).catch(() => {});
    return;
  }

  await thread.send({ content: '---', embeds: [buildEmbed(result)] });

  const dataDir   = path.join(__dirname, '../data');
  const slug      = `${(sprintName || start).replace(/\s+/g, '-')}-${(teamName || 'team').replace(/\s+/g, '-')}`;
  const tempFiles = [];
  let pngPath     = null;
  let htmlPath    = null;

  if (generateCompositePNG) {
    try {
      pngPath = path.join(dataDir, `aii-${slug}-${Date.now()}.png`);
      await generateCompositePNG(result, getHistory(teamName), pngPath);
      tempFiles.push(pngPath);
    } catch (err) {
      console.warn('[AII] Composite PNG failed:', err.message);
      pngPath = null;
    }
  }

  if (generateDashboardHTML) {
    try {
      htmlPath = path.join(dataDir, `aii-dashboard-${slug}.html`);
      fs.writeFileSync(htmlPath, generateDashboardHTML(loadStore(), teamName, sprintName), 'utf8');
      tempFiles.push(htmlPath);
    } catch (err) {
      console.warn('[AII] Dashboard HTML failed:', err.message);
      htmlPath = null;
    }
  }

  if (pngPath || htmlPath) {
    const files   = [];
    const caption = [];
    if (pngPath)  { files.push(new AttachmentBuilder(pngPath,  { name: path.basename(pngPath)  })); caption.push('📊 Sprint AII preview above'); }
    if (htmlPath) { files.push(new AttachmentBuilder(htmlPath, { name: path.basename(htmlPath) })); caption.push(`💻 Full dashboard → \`${path.basename(htmlPath)}\``); }
    await thread.send({ content: caption.join('  ·  '), files })
      .catch(err => console.warn('[AII] Failed to post files:', err.message));
  }

  // Clean up temp files after Discord has had time to process the upload.
  setTimeout(() => {
    for (const f of tempFiles) {
      try { if (fs.existsSync(f)) fs.unlinkSync(f); } catch {}
    }
  }, 15000);
}

// ═══════════════════════════════════════════════════════════════════════════════
// DAILY SUMMARY COLLECTOR
// Source 1: #chat-summaries   Format: "📄 Chat summary: **#channel** [Team] — YYYY-MM-DD"
// Source 2: #standup-recap    Format: "📋 Meeting recap from <datetime> [Team]"
// Legacy messages may omit the [Team] bracket — treated as 'Scrum Pilot'.
// ═══════════════════════════════════════════════════════════════════════════════

async function collectFromChatSummaries(guild, teamName, start, end) {
  const channel = guild.channels.cache.find(
    c => c.name === CHAT_SUMMARIES_CHANNEL && c.isTextBased()
  );
  if (!channel) {
    console.warn('[AII] #chat-summaries not found.');
    return [];
  }

  const since = new Date(start);
  since.setDate(since.getDate() - 1);
  const results = [];
  let lastId = null;

  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    let batch;
    try { batch = await channel.messages.fetch(opts); } catch { break; }
    if (batch.size === 0) break;

    const msgs = [...batch.values()];
    for (const msg of msgs) {
      if (msg.createdTimestamp < since.getTime()) return results.reverse();

      const dateMatch = msg.content.match(/—\s*(\d{4}-\d{2}-\d{2})/);
      if (!dateMatch || !isInRange(dateMatch[1], start, end)) continue;

      const teamMatch = msg.content.match(/\[(.+?)\]/);
      const msgTeam   = teamMatch ? teamMatch[1] : 'Scrum Pilot';
      if (teamName && msgTeam.toLowerCase() !== teamName.toLowerCase()) continue;

      const attachment = msg.attachments.first();
      if (!attachment) continue;

      try {
        const text         = await downloadAttachment(attachment.url);
        const summaryMatch = text.match(/## Summary\n([\s\S]*?)(?=\n## |$)/);
        const summary      = summaryMatch ? summaryMatch[1].trim() : text.slice(0, 2000);
        if (summary.trim()) {
          results.push({ label: `Daily summary [${dateMatch[1]}] — ${msgTeam}`, content: summary });
        }
      } catch (err) {
        console.warn('[AII] Could not download chat summary attachment:', err.message);
      }
    }

    if (batch.size < 100) break;
    lastId = msgs[msgs.length - 1].id;
    await new Promise(r => setTimeout(r, 300));
  }

  return results.reverse();
}

async function collectFromStandupRecaps(guild, teamName, start, end) {
  const channel = guild.channels.cache.find(
    c => c.name === RECAP_CHANNEL && c.isTextBased()
  );
  if (!channel) {
    console.warn('[AII] #standup-recap not found.');
    return [];
  }

  const startMs = new Date(start).getTime();
  const endMs   = new Date(end).getTime() + 86400000; // inclusive end day
  const results = [];
  let lastId    = null;

  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    let batch;
    try { batch = await channel.messages.fetch(opts); } catch { break; }
    if (batch.size === 0) break;

    const msgs = [...batch.values()];
    for (const msg of msgs) {
      if (msg.createdTimestamp < startMs) return results.reverse();
      if (msg.createdTimestamp > endMs)   continue;
      if (!msg.content.startsWith('📋'))  continue;

      const teamMatch = msg.content.match(/\[(.+?)\]/);
      const msgTeam   = teamMatch ? teamMatch[1] : 'Scrum Pilot';
      if (teamName && msgTeam.toLowerCase() !== teamName.toLowerCase()) continue;

      const attachment = msg.attachments.first();
      if (!attachment) continue;

      try {
        const text            = await downloadAttachment(attachment.url);
        const summaryMatch    = text.match(/## Summary\n([\s\S]*?)(?=\n## |$)/);
        const transcriptMatch = text.match(/## Full Transcript\n([\s\S]*?)(?=\n## |$)/);
        const combined = [
          summaryMatch    ? summaryMatch[1].trim()                            : '',
          transcriptMatch ? `\n[Transcript excerpt]\n${transcriptMatch[1].trim().slice(0, 3000)}` : '',
        ].filter(Boolean).join('\n');

        if (combined.trim()) {
          results.push({
            label:   `Standup recap [${estDateOf(msg)}] — ${msgTeam}`,
            content: combined,
          });
        }
      } catch (err) {
        console.warn('[AII] Could not download standup recap attachment:', err.message);
      }
    }

    if (batch.size < 100) break;
    lastId = msgs[msgs.length - 1].id;
    await new Promise(r => setTimeout(r, 300));
  }

  return results.reverse();
}

async function collectDailySummariesFromDiscord(guild, teamName, start, end) {
  const [chatItems, recapItems] = await Promise.all([
    collectFromChatSummaries(guild, teamName, start, end),
    collectFromStandupRecaps(guild, teamName, start, end),
  ]);
  console.log(`[AII] Collected ${chatItems.length} chat summaries + ${recapItems.length} standup recaps for ${teamName} ${start}→${end}`);
  return [...chatItems, ...recapItems].sort((a, b) => a.label.localeCompare(b.label));
}

// ═══════════════════════════════════════════════════════════════════════════════
// THREAD UTILITIES
// ═══════════════════════════════════════════════════════════════════════════════

async function fetchSummaryFromThread(thread) {
  const msgs = [];
  let lastId = null;
  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    const batch = await thread.messages.fetch(opts);
    if (batch.size === 0) break;
    msgs.push(...batch.values());
    if (batch.size < 100) break;
    lastId = [...batch.keys()].pop();
  }
  return msgs
    .sort((a, b) => a.createdTimestamp - b.createdTimestamp)
    .map(m => m.content)
    .filter(Boolean)
    .join('\n\n');
}

async function findSprintHeader(guild, sprintName, teamName) {
  const channel = guild.channels.cache.find(
    c => c.name === SPRINT_CHANNEL && c.isTextBased()
  );
  if (!channel) return null;

  const search = teamName
    ? `## 🏁 ${sprintName} · Sprint Summary · ${teamName}`
    : `## 🏁 ${sprintName} · Sprint Summary`;

  let lastId = null;
  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    const batch = await channel.messages.fetch(opts);
    if (batch.size === 0) break;
    for (const msg of batch.values()) {
      if (msg.content.startsWith(search)) return msg;
    }
    if (batch.size < 100) break;
    lastId = [...batch.keys()].pop();
  }
  return null;
}

async function deleteExistingAIIMessages(thread) {
  let deleted = 0;
  let lastId  = null;

  while (true) {
    const opts = { limit: 100 };
    if (lastId) opts.before = lastId;
    let batch;
    try { batch = await thread.messages.fetch(opts); } catch { break; }
    if (batch.size === 0) break;

    for (const msg of batch.values()) {
      if (!msg.author?.bot) continue;
      const isEmbedMsg = msg.content === '---' && msg.embeds?.length > 0 && msg.embeds[0]?.title?.startsWith('🏆 AII');
      const isFilesMsg = msg.content?.startsWith('📊') || msg.content?.startsWith('💻');
      if (isEmbedMsg || isFilesMsg) {
        try {
          await msg.delete();
          deleted++;
          await new Promise(r => setTimeout(r, 300));
        } catch (err) {
          console.warn(`[AII] Could not delete message ${msg.id}:`, err.message);
        }
      }
    }

    if (batch.size < 100) break;
    lastId = [...batch.keys()].pop();
  }

  return deleted;
}

// ═══════════════════════════════════════════════════════════════════════════════
// COMMAND HELPERS
// ═══════════════════════════════════════════════════════════════════════════════

/** Parses "Sprint 3" / "sprint 3" / "2026-03-22 2026-04-04" → { sprint, start, end }. */
function resolveSprintArgs(args, schedule) {
  if (!args?.length) return null;

  if (/^\d{4}-\d{2}-\d{2}$/.test(args[0]) && args[1] && /^\d{4}-\d{2}-\d{2}$/.test(args[1])) {
    const [start, end] = args;
    return { sprint: schedule.find(s => s.start === start && s.end === end) || null, start, end };
  }

  const name   = args.join(' ');
  const sprint = schedule.find(s => s.name.toLowerCase() === name.toLowerCase());
  return sprint ? { sprint, start: sprint.start, end: sprint.end } : null;
}

async function _rescoreOne(guild, sprint, team) {
  const hdr = await findSprintHeader(guild, sprint.name || '', team);
  if (!hdr) { console.log(`[AII] Rescore: no header for ${team} ${sprint.name}`); return false; }

  let thread = hdr.thread;
  if (!thread) thread = await hdr.fetchThread?.().catch(() => null);
  if (!thread) { console.log(`[AII] Rescore: no thread for ${team} ${sprint.name}`); return false; }

  try {
    const deletedCount = await deleteExistingAIIMessages(thread);
    console.log(`[AII] Rescore: deleted ${deletedCount} message(s) from ${team} ${sprint.name}`);
    removeScore(team, sprint.start, sprint.end);

    const [text, dailySummaries] = await Promise.all([
      fetchSummaryFromThread(thread),
      collectDailySummariesFromDiscord(guild, team, sprint.start, sprint.end),
    ]);
    await runAIIForSprint(thread, text, team, sprint.name || null, sprint.start, sprint.end, dailySummaries);
    return true;
  } catch (err) {
    console.error(`[AII] Rescore error ${team} ${sprint.name}:`, err.message);
    return false;
  }
}

// ═══════════════════════════════════════════════════════════════════════════════
// COMMAND HANDLER
// ═══════════════════════════════════════════════════════════════════════════════

async function handleAIICommand(message, schedule, getTeamNames) {
  const content = message.content.trim();

  if (content.startsWith('!aiiscore')) {
    const args    = content.split(/\s+/).slice(1);
    const tidx    = args.indexOf('--team');
    let teamName  = null;
    if (tidx !== -1) { teamName = args.slice(tidx + 1).join(' ') || null; args.splice(tidx); }

    const resolved = resolveSprintArgs(args, schedule);
    if (!resolved) {
      await message.reply(
        '⚠️ Usage:\n' +
        '`!aiiscore Sprint 3`\n' +
        '`!aiiscore Sprint 3 --team Scrum Pilot`\n' +
        '`!aiiscore 2026-03-22 2026-04-04`'
      );
      return true;
    }

    const { sprint, start, end } = resolved;
    const teams = teamName ? [teamName] : getTeamNames(message.guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));

    await message.reply(`⏳ AII scoring **${sprint?.name || start + '→' + end}** (${teams.length} team(s))...`);

    for (const team of teams) {
      const hdr = await findSprintHeader(message.guild, sprint?.name || '', team);
      if (!hdr) { await message.channel.send(`_No sprint summary found for ${team} (${start} → ${end})._`); continue; }

      let thread = hdr.thread;
      if (!thread) thread = await hdr.fetchThread?.().catch(() => null);
      if (!thread) { await message.channel.send(`_Could not access thread for ${team}._`); continue; }

      const [text, dailySummaries] = await Promise.all([
        fetchSummaryFromThread(thread),
        collectDailySummariesFromDiscord(message.guild, team, start, end),
      ]);
      await runAIIForSprint(thread, text, team, sprint?.name || null, start, end, dailySummaries);
      await new Promise(r => setTimeout(r, 2000));
    }

    await message.channel.send('✅ AII scoring complete.');
    return true;
  }

  if (content.startsWith('!aiibackfill')) {
    const today = new Date().toISOString().slice(0, 10);
    const past  = schedule.filter(s => s.end <= today);
    const teams = getTeamNames(message.guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));

    await message.reply(
      `⏳ AII backfill: **${past.length} sprint(s)** × **${teams.length} team(s)**.\n` +
      `Already-scored sprints will be skipped.`
    );

    let scored = 0, skipped = 0;
    for (const sprint of past) {
      for (const team of teams) {
        if (isAlreadyScored(team, sprint.start, sprint.end)) { skipped++; continue; }

        const hdr = await findSprintHeader(message.guild, sprint.name, team);
        if (!hdr) { skipped++; continue; }

        let thread = hdr.thread;
        if (!thread) thread = await hdr.fetchThread?.().catch(() => null);
        if (!thread) { skipped++; continue; }

        try {
          const [text, dailySummaries] = await Promise.all([
            fetchSummaryFromThread(thread),
            collectDailySummariesFromDiscord(message.guild, team, sprint.start, sprint.end),
          ]);
          await runAIIForSprint(thread, text, team, sprint.name, sprint.start, sprint.end, dailySummaries);
          scored++;
        } catch (err) {
          console.error(`[AII] Backfill error ${sprint.name} — ${team}:`, err.message);
        }

        await new Promise(r => setTimeout(r, 3000));
      }
    }

    await message.channel.send(`✅ AII backfill complete. Scored: **${scored}**, Skipped: **${skipped}**.`);
    return true;
  }

  if (content.startsWith('!aiihistory')) {
    const m    = content.match(/--team\s+(.+)/);
    const team = m ? m[1].trim() : null;
    const hist = getHistory(team);

    if (hist.length === 0) {
      await message.reply('No AII scores recorded yet. Run `!aiibackfill` to populate.');
      return true;
    }

    const rows = hist.map(e =>
      `**${e.sprint || e.start}** · ${e.team || '?'} → \`${e.aii_total}/100\` ` +
      `(A:${e.section_a.subtotal} B:${e.section_b.subtotal} C:${e.section_c.subtotal})`
    ).join('\n');

    await message.reply(`📊 **AII History${team ? ` — ${team}` : ''}**\n${rows}`);
    return true;
  }

  if (content.startsWith('!aiirescore')) {
    const args   = content.split(/\s+/).slice(1);
    const tidx   = args.indexOf('--team');
    let teamName = null;
    if (tidx !== -1) { teamName = args.slice(tidx + 1).join(' ') || null; args.splice(tidx); }

    if (args[0] === 'all') {
      const today = new Date().toISOString().slice(0, 10);
      const past  = schedule.filter(s => s.end <= today);
      const teams = teamName ? [teamName] : getTeamNames(message.guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));

      await message.reply(
        `♻️ Rescoring **all ${past.length} sprint(s)** × **${teams.length} team(s)**...\n` +
        `Existing AII posts will be replaced. Sprint summaries are untouched.`
      );

      let rescored = 0, failed = 0;
      for (const sp of past) {
        for (const team of teams) {
          (await _rescoreOne(message.guild, sp, team)) ? rescored++ : failed++;
          await new Promise(r => setTimeout(r, 3000));
        }
      }
      await message.channel.send(`✅ Rescore complete. Rescored: **${rescored}**, Failed: **${failed}**.`);
      return true;
    }

    const resolved = resolveSprintArgs(args, schedule);
    if (!resolved) {
      await message.reply(
        '⚠️ Usage:\n' +
        '`!aiirescore all`\n' +
        '`!aiirescore Sprint 3`\n' +
        '`!aiirescore Sprint 3 --team Scrum Pilot`\n' +
        '`!aiirescore 2026-03-22 2026-04-04`'
      );
      return true;
    }

    const { sprint, start, end } = resolved;
    const teams = teamName ? [teamName] : getTeamNames(message.guild).filter(t => !SPRINT_EXCLUDE_TEAMS.has(t));

    await message.reply(
      `♻️ Rescoring **${sprint?.name || start + '→' + end}** for **${teams.length}** team(s)...\n` +
      `Existing AII posts will be replaced. Sprint summaries are untouched.`
    );

    let rescored = 0, failed = 0;
    for (const team of teams) {
      (await _rescoreOne(message.guild, sprint || { name: '', start, end }, team)) ? rescored++ : failed++;
      await new Promise(r => setTimeout(r, 3000));
    }

    await message.channel.send(`✅ Rescore complete. Rescored: **${rescored}**, Failed: **${failed}**.`);
    return true;
  }

  return false;
}

// ═══════════════════════════════════════════════════════════════════════════════
// EXPORTS
// ═══════════════════════════════════════════════════════════════════════════════

module.exports = {
  runAIIForSprint,
  handleAIICommand,
  scoreSprintSummary,
  getHistory,
  loadStore,
  isAlreadyScored,
};
