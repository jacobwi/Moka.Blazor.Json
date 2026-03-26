# Changelog

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

[0.3.0]: https://github.com/jacobwi/Moka.Blazor.Json/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/jacobwi/Moka.Blazor.Json/releases/tag/v0.2.0
