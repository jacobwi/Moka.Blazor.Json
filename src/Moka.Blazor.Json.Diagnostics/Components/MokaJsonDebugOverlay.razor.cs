using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Diagnostics.Components;

/// <summary>
///     Diagnostic overlay that displays real-time lazy parsing stats for a
///     <see cref="MokaJsonViewer" />. Add this component alongside your viewer
///     and set <see cref="Enabled" /> to control visibility.
/// </summary>
public sealed partial class MokaJsonDebugOverlay : ComponentBase
{
	/// <summary>
	///     The viewer instance to read debug stats from.
	/// </summary>
	[Parameter]
	public MokaJsonViewer? Viewer { get; set; }

	/// <summary>
	///     Whether the overlay is visible. Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool Enabled { get; set; } = true;

	private LazyDebugStats? Stats => Viewer?.DebugStats;

	private string CacheHitRateClass => Stats?.CacheHitRate switch
	{
		>= 80 => "moka-json-debug-good",
		>= 50 => "moka-json-debug-warn",
		_ => "moka-json-debug-bad"
	};

	private static string FormatBytes(long bytes) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
		< 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
		_ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
	};

	private static string FormatMs(TimeSpan ts) => ts.TotalMilliseconds < 1
		? $"{ts.TotalMicroseconds:F0}us"
		: $"{ts.TotalMilliseconds:F1}ms";

	private static string TruncatePath(string path, int max = 30) =>
		path.Length <= max ? path : "..." + path[^(max - 3)..];
}
