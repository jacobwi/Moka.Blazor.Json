using System.Globalization;
using System.Runtime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Interop;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Moka.Blazor.Json.Utilities;

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
		_loadVersion++;

		_searchDebounceTimer?.Dispose();
		_toastTimer?.Dispose();

		if (_loadCts is not null)
		{
			await _loadCts.CancelAsync();
			_loadCts.Dispose();
			_loadCts = null;
		}

		if (_searchCts is not null)
		{
			await _searchCts.CancelAsync();
			_searchCts.Dispose();
			_searchCts = null;
		}

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

		if (OptionsAccessor.Value.AggressiveCleanup)
		{
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
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
	///     A stream containing JSON data. Use this for large documents.
	/// </summary>
	/// <remarks>
	///     A seekable stream (such as a <see cref="FileStream" />) above
	///     <see cref="MokaJsonViewerOptions.LazyParsingThresholdBytes" /> is read on demand, so it can be far larger
	///     than memory. Keep it open and don't read from it while the viewer shows it. Other streams, such as
	///     browser uploads, are copied to memory or a temp file first.
	/// </remarks>
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

	private const int ProgressRenderIntervalMs = 150;
	private const int BusyIndicatorDelayMs = 150;
	private const long MaxEagerRawValueBytes = 1024 * 1024;
	private const int MaxInlineEditChars = 16 * 1024;

	private const string ExpandTruncatedMessage =
		"This is too large to expand fully, so only the top levels are open. Right-click a node to expand or scope to it.";

	private const string ValueTooLargeToCopyMessage =
		"Value too large to copy. Use \"Scope to This Node\" to copy smaller sections.";

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
	private bool _cssReady;
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
	private bool _searchStale;
	private int _searchRequestId;
	private CancellationTokenSource? _searchCts;
	private MokaJsonSearchOverlay? _searchOverlay;
	private MokaJsonTreeViewport? _viewport;
	private string? _pendingRevealPath;
	private bool _focusViewerAfterRender;
	private bool _keyGuardsAttached;
	private int _loadVersion;

	private bool _contextMenuVisible;
	private MokaJsonNodeContext? _contextMenuNodeContext;
	private List<MokaJsonContextAction> _allContextActions = [];

	private bool _showSettings;
	private double _settingsLeft;
	private double _settingsTop;

	private string? _documentSize;
	private long _nodeCount;
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

	private CancellationTokenSource? _loadCts;
	private bool _loadCancelled;
	private string _loadVerb = "Loading";
	private long _loadBytesDone;
	private long _loadBytesTotal;
	private long _lastLoadRenderTick;
	private int _flattenVersion;
	private bool _isRefreshing;
	private bool _searchTruncated;
	private int _searchPercent = -1;
	private bool _skipNextRender;

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

	private string ComputedStyle =>
		_cssReady || _isLoading || !IsInteractive ? HeightStyle : $"visibility:hidden;{HeightStyle}";

	// False during prerendering and static server rendering, when OnAfterRenderAsync won't run.
	private bool IsInteractive
	{
		get
		{
			try
			{
				return RendererInfo.IsInteractive;
			}
			catch (Exception ex) when (ex.GetType().Name == "MissingRendererInfoException")
			{
				// bUnit throws this unless a test configures it, which shouldn't break tests of a page using the viewer.
				return true;
			}
		}
	}

	private bool IsPrerendering => !IsInteractive && AssignedRenderMode is not null;

	private JsonExpandLimits ExpandLimits => _isLazyMode ? JsonExpandLimits.LargeDocument : JsonExpandLimits.Default;

	private string LoadingText
	{
		get
		{
			long done = Volatile.Read(ref _loadBytesDone);
			if (_loadBytesTotal > 0)
			{
				return
					$"{_loadVerb} {JsonDocumentManager.FormatBytes(done)} of {JsonDocumentManager.FormatBytes(_loadBytesTotal)}";
			}

			return done > 0 ? $"{_loadVerb} {JsonDocumentManager.FormatBytes(done)}" : "Loading...";
		}
	}

	private string LoadPercentStyle =>
		string.Create(CultureInfo.InvariantCulture,
			$"width: {Math.Clamp(Volatile.Read(ref _loadBytesDone) * 100.0 / Math.Max(1, _loadBytesTotal), 0, 100):F1}%");

	private MokaJsonToolbarMode EffectiveToolbarMode =>
		ToolbarMode ?? OptionsAccessor.Value.DefaultToolbarMode;

	private bool EffectiveShowSettingsButton =>
		ShowSettingsButton ?? OptionsAccessor.Value.ShowSettingsButton;

	/// <summary>
	///     Effective read-only state: true if the ReadOnly parameter is set OR if the
	///     document source doesn't support editing (lazy mode).
	/// </summary>
	private bool EffectiveReadOnly =>
		ReadOnly || !OptionsAccessor.Value.EnableEditMode || _documentSource is { SupportsEditing: false };

	private string ThemeAttribute => Theme switch
	{
		MokaJsonTheme.Light => "light",
		MokaJsonTheme.Dark => "dark",
		MokaJsonTheme.Inherit => "",
		_ => "auto"
	};

	#endregion

	#region Lifecycle

	private sealed record ViewState(IReadOnlySet<string> ExpandedPaths, string? ScopedPath);

	/// <inheritdoc />
	protected override void OnInitialized()
	{
		MokaJsonViewerOptions options = OptionsAccessor.Value;
		if (!_parentSettings.ContainsKey(nameof(Theme)))
		{
			Theme = options.DefaultTheme;
		}

		if (!_parentSettings.ContainsKey(nameof(MaxDepthExpanded)))
		{
			MaxDepthExpanded = options.DefaultExpandDepth;
		}

		BuildContextActions();
	}

	/// <inheritdoc />
	protected override bool ShouldRender()
	{
		if (_skipNextRender)
		{
			_skipNextRender = false;
			return false;
		}

		return true;
	}

	/// <inheritdoc />
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender && !_cssReady)
		{
			_cssReady = true;
			StateHasChanged();
		}

		if (!_keyGuardsAttached)
		{
			_keyGuardsAttached = true;
			await TryInteropAsync(() => Interop.AttachKeyGuardsAsync(_viewerRef));
		}

		if (_focusViewerAfterRender)
		{
			_focusViewerAfterRender = false;
			await TryInteropAsync(() => _viewerRef.FocusAsync(preventScroll: true));
		}

		if (_pendingRevealPath is not null && _viewport is not null && _flatNodes is not null)
		{
			string path = _pendingRevealPath;
			_pendingRevealPath = null;
			int row = _flatNodes.FindIndex(n => !n.IsClosingBracket && n.Path == path);
			if (row >= 0)
			{
				MokaJsonTreeViewport viewport = _viewport;
				await TryInteropAsync(() => viewport.ScrollToRowAsync(row));
			}
		}
	}

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

			// The interactive render starts over with a new instance. Work done now would be thrown away, and it
			// would hold up the page's first response. A forward-only stream can also only be read once.
			if (IsPrerendering &&
			    (JsonStream is not null || Json?.Length > OptionsAccessor.Value.LazyParsingThresholdBytes))
			{
				_isLoading = true;
				return;
			}

			await LoadJsonAsync();
		}
	}

	private async Task LoadJsonAsync(bool preserveView = false)
	{
		int loadVersion = ++_loadVersion;
		ViewState? view = preserveView && _isLoaded
			? new ViewState(_treeFlattener.ExpandedPaths, _scopedPath)
			: null;

		_errorMessage = null;
		_loadCancelled = false;
		_isLoaded = false;
		_isLoading = true;
		_flatNodes = null;
		_loadVerb = "Loading";
		_loadBytesDone = 0;
		_loadBytesTotal = 0;

		if (_loadCts is not null)
		{
			await _loadCts.CancelAsync();
			_loadCts.Dispose();
		}

		CancellationTokenSource loadCts = _loadCts = new CancellationTokenSource();
		CancellationToken loadToken = loadCts.Token;

		CancelSearch();
		_searchEngine.Clear();
		_treeFlattener.ClearSearchMatches();
		_scopedPath = null;

		if (!preserveView)
		{
			SelectedPath = null;
			_selectedDepth = 0;
			_editHistory = null;
		}

		// Yield FIRST so Blazor renders the loading spinner before any work begins.
		// Without this yield the component blocks through disposal + parse without
		// ever showing the loading state.
		StateHasChanged();
		await Task.Yield();

		if (loadVersion != _loadVersion)
		{
			return;
		}

		try
		{
			// Dispose previous source/manager (may be slow for large lazy caches)
			IJsonDocumentSource? previous = _documentSource ?? _documentManager;
			_documentSource = null;
			_documentManager = null;
			_isLazyMode = false;
			DebugStats = null;

			if (previous is not null)
			{
				await previous.DisposeAsync();
			}

			IJsonDocumentSource? source = await CreateDocumentSourceAsync(loadToken);

			// A newer load started while this one was parsing; it owns the viewer state now.
			if (loadVersion != _loadVersion)
			{
				if (source is not null)
				{
					await source.DisposeAsync();
				}

				return;
			}

			if (source is null)
			{
				_isLoading = false;
				return;
			}

			_documentSource = source;
			_documentManager = source as JsonDocumentManager;
			_isLazyMode = source is LazyJsonDocumentSource;
			DebugStats = (source as LazyJsonDocumentSource)?.DebugStats;

			int expandDepth = CollapseMode switch
			{
				MokaJsonCollapseMode.Root => 0,
				MokaJsonCollapseMode.Expanded => -1,
				_ => MaxDepthExpanded
			};

			// Always offload tree expansion and flattening to a background thread
			// so the UI remains responsive during initial load.
			JsonExpandLimits firstViewLimits = _isLazyMode ? JsonExpandLimits.LargeDocumentFirstView : ExpandLimits;
			(List<FlattenedJsonNode> nodes, string? scopedPath) = await Task.Run(() =>
			{
				if (view is null)
				{
					_treeFlattener.ExpandToDepth(source, expandDepth, firstViewLimits, loadToken);
					return (_treeFlattener.Flatten(source), (string?)null);
				}

				_treeFlattener.RestoreExpandedPaths(view.ExpandedPaths);
				if (view.ScopedPath is not null)
				{
					try
					{
						return (_treeFlattener.FlattenScoped(source, view.ScopedPath), view.ScopedPath);
					}
					catch (KeyNotFoundException)
					{
					}
				}

				return (_treeFlattener.Flatten(source), (string?)null);
			});

			if (loadVersion != _loadVersion)
			{
				return;
			}

			_flatNodes = nodes;
			_scopedPath = scopedPath;

			_documentSize = JsonDocumentManager.FormatBytes(source.DocumentSizeBytes);
			_parseTimeMs = source.ParseTime.TotalSeconds >= 1
				? $"{source.ParseTime.TotalSeconds:F1} s"
				: $"{source.ParseTime.TotalMilliseconds:F1} ms";

			// Always compute stats on a background thread to keep the UI responsive.
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
				try
				{
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}

					long nc = source.CountNodes();
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
				}
				catch (Exception ex) when (
					ex is ObjectDisposedException or InvalidOperationException or OperationCanceledException
					&& (cts.Token.IsCancellationRequested || _disposed))
				{
					// Source was disposed or operation was cancelled — ignore
				}
			}, cts.Token);

			_isValid = true;
			_validationError = null;
			_isLoaded = true;
			_isLoading = false;

			// Rebuild context actions since EffectiveReadOnly may have changed
			BuildContextActions();

			if (_showSearch && !string.IsNullOrEmpty(_searchQuery))
			{
				await ExecuteSearchAsync(!preserveView);
			}
		}
		catch (Exception ex) when (loadVersion != _loadVersion && ex is not OutOfMemoryException)
		{
			// Superseded by a newer load, which owns the error/loading state.
		}
		catch (OperationCanceledException)
		{
			_loadCancelled = true;
			_isLoading = false;
		}
		catch (JsonException ex)
		{
			_isValid = false;
			_validationError = ex.Message;
			_errorMessage = ex.Message;
			_isLoading = false;
			await RaiseError(ex.Message, ex, ex.BytePositionInLine, ex.LineNumber);
		}
		catch (ObjectDisposedException)
		{
			_isLoading = false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_errorMessage = ex.Message;
			_isLoading = false;
			await RaiseError(ex.Message, ex);
		}
	}

	private void CancelLoad() => _loadCts?.Cancel();

	private void ReportLoadProgress(long bytes)
	{
		Volatile.Write(ref _loadBytesDone, bytes);

		long now = Environment.TickCount64;
		if (now - Volatile.Read(ref _lastLoadRenderTick) >= ProgressRenderIntervalMs)
		{
			Volatile.Write(ref _lastLoadRenderTick, now);
			_ = InvokeAsync(StateHasChanged);
		}
	}

	private async Task<IJsonDocumentSource?> CreateDocumentSourceAsync(CancellationToken cancellationToken)
	{
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
				return await LazyJsonDocumentSource.CreateFromStringAsync(json, lazyLogger, OptionsAccessor.Value,
					cancellationToken: cancellationToken);
			}

			var manager = new JsonDocumentManager(
				LoggerFactory.CreateLogger<JsonDocumentManager>(),
				OptionsAccessor);

			try
			{
				// Always offload parsing to a background thread so the UI stays responsive.
				// Even small documents benefit since JsonDocument.Parse is synchronous.
				await Task.Run(() => manager.ParseAsync(json, cancellationToken), cancellationToken);
			}
			catch
			{
				await manager.DisposeAsync();
				throw;
			}

			return manager;
		}

		if (JsonStream is not null)
		{
			Stream stream = JsonStream;
			bool ownsStream = false;
			long maxBytes = OptionsAccessor.Value.MaxDocumentSizeBytes;

			if (stream.CanSeek)
			{
				// Callers may pass a reused stream, so rewind it first
				if (stream.Position != 0)
				{
					stream.Position = 0;
				}
			}
			else
			{
				_loadVerb = "Reading";
				stream = await JsonStreamSpooler.ToSeekableAsync(stream, lazyThreshold, maxBytes, ReportLoadProgress,
					cancellationToken);
				ownsStream = true;
			}

			if (stream.Length > maxBytes)
			{
				throw new InvalidOperationException(
					$"Document size ({JsonDocumentManager.FormatBytes(stream.Length)}) exceeds the maximum allowed size ({JsonDocumentManager.FormatBytes(maxBytes)}).");
			}

			if (stream.Length > lazyThreshold)
			{
				_loadVerb = "Indexing";
				_loadBytesTotal = stream.Length;
				return await LazyJsonDocumentSource.CreateAsync(stream, ownsStream, lazyLogger, null,
					ReportLoadProgress, cancellationToken);
			}

			var manager = new JsonDocumentManager(
				LoggerFactory.CreateLogger<JsonDocumentManager>(),
				OptionsAccessor);

			try
			{
				await Task.Run(() => manager.ParseAsync(stream, cancellationToken), cancellationToken);
			}
			catch
			{
				await manager.DisposeAsync();
				throw;
			}
			finally
			{
				if (ownsStream)
				{
					await stream.DisposeAsync();
				}
			}

			return manager;
		}

		return null;
	}

	private async Task ReloadFromJsonAsync(string json)
	{
		// Temporarily set Json so LoadJsonAsync picks it up, then restore.
		string? originalJson = Json;
		Json = json;
		await LoadJsonAsync(preserveView: true);
		// Don't leave the parameter mutated — restore original so parent stays in control
		Json = originalJson;
	}

	private void RefreshFlatNodes(string? revealPath = null)
	{
		if (!_isLoaded || _documentSource is null)
		{
			return;
		}

		// Rows of a large document can come from disk, which mustn't hold up the UI thread.
		if (_isLazyMode)
		{
			RunDetached(() => new ValueTask(InvokeAsync(() => RefreshFlatNodesAsync(revealPath))));
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

		if (revealPath is not null)
		{
			_pendingRevealPath = revealPath;
		}

		StateHasChanged();
	}

	private async Task RefreshFlatNodesAsync(string? revealPath = null)
	{
		if (!_isLoaded || _documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		string? scopedPath = _scopedPath;
		int version = ++_flattenVersion;

		Task<List<FlattenedJsonNode>> flatten = Task.Run(() =>
		{
			if (revealPath is not null)
			{
				try
				{
					_treeFlattener.RevealPath(source, revealPath);
				}
				catch (KeyNotFoundException)
				{
				}
			}

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

		List<FlattenedJsonNode> nodes;
		try
		{
			if (await Task.WhenAny(flatten, Task.Delay(BusyIndicatorDelayMs)) != flatten)
			{
				_isRefreshing = true;
				StateHasChanged();
			}

			nodes = await flatten;
		}
		catch (ObjectDisposedException)
		{
			// Source was disposed by a concurrent operation (e.g., new document loaded) — ignore
			return;
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			ShowToast(CouldNotRead(ex));
			return;
		}
		finally
		{
			_isRefreshing = false;
		}

		// Toggling quickly starts overlapping flattens, which can finish out of order.
		if (source != _documentSource || version != _flattenVersion)
		{
			return;
		}

		// If scoped path was invalid, reset it on the UI thread
		if (scopedPath is not null && nodes.Count > 0 && nodes[0].Path != scopedPath)
		{
			_scopedPath = null;
		}

		_flatNodes = nodes;
		if (revealPath is not null)
		{
			_pendingRevealPath = revealPath;
		}

		StateHasChanged();
	}

	#endregion

	#region Toolbar Handlers

	private async Task HandleExpandAll()
	{
		if (_documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		JsonExpandLimits limits = ExpandLimits;

		// Cancel any in-flight tree operation
		await CancelTreeOperationAsync();
		_isBusy = true;
		_busyMessage = "Expanding all nodes...";
		StateHasChanged();

		CancellationTokenSource cts = _treeCts = new CancellationTokenSource();
		CancellationToken token = cts.Token;
		try
		{
			bool truncated = await Task.Run(() => _treeFlattener.ExpandAll(source, limits, token), token);
			if (!token.IsCancellationRequested)
			{
				await RefreshFlatNodesAsync();
				if (truncated)
				{
					ShowToast(ExpandTruncatedMessage);
				}
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			ShowToast(CouldNotRead(ex));
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

	private async Task HandleCollapseAll()
	{
		if (_documentSource is null)
		{
			return;
		}

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
		if (_documentSource is not { } source)
		{
			return;
		}

		// Guard against OOM for very large documents
		if (source.DocumentSizeBytes > OptionsAccessor.Value.MaxClipboardSizeBytes)
		{
			ShowToast(
				"Document too large to copy. Right-click a node and use \"Scope to This Node\" to copy smaller sections.");
			return;
		}

		bool formatted = _isFormatted;
		string json;
		try
		{
			json = await Task.Run(() => source.GetJsonString(formatted));
		}
		catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException or IOException)
		{
			ShowToast(ex.Message);
			return;
		}

		await Interop.CopyToClipboardAsync(json);
	}

	private async Task HandleExport()
	{
		if (_documentSource is not { } source)
		{
			return;
		}

		long maxBytes = OptionsAccessor.Value.MaxExportSizeBytes;
		if (source.DocumentSizeBytes > maxBytes)
		{
			ShowToast($"Document too large to export here (limit {JsonDocumentManager.FormatBytes(maxBytes)}).");
			return;
		}

		bool formatted = _isFormatted;
		string fileName = $"export-{DateTime.Now:yyyyMMdd-HHmmss}.json";

		var content = new MemoryStream();
		try
		{
			await Task.Run(() => source.WriteJson(content, formatted));
			content.Position = 0;
		}
		catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException or IOException)
		{
			await content.DisposeAsync();
			ShowToast(ex.Message);
			return;
		}

		await Interop.DownloadFileAsync(fileName, content);
	}

	#endregion

	#region Search Handlers

	private bool HasSearchQuery => !string.IsNullOrEmpty(_searchQuery);

	private async Task ToggleSearch()
	{
		if (_showSearch)
		{
			await CloseSearchAsync();
		}
		else
		{
			_showSearch = true;
		}
	}

	private async Task CloseSearchAsync()
	{
		_showSearch = false;
		_searchQuery = null;
		ResetSearchState();
		_focusViewerAfterRender = true;
		await RefreshFlatNodesAsync();
	}

	private void ResetSearchState()
	{
		_searchStale = false;
		_searchTruncated = false;
		_searchPercent = -1;
		_searchRequestId++;
		_searchDebounceTimer?.Dispose();
		_searchDebounceTimer = null;
		CancelSearch();
		_searchEngine.Clear();
		_treeFlattener.ClearSearchMatches();
	}

	private void CancelSearch()
	{
		CancellationTokenSource? cts = _searchCts;
		_searchCts = null;
		cts?.Cancel();
	}

	private async Task HandleSearchQueryChanged(string query)
	{
		_searchQuery = query;
		_searchStale = true;
		int requestId = ++_searchRequestId;
		_searchDebounceTimer?.Dispose();
		_searchDebounceTimer = null;
		CancelSearch();

		int debounceMs = OptionsAccessor.Value.SearchDebounceMs;
		if (debounceMs <= 0)
		{
			await ExecuteSearchAsync();
			return;
		}

		_searchDebounceTimer = new Timer(_ =>
		{
			if (_disposed)
			{
				return;
			}

			_ = InvokeAsync(async () =>
			{
				// A newer keystroke, an Enter press or a close already handled this request.
				if (requestId == _searchRequestId && !_disposed)
				{
					await ExecuteSearchAsync();
				}
			});
		}, null, debounceMs, Timeout.Infinite);
	}

	private Task HandleSearchNext() => NavigateSearchAsync(true);

	private Task HandleSearchPrevious() => NavigateSearchAsync(false);

	private async Task NavigateSearchAsync(bool forward)
	{
		// Results still belong to the previous query (debounce pending or search running):
		// run the current query now so this press lands on its first match.
		if (_searchStale)
		{
			// The current query is already being searched; on a large document starting over would lose that work.
			if (_searchCts is null)
			{
				await ExecuteSearchAsync();
			}

			return;
		}

		string? path = forward ? _searchEngine.NextMatch() : _searchEngine.PreviousMatch();
		if (path is null)
		{
			return;
		}

		_treeFlattener.SetActiveSearchMatch(path);
		await RefreshFlatNodesAsync(path);
	}

	private async Task HandleCaseSensitiveChanged(bool value)
	{
		_searchCaseSensitive = value;
		await ExecuteSearchAsync();
	}

	private async Task HandleRegexChanged(bool value)
	{
		_searchUseRegex = value;
		await ExecuteSearchAsync();
	}

	private Task ExecuteSearchAsync() => ExecuteSearchAsync(true);

	private async Task ExecuteSearchAsync(bool reveal)
	{
		_searchRequestId++;
		_searchDebounceTimer?.Dispose();
		_searchDebounceTimer = null;
		CancelSearch();

		var options = new JsonSearchOptions
		{
			CaseSensitive = _searchCaseSensitive,
			UseRegex = _searchUseRegex
		};

		await RunSearchAsync(_searchQuery, options, reveal, CancellationToken.None);
	}

	private async Task<int> RunSearchAsync(string? query, JsonSearchOptions options, bool reveal,
		CancellationToken cancellationToken)
	{
		IJsonDocumentSource? source = _documentSource;
		if (source is null || !_isLoaded || string.IsNullOrEmpty(query))
		{
			_searchStale = false;
			_searchTruncated = false;
			_searchEngine.Clear();
			_treeFlattener.ClearSearchMatches();
			await RefreshFlatNodesAsync();
			return 0;
		}

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_searchCts = cts;
		_searchPercent = -1;
		string rootPath = _scopedPath ?? "";

		JsonSearchResult result;
		try
		{
			result = await FindMatchesAsync(source, rootPath, query, options, cts.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return 0;
		}
		catch (Exception ex) when (ex is ObjectDisposedException or KeyNotFoundException)
		{
			return 0;
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			_searchStale = false;
			ShowToast(CouldNotRead(ex));
			return 0;
		}
		finally
		{
			if (_searchCts == cts)
			{
				_searchCts = null;
			}
		}

		if (cts.IsCancellationRequested || source != _documentSource)
		{
			return 0;
		}

		_searchStale = false;
		_searchTruncated = result.IsTruncated;
		_searchPercent = -1;
		_searchEngine.SetMatches(result.Paths);
		string? activePath = _searchEngine.ActiveMatchPath;
		_treeFlattener.SetSearchMatches(result.Paths, activePath);
		await RefreshFlatNodesAsync(reveal ? activePath : null);
		return result.Paths.Count;
	}

	private Task<JsonSearchResult> FindMatchesAsync(IJsonDocumentSource source, string rootPath, string query,
		JsonSearchOptions options, CancellationToken cancellationToken)
	{
		if (source is LazyJsonDocumentSource lazy && lazy.TryGetSearchRange(rootPath, out long start, out long end))
		{
			return lazy.SearchAsync(rootPath, start, end, query, options, OptionsAccessor.Value.MaxSearchMatches,
				new SearchProgressReporter(this, cancellationToken), cancellationToken);
		}

		return Task.Run(
			() => new JsonSearchResult(_searchEngine.FindMatches(source, rootPath, query, options, cancellationToken),
				false),
			cancellationToken);
	}

	private sealed class SearchProgressReporter(MokaJsonViewer viewer, CancellationToken cancellationToken)
		: IProgress<StreamingJsonSearcher.SearchProgress>
	{
		private long _lastRenderTick;

		public void Report(StreamingJsonSearcher.SearchProgress value)
		{
			long now = Environment.TickCount64;
			if (now - _lastRenderTick < ProgressRenderIntervalMs || cancellationToken.IsCancellationRequested)
			{
				return;
			}

			_lastRenderTick = now;
			int percent = (int)(value.BytesScanned * 100 / Math.Max(1, value.TotalBytes));
			_ = viewer.InvokeAsync(() =>
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					viewer._searchPercent = percent;
					viewer.StateHasChanged();
				}
			});
		}
	}

	#endregion

	#region Tree Interaction Handlers

	private async Task HandleToggle(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		_treeFlattener.ToggleExpand(path);

		try
		{
			await RefreshFlatNodesAsync();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task HandleSelect(string path)
	{
		SelectedPath = path;
		_selectedDepth = JsonPointerHelper.Split(path).Length;

		if (_documentSource is { } source && OnNodeSelected.HasDelegate)
		{
			try
			{
				JsonNodeSelectedEventArgs args = await RunOnSourceAsync(() => BuildSelectionArgs(source, path));
				await OnNodeSelected.InvokeAsync(args);
			}
			catch (Exception ex) when (ex is KeyNotFoundException or ObjectDisposedException)
			{
				// Path no longer valid
			}
			catch (Exception ex) when (ex is IOException or JsonException)
			{
				ShowToast(CouldNotRead(ex));
			}
		}

		StateHasChanged();
	}

	private JsonNodeSelectedEventArgs BuildSelectionArgs(IJsonDocumentSource source, string path)
	{
		JsonValueKind kind = source.GetValueKind(path);
		bool isContainer = kind is JsonValueKind.Object or JsonValueKind.Array;
		string rawText = isContainer ? "" : source.GetElement(path).GetRawText();

		return new JsonNodeSelectedEventArgs
		{
			Path = path,
			Depth = JsonPointerHelper.Split(path).Length,
			ValueKind = kind,
			PropertyName = GetPropertyName(source, path),
			RawValue = rawText,
			RawValuePreview = isContainer ? $"{source.GetChildCount(path)} items" : TruncatePreview(rawText)
		};
	}

	// A read from a large document might go to disk. In-memory documents answer straight away.
	private Task<T> RunOnSourceAsync<T>(Func<T> read) => _isLazyMode ? Task.Run(read) : Task.FromResult(read());

	private async Task HandleContextMenuRequest((string Path, double ClientX, double ClientY) args)
	{
		if (_documentSource is not { } source)
		{
			return;
		}

		try
		{
			_contextMenuNodeContext = await RunOnSourceAsync(() => BuildNodeContext(source, args.Path));
			_contextMenuVisible = true;
			StateHasChanged();

			if (_contextMenu is not null)
			{
				await _contextMenu.ShowAsync(args.ClientX, args.ClientY);
			}
		}
		catch (Exception ex) when (ex is KeyNotFoundException or ObjectDisposedException)
		{
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			ShowToast(CouldNotRead(ex));
		}
	}

	private MokaJsonNodeContext BuildNodeContext(IJsonDocumentSource source, string path)
	{
		JsonValueKind kind = source.GetValueKind(path);
		bool isContainer = kind is JsonValueKind.Object or JsonValueKind.Array;

		// Right-clicking must stay instant whatever the node's size. Copy Value reads large values when it runs.
		string rawText = source.GetByteLength(path) <= MaxEagerRawValueBytes
			? source.GetElement(path).GetRawText()
			: "";

		return new MokaJsonNodeContext
		{
			Path = path,
			Depth = JsonPointerHelper.Split(path).Length,
			ValueKind = kind,
			PropertyName = GetPropertyName(source, path),
			RawValue = rawText,
			RawValuePreview = isContainer ? $"{source.GetChildCount(path)} items" : TruncatePreview(rawText),
			Viewer = this
		};
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

			// On Blazor Server each keystroke sends the whole value, and a message over 32 KB by default ends the circuit.
			if (rawValue.Length > MaxInlineEditChars)
			{
				ShowToast("This value is too long to edit inline.");
				return;
			}

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

		InlineEditState edit = _activeEdit;
		if (!result.Committed || result.NewValue == edit.OriginalValue)
		{
			_activeEdit = null;
			StateHasChanged();
			return;
		}

		string before;
		string newJson;
		JsonChangeType changeType;
		try
		{
			if (edit.Target == InlineEditTarget.Value)
			{
				string? error = JsonEditValidator.ValidateValue(result.NewValue, edit.ValueKind);
				if (error is not null)
				{
					_activeEdit = edit.WithError(error);
					StateHasChanged();
					return;
				}

				newJson = _documentManager.ReplaceValueAtPath(edit.Path,
					JsonEditValidator.ToJsonLiteral(result.NewValue, edit.ValueKind));
				changeType = JsonChangeType.ValueChanged;
			}
			else
			{
				newJson = _documentManager.RenameKeyAtPath(edit.Path, result.NewValue);
				changeType = JsonChangeType.KeyRenamed;
			}

			before = _documentManager.GetJsonString();
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
			                           or ArgumentException)
		{
			_activeEdit = edit.WithError(ex.Message);
			StateHasChanged();
			return;
		}

		if (changeType == JsonChangeType.KeyRenamed && SelectedPath == edit.Path)
		{
			int slash = edit.Path.LastIndexOf('/');
			SelectedPath = $"{edit.Path[..slash]}/{JsonPointerHelper.EscapeSegment(result.NewValue)}";
		}

		_activeEdit = null;
		await CommitDocumentChangeAsync(before, newJson, new JsonChangeEventArgs
		{
			FullJson = newJson,
			Path = edit.Path,
			ChangeType = changeType,
			OldValue = edit.OriginalValue,
			NewValue = result.NewValue
		});
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

		string before;
		string newJson;
		try
		{
			before = _documentManager.GetJsonString();
			newJson = _documentManager.RemoveNodeAtPath(path);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
			                           or ArgumentException)
		{
			ShowToast(ex.Message);
			return;
		}

		if (SelectedPath is not null &&
		    (SelectedPath == path || SelectedPath.StartsWith(path + "/", StringComparison.Ordinal)))
		{
			SelectedPath = null;
		}

		_activeEdit = null;
		await CommitDocumentChangeAsync(before, newJson, new JsonChangeEventArgs
		{
			FullJson = newJson,
			Path = path,
			ChangeType = JsonChangeType.NodeRemoved
		});
	}

	private Task AddPropertyAtPath(string parentPath) => AddChildAtPath(parentPath);

	private Task AddElementAtPath(string parentPath) => AddChildAtPath(parentPath);

	private async Task AddChildAtPath(string parentPath)
	{
		if (_documentManager is null)
		{
			return;
		}

		string before;
		string newJson;
		try
		{
			before = _documentManager.GetJsonString();
			newJson = _documentManager.AddNodeAtPath(parentPath, null, "null");
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
			                           or ArgumentException)
		{
			ShowToast(ex.Message);
			return;
		}

		_treeFlattener.Expand(parentPath);
		await CommitDocumentChangeAsync(before, newJson, new JsonChangeEventArgs
		{
			FullJson = newJson,
			Path = parentPath,
			ChangeType = JsonChangeType.NodeAdded
		});
	}

	private async Task CommitDocumentChangeAsync(string? before, string newJson, JsonChangeEventArgs change)
	{
		if (before is not null)
		{
			RecordEdit(before, newJson);
		}

		// Keep OnParametersSetAsync from treating the parent's next render as a new document.
		_previousJson = newJson;
		await ReloadFromJsonAsync(newJson);

		await JsonChanged.InvokeAsync(newJson);
		await OnJsonChanged.InvokeAsync(change);
	}

	private void RecordEdit(string before, string after)
	{
		// The history stores document states, so it needs the state before the first edit
		// as well as the state after every edit for undo and redo to line up.
		if (_editHistory is null)
		{
			_editHistory = new EditHistory();
			_editHistory.PushSnapshot(before);
		}

		_editHistory.PushSnapshot(after);
	}

	private void DismissContextMenu()
	{
		_contextMenuVisible = false;
		_contextMenuNodeContext = null;
		StateHasChanged();
	}

	#region Settings Panel

	// Settings-panel choices are written straight to these parameter properties. A parent re-render passes
	// its own values again, so a choice is kept until the parent actually changes that parameter.
	private static readonly HashSet<string> UserSettingNames =
	[
		nameof(Theme), nameof(ToolbarMode), nameof(ToggleStyle), nameof(ToggleSize), nameof(ShowLineNumbers),
		nameof(WordWrap), nameof(ShowBreadcrumb), nameof(ShowBottomBar), nameof(ShowChildCount),
		nameof(MaxDepthExpanded), nameof(CollapseMode), nameof(ReadOnly)
	];

	private readonly Dictionary<string, object?> _userSettings = [];
	private readonly Dictionary<string, object?> _parentSettings = [];

	/// <inheritdoc />
	public override Task SetParametersAsync(ParameterView parameters)
	{
		if (_userSettings.Count == 0)
		{
			RecordParentSettings(parameters);
			return base.SetParametersAsync(parameters);
		}

		var values = new Dictionary<string, object?>(parameters.ToDictionary());
		foreach ((string name, object? userValue) in _userSettings.ToList())
		{
			if (!values.TryGetValue(name, out object? incoming))
			{
				continue;
			}

			if (_parentSettings.TryGetValue(name, out object? previous) && Equals(previous, incoming))
			{
				values[name] = userValue;
			}
			else
			{
				_userSettings.Remove(name);
			}
		}

		RecordParentSettings(parameters);
		return base.SetParametersAsync(ParameterView.FromDictionary(values));
	}

	private void RecordParentSettings(ParameterView parameters)
	{
		foreach (ParameterValue parameter in parameters)
		{
			if (UserSettingNames.Contains(parameter.Name))
			{
				_parentSettings[parameter.Name] = parameter.Value;
			}
		}
	}

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

	private void HandleSettingsThemeChanged(MokaJsonTheme value)
	{
		Theme = value;
		_userSettings[nameof(Theme)] = value;
	}

	private void HandleSettingsToolbarModeChanged(MokaJsonToolbarMode value)
	{
		ToolbarMode = value;
		_userSettings[nameof(ToolbarMode)] = (MokaJsonToolbarMode?)value;
	}

	private void HandleSettingsToggleStyleChanged(MokaJsonToggleStyle value)
	{
		ToggleStyle = value;
		_userSettings[nameof(ToggleStyle)] = value;
		StateHasChanged();
	}

	private void HandleSettingsToggleSizeChanged(MokaJsonToggleSize value)
	{
		ToggleSize = value;
		_userSettings[nameof(ToggleSize)] = value;
		StateHasChanged();
	}

	private void HandleSettingsShowLineNumbersChanged(bool value)
	{
		ShowLineNumbers = value;
		_userSettings[nameof(ShowLineNumbers)] = value;
		StateHasChanged();
	}

	private void HandleSettingsWordWrapChanged(bool value)
	{
		WordWrap = value;
		_userSettings[nameof(WordWrap)] = value;
	}

	private void HandleSettingsShowBreadcrumbChanged(bool value)
	{
		ShowBreadcrumb = value;
		_userSettings[nameof(ShowBreadcrumb)] = value;
	}

	private void HandleSettingsShowBottomBarChanged(bool value)
	{
		ShowBottomBar = value;
		_userSettings[nameof(ShowBottomBar)] = value;
	}

	private void HandleSettingsShowChildCountChanged(bool value)
	{
		ShowChildCount = value;
		_userSettings[nameof(ShowChildCount)] = value;
		StateHasChanged();
	}

	private void HandleSettingsMaxDepthChanged(int value)
	{
		MaxDepthExpanded = value;
		_userSettings[nameof(MaxDepthExpanded)] = value;
	}

	private void HandleSettingsCollapseModeChanged(MokaJsonCollapseMode value)
	{
		CollapseMode = value;
		_userSettings[nameof(CollapseMode)] = value;
	}

	private void HandleSettingsReadOnlyChanged(bool value)
	{
		ReadOnly = value;
		_userSettings[nameof(ReadOnly)] = value;
		if (EffectiveReadOnly)
		{
			_activeEdit = null;
		}

		BuildContextActions();
	}

	private async Task HandleSettingsSearchCaseSensitiveChanged(bool value)
	{
		_searchCaseSensitive = value;
		if (HasSearchQuery)
		{
			await ExecuteSearchAsync();
		}
	}

	private async Task HandleSettingsSearchUseRegexChanged(bool value)
	{
		_searchUseRegex = value;
		if (HasSearchQuery)
		{
			await ExecuteSearchAsync();
		}
	}

	#endregion

	private async Task HandleBreadcrumbNavigate(string path)
	{
		// If clicking root or a path above the current scope, unscope
		if (_scopedPath is not null && !IsWithinScope(path))
		{
			await UnscopeToRootAsync();
		}

		_pendingRevealPath = path;
		await HandleSelect(path);
	}

	private async Task HandleKeyDown(KeyboardEventArgs e)
	{
		bool ctrl = (e.CtrlKey || e.MetaKey) && !e.AltKey;
		bool canEdit = !EffectiveReadOnly && _activeEdit is null;

		switch (e.Key)
		{
			case "f" or "F" when ctrl:
				if (!_showSearch)
				{
					_showSearch = true;
				}
				else if (_searchOverlay is not null)
				{
					await _searchOverlay.FocusAsync();
				}

				break;
			case "F3" when _showSearch:
				await NavigateSearchAsync(!e.ShiftKey);
				break;
			case "Escape" when _showSearch:
				await CloseSearchAsync();
				break;
			case "z" or "Z" when ctrl && !e.ShiftKey && !EffectiveReadOnly:
				await UndoCoreAsync();
				break;
			case "y" or "Y" when ctrl && !EffectiveReadOnly:
			case "z" or "Z" when ctrl && e.ShiftKey && !EffectiveReadOnly:
				await RedoCoreAsync();
				break;
			case "F2" when canEdit && SelectedPath is not null && _documentSource is not null &&
			               GetPropertyName(_documentSource, SelectedPath) is { } key:
				StartKeyRename(SelectedPath, key);
				break;
			case "Delete" when canEdit && !string.IsNullOrEmpty(SelectedPath):
				await DeleteNodeAtPath(SelectedPath);
				break;
			default:
				_skipNextRender = true;
				break;
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
				Order = 10,
				OnExecute = async ctx =>
				{
					string value = ctx.RawValue;
					if (string.IsNullOrEmpty(value) && _documentSource is { } source)
					{
						// Large values aren't read until they're needed, and only if they'll fit.
						try
						{
							long maxBytes = OptionsAccessor.Value.MaxClipboardSizeBytes;
							string? text = await Task.Run(() => source.GetByteLength(ctx.Path) <= maxBytes
								? source.GetElement(ctx.Path).GetRawText()
								: null);

							if (text is null)
							{
								ShowToast(ValueTooLargeToCopyMessage);
								return;
							}

							value = text;
						}
						catch (Exception ex) when (ex is OutOfMemoryException or InvalidOperationException)
						{
							ShowToast(ValueTooLargeToCopyMessage);
							return;
						}
						catch (Exception ex) when (ex is IOException or JsonException)
						{
							ShowToast(CouldNotRead(ex));
							return;
						}
					}

					if (value.Length > OptionsAccessor.Value.MaxClipboardSizeBytes)
					{
						ShowToast(ValueTooLargeToCopyMessage);
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
					_treeFlattener.CollapseSubtree(ctx.Path);
						RefreshFlatNodes();
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
				OnExecute = async ctx => await ScopeToNodeAsync(ctx.Path)
			},
			new MokaJsonContextAction
			{
				Id = "sort-keys",
				Label = "Sort Keys",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Sort),
				Order = 300,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object && _documentManager is not null,
				OnExecute = async ctx => await SortKeysAtPath(ctx.Path, false)
			},
			new MokaJsonContextAction
			{
				Id = "sort-keys-recursive",
				Label = "Sort Keys (Recursive)",
				IconSvg = MokaJsonIcons.SvgString(MokaJsonIcons.Sort),
				Order = 310,
				IsVisible = ctx => ctx.ValueKind is JsonValueKind.Object && _documentManager is not null,
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
				IsVisible = ctx => !string.IsNullOrEmpty(ctx.Path),
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

			string? before = EffectiveReadOnly ? null : _documentManager.GetJsonString();

			// Sort the subtree
			string sortedSubtreeJson = recursive
				? JsonSorter.SortKeysRecursive(element)
				: JsonSorter.SortKeys(element);

			// Rebuild the entire document with the sorted subtree
			string newJson = ReplaceSubtreeJson(path, sortedSubtreeJson);

			await CommitDocumentChangeAsync(before, newJson, new JsonChangeEventArgs
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

		if (string.IsNullOrEmpty(path))
		{
			return newSubtreeJson;
		}

		// Rebuild the full document with the sorted subtree inserted at the given path
		JsonElement rootElement = _documentSource.GetElement("");
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, JsonFormatting.Writer(true));

		using var replacementDoc = JsonDocument.Parse(newSubtreeJson);
		string[] segments = JsonPointerHelper.Split(path);
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

		string targetSegment = JsonPointerHelper.UnescapeSegment(pathSegments[segmentIndex]);

		switch (current.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (KeyValuePair<string, JsonElement> prop in JsonObjectReader.GetProperties(current))
				{
					writer.WritePropertyName(prop.Key);
					if (prop.Key == targetSegment)
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

	private bool IsWithinScope(string path) =>
		_scopedPath is null || path == _scopedPath ||
		path.StartsWith(_scopedPath + "/", StringComparison.Ordinal);

	private static string? GetPropertyName(IJsonDocumentSource source, string path)
	{
		int slash = path.LastIndexOf('/');
		if (slash < 0)
		{
			return null;
		}

		try
		{
			return source.GetValueKind(path[..slash]) == JsonValueKind.Object
				? JsonPointerHelper.UnescapeSegment(path[(slash + 1)..])
				: null;
		}
		catch (KeyNotFoundException)
		{
			return null;
		}
	}

	private async Task ScopeToNodeAsync(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		try
		{
			JsonValueKind kind = _documentSource.GetValueKind(path);
			if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
			{
				return;
			}
		}
		catch (KeyNotFoundException)
		{
			return;
		}

		_scopedPath = path;
		_treeFlattener.Expand(path);
		await RefreshAfterScopeChangeAsync();
	}

	private async Task UnscopeToRootAsync()
	{
		_scopedPath = null;
		await RefreshAfterScopeChangeAsync();
	}

	private async Task RefreshAfterScopeChangeAsync()
	{
		if (_showSearch && HasSearchQuery)
		{
			await ExecuteSearchAsync();
		}
		else
		{
			RefreshFlatNodes();
		}
	}

	private async Task ExpandSubtree(string path)
	{
		if (_documentSource is null)
		{
			return;
		}

		IJsonDocumentSource source = _documentSource;
		JsonExpandLimits limits = ExpandLimits;

		await CancelTreeOperationAsync();
		_isBusy = true;
		_busyMessage = "Expanding subtree...";
		StateHasChanged();

		CancellationTokenSource cts = _treeCts = new CancellationTokenSource();
		CancellationToken token = cts.Token;
		try
		{
			bool truncated = await Task.Run(() => _treeFlattener.ExpandSubtree(source, path, limits, token), token);
			if (!token.IsCancellationRequested)
			{
				await RefreshFlatNodesAsync();
				if (truncated)
				{
					ShowToast(ExpandTruncatedMessage);
				}
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
			                           or KeyNotFoundException)
		{
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			ShowToast(CouldNotRead(ex));
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

	private static async Task TryInteropAsync(Func<ValueTask> call)
	{
		try
		{
			await call();
		}
		catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException
			                           or TaskCanceledException or InvalidOperationException)
		{
		}
	}

	private static string CouldNotRead(Exception ex) => $"Could not read the document: {ex.Message}";

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

		await InvokeAsync(async () =>
		{
			if (!IsWithinScope(jsonPointer))
			{
				_scopedPath = null;
			}

			_treeFlattener.ExpandAncestors(jsonPointer);
			await HandleSelect(jsonPointer);
			await RefreshFlatNodesAsync(jsonPointer);
		});
	}

	/// <inheritdoc />
	public void ExpandToDepth(int depth)
	{
		if (_documentSource is not { } source)
		{
			return;
		}

		if (_isLazyMode)
		{
			RunDetached(async () =>
			{
				await Task.Run(() => _treeFlattener.ExpandToDepth(source, depth, JsonExpandLimits.LargeDocument));
				await InvokeAsync(() => RefreshFlatNodesAsync());
			});
			return;
		}

		_treeFlattener.ExpandToDepth(source, depth);
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public void CollapseAll()
	{
		_treeFlattener.CollapseAll();
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public void ExpandAll() => RunDetached(ExpandAllAsync);

	/// <inheritdoc />
	public ValueTask ExpandAllAsync() => new(InvokeAsync(HandleExpandAll));

	/// <inheritdoc />
	public async ValueTask<int> SearchAsync(string query, JsonSearchOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		JsonSearchOptions searchOptions = options ?? new JsonSearchOptions();

		int matchCount = 0;
		await InvokeAsync(async () =>
		{
			_searchQuery = query;
			_searchCaseSensitive = searchOptions.CaseSensitive;
			_searchUseRegex = searchOptions.UseRegex;
			_searchRequestId++;
			_searchDebounceTimer?.Dispose();
			_searchDebounceTimer = null;
			CancelSearch();

			matchCount = await RunSearchAsync(query, searchOptions, true, cancellationToken);
		});

		return matchCount;
	}

	/// <inheritdoc />
	public void NextMatch() => RunDetached(NextMatchAsync);

	/// <inheritdoc />
	public ValueTask NextMatchAsync() => new(InvokeAsync(() => NavigateSearchAsync(true)));

	/// <inheritdoc />
	public void PreviousMatch() => RunDetached(PreviousMatchAsync);

	/// <inheritdoc />
	public ValueTask PreviousMatchAsync() => new(InvokeAsync(() => NavigateSearchAsync(false)));

	/// <inheritdoc />
	public void ClearSearch()
	{
		_searchQuery = null;
		ResetSearchState();
		RefreshFlatNodes();
	}

	/// <inheritdoc />
	public void Undo() => RunDetached(UndoAsync);

	/// <inheritdoc />
	public ValueTask UndoAsync() => new(InvokeAsync(UndoCoreAsync));

	/// <inheritdoc />
	public void Redo() => RunDetached(RedoAsync);

	/// <inheritdoc />
	public ValueTask RedoAsync() => new(InvokeAsync(RedoCoreAsync));

	private void RunDetached(Func<ValueTask> operation) => _ = RunDetachedAsync(operation);

	private async Task RunDetachedAsync(Func<ValueTask> operation)
	{
		// Nobody awaits the void API methods, so an exception here would otherwise go unobserved
		// (or, through async void, tear down the circuit). Report it through OnError instead.
		try
		{
			await operation();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await InvokeAsync(() => RaiseError(ex.Message, ex));
		}
	}

	private async Task UndoCoreAsync()
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

		await ApplyHistorySnapshotAsync(snapshot);
	}

	private async Task RedoCoreAsync()
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

		await ApplyHistorySnapshotAsync(snapshot);
	}

	private async Task ApplyHistorySnapshotAsync(string snapshot)
	{
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
