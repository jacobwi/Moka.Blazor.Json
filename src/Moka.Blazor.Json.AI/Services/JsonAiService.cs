using System.Runtime.CompilerServices;
using Moka.Blazor.AI.Models;
using Moka.Blazor.AI.Services;
using Moka.Blazor.Json.AI.Models;
using Moka.Blazor.Json.Components;

namespace Moka.Blazor.Json.AI.Services;

/// <summary>
///     JSON-specific AI service that wraps <see cref="AiChatService" /> and adds
///     JSON analysis capability prompts, viewer-aware context building, and
///     a JSON-tailored system prompt.
/// </summary>
internal sealed class JsonAiService
{
	private const string DefaultSystemPrompt = """
	                                           You are a JSON analysis assistant embedded in a JSON viewer/editor tool.
	                                           The user has a JSON document loaded and will ask you questions about it.

	                                           Guidelines:
	                                           - Be concise and direct in your answers.
	                                           - When referencing specific values, quote the JSON path (e.g., $.users[0].name).
	                                           - When asked to transform JSON, return ONLY valid JSON in a code block.
	                                           - When analyzing, look for: missing fields, type inconsistencies, unusual values, patterns.
	                                           - When summarizing, describe the structure (keys, nesting depth, array sizes) and notable content.
	                                           - If the JSON context is truncated, mention that your answer is based on a partial view.
	                                           """;

	private static readonly Dictionary<AiCapability, string> CapabilityPrompts = new()
	{
		[AiCapability.Summarize] =
			"Summarize the structure and content of this JSON document. Describe the top-level shape, key fields, data types, and any notable patterns.",
		[AiCapability.Analyze] =
			"Analyze this JSON for data quality issues, anomalies, inconsistencies, missing fields, type mismatches, or unusual patterns. Be specific about what you find.",
		[AiCapability.Schema] =
			"Infer and describe the JSON schema. List all fields with their types, whether they're required/optional, and any constraints you can deduce from the data.",
		[AiCapability.Transform] =
			"The user will describe how to transform this JSON. Return ONLY the transformed JSON in a code block.",
		[AiCapability.Query] = "" // Free-form, user provides the question
	};

	private readonly AiChatService _chatService;
	private readonly JsonContextBuilder _contextBuilder;

	public JsonAiService(AiChatService chatService, JsonContextBuilder contextBuilder)
	{
		_chatService = chatService;
		_contextBuilder = contextBuilder;
	}

	/// <summary>
	///     The current AI options configuration.
	/// </summary>
	public AiChatOptions Options => _chatService.Options;

	/// <inheritdoc cref="AiChatService.SetModel" />
	public void SetModel(string modelName) => _chatService.SetModel(modelName);

	/// <inheritdoc cref="AiChatService.SetTemperature" />
	public void SetTemperature(float temperature) => _chatService.SetTemperature(temperature);

	/// <inheritdoc cref="AiChatService.SetMaxContextChars" />
	public void SetMaxContextChars(int chars) => _chatService.SetMaxContextChars(chars);

	/// <inheritdoc cref="AiChatService.SetStreamResponses" />
	public void SetStreamResponses(bool stream) => _chatService.SetStreamResponses(stream);

	/// <summary>
	///     Sends a message and streams the response token-by-token.
	/// </summary>
	public async IAsyncEnumerable<string> StreamAsync(
		MokaJsonViewer viewer,
		string userMessage,
		List<AiMessage> history,
		AiCapability capability = AiCapability.Query,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		_contextBuilder.SetViewer(viewer);

		string capabilityPrefix = capability != AiCapability.Query
			? CapabilityPrompts[capability] + "\n\n"
			: "";

		string fullMessage = capabilityPrefix + userMessage;
		string systemPrompt = Options.SystemPrompt ?? DefaultSystemPrompt;

		await foreach (string token in _chatService.StreamAsync(
			               fullMessage, systemPrompt, history, cancellationToken))
		{
			yield return token;
		}
	}

	/// <summary>
	///     Tests connectivity to the AI backend.
	/// </summary>
	public Task<(bool Connected, string ModelName)> TestConnectionAsync(
		CancellationToken cancellationToken = default)
		=> _chatService.TestConnectionAsync(cancellationToken);

	/// <summary>
	///     Builds the JSON context string for display purposes (e.g., char count badge).
	/// </summary>
	public string BuildContext(MokaJsonViewer viewer)
	{
		_contextBuilder.SetViewer(viewer);
		return _chatService.BuildContext();
	}
}
