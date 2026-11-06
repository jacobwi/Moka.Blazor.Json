# Changelog

## [0.7.0] - 2026-11-06

### ⚡ Large files
A file no longer has to fit in memory or in a single array. A seekable `JsonStream` over the lazy threshold is read on demand, so 2 GB is no longer a limit. Measured on a 5.6 GB file with 134 million nodes, which could not be opened before (release build, file already in the Windows file cache):

| | |
|---|---|
| Open the file | 9.5 s |
| Memory after opening | 42 MB (7 MB managed) |
| Memory after expanding and searching | 165 MB |
| Expand an array with 18,151 items (1.4 GB) | 9 ms |
| Search the whole file | 9 s |
| Longest browser frame during load, on a Debug build | 12 ms |

- **New index.** It's built in one streaming pass that allocates almost nothing. It keeps only containers of 4 MB or more, so that file needs 61 entries. Smaller parts are parsed from disk when opened. Before, opening a depth-2 array parsed all of it (1.4 GB here), and the default depth opened it on load
- **Load progress and Cancel.** Large loads show how far they've got and can be cancelled. Loads are also cancelled when the viewer is disposed or given a new document, instead of finishing and leaking
- **Streaming search** for large documents, with progress. It stops at `MaxSearchMatches` and shows `10,000+`. Enter no longer restarts a search that's already running
- **Forward-only streams** (`InputFile` uploads, network responses) used to skip the size limit and the switch to lazy mode, and failed past 2 GB. They're now copied to memory or a temp file first, with the limit enforced
- **Expand All has a limit.** It stops after 1,000,000 rows, or 100,000 on a large document, opens the levels nearest the root first, says so, and can be cancelled. The first view of a large document leaves containers with over 1,000 children closed
- **Arrays and objects with over 100,000 children open as groups**, such as `[0 ... 99,999]`. Search and `NavigateToAsync()` open the right group
- Node count and max depth now show for large documents
- The lazy cache has limits on subtrees and on rows, and diagnostics stay cheap however many parses a session does

### ⚡ Responsiveness
- A background flatten held a lock for its whole run, so a click on the UI thread could wait for it. It now works from a snapshot
- Overlapping flattens could finish out of order and show an older tree. Stale results are dropped
- Selecting a row, right-clicking, Copy and Export no longer do large work on the UI thread. Right-clicking used to build the node's whole text on the spot
- A slow refresh shows a busy indicator after 150 ms
- The viewport skips rendering when nothing it shows has changed, and starting an edit re-renders only that row
- Keys the viewer doesn't handle no longer re-render it
- `Virtualize` has the right item size from the first render
- Rows show the first 10,000 characters of a very long value and say how many more there are. Copy Value still gets all of it
- Undo history is capped at 256 MB as well as 50 steps. Fifty copies of a large document could hold several GB

### 🐛 Fixed
- **WebAssembly apps couldn't use the library at all.** `FrameworkReference Microsoft.AspNetCore.App` fails with NETSDK1082 on `browser-wasm`. The main and Diagnostics packages now reference `Microsoft.AspNetCore.Components.Web`. Checked by running a WebAssembly app in a browser and building a MAUI app for Android. `Moka.Blazor.Json.AI` still doesn't build for WebAssembly
- **WebAssembly loads froze the page**, since `Task.Run` doesn't leave the UI thread there. Indexing and search now stop between blocks to let it paint, and the defaults are lower in the browser
- **Wrapped values lost their indent.** In word-wrap mode a long value dropped to the far left of the row, under the line numbers. Key and value now wrap together at the row's indent
- **Theme `Auto` was always light.** It now follows the system setting, in CSS, so it also works before JS runs
- `DefaultTheme`, `DefaultExpandDepth` and `EnableEditMode` were never read. They work now
- **The stylesheet needed a `HeadOutlet`** and took the place of the app's own `<HeadContent>`. The viewer now links it directly, using the fingerprinted path when available
- **Static server rendering left the viewer hidden** for good, since the render that reveals it never ran
- **Prerendering** no longer loads a stream or a large string, which held up the first response and used up forward-only streams
- Inline edit errors never showed, because the row didn't see a change. They show now, and typing clears them
- Editing a value over 16,000 characters could end a Blazor Server circuit through the 32 KB message limit. It now says the value is too long
- Export sent the whole document through one interop call with no size check. It now streams, with a limit
- "Sort Keys" was offered on large documents and did nothing. It's hidden there now
- Context menu listeners no longer stay on the page after a lost circuit
- A read error, such as a file changing while it's open, shows a message instead of ending the circuit

### ✨ New
- `MaxSearchMatches` and `MaxExportSizeBytes` options
- `FlattenedJsonNode.IsRangeGroup` and `RangeStart`
- The library is marked as trimmable, and no longer uses the reflection-based serializer

### 🔧 Changed
- `MaxDocumentSizeBytes` defaults to 1 TB, or 1 GB in WebAssembly
- `LazyParsingThresholdBytes` defaults to 16 MB in WebAssembly
- `MokaJsonNodeContext.RawValue` is empty for a node over 1 MB. Read it through the viewer if you need it
- `GetJson()` throws `InvalidOperationException` on a document over 1 GB
- `MokaJsonBottomBar.NodeCount` is a `long`
- `BackgroundStatsThresholdBytes` is obsolete. Stats always run in the background
- A seekable `JsonStream` over the threshold has to stay open while the viewer shows it, and isn't read from by anything else

### 📦 Dependencies
| Package | From | To |
|---|---|---|
| Microsoft.AspNetCore.Components.Web (net9.0) | 9.0.20 | 9.0.22 |
| Microsoft.AspNetCore.Components.Web (net10.0) | 10.0.12 | 10.0.14 |
| Moka.AI, Moka.Blazor.AI | 0.5.0 | 0.6.0 |
| Markdig | 1.4.0 | 1.6.0 |
| Microsoft.Extensions.AI.Abstractions, .OpenAI | 10.10.1 | 10.11.0 |
| OllamaSharp | 5.5.0 | 5.5.2 |
| bunit | 2.11.3 | 2.13.3 |
| xunit.v3 | 4.0.1 | 4.0.2 |
| xunit.runner.visualstudio | 4.0.0 | 4.0.1 |
| Microsoft.NET.Test.Sdk | 18.10.1 | 18.11.0 |

### 🧪 Tests
- **381 tests** (up from 267). The lazy source is compared with the in-memory one at every node and for every search, from memory and from a stream, with index settings that force each code path

## [0.6.3] - 2026-11-04

### ⚡ Performance
Measured with release builds (best of 3 where repeated).

| Scenario | Before | After |
|---|---|---|
| Expand All + render rows, 80K objects (in-memory) | 16.9 s | 0.12 s |
| Search with no match, 80K objects (in-memory) | 11.1 s | 27 ms |
| Load a 28 MB document (lazy mode) | 2.15 s | 0.16 s |
| Index memory for that document | 357 MB | 17 MB |
| 50 child counts of nested containers (lazy mode) | 3.5 s | 0.7 ms |
| Worst cached lookup while another thread parses (lazy mode) | 112 ms | 2.5 ms |

- **In-memory documents**: every lookup by path walked down from the root, which is linear in both array position and property count, so Expand All and search were quadratic. Objects and arrays are now remembered as they're enumerated and lookups start from the nearest remembered ancestor. Recursion also passes along the node type it already knows instead of looking it up again
- **Lazy index holds only objects and arrays.** Primitive values are read from their parsed parent. Property names are only decoded when a path needs them
- **Lazy child counts**: indexing a container's children now records the count on the container, instead of counting by scanning the whole index on every call (and blocking on a background task to do it)
- **Lazy parsing happens outside the cache lock**, so a lookup that hits the cache no longer waits for another thread's parse

### 🔧 Build
- CI, release and docs jobs can run on a self-hosted runner: set the `MOKA_RUNNER` repository variable to the runner's label. Pull requests always run on GitHub's runners, since a fork's PR would otherwise run its code on that machine. Delete the variable to go back
- NuGet.org publishing uses trusted publishing: the release job exchanges its GitHub OIDC token for a short-lived API key, so no `NUGET_API_KEY` secret is needed (the old one had expired, which is why 0.6.2 never reached NuGet.org)

## [0.6.2] - 2026-11-04

### 🐛 Fixed
- **Search navigation**: Enter, F3, the ▲/▼ buttons and now ArrowUp/ArrowDown move to the next/previous match *and* scroll it into view. Previously the active match changed but the virtualized tree never scrolled, so off-screen matches looked like nothing happened
- **First Enter after typing**: pressing Enter before the search debounce fired navigated the old results and was then reset to match 1. It now runs the pending search immediately and lands on its first match. The counter shows "Searching..." while results are stale
- **Search thread safety**: results were written on a background thread while the UI read them, and overlapping searches (fast typing on large documents) shared the same lists. Searches now build their own result list, superseded searches are cancelled, and the tree flattener's expand/match state is guarded by a lock
- **Regex search**: a key and value matching on the same node produced a duplicate result. A catastrophic pattern (`(a+)+$`) threw `RegexMatchTimeoutException` out of the search handler instead of returning no matches
- **Scoped search**: with "Scope to This Node" active, search covered the whole document and navigated to matches outside the visible subtree. It now searches the scoped node and re-runs when the scope changes
- Search toggles in the settings panel didn't re-run the current search. Results also refresh after an edit instead of being cleared
- `Ctrl+F` no longer opens the browser's find bar when focus is in the viewer, and it focuses the search box (it used to only toggle). The search box is focused when opened. `Ctrl+Z` typed in the search box no longer undoes document edits
- `SearchAsync()` now highlights results, expands to the first match and scrolls to it. `NavigateToAsync()` scrolls to the node
- **Lazy mode (large documents)**: primitives in arrays were indexed one slot too far, so `/items/1` could report the value of `/items/0`, objects after a primitive were treated as primitives, and search/expand skipped everything inside them
- **Edits on real-world documents**: editing a document with comments, trailing commas, or nesting deeper than 64 levels threw a reader exception. Edits now parse with the same options as loading
- **Rename onto an existing key** silently dropped the other property. It is now rejected with "Duplicate key"
- **Editing a value under a key containing `/` or `~`** added a new `a~1b` property instead of changing `a/b`
- **Unicode escaping**: edits, sort, copy and export rewrote every non-ASCII character as `\uXXXX` (`é` became `é`) and that text was sent through `JsonChanged`. Output now keeps the original characters
- **Undo/redo**: one edit couldn't be undone, two edits undid two steps at once, and redo never reached the latest state. History now records the state before the first edit and after every edit, and is cleared when a new document is loaded
- Edits no longer collapse the tree back to the default depth or drop the current scope. The expanded state is kept across the re-parse
- A new `Json` value arriving while a previous one was still parsing could leave the older document on screen and leak the newer one. Loads now carry a version and stale results are discarded
- "Rename Key" was offered for array elements, and `OnNodeSelected` reported the index as `PropertyName` for array elements (now `null`, as documented)
- Toggling Read-only in the settings panel didn't add or remove the edit actions in the context menu
- Changing "Show child count" or Read-only didn't re-render existing rows
- A failed delete or add replaced the whole tree with an error panel. It now shows a toast
- Lazy minified output (`GetJsonString(false)`, Minify + Copy/Export) returned the original bytes including comments and formatting instead of minified JSON
- The lazy subtree cache disposed evicted documents that a concurrent search or flatten could still be reading
- Number edits accepted `1,000`, `NaN` and `.5`, which then failed with a parser error
- Streaming search threw on a regex timeout
- AI context summary reported one property too many in "... (N more properties)"
- **Null values can be edited.** Previously a `null` could only be "changed" to `null`, so a property created with "Add Property" was stuck. The input is stored as whatever JSON it parses as (number, boolean, quoted string, object, array), or as a string otherwise
- **Empty key vs root**: the path `"/"` (a property named `""`) was treated as the root everywhere, so `{"": 5}` showed the whole document under that row and couldn't be edited or deleted. `""` is now the root and `"/"` the empty key, per RFC 6901, in both small and large documents, the breadcrumb, Copy Path and the AI context
- **Repeated keys**: `{"a":1,"a":2}` showed two rows that both resolved to one path, small documents returned the last value and large ones the first, and editing such a document threw. A repeated key now shows once, in its first position, with the last value (what `JSON.parse` and `TryGetProperty` return), in both modes. Edits, sort, child counts and node counts follow the same rule. Documents without repeated keys skip this work after a fast check at load
- **Settings panel choices were undone by any parent re-render** (they were written into the component's parameters). A choice now sticks until the parent passes a different value for that parameter
- `ExpandAll()`, `NextMatch()`, `PreviousMatch()`, `Undo()` and `Redo()` were `async void`: an exception could crash the circuit and callers couldn't wait for them. They now report errors through `OnError` (see the new async methods below)
- Copy Path quoted a key like `" 1"` as an array index (`$[1]`) and didn't escape backslashes; the breadcrumb showed numeric object keys as indexes
- Replacing or removing a property that doesn't exist now throws instead of quietly adding it or doing nothing

### ✨ New
- `IMokaJsonViewer.ExpandAllAsync()`, `NextMatchAsync()`, `PreviousMatchAsync()`, `UndoAsync()` and `RedoAsync()` return when the work is done. They, `NavigateToAsync()` and `SearchAsync()` run on the component's dispatcher, so they can be called from any thread. If you implement `IMokaJsonViewer` yourself, add these five methods

### 🔧 Changed
- `NavigateToAsync("/")` now means the property named `""`, not the root. Use `""` for the root
- `F2` (rename key) and `Delete` (delete node) now work on the selected node in edit mode. The context menu already advertised them
- `Ctrl+Shift+Z` also redoes, and `Cmd` works in place of `Ctrl` on macOS
- Removed the `Ctrl+C` hint from "Copy Value": it was never wired and would fight normal text copying
- Selection is cleared when a different document is loaded

### 🧪 Tests
- **266 tests** (up from 208): search keyboard navigation and scrolling, debounce flush, the lazy index, edit escaping and rename collisions, undo/redo, flattener helpers, null editing, empty and repeated keys in both document modes, settings persistence, and the async API

## [0.6.1] - 2026-10-31

### 📦 Dependencies
| Package | From | To |
|---|---|---|
| Moka.AI (Moka.Blazor.Json.AI) | 0.3.0 | 0.5.0 |
| Moka.Blazor.AI (Moka.Blazor.Json.AI) | 0.3.0 | 0.5.0 |

### ✨ New (from the Moka.AI 0.5.0 upgrade)
- **Tool calling & MCP support** — the AI assistant can now call tools: register .NET functions via `AiChatOptions.Tools`, or connect MCP servers with the separate `Moka.AI.Mcp` package (`AddMokaAiMcp` + `MokaAiMcpClient.ConnectAsync`). Tool calls execute automatically during chat; see the AI Assistant guide
- Picks up conversation history trimming (`MaxHistoryChars`) from Moka.AI 0.4.0

## [0.6.0] - 2026-10-30

### 💥 Breaking Changes
- Upgraded to Moka.Blazor.AI 0.3.0 / Moka.AI 0.3.0. The AI engine now lives in the
  UI-free **Moka.AI** package — `AiChatService`, `IAiContextBuilder`, `AiChatOptions`,
  and `AddMokaAi()` moved from `Moka.Blazor.AI.*` namespaces to `Moka.AI.*`
  (components remain in `Moka.Blazor.AI.Components`). Consumers must update `using`
  directives for service/model types

### 📦 Dependencies
| Package | From | To |
|---|---|---|
| Moka.Blazor.AI (Moka.Blazor.Json.AI) | 0.2.0 | 0.3.0 |
| Moka.AI (Moka.Blazor.Json.AI) | — | 0.3.0 (new direct reference) |

### ✨ New (from the Moka.AI 0.3.0 upgrade)
- `AiChatOptions.ApiKey` — authenticate against providers that require it
- Runtime settings (model, temperature, …) are isolated per scope/circuit instead of
  leaking across users, and `SetModel()` now takes effect on the next request

## [0.5.2] - 2026-10-03

### 📦 Dependencies
| Package | From | To |
|---|---|---|
| Moka.Blazor.AI (Moka.Blazor.Json.AI) | 0.1.0 | 0.2.0 |

Moka.Blazor.AI 0.2.0 brings Microsoft.Extensions.AI 10.10.1, OllamaSharp 5.5.0 and Markdig 1.4.0.

### 🧪 Tests
- xunit.v3 3.2.2 to 4.0.1, bUnit 2.6.2 to 2.11.3, Microsoft.NET.Test.Sdk 18.3.0 to 18.10.1
- `dotnet test` now runs on Microsoft.Testing.Platform via `global.json`. xunit.v3 4.x needs it on the .NET 10 SDK

## [0.5.1] - 2026-04-02

### 🐛 Fixed
- **Duplicate key error**: Expanding nodes could throw `InvalidOperationException` (duplicate `@key`) when concurrent flatten operations reset the shared ID counter — now uses local counter per flatten call
- **Loading spinner hidden**: First load appeared to block because `visibility:hidden` CSS flash guard hid the spinner until `OnAfterRender` — spinner now shows immediately

## [0.5.0] - 2026-04-02

### 🐛 Fixed
- **Memory leak**: LruCache now disposes evicted `IDisposable` values (e.g., `JsonDocument` holding native memory)
- **Thread safety**: LazyJsonDocumentSource subtree cache access now synchronized with lock
- **JS listener leak**: Color scheme listener uses ID-based Map pattern instead of global array (fixes multi-instance cleanup)
- **Search debounce race**: Timer callback guarded against firing after component disposal
- **Double render**: Removed redundant `StateHasChanged()` call after search execution

### ⚡ Performance
- CSS `contain: layout style paint` on `.moka-json-node` — prevents layout thrashing across 1000s of virtualized nodes
- `will-change` hints on spinner and toast animations
- Zero-allocation CSS class computation via precomputed `string[16]` lookup table indexed by boolean flags
- `LazyDebugStats` uses binary search insert instead of `OrderBy().ToList()` on every parse
- Breadcrumb root click uses named method instead of lambda allocation

### 🔧 Changed
- **Breaking**: Removed dead DI registrations for `JsonDocumentManager`, `JsonTreeFlattener`, `JsonSearchEngine` — these were never injected (MokaJsonViewer creates them internally via `new`)
- Centralized JSON Pointer escape/unescape into `JsonPointerHelper` utility (deduplicated from 7+ files)

### 🧪 Tests
- Replaced `Thread.Sleep` with `WaitForAssertion` in component tests
- Added concurrency test for LazyJsonDocumentSource cache access
- Added unicode/emoji tests for search engine, path converter, and lazy source
- Added `JsonPointerHelper` round-trip tests
- Added LruCache eviction disposal and clear disposal tests
- **208 tests** (up from 184)

## [0.4.4] - 2026-04-02

### 🐛 Fixed
- "No document is loaded" error caused by race condition when concurrent loads dispose previous document source
- Stream position not reset before parsing — reused/shared `MemoryStream` instances now handled correctly
- Unobserved exception in background stats task when document source is disposed during computation

## [0.4.3] - 2026-04-02

### 🐛 Fixed
- UI thread blocking — all parsing, flattening, stats, search, expand/collapse now always run on background thread via `Task.Run`
- CSS flash of unstyled content — viewer hidden with `visibility:hidden` until first render completes
- Race conditions when loading new documents while background operations are in-flight (`ObjectDisposedException` guards)
- bUnit tests updated with `WaitForState` for all async interactions (edit, context menu, re-render, expand/collapse)

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
