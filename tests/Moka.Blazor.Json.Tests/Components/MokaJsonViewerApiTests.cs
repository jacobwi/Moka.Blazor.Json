using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Extensions;
using Moka.Blazor.Json.Models;
using Xunit;

namespace Moka.Blazor.Json.Tests.Components;

public sealed class MokaJsonViewerApiTests : IAsyncLifetime
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	private readonly BunitContext _ctx = new();

	public ValueTask InitializeAsync()
	{
		_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		_ctx.Services.AddMokaJsonViewer(o => o.SearchDebounceMs = 0);
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

	private IRenderedComponent<MokaJsonViewer> RenderViewer(
		Action<ComponentParameterCollectionBuilder<MokaJsonViewer>> configure)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render(configure);
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);
		return cut;
	}

	private static void EditValue(IRenderedComponent<MokaJsonViewer> cut, string currentText, string newValue)
	{
		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains(currentText)).DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, Timeout);
		IElement editor = cut.Find(".moka-json-inline-edit");
		editor.Input(newValue);
		editor.KeyDown(new KeyboardEventArgs { Key = "Enter" });
	}

	[Fact]
	public void Null_Value_Can_Be_Edited_To_A_Number()
	{
		string? changed = null;
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"count":null}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.JsonChanged, json => changed = json));

		EditValue(cut, "null", "42");

		cut.WaitForAssertion(() =>
		{
			Assert.NotNull(changed);
			Assert.Equal(42, System.Text.Json.JsonDocument.Parse(changed!).RootElement.GetProperty("count").GetInt32());
		}, Timeout);
	}

	[Fact]
	public void Settings_Choice_Survives_A_Parent_Rerender_Until_The_Parent_Changes_It()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.Theme, MokaJsonTheme.Light));

		cut.Find("button[title='Settings']").Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-settings-panel").Count > 0, Timeout);
		cut.FindAll(".moka-json-settings-panel label").First(l => l.TextContent.Trim() == "Dark")
			.QuerySelector("input")!.Change(true);
		cut.WaitForAssertion(
			() => Assert.Equal("dark", cut.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme")), Timeout);

		cut.Render(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.Theme, MokaJsonTheme.Light));
		Assert.Equal("dark", cut.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme"));

		cut.Render(p => p.Add(v => v.Theme, MokaJsonTheme.Inherit));
		cut.Render(p => p.Add(v => v.Theme, MokaJsonTheme.Light));
		Assert.Equal("light", cut.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public async Task UndoAsync_And_RedoAsync_Complete_After_The_Document_Changes()
	{
		string? changed = null;
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.JsonChanged, json => changed = json));

		EditValue(cut, "Alice", "Bob");
		cut.WaitForState(() => changed?.Contains("Bob", StringComparison.Ordinal) == true, Timeout);

		MokaJsonViewer viewer = cut.Instance;
		await viewer.UndoAsync();
		Assert.Contains("Alice", changed);
		cut.WaitForAssertion(() => Assert.Contains("Alice", cut.Markup), Timeout);

		await viewer.RedoAsync();
		Assert.Contains("Bob", changed);
	}

	[Fact]
	public async Task ExpandAllAsync_Expands_Every_Node()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.MaxDepthExpanded, 0));

		await cut.Instance.ExpandAllAsync();

		cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "c"), Timeout);
	}

	[Fact]
	public async Task NextMatchAsync_Moves_The_Active_Match()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"x":"hit-1","y":"hit-2"}""")
			.Add(v => v.MaxDepthExpanded, 2));

		Assert.Equal(2, await cut.Instance.SearchAsync("hit"));
		await cut.Instance.NextMatchAsync();

		cut.WaitForAssertion(
			() => Assert.Contains("hit-2", cut.Find(".moka-json-node--search-active").TextContent), Timeout);
	}

	[Fact]
	public void Void_Undo_Without_History_Does_Not_Throw()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ReadOnly, false));

		cut.Instance.Undo();
		cut.Instance.Redo();
		cut.Instance.NextMatch();
	}
}
