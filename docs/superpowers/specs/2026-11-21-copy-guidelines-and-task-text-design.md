# Copy Project Guidelines and Task Text Design

## Summary

Add copy-to-clipboard controls to the Fluent Web project planning surfaces:

- Project Guidelines: copy editable guidelines, safe-text preview content, and any
  displayed revision content.
- Task specifications: copy both editable JSON textareas, and generated Markdown
  and scope-manifest text represented by the current editor state.

The feature is browser-only and does not add, modify, or save any server data.

## Approaches considered

1. **Shared browser helper (recommended):** Add `copyText(text)` to the existing
   `mermaidHelper` browser helper. Components call the helper through `IJSRuntime`.
   This centralizes clipboard API use and gives consistent success/failure handling.
2. **Direct component calls:** Invoke `navigator.clipboard` separately in every
   component. This duplicates browser logic and makes tests less consistent.
3. **Text selection fallback:** Select textbox content and instruct the user to
   copy it manually. It provides no reliable success feedback and is weaker UX.

## Component behavior

### Project Guidelines

The panel will add a copy button beside or above:

- the editable Markdown textarea;
- the safe-text preview;
- each revision-history preview.

The copy button copies the exact string currently shown by that surface. It does
not reload, save, restore, or alter the guideline state. Revision copies use the
revision content from the current component data.

### Task Specification

The editor will add copy buttons beside:

- the structured implementation-specification JSON textarea;
- the exact-file-operations JSON textarea.

It will also add copy actions for the generated text exports already supported by
the screen: the Markdown handoff and scope manifest. These copy actions use the
text the export action would send, without triggering a download.

## Browser helper

Add `copyText(text)` to `mermaidHelper`:

- Return `true` when the browser Clipboard API reports success.
- Return `false` for missing Clipboard API support or a rejected write.
- Keep the existing download helper unchanged.

The helper must copy the supplied text exactly and must not mutate or trim it.

## Feedback and stale scope

Each copy action will:

1. stop when the work scope is stale;
2. invoke `mermaidHelper.copyText`;
3. set a concise success notice on success;
4. set an inline error notice on failure.

Guideline copy actions use existing `_error`/status rendering conventions.
Task-specification copy actions use the existing `_error` area. No notification
service is required for this feature.

## Testing

Add focused bUnit coverage to:

- verify guideline content and revision content are passed to the clipboard helper;
- verify task-spec JSON content and export text are passed to the clipboard helper;
- verify stale work scopes do not invoke browser clipboard code;
- verify success and failure messages render correctly.

Add JavaScript interop coverage to:

- mock `navigator.clipboard.writeText` and confirm exact text is copied;
- confirm a rejected clipboard write returns failure;
- confirm missing Clipboard API support returns failure.

## Boundaries

- No API, EF, SQL, or Shared model changes.
- No changes to save, restore, export, or revision semantics.
- No credentials or private content handling beyond copying the text already
  rendered or edited in the user's browser.
- Both existing Fluent and Mudstandalone clients remain buildable; the feature is
  scoped to the existing planning implementation in Fluent Web.
