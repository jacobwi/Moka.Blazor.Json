# Changelog

## [0.4.2] - 2026-03-31

### ✨ New
- `AggressiveCleanup` option — opt-in full GC collection + LOH compaction on viewer dispose for reclaiming memory from large documents

### 🐛 Fixed
- Context menu `RawValue` now populated for container nodes (objects/arrays), fixing null data in custom context menu actions
- bUnit component tests updated to wait for async loading (`Task.Yield()`)

## [0.4.0] - 2026-03-27

### ✨ New
- **Moka.Blazor.Json.AI** — AI-powered chat panel for JSON analysis, summarization, schema generation, transformation, and querying
- AI provider support: OpenAI-compatible (LM Studio, vLLM), Ollama, ONNX Runtime GenAI (embedded), or custom `IChatClient`
- Built on the new [Moka.Blazor.AI](https://github.com/jacobwi/Moka.Blazor.AI) base library for reusable chat panel components
- Streaming responses with stop/cancel support
- Editable messages — edit a previous message and re-send from that point
- Selection-aware context — "Ask about selection" sends the selected JSON subtree
- **Scoped AI context** — scope the AI to a specific node path or combine multiple JSON sources for multi-object analysis
- Three chat styles: Bubble (modern), Classic (flat), Compact (minimal) — switchable from settings
- Quick actions: Summarize, Analyze, Schema, Transform, Query
- Settings panel: model, temperature, max context, streaming toggle, chat style
- Light and dark theme support
- Loading spinner shown during JSON parsing

### 🐛 Fixed
- CSS flash of unstyled content — moved stylesheet to `<HeadContent>` so it loads in `<head>` before rendering
- Loading state now renders correctly — added `Task.Yield()` so the spinner displays before synchronous parsing

## [0.3.0] - 2026-03-26

### ✨ New
- Settings panel — gear button in toolbar opens dropdown with all viewer settings (theme, toolbar mode, toggle style/size, line numbers, word wrap, breadcrumb, bottom bar, child count, expand depth, collapse mode, read-only, search defaults)
- `ShowChildCount` parameter to toggle collapsed container child counts (e.g., "5 items")
- `ShowSettingsButton` option in `MokaJsonViewerOptions` and per-instance parameter
- Collapse All now preserves the root container expanded
- GitHub Release CI workflow

### 🔄 Changed
- Default toolbar mode: `Text` → `IconAndText`
- Migrated to xUnit v3 (`xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5)

### 🐛 Fixed
- Settings toggles (child count, line numbers, toggle style/size) now re-render visible nodes immediately

## [0.2.0] - 2026-03-26

### ✨ New
- Lazy parsing for documents >50 MB with byte-offset indexing and on-demand subtree parsing
- Streaming search for large documents without full DOM
- `Moka.Blazor.Json.Diagnostics` package with debug overlay
- `Moka.Blazor.Json.Abstractions` package for public models/enums/interfaces
- MokaDocs documentation site
- Demo app with interactive toolbar mode/theme switchers

[0.4.2]: https://github.com/jacobwi/Moka.Blazor.Json/compare/v0.4.0...v0.4.2
[0.4.0]: https://github.com/jacobwi/Moka.Blazor.Json/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/jacobwi/Moka.Blazor.Json/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/jacobwi/Moka.Blazor.Json/releases/tag/v0.2.0
