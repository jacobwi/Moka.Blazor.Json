using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Interop;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Top-level JSON viewer and editor component. The primary entry point for consumers.
/// </summary>
public sealed partial class MokaJsonViewer : ComponentBase, IMokaJsonViewer, IAsyncDisposable
{
	#region IAsyncDisposable

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		_searchDebounceTimer?.Dispose();
		_toastTimer?.Dispose();

		if (_treeCts is not null)
		{
			await _treeCts.CancelAsync();
			_treeCts.Dispose();
		}

		if (_statsCts is not null)
		{
			await _statsCts.CancelAsync();
			_statsCts.Dispose();
		}

		if (_documentSource is not null)
		{
			await _documentSource.DisposeAsync();
		}
		else if (_documentManager is not null)
		{
			await _documentManager.DisposeAsync();
		}

		if (_contextMenu is not null)
		{
			await _contextMenu.DisposeAsync();
		}
	}

	#endregion

	#region Injected Services

	[Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;
	[Inject] private IOptions<MokaJsonViewerOptions> OptionsAccessor { get; set; } = null!;
	[Inject] private MokaJsonInterop Interop { get; set; } = null!;

	#endregion

	#region Parameters

	/// <summary>
	///     The JSON string to display. Mutually exclusive with <see cref="JsonStream" />.
	/// </summary>
	[Parameter]
	public string? Json { get; set; }

	/// <summary>
	///     A stream containing JSON data for incremental parsing.
	/// </summary>
	[Parameter]
	public Stream? JsonStream { get; set; }

	/// <summary>
	///     Theme mode for the viewer.
	/// </summary>
	[Parameter]
	public MokaJsonTheme Theme { get; set; } = MokaJsonTheme.Auto;

	/// <summary>
	///     Whether the toolbar is displayed. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowToolbar { get; set; } = true;

	/// <summary>
	///     Whether the bottom status bar is displayed. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowBottomBar { get; set; } = true;

	/// <summary>
	///     Whether the breadcrumb path is displayed. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowBreadcrumb { get; set; } = true;

	/// <summary>
	///     Whether to show line numbers in the gutter. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowLineNumbers { get; set; }

	/// <summary>
	///     Whether the viewer is read-only. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ReadOnly { get; set; } = true;

	/// <summary>
	///     Initial depth to expand. Default is <c>2</c>.
	/// </summary>
	[Parameter]
	public int MaxDepthExpanded { get; set; } = 2;

	/// <summary>
	///     Height of the viewer. Default is "400px". Use "100%" for fill.
	/// </summary>
	[Parameter]
	public string Height { get; set; } = "400px";

	/// <summary>
	///     Callback when a node is selected.
	/// </summary>
	[Parameter]
	public EventCallback<JsonNodeSelectedEventArgs> OnNodeSelected { get; set; }

	/// <summary>
	///     Callback when the JSON content changes in edit mode.
	/// </summary>
	[Parameter]
	public EventCallback<JsonChangeEventArgs> OnJsonChanged { get; set; }

	/// <summary>
	///     Two-way binding callback for <see cref="Json" />. Enables <c>@bind-Json</c>.
	/// </summary>
	[Parameter]
	public EventCallback<string?> JsonChanged { get; set; }

	/// <summary>
	///     Callback when an error occurs.
	/// </summary>
	[Parameter]
	public EventCallback<JsonErrorEventArgs> OnError { get; set; }

	/// <summary>
	///     Custom context menu actions to add.
	/// </summary>
	[Parameter]
	public IReadOnlyList<MokaJsonContextAction>? ContextMenuActions { get; set; }

	/// <summary>
	///     Style of expand/collapse toggle indicators. Default is <see cref="MokaJsonToggleStyle.Triangle" />.
	/// </summary>
	[Parameter]
	public MokaJsonToggleStyle ToggleStyle { get; set; } = MokaJsonToggleStyle.Triangle;

	/// <summary>
	///     Size of expand/collapse toggle indicators. Default is <see cref="MokaJsonToggleSize.Small" />.
	/// </summary>
	[Parameter]
	public MokaJsonToggleSize ToggleSize { get; set; } = MokaJsonToggleSize.Small;

	/// <summary>
	///     Initial collapse behavior when a document is loaded. Default is <see cref="MokaJsonCollapseMode.Depth" />.
	/// </summary>
	[Parameter]
	public MokaJsonCollapseMode CollapseMode { get; set; } = MokaJsonCollapseMode.Depth;

	/// <summary>
	///     Whether long values wrap to the next line. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool WordWrap { get; set; } = true;

	/// <summary>
	///     Whether to show child count (e.g. "13 items") on collapsed containers. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowChildCount { get; set; } = true;

	/// <summary>
	///     How toolbar buttons are displayed. Overrides <see cref="MokaJsonViewerOptions.DefaultToolbarMode" />.
	///     When <c>null</c>, the global default from DI is used.
	/// </summary>
	[Parameter]
	public MokaJsonToolbarMode? ToolbarMode { get; set; }

	/// <summary>
	///     Extra content to render in the toolbar.
	/// </summary>
	[Parameter]
	public RenderFragment? ToolbarExtra { get; set; }

	/// <summary>
	///     Whether the settings gear button is shown in the toolbar.
	///     When <c>null</c>, the global default from DI (<see cref="MokaJsonViewerOptions.ShowSettingsButton" />) is used.
	/// </summary>
	[Parameter]
	public bool? ShowSettingsButton { get; set; }

	/// <summary>
	///     Additional HTML attributes to apply to the root element.
	/// </summary>
	[Parameter(CaptureUnmatchedValues = true)]
	public Dictionary<string, object>? AdditionalAttributes { get; set; }

	#endregion

	#region State Fields

	private ElementReference _viewerRef;
	private JsonDocumentManager? _documentManager;
#pragma warning disable CA1859 // Intentionally using interface to support future LazyJsonDocumentSource
	private IJsonDocumentSource? _documentSource;
#pragma warning restore CA1859
	private bool _isLazyMode;
	private readonly JsonTreeFlattener _treeFlattener = new();
	private readonly JsonSearchEngine _searchEngine = new();
	private MokaJsonContextMenu? _contextMenu;
	private IReadOnlyList<MokaJsonContextAction>? _previousContextMenuActions;

	private List<FlattenedJsonNode>? _flatNodes;
	private int _selectedDepth;
	private bool _isLoaded;
	private bool _isLoading;
	private bool _isFormatted = true;
	private string? _errorMessage;
	private string? _previousJson;
	private Stream? _previousStream;

	/// <summary>
	///     When non-null, the tree is scoped to show only the subtree at this JSON Pointer path.
	///     Null means the full document is displayed.
	/// </summary>
	private string? _scopedPath;

	private bool _showSearch;
	private string? _searchQuery;
	private bool _searchCaseSensitive;
	private bool _searchUseRegex;

	private bool _contextMenuVisible;
	private MokaJsonNodeContext? _contextMenuNodeContext;
	private List<MokaJsonContextAction> _allContextActions = [];

	private bool _showSettings;
	private double _settingsLeft;
	private double _settingsTop;

	private string? _documentSize;
	private int _nodeCount;
	private int _maxDepth;
	private string? _parseTimeMs;
	private bool _isValid = true;
	private string? _validationError;

	private CancellationTokenSource? _statsCts;
	private CancellationTokenSource? _treeCts;
	private Timer? _searchDebounceTimer;
	private bool _disposed;
	private bool _isBusy;
	private string? _busyMessage;
	private string? _toastMessage;
	private Timer? _toastTimer;

	private InlineEditState? _activeEdit;
	private EditHistory? _editHistory;

	/// <summary>
	///     Debug stats from the lazy source, set only when MOKA_DEBUG_LAZY env var is set.
	///     Used by Moka.Blazor.Json.Diagnostics overlay.
	/// </summary>
	public LazyDebugStats? DebugStats { get; private set; }

	#endregion

	#region Computed Properties

	private string HeightStyle => $"height: {Height}";

	private MokaJsonToolbarMode EffectiveToolbarMode =>
		ToolbarMode ?? OptionsAccessor.Value.DefaultToolbarMode;

	private bool EffectiveShowSettingsButton =>
		ShowSettingsButton ?? OptionsAccessor.Value.ShowSettingsButton;

	/// <summary>
	///     Effective read-only state: true if the ReadOnly parameter is set OR if the
	///     document source doesn't support editing (lazy mode).
	/// </summary>
	private bool EffectiveReadOnly => ReadOnly || _documentSource is { SupportsEditing: false };

	private string ThemeAttribute => Theme switch
	{
		MokaJsonTheme.Light => "light",
		MokaJsonTheme.Dark => "dark",
		MokaJsonTheme.Inherit => "",
		_ => "light" // Auto defaults to light, JS will switch if needed
	};

	#endregion

	#region Lifecycle

	/// <inheritdoc />
	protected override void OnInitialized() => BuildContextActions();

	/// <inheritdoc />
	protected override async Task OnParametersSetAsync()
	{
		if (ContextMenuActions != _previousContextMenuActions)
		{
			_previousContextMenuActions = ContextMenuActions;
			BuildContextActions();
		}

		if (Json != _previousJson || JsonStream != _previousStream)
		{
			_previousJson = Json;
			_previousStream = JsonStream;
			await LoadJsonAsync();
		}
	}

	private async Task LoadJsonAsync()
	{
		_errorMessage = null;
		_isLoaded = false;
		_isLoading = true;
		_flatNodes = null;

		// Clear stale search state from previous document
		_searchEngine.Clear();
		_treeFlattener.ClearSearchMatches();
		_scopedPath = null;

		// Yield so Blazor renders the loading spinner before parsing begins.
		// ParseAsync(string) completes synchronously, so without this yield
		// the component would block through the entire parse without ever
		// showing the loading state.
		StateHasChanged();
		await Task.Yield();

		try
		{
			if (_documentSource is not null)
			{
				await _documentSource.DisposeAsync();
				_documentSource = null;
			}
			else if (_documentManager is not null)
			{
				await _documentManager.DisposeAsync();
			}

			_documentManager = null;
			_isLazyMode = false;
			DebugStats = null;

			long lazyThreshold = OptionsAccessor.Value.LazyParsingThresholdBytes;
			ILogger lazyLogger = LoggerFactory.CreateLogger<LazyJsonDocumentSource>();

			if (!string.IsNullOrWhiteSpace(Json))
			{
				string json = Json;
				long byteCount = json.Length <= 1_000_000
					? Encoding.UTF8.GetByteCount(json)
					: await Task.Run(() => Encoding.UTF8.GetByteCount(json));

				if (byteCount > OptionsAccessor.Value.MaxDocumentSizeBytes)
				{
					throw new InvalidOperationException(
						$"Document size ({JsonDocumentManager.FormatBytes(byteCount)}) exceeds the maximum allowed size ({JsonDocumentManager.FormatBytes(OptionsAccessor.Value.MaxDocumentSizeBytes)}).");
				}

				if (byteCount > lazyThreshold)
				{
					LazyJsonDocumentSource lazySource = await LazyJsonDocumentSource.CreateFromStringAsync(
						json, lazyLogger, OptionsAccessor.Value);
					_documentSource = lazySource;
					_isLazyMode = true;
					DebugStats = lazySource.DebugStats;
				}
				else
				{
					var manager = new JsonDocumentManager(
						LoggerFactory.CreateLogger<JsonDocumentManager>(),
						OptionsAccessor);

					// Offload parsing to background thread so UI stays responsive
					if (byteCount > OptionsAccessor.Value.BackgroundStatsThresholdBytes)
					{
						await Task.Run(() => manager.ParseAsync(json));
					}
					else
					{
						await manager.ParseAsync(json);
					}

					_documentManager = manager;
					_documentSource = manager;
				}
			}
			else if (JsonStream is not null)
			{
				// Check size if seekable
				if (JsonStream.CanSeek && JsonStream.Length > OptionsAccessor.Value.MaxDocumentSizeBytes)
				{
					throw new InvalidOperationException(
						$"Document size ({JsonDocumentManager.FormatBytes(JsonStream.Length)}) exceeds the maximum allowed size ({JsonDocumentManager.FormatBytes(OptionsAccessor.Value.MaxDocumentSizeBytes)}).");
				}

				if (JsonStream.CanSeek && JsonStream.Length > lazyThreshold)
				{
					LazyJsonDocumentSource lazySource = await LazyJsonDocumentSource.CreateAsync(
						JsonStream, lazyLogger, OptionsAccessor.Value);
					_documentSource = lazySource;
					_isLazyMode = true;
					DebugStats = lazySource.DebugStats;
				}
				else
				{
					var manager = new JsonDocumentManager(
						LoggerFactory.CreateLogger<JsonDocumentManager>(),
						OptionsAccessor);

					// Offload stream parsing to background thread so UI stays responsive
					bool isLargeStream = JsonStream.CanSeek &&
					                     JsonStream.Length > OptionsAccessor.Value.BackgroundStatsThresholdBytes;
					if (isLargeStream)
					{
						StateHasChanged();
						Stream stream = JsonStream;
						await Task.Run(() => manager.ParseAsync(stream));
					}
					else
					{
						await manager.ParseAsync(JsonStream);
					}

					_documentManager = manager;
					_documentSource = manager;
				}
			}
			else
			{
				_isLoading = false;
				return;
			}

			if (_documentSource is null)
			{
				_isLoading = false;
				return;
			}

			IJsonDocumentSource source = _documentSource;

			int expandDepth = CollapseMode switch
			{
				MokaJsonCollapseMode.Root => 0,
				MokaJsonCollapseMode.Expanded => -1,
				_ => MaxDepthExpanded
			};

			// Offload tree expansion and flattening to a background thread for large documents
			// so the UI remains responsive during initial load.
			if (source.DocumentSizeBytes >= OptionsAccessor.Value.BackgroundStatsThresholdBytes)
			{
				_flatNodes = await Task.Run(() =>
				{
					_treeFlattener.ExpandToDepth(source, expandDepth);
					return _treeFlattener.Flatten(source);
				});
			}
			else
			{
				_treeFlattener.ExpandToDepth(source, expandDepth);
				_flatNodes = _treeFlattener.Flatten(source);
			}

			_documentSize = JsonDocumentManager.FormatBytes(source.DocumentSizeBytes);
			_parseTimeMs = $"{source.ParseTime.TotalMilliseconds:F1} ms";

			// For large documents, defer expensive stats to a background thread
			if (source.DocumentSizeBytes < OptionsAccessor.Value.BackgroundStatsThresholdBytes)
			{
				_nodeCount = source.CountNodes();
				_maxDepth = source.GetMaxDepth();
			}
			else
			{
				_nodeCount = -1;
				_maxDepth = -1;
				if (_statsCts is not null)
				{
					await _statsCts.CancelAsync();
					_statsCts.Dispose();
				}

				CancellationTokenSource cts = _statsCts = new CancellationTokenSource();
				_ = Task.Run(() =>
				{
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}

					int nc = source.CountNodes();
					int md = source.GetMaxDepth();
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}

					_ = InvokeAsync(() =>
					{
						if (cts.Token.IsCancellationRequested)
						{
							return;
						}

						_nodeCount = nc;
						_maxDepth = md;
						StateHasChanged();
					});
				}, cts.Token);
			}

			_isValid = true;
			_validationError = null;
			_isLoaded = true;
			_isLoading = false;

			// Rebuild context actions since EffectiveReadOnly may have changed
			BuildContextActions();
		}
		catch (JsonException ex)
		{
			_isValid = false;
			_validationError = ex.Message;
			_errorMessage = ex.Message;
			_isLoading = false;
			await RaiseError(ex.Message, ex, ex.BytePositionInLine, ex.LineNumber);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_errorMessage = ex.Message;
			_isLoading = false;
			await RaiseError(ex.Message, ex);
		}
	}

	private async Task ReloadFromJsonAsync(string json)
	{
		// Temporarily set Json so LoadJsonAsync picks it up, then restore.
		string? originalJson = Json;
		Json = json;
		await LoadJsonAsync();
		// Don't leave the parameter mutated — restore original so parent stays in control
		Json = originalJson;
	}

	private void RefreshFlatNodes()
	{
		if (!_isLoaded || _documentSource is null)
		{
			return;
		}

		if (_scopedPath is not null)
		{
			try
			{
				_flatNodes = _treeFlattener.FlattenScoped(_documentSource, _scopedPath);
			}
			catch (KeyNotFoundException)
			{
				// Scoped path no longer valid, reset to root
				_scopedPath = null;
				_flatNodes = _treeFlattener.Flatten(_documentSource);
			}
		}
		else
		{
			_flatNodes = _treeFlattener.Flatten(_documentSource);
		}

		StateHasChanged();
	}

	private async Task RefreshFlatNodesAsync()
	{
		if (!_isLoaded || _documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		string? scopedPath = _scopedPath;

		List<FlattenedJsonNode> nodes;
		try
		{
			nodes = await Task.Run(() =>
			{
				if (scopedPath is not null)
				{
					try
					{
						return _treeFlattener.FlattenScoped(source, scopedPath);
					}
					catch (KeyNotFoundException)
					{
						return _treeFlattener.Flatten(source);
					}
				}

				return _treeFlattener.Flatten(source);
			});
		}
		catch (ObjectDisposedException)
		{
			// Source was disposed by a concurrent operation (e.g., new document loaded) — ignore
			return;
		}

		// If scoped path was invalid, reset it on the UI thread
		if (scopedPath is not null && nodes.Count > 0 && nodes[0].Path != scopedPath)
		{
			_scopedPath = null;
		}

		_flatNodes = nodes;
		StateHasChanged();
	}

	#endregion

	#region Toolbar Handlers

	private void ToggleSearch()
	{
		_showSearch = !_showSearch;
		if (!_showSearch)
		{
			_searchQuery = null;
			_searchEngine.Clear();
			_treeFlattener.ClearSearchMatches();
			RefreshFlatNodes();
		}
	}

	private async Task HandleExpandAll()
	{
		if (_documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		bool isLarge = source.DocumentSizeBytes >= OptionsAccessor.Value.BackgroundStatsThresholdBytes;

		if (isLarge)
		{
			// Cancel any in-flight tree operation
			await CancelTreeOperationAsync();
			_isBusy = true;
			_busyMessage = "Expanding all nodes...";
			StateHasChanged();

			CancellationTokenSource cts = _treeCts = new CancellationTokenSource();
			try
			{
				await Task.Run(() => _treeFlattener.ExpandAll(source), cts.Token);
				if (!cts.Token.IsCancellationRequested)
				{
					await RefreshFlatNodesAsync();
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
			{
			}
			finally
			{
				if (_treeCts == cts)
				{
					_isBusy = false;
					_busyMessage = null;
					StateHasChanged();
				}
			}
		}
		else
		{
			_treeFlattener.ExpandAll(source);
			RefreshFlatNodes();
		}
	}

	private async Task HandleCollapseAll()
	{
		if (_documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		bool isLarge = source.DocumentSizeBytes >= OptionsAccessor.Value.BackgroundStatsThresholdBytes;

		if (isLarge)
		{
			// Cancel any in-flight tree operation
			await CancelTreeOperationAsync();
			_isBusy = true;
			_busyMessage = "Collapsing...";
			StateHasChanged();

			CancellationTokenSource cts = _treeCts = new CancellationTokenSource();
			try
			{
				await Task.Run(() =>
				{
					_treeFlattener.CollapseAll();
					_treeFlattener.Expand("");
				}, cts.Token);
				if (!cts.Token.IsCancellationRequested)
				{
					await RefreshFlatNodesAsync();
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
			{
			}
			finally
			{
				if (_treeCts == cts)
				{
					_isBusy = false;
					_busyMessage = null;
					StateHasChanged();
				}
			}
		}
		else
		{
			_treeFlattener.CollapseAll();
			_treeFlattener.Expand("");
			RefreshFlatNodes();
		}
	}

	private async Task CancelTreeOperationAsync()
	{
		if (_treeCts is not null)
		{
			await _treeCts.CancelAsync();
			_treeCts.Dispose();
			_treeCts = null;
			_isBusy = false;
			_busyMessage = null;
		}
	}

	private void HandleFormatToggle() => _isFormatted = !_isFormatted;

	private async Task HandleCopyAll()
	{
		if (_documentSource is null)
		{
			return;
		}

		// Guard against OOM for very large documents
		if (_documentSource.DocumentSizeBytes > OptionsAccessor.Value.MaxClipboardSizeBytes)
		{
			ShowToast(
				"Document too large to copy. Right-click a node and use \"Scope to This Node\" to copy smaller sections.");
			return;
		}

		string json = _documentSource.GetJsonString(_isFormatted);
		await Interop.CopyToClipboardAsync(json);
	}

	private async Task HandleExport()
	{
		if (_documentSource is null)
		{
			return;
		}

		string json = _documentSource.GetJsonString(_isFormatted);
		string fileName = $"export-{DateTime.Now:yyyyMMdd-HHmmss}.json";
		await Interop.DownloadFileAsync(fileName, json);
	}

	#endregion

	#region Search Handlers

	private void HandleSearchQueryChanged(string query)
	{
		_searchQuery = query;

		int debounceMs = OptionsAccessor.Value.SearchDebounceMs;
		if (debounceMs <= 0)
		{
			ExecuteSearch();
			return;
		}

		_searchDebounceTimer?.Dispose();
		_searchDebounceTimer = new Timer(_ =>
		{
			_ = InvokeAsync(() =>
			{
				ExecuteSearch();
				StateHasChanged();
			});
		}, null, debounceMs, Timeout.Infinite);
	}

	private void HandleSearchNext()
	{
		string? path = _searchEngine.NextMatch();
		if (path is not null)
		{
			EnsurePathExpanded(path);
			_treeFlattener.SetSearchMatches(_searchEngine.MatchPaths, _searchEngine.ActiveMatchPath);
			RefreshFlatNodes();
		}
	}

	private void HandleSearchPrevious()
	{
		string? path = _searchEngine.PreviousMatch();
		if (path is not null)
		{
			EnsurePathExpanded(path);
			_treeFlattener.SetSearchMatches(_searchEngine.MatchPaths, _searchEngine.ActiveMatchPath);
			RefreshFlatNodes();
		}
	}

	private void HandleCaseSensitiveChanged(bool value)
	{
		_searchCaseSensitive = value;
		ExecuteSearch();
	}

	private void HandleRegexChanged(bool value)
	{
		_searchUseRegex = value;
		ExecuteSearch();
	}

	private void ExecuteSearch()
	{
		if (_documentSource is null || string.IsNullOrEmpty(_searchQuery))
		{
			_searchEngine.Clear();
			_treeFlattener.ClearSearchMatches();
			RefreshFlatNodes();
			return;
		}

		var options = new JsonSearchOptions
		{
			CaseSensitive = _searchCaseSensitive,
			UseRegex = _searchUseRegex
		};

		_searchEngine.Search(_documentSource, _searchQuery, options);

		if (_searchEngine.ActiveMatchPath is not null)
		{
			EnsurePathExpanded(_searchEngine.ActiveMatchPath);
		}

		_treeFlattener.SetSearchMatches(_searchEngine.MatchPaths, _searchEngine.ActiveMatchPath);
		RefreshFlatNodes();
	}

	#endregion

	#region Tree Interaction Handlers

	private async Task HandleToggle(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		bool isLarge = _documentSource.DocumentSizeBytes >= OptionsAccessor.Value.BackgroundStatsThresholdBytes;
		_treeFlattener.ToggleExpand(path);

		if (isLarge)
		{
			_isBusy = true;
			_busyMessage = "Loading...";
			StateHasChanged();

			try
			{
				await RefreshFlatNodesAsync();
			}
			catch (ObjectDisposedException)
			{
			}
			finally
			{
				_isBusy = false;
				_busyMessage = null;
				StateHasChanged();
			}
		}
		else
		{
			RefreshFlatNodes();
		}
	}

	private async Task HandleSelect(string path)
	{
		SelectedPath = path;

		if (_documentSource is not null)
		{
			try
			{
				JsonElement element = _documentSource.GetElement(path);
				string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
				_selectedDepth = segments.Length;
				string? propName = segments.Length > 0 ? segments[^1].Replace("~1", "/").Replace("~0", "~") : null;

				bool isContainer = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
				string rawText = isContainer ? "" : element.GetRawText();
				string preview = isContainer
					? $"{_documentSource.GetChildCount(path)} items"
					: TruncatePreview(rawText);

				await OnNodeSelected.InvokeAsync(new JsonNodeSelectedEventArgs
				{
					Path = path,
					Depth = _selectedDepth,
					ValueKind = element.ValueKind,
					PropertyName = propName,
					RawValue = rawText,
					RawValuePreview = preview
				});
			}
			catch (KeyNotFoundException)
			{
				// Path no longer valid
			}
		}

		StateHasChanged();
	}

	private async Task HandleContextMenuRequest((string Path, double ClientX, double ClientY) args)
	{
		if (_documentSource is null)
		{
			return;
		}

		try
		{
			JsonElement element = _documentSource.GetElement(args.Path);
			string[] segments = args.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			string? propName = segments.Length > 0 ? segments[^1].Replace("~1", "/").Replace("~0", "~") : null;

			// For containers, defer GetRawText — serializing a large subtree just to
			// populate the context menu is wasteful. Copy Value will fetch on demand.
			bool isContainer = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
			string rawText = isContainer ? "" : element.GetRawText();
			string preview = isContainer
				? $"{_documentSource.GetChildCount(args.Path)} items"
				: TruncatePreview(rawText);

			_contextMenuNodeContext = new MokaJsonNodeContext
			{
				Path = args.Path,
				Depth = segments.Length,
				ValueKind = element.ValueKind,
				PropertyName = propName,
				RawValue = rawText,
				RawValuePreview = preview,
				Viewer = this
			};

			_contextMenuVisible = true;
			StateHasChanged();

			if (_contextMenu is not null)
			{
				await _contextMenu.ShowAsync(args.ClientX, args.ClientY);
			}
		}
		catch (KeyNotFoundException)
		{
		}
	}

	private void HandleDoubleClick(string path)
	{
		if (EffectiveReadOnly || _documentManager is null)
		{
			return;
		}

		try
		{
			JsonElement element = _documentManager.NavigateToElement(path);

			// Only primitives are directly editable via double-click
			if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				return;
			}

			string rawValue = element.ValueKind == JsonValueKind.String
				? element.GetString() ?? ""
				: element.GetRawText();

			_activeEdit = new InlineEditState
			{
				Path = path,
				Target = InlineEditTarget.Value,
				OriginalValue = rawValue,
				CurrentValue = rawValue,
				ValueKind = element.ValueKind
			};
			StateHasChanged();
		}
		catch (KeyNotFoundException)
		{
		}
	}

	private void StartKeyRename(string path, string currentKey)
	{
		_activeEdit = new InlineEditState
		{
			Path = path,
			Target = InlineEditTarget.Key,
			OriginalValue = currentKey,
			CurrentValue = currentKey,
			ValueKind = JsonValueKind.String
		};
		StateHasChanged();
	}

	private async Task HandleEditCommit(InlineEditResult result)
	{
		if (_activeEdit is null || _documentManager is null)
		{
			return;
		}

		if (!result.Committed)
		{
			_activeEdit = null;
			StateHasChanged();
			return;
		}

		// Don't commit if value hasn't changed
		if (result.NewValue == _activeEdit.OriginalValue)
		{
			_activeEdit = null;
			StateHasChanged();
			return;
		}

		string editPath = _activeEdit.Path;
		InlineEditTarget editTarget = _activeEdit.Target;
		string? oldValue = _activeEdit.OriginalValue;

		try
		{
			string newJson;
			JsonChangeType changeType;

			if (editTarget == InlineEditTarget.Value)
			{
				string? error = JsonEditValidator.ValidateValue(result.NewValue, _activeEdit.ValueKind);
				if (error is not null)
				{
					_activeEdit.ValidationError = error;
					StateHasChanged();
					return;
				}

				string jsonLiteral = _activeEdit.ValueKind switch
				{
					JsonValueKind.String => JsonSerializer.Serialize(result.NewValue),
					JsonValueKind.Number => result.NewValue,
					JsonValueKind.True or JsonValueKind.False => result.NewValue == "true" ? "true" : "false",
					JsonValueKind.Null => "null",
					_ => result.NewValue
				};

				newJson = _documentManager.ReplaceValueAtPath(editPath, jsonLiteral);
				changeType = JsonChangeType.ValueChanged;
			}
			else
			{
				newJson = _documentManager.RenameKeyAtPath(editPath, result.NewValue);
				changeType = JsonChangeType.KeyRenamed;
			}

			_editHistory ??= new EditHistory();
			_editHistory.PushSnapshot(_documentManager.GetJsonString());

			_activeEdit = null;
			_previousJson = newJson;
			await ReloadFromJsonAsync(newJson);

			await JsonChanged.InvokeAsync(newJson);
			await OnJsonChanged.InvokeAsync(new JsonChangeEventArgs
			{
				FullJson = newJson,
				Path = editPath,
				ChangeType = changeType,
				OldValue = oldValue,
				NewValue = result.NewValue
			});
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
		{
			_activeEdit!.ValidationError = ex.Message;
			StateHasChanged();
		}
	}

	private void HandleEditCancel()
	{
		_activeEdit = null;
		StateHasChanged();
	}

	private async Task DeleteNodeAtPath(string path)
	{
		if (_documentManager is null)
		{
			return;
		}

		try
		{
			_editHistory ??= new EditHistory();
			_editHistory.PushSnapshot(_documentManager.GetJsonString());

			string newJson = _documentManager.RemoveNodeAtPath(path);
			_previousJson = newJson;
			_activeEdit = null;
			await ReloadFromJsonAsync(newJson);

			await JsonChanged.InvokeAsync(newJson);
			await OnJsonChanged.InvokeAsync(new JsonChangeEventArgs
			{
				FullJson = newJson,
				Path = path,
				ChangeType = JsonChangeType.NodeRemoved
			});
		}
		catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
		{
			_errorMessage = ex.Message;
			StateHasChanged();
		}
	}

	private async Task AddPropertyAtPath(string parentPath)
	{
		if (_documentManager is null)
		{
			return;
		}

		try
		{
			_editHistory ??= new EditHistory();
			_editHistory.PushSnapshot(_documentManager.GetJsonString());

			string newJson = _documentManager.AddNodeAtPath(parentPath, null, "null");
			_previousJson = newJson;
			await ReloadFromJsonAsync(newJson);

			await JsonChanged.InvokeAsync(newJson);
			await OnJsonChanged.InvokeAsync(new JsonChangeEventArgs
			{
				FullJson = newJson,
				Path = parentPath,
				ChangeType = JsonChangeType.NodeAdded
			});
		}
		catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
		{
			_errorMessage = ex.Message;
			StateHasChanged();
		}
	}

	private async Task AddElementAtPath(string parentPath)
	{
		if (_documentManager is null)
		{
			return;
		}

		try
		{
			_editHistory ??= new EditHistory();
			_editHistory.PushSnapshot(_documentManager.GetJsonString());

			string newJson = _documentManager.AddNodeAtPath(parentPath, null, "null");
			_previousJson = newJson;
			await ReloadFromJsonAsync(newJson);

			await JsonChanged.InvokeAsync(newJson);
			await OnJsonChanged.InvokeAsync(new JsonChangeEventArgs
			{
				FullJson = newJson,
				Path = parentPath,
				ChangeType = JsonChangeType.NodeAdded
			});
		}
		catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
		{
			_errorMessage = ex.Message;
			StateHasChanged();
		}
	}

	private void DismissContextMenu()
	{
		_contextMenuVisible = false;
		_contextMenuNodeContext = null;
		StateHasChanged();
	}

	#region Settings Panel

	private void ToggleSettings()
	{
		_showSettings = !_showSettings;
		if (_showSettings)
		{
			// Position below the toolbar, aligned right
			_settingsLeft = 0;
			_settingsTop = 0;
		}
	}

	private void DismissSettings()
	{
		_showSettings = false;
		StateHasChanged();
	}

	private void HandleSettingsThemeChanged(MokaJsonTheme value) => Theme = value;

	private void HandleSettingsToolbarModeChanged(MokaJsonToolbarMode value) =>
		ToolbarMode = value;

	private void HandleSettingsToggleStyleChanged(MokaJsonToggleStyle value)
	{
		ToggleStyle = value;
		StateHasChanged();
	}

	private void HandleSettingsToggleSizeChanged(MokaJsonToggleSize value)
	{
		ToggleSize = value;
		StateHasChanged();
	}

	private void HandleSettingsShowLineNumbersChanged(bool value)
	{
		ShowLineNumbers = value;
		StateHasChanged();
	}

	private void HandleSettingsWordWrapChanged(bool value) =>
		WordWrap = value;

	private void HandleSettingsShowBreadcrumbChanged(bool value) =>
		ShowBreadcrumb = value;

	private void HandleSettingsShowBottomBarChanged(bool value) =>
		ShowBottomBar = value;

	private void HandleSettingsShowChildCountChanged(bool value)
	{
		ShowChildCount = value;
		StateHasChanged();
	}

	private void HandleSettingsMaxDepthChanged(int value) =>
		MaxDepthExpanded = value;

	private void HandleSettingsCollapseModeChanged(MokaJsonCollapseMode value) =>
		CollapseMode = value;

	private void HandleSettingsReadOnlyChanged(bool value) =>
		ReadOnly = value;

	private void HandleSettingsSearchCaseSensitiveChanged(bool value) =>
		_searchCaseSensitive = value;

	private void HandleSettingsSearchUseRegexChanged(bool value) =>
		_searchUseRegex = value;

	#endregion

	private async Task HandleBreadcrumbNavigate(string path)
	{
		// If clicking root or a path above the current scope, unscope
		if (_scopedPath is not null)
		{
			if (string.IsNullOrEmpty(path) || !path.StartsWith(_scopedPath, StringComparison.Ordinal))
			{
				UnscopeToRoot();
			}
		}

		await HandleSelect(path);
	}

	private async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if (e is { CtrlKey: true, Key: "f" })
		{
			ToggleSearch();
		}
		else if (e is { CtrlKey: true, Key: "z" } && !EffectiveReadOnly)
		{
			Undo();
		}
		else if (e is { CtrlKey: true, Key: "y" } && !EffectiveReadOnly)
		{
			Redo();
		}
	}

	#endregion

	#region Context Action Builders

	private void BuildContextActions()
	{
		_allContextActions =
		[
			new MokaJsonContextAction
			{
				Id = "copy-value",
				Label = "Copy Value",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Copy),
				ShortcutHint = "Ctrl+C",
				Order = 10,
				OnExecute = async ctx =>
				{
					string value = ctx.RawValue;
					if (string.IsNullOrEmpty(value) && _documentSource is not null)
					{
						// Deferred for large containers — serialize on background thread
						try
						{
							value = await Task.Run(() => _documentSource.GetElement(ctx.Path).GetRawText());
						}
						catch (OutOfMemoryException)
						{
							ShowToast("Value too large to copy. Use \"Scope to This Node\" to copy smaller sections.");
							return;
						}
					}

					if (value.Length > OptionsAccessor.Value.MaxClipboardSizeBytes)
					{
						ShowToast("Value too large to copy. Use \"Scope to This Node\" to copy smaller sections.");
						return;
					}

					await Interop.CopyToClipboardAsync(value);
				}
			},
			new MokaJsonContextAction
			{
				Id = "copy-path",
				Label = "Copy Path",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.CopyPath),
				Order = 20,
				OnExecute = async ctx => await Interop.CopyToClipboardAsync(JsonPathConverter.ToDotNotation(ctx.Path))
			},
			new MokaJsonContextAction
			{
				Id = "expand-children",
				Label = "Expand All Children",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Expand),
				Order = 100,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
				OnExecute = async ctx => await ExpandSubtree(ctx.Path)
			},
			new MokaJsonContextAction
			{
				Id = "collapse-children",
				Label = "Collapse All Children",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Collapse),
				Order = 110,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
				OnExecute = ctx =>
				{
					CollapseSubtree(ctx.Path);
					return ValueTask.CompletedTask;
				}
			},
			new MokaJsonContextAction
			{
				Id = "scope-to-node",
				Label = "Scope to This Node",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Scope),
				Order = 200,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
				OnExecute = ctx =>
				{
					ScopeToNode(ctx.Path);
					return ValueTask.CompletedTask;
				}
			},
			new MokaJsonContextAction
			{
				Id = "sort-keys",
				Label = "Sort Keys",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Sort),
				Order = 300,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object,
				OnExecute = async ctx => await SortKeysAtPath(ctx.Path, false)
			},
			new MokaJsonContextAction
			{
				Id = "sort-keys-recursive",
				Label = "Sort Keys (Recursive)",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Sort),
				Order = 310,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object,
				OnExecute = async ctx => await SortKeysAtPath(ctx.Path, true)
			}
		];

		if (!EffectiveReadOnly)
		{
			_allContextActions.Add(new MokaJsonContextAction
			{
				Id = "edit-value",
				Label = "Edit Value",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Edit),
				ShortcutHint = "Dbl-click",
				Order = 400,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array),
				OnExecute = ctx =>
				{
					HandleDoubleClick(ctx.Path);
					return ValueTask.CompletedTask;
				}
			});
			_allContextActions.Add(new MokaJsonContextAction
			{
				Id = "rename-key",
				Label = "Rename Key",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Rename),
				ShortcutHint = "F2",
				Order = 410,
				IsVisible = ctx => ctx.PropertyName is not null,
				OnExecute = ctx =>
				{
					StartKeyRename(ctx.Path, ctx.PropertyName!);
					return ValueTask.CompletedTask;
				}
			});
			_allContextActions.Add(new MokaJsonContextAction
			{
				Id = "delete-node",
				Label = "Delete",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Delete),
				ShortcutHint = "Del",
				Order = 420,
				IsVisible = ctx => !string.IsNullOrEmpty(ctx.Path) && ctx.Path != "/",
				OnExecute = async ctx => await DeleteNodeAtPath(ctx.Path)
			});
			_allContextActions.Add(new MokaJsonContextAction
			{
				Id = "add-property",
				Label = "Add Property",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Add),
				Order = 430,
				IsVisible = ctx => ctx.ValueKind == JsonValueKind.Object,
				OnExecute = async ctx => await AddPropertyAtPath(ctx.Path)
			});
			_allContextActions.Add(new MokaJsonContextAction
			{
				Id = "add-element",
				Label = "Add Element",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Add),
				Order = 440,
				IsVisible = ctx => ctx.ValueKind == JsonValueKind.Array,
				OnExecute = async ctx => await AddElementAtPath(ctx.Path)
			});
		}

		if (ContextMenuActions is not null)
		{
			_allContextActions.AddRange(ContextMenuActions);
		}
	}

	private async Task SortKeysAtPath(string path, bool recursive)
	{
		if (_documentSource is null || _documentManager is null)
		{
			return;
		}

		try
		{
			JsonElement element = _documentSource.GetElement(path);
			if (element.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			if (!EffectiveReadOnly)
			{
				_editHistory ??= new EditHistory();
				_editHistory.PushSnapshot(_documentManager.GetJsonString());
			}

			// Sort the subtree
			string sortedSubtreeJson = recursive
				? JsonSorter.SortKeysRecursive(element)
				: JsonSorter.SortKeys(element);

			// Rebuild the entire document with the sorted subtree
			string newJson = ReplaceSubtreeJson(path, sortedSubtreeJson);

			// Re-parse the document with sorted keys.
			// Update _previousJson so OnParametersSetAsync won't detect a mismatch
			// if the parent re-renders with the old Json value.
			_previousJson = newJson;
			await ReloadFromJsonAsync(newJson);

			await JsonChanged.InvokeAsync(newJson);
			await OnJsonChanged.InvokeAsync(new JsonChangeEventArgs
			{
				FullJson = newJson,
				Path = path,
				ChangeType = JsonChangeType.KeysSorted
			});
		}
		catch (KeyNotFoundException)
		{
		}
	}

	private string ReplaceSubtreeJson(string path, string newSubtreeJson)
	{
		if (_documentSource is null)
		{
			return newSubtreeJson;
		}

		if (string.IsNullOrEmpty(path) || path == "/")
			// Replacing root entirely
		{
			return newSubtreeJson;
		}

		// Rebuild the full document with the sorted subtree inserted at the given path
		JsonElement rootElement = _documentSource.GetElement("");
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

		using var replacementDoc = JsonDocument.Parse(newSubtreeJson);
		string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		WriteWithReplacement(rootElement, writer, segments, 0, replacementDoc.RootElement);

		writer.Flush();
		return Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
	}

	private static void WriteWithReplacement(
		JsonElement current,
		Utf8JsonWriter writer,
		string[] pathSegments,
		int segmentIndex,
		JsonElement replacement)
	{
		if (segmentIndex >= pathSegments.Length)
		{
			// This is the target node - write the replacement
			replacement.WriteTo(writer);
			return;
		}

		string targetSegment = pathSegments[segmentIndex].Replace("~1", "/").Replace("~0", "~");

		switch (current.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (JsonProperty prop in current.EnumerateObject())
				{
					writer.WritePropertyName(prop.Name);
					if (prop.Name == targetSegment)
					{
						WriteWithReplacement(prop.Value, writer, pathSegments, segmentIndex + 1, replacement);
					}
					else
					{
						prop.Value.WriteTo(writer);
					}
				}

				writer.WriteEndObject();
				break;

			case JsonValueKind.Array:
				writer.WriteStartArray();
				int i = 0;
				foreach (JsonElement item in current.EnumerateArray())
				{
					if (i.ToString(CultureInfo.InvariantCulture) == targetSegment)
					{
						WriteWithReplacement(item, writer, pathSegments, segmentIndex + 1, replacement);
					}
					else
					{
						item.WriteTo(writer);
					}

					i++;
				}

				writer.WriteEndArray();
				break;

			default:
				current.WriteTo(writer);
				break;
		}
	}

	#endregion

	#region Private Helpers

	private void ScopeToNode(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		try
		{
			// Verify the path is valid
			JsonValueKind kind = _documentSource.GetValueKind(path);
			if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
			{
				return;
			}

			_scopedPath = path;
			_treeFlattener.Expand(path);
			RefreshFlatNodes();
		}
		catch (KeyNotFoundException)
		{
		}
	}

	private void UnscopeToRoot()
	{
		_scopedPath = null;
		RefreshFlatNodes();
	}

	private async Task ExpandSubtree(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		bool isLarge = source.DocumentSizeBytes >= OptionsAccessor.Value.BackgroundStatsThresholdBytes;

		if (isLarge)
		{
			await CancelTreeOperationAsync();
			_isBusy = true;
			_busyMessage = "Expanding subtree...";
			StateHasChanged();

			CancellationTokenSource cts = _treeCts = new CancellationTokenSource();
			try
			{
				await Task.Run(() => ExpandSubtreeRecursive(source, path), cts.Token);
				if (!cts.Token.IsCancellationRequested)
				{
					await RefreshFlatNodesAsync();
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
				                           or KeyNotFoundException)
			{
			}
			finally
			{
				if (_treeCts == cts)
				{
					_isBusy = false;
					_busyMessage = null;
					StateHasChanged();
				}
			}
		}
		else
		{
			try
			{
				ExpandSubtreeRecursive(source, path);
				RefreshFlatNodes();
			}
			catch (KeyNotFoundException)
			{
			}
		}
	}

	private void ExpandSubtreeRecursive(IJsonDocumentSource source, string path)
	{
		JsonValueKind kind = source.GetValueKind(path);
		if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return;
		}

		_treeFlattener.Expand(path);

		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			if (child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				ExpandSubtreeRecursive(source, child.Path);
			}
		}
	}

	private void CollapseSubtree(string path)
	{
		// Remove all expanded paths that start with the given path
		var toRemove = _treeFlattener.ExpandedPaths
			.Where(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal))
			.ToList();

		foreach (string p in toRemove)
		{
			_treeFlattener.Collapse(p);
		}

		RefreshFlatNodes();
	}

	private void EnsurePathExpanded(string path)
	{
		// Also expand the root
		_treeFlattener.Expand("");

		string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var sb = new StringBuilder(path.Length);
		foreach (string segment in segments)
		{
			sb.Append('/');
			sb.Append(segment);
			string currentPath = sb.ToString();
			if (currentPath != path)
			{
				_treeFlattener.Expand(currentPath);
			}
		}
	}

	private void ShowToast(string message, int durationMs = 5000)
	{
		_toastTimer?.Dispose();
		_toastMessage = message;
		StateHasChanged();
		_toastTimer = new Timer(_ =>
		{
			_ = InvokeAsync(() =>
			{
				_toastMessage = null;
				StateHasChanged();
			});
		}, null, durationMs, Timeout.Infinite);
	}

	private void DismissToast()
	{
		_toastTimer?.Dispose();
		_toastTimer = null;
		_toastMessage = null;
		StateHasChanged();
	}

	private static string TruncatePreview(string raw, int maxLength = 500) =>
		raw.Length > maxLength ? raw[..maxLength] + "..." : raw;


	private static string EscapeJsonPointer(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

	private async Task RaiseError(string message, Exception? ex = null, long? bytePos = null, long? lineNumber = null)
	{
		await OnError.InvokeAsync(new JsonErrorEventArgs
		{
			Message = message,
			Exception = ex,
			BytePosition = bytePos,
			LineNumber = lineNumber
		});
	}

	#endregion

	#region IMokaJsonViewer Implementation

	/// <inheritdoc />
	public async ValueTask NavigateToAsync(string jsonPointer, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(jsonPointer);
		EnsurePathExpanded(jsonPointer);
		await HandleSelect(jsonPointer);
		await RefreshFlatNodesAsync();
	}

	/// <inheritdoc />
	public void ExpandToDepth(int depth)
	{
		if (_documentSource is null)
		{
			return;
		}

		_treeFlattener.ExpandToDepth(_documentSource, depth);
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public void CollapseAll()
	{
		_treeFlattener.CollapseAll();
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public async void ExpandAll() => await HandleExpandAll();

	/// <inheritdoc />
	public async ValueTask<int> SearchAsync(string query, JsonSearchOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		if (_documentSource is null)
		{
			return 0;
		}

		return _searchEngine.Search(_documentSource, query, options, cancellationToken);
	}

	/// <inheritdoc />
	public void NextMatch() => HandleSearchNext();

	/// <inheritdoc />
	public void PreviousMatch() => HandleSearchPrevious();

	/// <inheritdoc />
	public void ClearSearch()
	{
		_searchEngine.Clear();
		_treeFlattener.ClearSearchMatches();
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public async void Undo()
	{
		if (EffectiveReadOnly || _editHistory is null || !_editHistory.CanUndo)
		{
			return;
		}

		string? snapshot = _editHistory.Undo();
		if (snapshot is null)
		{
			return;
		}

		_activeEdit = null;
		_previousJson = snapshot;
		await ReloadFromJsonAsync(snapshot);
		await JsonChanged.InvokeAsync(snapshot);
	}

	/// <inheritdoc />
	public async void Redo()
	{
		if (EffectiveReadOnly || _editHistory is null || !_editHistory.CanRedo)
		{
			return;
		}

		string? snapshot = _editHistory.Redo();
		if (snapshot is null)
		{
			return;
		}

		_activeEdit = null;
		_previousJson = snapshot;
		await ReloadFromJsonAsync(snapshot);
		await JsonChanged.InvokeAsync(snapshot);
	}

	/// <inheritdoc />
	public string GetJson(bool indented = true) => _documentSource?.GetJsonString(indented) ?? string.Empty;

	/// <inheritdoc />
	public string? SelectedPath { get; private set; }

	/// <inheritdoc />
	public bool IsEditing => _activeEdit is not null;

	#endregion
}
