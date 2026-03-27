using Microsoft.AspNetCore.Components;
using Moka.Blazor.AI.Components;
using Moka.Blazor.AI.Models;
using Moka.Blazor.Json.AI.Services;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.AI.Components;

/// <summary>
///     JSON-specific AI chat panel that wraps the generic <see cref="MokaAiPanel" />
///     and adds JSON analysis quick actions, viewer integration, and context building.
/// </summary>
public sealed partial class MokaJsonAiPanel : ComponentBase
{
	private const string DefaultJsonSystemPrompt = """
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

	private static readonly IReadOnlyList<AiQuickAction> _quickActions =
	[
		new("Summarize", "Summarize this JSON document.", "Summarize the JSON structure and content"),
		new("Analyze", "Analyze this JSON for issues and anomalies.", "Find anomalies, inconsistencies, or issues"),
		new("Schema", "Describe the schema of this JSON.", "Infer the JSON schema")
	];

	private MokaAiPanel? _panel;

	[Inject] private JsonContextBuilder ContextBuilder { get; set; } = null!;

	/// <summary>
	///     The viewer instance to analyze. Required.
	/// </summary>
	[Parameter]
	public MokaJsonViewer? Viewer { get; set; }

	/// <summary>
	///     Theme for the panel. When <c>null</c>, inherits from parent.
	/// </summary>
	[Parameter]
	public MokaJsonTheme? Theme { get; set; }

	/// <summary>
	///     Maximum height of the messages area. Default is <c>"350px"</c>.
	/// </summary>
	[Parameter]
	public string MessagesHeight { get; set; } = "350px";

	/// <summary>
	///     Placeholder text for the input field. Default is <c>"Ask about the JSON..."</c>.
	/// </summary>
	[Parameter]
	public string Placeholder { get; set; } = "Ask about the JSON...";

	/// <summary>
	///     Whether to show the quick action buttons (Summarize, Analyze, Schema). Default is <c>true</c>.
	/// </summary>
	[Parameter]
	public bool ShowQuickActions { get; set; } = true;

	/// <summary>
	///     Title text shown in the panel header. Default is <c>"AI Assistant"</c>.
	/// </summary>
	[Parameter]
	public string Title { get; set; } = "AI Assistant";

	private string SystemPrompt => DefaultJsonSystemPrompt;

	private string ThemeAttribute => Theme switch
	{
		MokaJsonTheme.Light => "light",
		MokaJsonTheme.Dark => "dark",
		MokaJsonTheme.Auto => "auto",
		_ => ""
	};

	/// <summary>
	///     Whether the AI panel is currently sending/streaming a response.
	/// </summary>
	public bool IsSending => _panel?.IsSending ?? false;

	private bool HasSelection => !string.IsNullOrEmpty(Viewer?.SelectedPath);

	protected override void OnParametersSet()
	{
		// Keep the context builder in sync with the current viewer
		ContextBuilder.SetViewer(Viewer);
	}

	/// <summary>
	///     Scopes the AI context to a specific node path. Subsequent AI calls will include
	///     only the subtree at this path rather than the full document.
	/// </summary>
	/// <param name="path">JSON Pointer path (e.g., "/users/0").</param>
	public void ScopeToNode(string path) => ContextBuilder.SetScope("path", path);

	/// <summary>
	///     Adds a named JSON data source to the multi-source scope.
	///     When multiple sources are added, the AI sees all of them labeled by name.
	///     This replaces the single-viewer context for the duration of the scope.
	/// </summary>
	/// <param name="label">Human-readable label (e.g., "Customer", "Order").</param>
	/// <param name="json">The JSON string for this source.</param>
	public void AddSource(string label, string json)
	{
		if (!ContextBuilder.GetScopes().TryGetValue("sources", out object? existing)
		    || existing is not Dictionary<string, string> sources)
		{
			sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			ContextBuilder.SetScope("sources", sources);
		}

		sources[label] = json;
	}

	/// <summary>
	///     Removes a named data source from the multi-source scope.
	/// </summary>
	public void RemoveSource(string label)
	{
		if (ContextBuilder.GetScopes().TryGetValue("sources", out object? existing)
		    && existing is Dictionary<string, string> sources)
		{
			sources.Remove(label);
			if (sources.Count == 0)
			{
				ContextBuilder.ClearScope("sources");
			}
		}
	}

	/// <summary>
	///     Clears any active scope so the AI sees the full document context.
	/// </summary>
	public void ClearScope() => ContextBuilder.ClearScope();

	private async Task AskAboutSelection()
	{
		if (_panel is null || _panel.IsSending || Viewer is null || string.IsNullOrEmpty(Viewer.SelectedPath))
		{
			return;
		}

		await AskAboutNode(Viewer.SelectedPath);
	}

	/// <summary>
	///     Sends a prompt to the AI asking it to analyze the node at the given JSON Pointer path.
	///     Automatically scopes the context to the target node so the AI sees only the relevant subtree.
	///     Can be called from external components such as context menu actions.
	/// </summary>
	/// <param name="path">JSON Pointer path (e.g., "/users/0/name").</param>
	public async Task AskAboutNode(string path)
	{
		if (_panel is null || _panel.IsSending || string.IsNullOrEmpty(path))
		{
			return;
		}

		// Scope context to the target node so the AI receives the subtree, not the full document
		ContextBuilder.SetScope("path", path);

		string prompt =
			$"Describe and analyze the node at path `{path}`. What is this data, what are its properties, and is there anything notable?";
		await _panel.SendToAi(prompt);

		// Clear scope after sending so subsequent general questions see the full document
		ContextBuilder.ClearScope("path");
	}

	/// <summary>
	///     Creates a <see cref="MokaJsonContextAction" /> that sends a "Ask AI" prompt
	///     for the right-clicked node. Add the returned action to the viewer's
	///     <see cref="MokaJsonViewer.ContextMenuActions" /> list.
	/// </summary>
	/// <example>
	///     <code>
	///     &lt;MokaJsonViewer @ref="_viewer" Json="@json" ContextMenuActions="_contextActions" /&gt;
	///     &lt;MokaJsonAiPanel @ref="_aiPanel" Viewer="_viewer" /&gt;
	/// 
	///     @code {
	///         private MokaJsonAiPanel _aiPanel = null!;
	///         private List&lt;MokaJsonContextAction&gt; _contextActions = null!;
	/// 
	///         protected override void OnInitialized()
	///             =&gt; _contextActions = [_aiPanel.CreateAskAiContextAction()];
	///     }
	///     </code>
	/// </example>
	public MokaJsonContextAction CreateAskAiContextAction(
		string label = "Ask AI",
		string? tooltip = null,
		int order = 900)
	{
		return new MokaJsonContextAction
		{
			Id = "moka-ask-ai",
			Label = label,
			IconSvg =
				"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M8 2a4 4 0 0 1 4 4v1a4 4 0 0 1-8 0V6a4 4 0 0 1 4-4z"/><circle cx="6.5" cy="6" r="0.6" fill="currentColor" stroke="none"/><circle cx="9.5" cy="6" r="0.6" fill="currentColor" stroke="none"/><path d="M6.5 8.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5"/><path d="M4 13h8"/></svg>""",
			ShortcutHint = tooltip,
			Order = order,
			HasSeparatorBefore = true,
			IsEnabled = _ => _panel is not null && !_panel.IsSending,
			OnExecute = async ctx => { await AskAboutNode(ctx.Path); }
		};
	}
}
