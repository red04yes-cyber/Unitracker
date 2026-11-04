# AI & PDF Intelligence — UI/UX Design Specification

> Scope: WPF presentation and interaction design. AI orchestration, PDF parsing, indexing, OCR, provider configuration and persistence belong to the technical implementation.
> Placement: inside the existing **Accademia** workspace. Do not add a new top-level navigation destination unless the application shell is explicitly redesigned.

## 1. Product intent

AI & PDF Intelligence turns a user's own course material into a searchable, explainable study workspace. The document remains the source of truth; AI is an assistant layered on top of it.

The interface must communicate three things clearly:
- which document and pages are being used;
- whether the displayed content is extracted text, an AI-generated interpretation or a user annotation;
- whether an answer is supported by a traceable source.

Never fabricate documents, extracted text, page references, summaries, questions, flashcards, indexing progress or provider availability. A document with no usable text must not appear ready for semantic questions.

## 2. Visual direction

Use the existing StudyHub design system without changing the global shell:
- canvas `#F3F6FB`, surfaces `#FFFFFF`, ink/navy `#17243B`, accent `#3157D5`;
- muted text `#718096`, border `#E4EAF2`, AI accent `#7654C8` only for AI-specific actions and provenance;
- Segoe UI Variable, 28–30 px page title, 13–15 px body text, 11–12 px metadata;
- 12 px card radius, 18–24 px internal padding, quiet borders and minimal shadow.

Do not use a large purple surface for the whole page. AI should be visually identifiable but not overpower the academic material.

## 3. Information architecture

Use a single document workspace with three primary areas:
1. **Libreria** — imported documents and their processing state.
2. **Documento** — PDF preview or extracted text, page navigation and document metadata.
3. **Assistente** — question composer, generated response and cited sources.

At desktop width (1440×900), use a three-column layout: a narrow library rail, a flexible document canvas and a right assistant panel. At 1024×700, collapse the library and assistant into switchable side panels; preserve a readable document canvas. Do not squeeze all three columns until the content becomes illegible.

Suggested local sub-navigation: `Documenti`, `Sintesi`, `Domande`, `Flashcard`. Show a sub-view only when its backing capability exists.

## 4. Library and import

Header:
- Eyebrow: `ACCADEMIA`.
- Title: `AI & PDF Intelligence`.
- Subtitle: `Comprendi, cerca e ripassa a partire dai tuoi materiali.`
- Primary action: `Aggiungi PDF`, enabled only when a real file-import command is available.

Each document row/card may show:
- file name;
- course association, if present;
- page count and file size, when available;
- import date;
- processing state and source.

Processing states must be explicit:
- `In attesa`
- `Estrazione in corso`
- `Indicizzazione in corso`
- `Pronto`
- `Testo non disponibile`
- `Richiede OCR`
- `Errore`

Do not display a generic green success state before text extraction and index creation have completed. A failed import must preserve the original file and offer retry only if a retry command exists.

## 5. Document canvas

The center area should prioritize reading:
- clear document title and breadcrumb;
- page number / total pages when known;
- previous/next page navigation;
- zoom and fit controls only when a PDF renderer supports them;
- optional extracted-text view for accessibility and search.

Selecting a citation should navigate to the cited page and visually highlight the relevant passage only when the extractor can provide a reliable text range. A page-only citation is acceptable when exact passage coordinates are unavailable.

If a PDF is image-only, show `Questo PDF contiene pagine scansionate` and the real OCR availability state. Do not imply OCR has run when it has not.

## 6. Assistant interaction

The assistant panel includes:
- a concise explanation of the active context (document name, selected course, selected page or whole document);
- a composer with a clear send action;
- optional suggested tasks only when the corresponding command exists: `Riassumi`, `Spiega un concetto`, `Crea domande`, `Genera flashcard`;
- a response area with answer, caveats and cited source chips.

Every factual answer about an imported document must expose at least one verifiable source reference. A citation chip should contain document title and page number; selecting it opens the corresponding page. Do not invent citations to fill a missing reference.

When evidence is missing, prefer an explicit response such as `Non trovo questo contenuto nei documenti selezionati.` The UI must not turn uncertain or unsupported content into a confident-looking answer.

Generated summaries, questions and flashcards must be visibly labeled as AI-generated drafts until the user saves or accepts them. Saving is a distinct user action.

## 7. Privacy, provider and trust states

Remote processing must never be implied by a generic `Analizza` button. Before the first remote AI operation, show the configured provider and explain what document content will be sent. Require explicit consent/configuration as defined by the technical contract.

Distinguish:
- local extraction/indexing;
- remote AI request;
- provider unavailable or not configured;
- usage limit / timeout / network error;
- user cancellation.

Do not add API-key fields until a secure provider-settings contract exists. Never display tokens or secrets in the UI, logs, tooltips or error text.

Treat document text as untrusted data. If a PDF contains instructions directed at the AI, those instructions remain document content and must not change assistant permissions or application behavior.

## 8. Empty, loading and error states

- Empty library: explain that no PDFs have been imported and show the real `Aggiungi PDF` action if available.
- Importing: show per-document progress without inventing percentages. Use an indeterminate indicator when exact progress is unavailable.
- No extracted text: explain whether the file is scanned, encrypted, unsupported or empty, based on the actual parser result.
- No citation: do not render a fake source chip; show an evidence limitation.
- Provider unavailable: keep local reading/search available where supported and explain why AI actions are disabled.
- Save failure: preserve the generated draft and allow recovery.
- Offline: distinguish local document access from remote AI unavailability.

## 9. Proposed presentation contract

Names below are proposals, not assumptions. Align to the actual technical ViewModel and service contract before wiring:
- State: `Documents`, `SelectedDocument`, `DocumentPages`, `CurrentPage`, `ProcessingState`, `ExtractedTextAvailable`, `IsProviderConfigured`, `IsBusy`, `ErrorMessage`.
- Assistant: `ConversationMessages`, `PromptText`, `Citations`, `SelectedCitation`, `GeneratedArtifacts`.
- Commands: `ImportPdfCommand`, `SelectDocumentCommand`, `NavigatePageCommand`, `SearchDocumentCommand`, `AskDocumentCommand`, `GenerateSummaryCommand`, `GenerateQuestionsCommand`, `GenerateFlashcardsCommand`, `SaveGeneratedArtifactCommand`, `RetryProcessingCommand`.

Keep PDF parsing, OCR, indexing, provider calls, citation resolution and prompt-injection defenses outside XAML and code-behind.

## 10. Acceptance criteria

- A user can identify the active document and its processing state at all times.
- Every displayed citation resolves to a real document and page.
- AI output is visually distinguished from source text and saved user content.
- The UI never claims a document is indexed or analyzed before the service confirms it.
- Remote processing is disclosed and consented to before content is sent.
- Unsupported, scanned, empty and failed PDFs have recoverable states.
- The layout is usable at 1440×900 and 1024×700, with keyboard access and Windows text scaling.
- No fake documents, fake answers, invented page references or inert AI controls are shipped.
- Visual verification on Windows is required before calling the screen complete.
