'use strict';
// Shared AI clients and the unified callAI() fallback helper.
// All modules that need Claude or OpenAI import from here — never instantiate directly.

const Anthropic = require('@anthropic-ai/sdk');
const OpenAI    = require('openai');

const anthropic = new Anthropic({ apiKey: process.env.ANTHROPIC_API_KEY });
const openai    = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

/**
 * Sends a prompt to Claude (claude-opus-4-6), falling back to GPT-4o-mini on failure.
 *
 * @param {string}      prompt    - The full user prompt text.
 * @param {number}      maxTokens - Token budget for the response (default: 1024).
 * @param {number|null} timeout   - Optional Claude-specific timeout in ms (e.g. 60000).
 *                                  Does not apply to the OpenAI fallback.
 * @returns {Promise<string|null>} Response text, or null if both providers fail.
 */
async function callAI(prompt, maxTokens = 1024, timeout = null) {
  const claudeRequest = anthropic.messages.create({
    model:      'claude-opus-4-6',
    max_tokens: maxTokens,
    messages:   [{ role: 'user', content: prompt }],
  });

  try {
    const res = timeout
      ? await Promise.race([
          claudeRequest,
          new Promise((_, reject) =>
            setTimeout(() => reject(new Error('Claude timeout')), timeout)
          ),
        ])
      : await claudeRequest;
    console.log('[Claude] Response received.');
    return res.content[0].text;
  } catch (err) {
    console.warn('[Claude] Failed, falling back to GPT-4o-mini:', err.message);
  }

  try {
    const res = await openai.chat.completions.create({
      model:      'gpt-4o-mini',
      max_tokens: maxTokens,
      messages:   [{ role: 'user', content: prompt }],
    });
    console.log('[OpenAI] Response received.');
    return res.choices[0].message.content;
  } catch (err) {
    console.warn('[OpenAI] Failed:', err.message);
  }

  return null;
}

module.exports = { anthropic, openai, callAI };
