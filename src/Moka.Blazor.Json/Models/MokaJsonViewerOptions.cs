namespace Moka.Blazor.Json.Models;

/// <summary>
///     Global configuration options for the Moka JSON viewer, registered via DI.
/// </summary>
public sealed class MokaJsonViewerOptions
{
	/// <summary>
	///     The default theme applied to new viewer instances. Default is <see cref="MokaJsonTheme.Auto" />.
	/// </summary>
	public MokaJsonTheme DefaultTheme { get; set; } = MokaJsonTheme.Auto;

	/// <summary>
	///     The default toolbar display mode. Default is <see cref="MokaJsonToolbarMode.IconAndText" />.
	/// </summary>
	public MokaJsonToolbarMode DefaultToolbarMode { get; set; } = MokaJsonToolbarMode.IconAndText;

	/// <summary>
	///     Maximum document size in bytes that the viewer will open. Documents exceeding this limit raise an
	///     error. Default is 1 TB, or 1 GB in browser WebAssembly, where a document has to fit in the page's memory.
	/// </summary>
	/// <remarks>
	///     A document over about 1 GB has to be passed as a seekable stream, since a string can't hold it.
	/// </remarks>
	public long MaxDocumentSizeBytes { get; set; } = OperatingSystem.IsBrowser()
		? 1024L * 1024 * 1024
		: 1024L * 1024 * 1024 * 1024;

	/// <summary>
	///     Whether edit mode is available. Default is <c>true</c>.
	/// </summary>
	public bool EnableEditMode { get; set; } = true;

	/// <summary>
	///     The default depth to which nodes are expanded on initial render.
	///     Use <c>-1</c> to expand all. Default is <c>2</c>.
	/// </summary>
	public int DefaultExpandDepth { get; set; } = 2;

	/// <summary>
	///     Debounce interval in milliseconds for search input. Default is <c>250</c>.
	/// </summary>
	public int SearchDebounceMs { get; set; } = 250;

	/// <summary>
	///     Document size threshold in bytes above which lazy/indexed parsing is used
	///     instead of full DOM parse. Documents above this size will not support editing.
	///     Default is 50 MB, or 16 MB in browser WebAssembly.
	/// </summary>
	public long LazyParsingThresholdBytes { get; set; } = OperatingSystem.IsBrowser()
		? 16 * 1024 * 1024
		: 50 * 1024 * 1024;

	/// <summary>
	///     Alias for <see cref="LazyParsingThresholdBytes" />.
	/// </summary>
	[Obsolete("Use LazyParsingThresholdBytes instead.")]
	public long StreamingThresholdBytes
	{
		get => LazyParsingThresholdBytes;
		set => LazyParsingThresholdBytes = value;
	}

	/// <summary>
	///     No longer used: node count and max depth are always computed on a background thread.
	/// </summary>
	[Obsolete("Node count and max depth are always computed on a background thread.")]
	public long BackgroundStatsThresholdBytes { get; set; } = 10 * 1024 * 1024;

	/// <summary>
	///     Maximum document size in bytes that can be copied to clipboard.
	///     Default is 50 MB.
	/// </summary>
	public long MaxClipboardSizeBytes { get; set; } = 50 * 1024 * 1024;

	/// <summary>
	///     Maximum document size in bytes that the Export button will download. The browser has to hold the
	///     whole file in memory to save it. Default is 512 MB.
	/// </summary>
	public long MaxExportSizeBytes { get; set; } = 512 * 1024 * 1024;

	/// <summary>
	///     The most matches a search of a large (lazily parsed) document collects before it stops. Each match
	///     keeps a path in memory, and nobody steps through more than this. Default is 10,000.
	/// </summary>
	public int MaxSearchMatches { get; set; } = 10_000;

	/// <summary>
	///     Whether the settings gear button is shown in the toolbar.
	///     Default is <c>true</c>.
	/// </summary>
	public bool ShowSettingsButton { get; set; } = true;

	/// <summary>
	///     When <c>true</c>, forces a full garbage collection (all generations + LOH compaction)
	///     after the viewer disposes its document and caches. Useful for applications that load
	///     large JSON documents and need to reclaim memory promptly. Default is <c>false</c>.
	/// </summary>
	public bool AggressiveCleanup { get; set; }
}
