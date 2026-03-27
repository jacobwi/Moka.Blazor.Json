namespace Moka.Blazor.Json.AI.Models;

/// <summary>
///     AI capabilities / quick action types.
/// </summary>
public enum AiCapability
{
	/// <summary>Free-form question about the JSON data.</summary>
	Query,

	/// <summary>Transform the JSON (restructure, rename, filter).</summary>
	Transform,

	/// <summary>Analyze for patterns, anomalies, or data quality issues.</summary>
	Analyze,

	/// <summary>Summarize the structure and content.</summary>
	Summarize,

	/// <summary>Infer and describe the JSON schema.</summary>
	Schema
}
