using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Extensions;
using Xunit;

namespace Moka.Blazor.Json.Tests.Components;

public sealed class MokaJsonViewerSearchTests : IAsyncLifetime
{
	private const string ModulePath = "./_content/Moka.Blazor.Json/js/moka-json.js";

	private const string Json = """
	                            {"items":[{"name":"match-a"},{"name":"other"},{"name":"match-b"},{"name":"match-c"}]}
	                            """;

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	private readonly BunitContext _ctx = new();
	private BunitJSModuleInterop _module = null!;

	public ValueTask InitializeAsync()
	{
		_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		_module = _ctx.JSInterop.SetupModule(ModulePath);
		_module.Mode = JSRuntimeMode.Loose;
		_ctx.Services.AddMokaJsonViewer(o => o.SearchDebounceMs = 0);
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

	private IRenderedComponent<MokaJsonViewer> RenderViewer(bool readOnly = true)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, Json)
			.Add(v => v.ReadOnly, readOnly)
			.Add(v => v.MaxDepthExpanded, 1));
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);
		return cut;
	}

	private static IElement OpenSearch(IRenderedComponent<MokaJsonViewer> cut)
	{
		cut.Find(".moka-json-toolbar button").Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-search").Count > 0, Timeout);
		return cut.Find(".moka-json-search input[type=text]");
	}

	private static void Search(IRenderedComponent<MokaJsonViewer> cut, string query, string expectedInfo)
	{
		OpenSearch(cut).Input(query);
		ExpectActive(cut, expectedInfo, null);
	}

	private static void ExpectActive(IRenderedComponent<MokaJsonViewer> cut, string info, string? activeText) =>
		cut.WaitForAssertion(() =>
		{
			Assert.Equal(info, cut.Find(".moka-json-search-info").TextContent.Trim());
			if (activeText is not null)
			{
				IReadOnlyList<IElement> active = cut.FindAll(".moka-json-node--search-active");
				Assert.Single(active);
				Assert.Contains(activeText, active[0].TextContent);
			}
		}, Timeout);

	private static void Press(IRenderedComponent<MokaJsonViewer> cut, string key, bool shift = false) =>
		cut.Find(".moka-json-search input[type=text]")
			.KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift });

	[Fact]
	public void Enter_Moves_Active_Highlight_To_Next_Match_And_Wraps()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();
		Search(cut, "match", "1 of 3");
		ExpectActive(cut, "1 of 3", "match-a");

		Press(cut, "Enter");
		ExpectActive(cut, "2 of 3", "match-b");

		Press(cut, "Enter");
		ExpectActive(cut, "3 of 3", "match-c");

		Press(cut, "Enter");
		ExpectActive(cut, "1 of 3", "match-a");
	}

	[Fact]
	public void Arrow_Keys_And_Shift_Enter_Navigate_Matches()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();
		Search(cut, "match", "1 of 3");

		Press(cut, "ArrowUp");
		ExpectActive(cut, "3 of 3", "match-c");

		Press(cut, "ArrowDown");
		ExpectActive(cut, "1 of 3", "match-a");

		Press(cut, "Enter", shift: true);
		ExpectActive(cut, "3 of 3", "match-c");

		Press(cut, "F3");
		ExpectActive(cut, "1 of 3", "match-a");
	}

	[Fact]
	public void Next_Button_Moves_Active_Highlight()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();
		Search(cut, "match", "1 of 3");

		cut.Find(".moka-json-search button[title^='Next']").Click();
		ExpectActive(cut, "2 of 3", "match-b");
	}

	[Fact]
	public void Navigating_Scrolls_The_Active_Row_Into_View()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();
		Search(cut, "match", "1 of 3");

		Press(cut, "Enter");
		ExpectActive(cut, "2 of 3", "match-b");

		cut.WaitForAssertion(() =>
		{
			string activeId = cut.Find(".moka-json-node--search-active").Id!;
			IReadOnlyList<JSRuntimeInvocation> calls = _module.Invocations["scrollToRow"];
			Assert.NotEmpty(calls);
			int row = Assert.IsType<int>(calls[^1].Arguments[1]);
			Assert.Equal($"moka-node-{row}", activeId);
		}, Timeout);
	}

	[Fact]
	public void Enter_While_Debounce_Pending_Runs_Search_Immediately()
	{
		_ctx.Services.AddMokaJsonViewer(o => o.SearchDebounceMs = 60_000);
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();

		OpenSearch(cut).Input("match");
		cut.WaitForAssertion(
			() => Assert.Equal("Searching...", cut.Find(".moka-json-search-info").TextContent.Trim()), Timeout);

		Press(cut, "Enter");
		ExpectActive(cut, "1 of 3", "match-a");

		Press(cut, "Enter");
		ExpectActive(cut, "2 of 3", "match-b");
	}

	[Fact]
	public void Escape_Closes_Search_And_Clears_Highlights()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer();
		Search(cut, "match", "1 of 3");

		Press(cut, "Escape");

		cut.WaitForAssertion(() =>
		{
			Assert.Empty(cut.FindAll(".moka-json-search"));
			Assert.Empty(cut.FindAll(".moka-json-node--search-match"));
		}, Timeout);
	}

	[Fact]
	public void Ctrl_Z_In_Search_Box_Does_Not_Undo_Document_Edits()
	{
		string? changed = null;
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.JsonChanged, json => changed = json));
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("Alice")).DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, Timeout);
		IElement editor = cut.Find(".moka-json-inline-edit");
		editor.Input("Bob");
		editor.KeyDown(new KeyboardEventArgs { Key = "Enter" });
		cut.WaitForState(() => changed?.Contains("Bob", StringComparison.Ordinal) == true, Timeout);

		OpenSearch(cut).KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });

		cut.WaitForAssertion(() => Assert.Contains("Bob", cut.Markup), Timeout);
		Assert.Contains("Bob", changed);
	}

	[Fact]
	public void Rename_Key_Is_Not_Offered_For_Array_Elements()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"tags":["dev","admin"]}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("admin")).ContextMenu();
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, Timeout);

		Assert.DoesNotContain("Rename Key", cut.Markup);
		Assert.Contains("Edit Value", cut.Markup);
	}
}
