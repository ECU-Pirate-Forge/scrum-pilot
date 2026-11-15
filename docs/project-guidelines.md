# Project Guidelines

Project guidelines describe a project's architecture, standards, and constraints.
They are optional Markdown, not a new programming framework or a requirement to
use every technology in the template.

## Getting Started

1. Select the project in the Fluent client.
2. Open `/project-management`, select the project, and find **Project guidelines** in its details.
3. Copy the [single-file template](templates/ai-agent-project-guidelines.md),
   replace the prompts with your decisions, and paste the document into settings.
4. An Owner can choose **Use full 40-section template** only when the editor is empty, or paste the template manually. Edit it for your project, then choose **Save guidelines**.
5. Inspect **Safe text preview** and **Revision history**. The preview displays escaped text, not rendered Markdown/HTML. **Reload saved revision** discards local edits; copy unsaved work before reloading.

Expected result: a new numbered revision appears in history with its timestamp/content; the API also retains actor and content hash. To restore older guidance, choose **Restore as new revision**; the old revision remains unchanged.

Organization Owners manage guidance. Members with project access can read it.
There is no separate project-administrator role.

## Revisions and Generation

Each save creates a revision instead of editing history. Restore by saving an old
revision's content as a new revision. Guidance is optional until the first save,
but saving empty/whitespace-only content is rejected; there is no clear/delete
workflow. The document limit is 128 KiB UTF-8; oversize documents are rejected,
not truncated. Sections are guidance, not required form fields. Template examples
(including SQL Server) do not require changes to ScrumPilot's SQLite/PostgreSQL architecture.

Project-aware story and task generation record the guidance revision they used.
Changing guidance does not rewrite stories or task specifications. Older generated
content needs applicability review or regeneration. Manual work is not retroactively
represented as AI-generated.

### Review retained story applicability

An organization Owner can open a saved story's provenance controls, choose
**Review content applicability**, enter a **Review reason**, and select
**Confirm content applicability** after comparing the retained story with the
current source/guidelines. **Refresh provenance** reloads current state.
The [review API](api.md#project-story-generation) records actor, timestamp, reason,
and the exact current content/source/guidance fingerprint.

If inputs change before confirmation, the request conflicts; refresh and review
again. The acknowledgement does not rewrite content or original generation
provenance, unarchive a source, approve task scope, or clear dependency acceptance
review. Later input changes require another matching review. For a legacy-linked
story without a generation snapshot, review acknowledges applicability; it does
not fabricate a historical snapshot.

Application security, output contracts, and approved task file scope take
precedence over project guidance. A statement inside a Markdown document cannot
grant access to another project or authorize unrelated file changes.

Saved project guidance and private source context are processed with local Ollama,
not silently forwarded to an external provider. Configure a loopback
`OllamaBaseUrl` and `OllamaModel` on the API. Reduce the selected context if a
generation request exceeds the **192 KiB UTF-8 assembled prompt** limit; required
content is not silently omitted. That limit includes instructions and serialized
context, so a valid saved document can still be too large for a particular request.
Story descriptions have a separate **2,000-character** hard limit, including
acceptance criteria, across provider output/acceptance/improvement; the guideline
size limit does not raise that existing column limit.

## Recover From a Failed Save or Generation

| Problem | Recovery |
|---|---|
| Revision conflict (`409`) | Copy your edits, reload the current revision, reconcile changes, and save again. Do not retry with a guessed revision ID. |
| Empty/oversize input (`400`) | Supply nonempty guidance within 128 KiB UTF-8. |
| Local provider unavailable/not ready | Configure loopback `OllamaBaseUrl`, install the selected `OllamaModel`, start Ollama, then explicitly retry. No cloud fallback occurs. |
| Changed-guidance review warning | Compare the recorded revision with current guidance; review/regenerate work or save and approve a new task specification. Historical content is not rewritten. |

## Safe Content

Do not paste tokens, credentials, personal records, or production secrets into
guidelines or examples. Raw HTML/scripts must not execute in previews. Describe
secret configuration by variable name, never by its value.

See [task specifications](task-specifications.md) for bounded implementation
handoffs and [dependency graphs](dependency-graphs.md) for sequencing.
