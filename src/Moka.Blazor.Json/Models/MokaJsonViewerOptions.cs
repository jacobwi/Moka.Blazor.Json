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
	///     Maximum document size in bytes that the viewer will attempt to parse.
	///     Documents exceeding this limit will raise an error. Default is 2 GB.
	/// </summary>
	public long MaxDocumentSizeBytes { get; set; } = 2L * 1024 * 1024 * 1024;

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
	///     Default is 50 MB.
	/// </summary>
	public long LazyParsingThresholdBytes { get; set; } = 50 * 1024 * 1024;

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
	///     Document size threshold in bytes above which node count and max depth
	///     are computed on a background thread. Default is 10 MB.
	/// </summary>
	public long BackgroundStatsThresholdBytes { get; set; } = 10 * 1024 * 1024;

	/// <summary>
	///     Maximum document size in bytes that can be copied to clipboard.
	///     Default is 50 MB.
	/// </summary>
	public long MaxClipboardSizeBytes { get; set; } = 50 * 1024 * 1024;

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
