using AngleSharp.Dom;
using Bunit;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Extensions;
using Moka.Blazor.Json.Models;
using Xunit;

namespace Moka.Blazor.Json.Tests.Components;

public sealed class MokaJsonViewerComponentTests : IAsyncLifetime
{
	private readonly BunitContext _ctx = new();

	public Task InitializeAsync()
	{
		_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		_ctx.Services.AddMokaJsonViewer();
		return Task.CompletedTask;
	}

	public async Task DisposeAsync() => await _ctx.DisposeAsync();

	#region Rendering & Parameters

	[Fact]
	public void Renders_Loading_State_When_No_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>();

		IElement loading = cut.Find(".moka-json-loading");
		Assert.Equal("Loading...", loading.TextContent);
	}

	[Fact]
	public void Renders_Json_Tree_When_Json_Provided()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"test"}"""));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);
	}

	[Fact]
	public void Shows_Error_For_Invalid_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, "{invalid json}"));

		IElement error = cut.Find(".moka-json-error");
		Assert.NotNull(error);
	}

	[Fact]
	public void ShowToolbar_False_Hides_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, false));

		Assert.Empty(cut.FindAll(".moka-json-toolbar"));
	}

	[Fact]
	public void ShowToolbar_True_Shows_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		Assert.Single(cut.FindAll(".moka-json-toolbar"));
	}

	[Fact]
	public void ShowBottomBar_False_Hides_BottomBar()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, false));

		Assert.Empty(cut.FindAll(".moka-json-bottom-bar"));
	}

	[Fact]
	public void ShowBottomBar_True_Shows_BottomBar()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		Assert.Single(cut.FindAll(".moka-json-bottom-bar"));
	}

	[Fact]
	public void Theme_Dark_Sets_DataAttribute()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.Theme, MokaJsonTheme.Dark));

		IElement viewer = cut.Find(".moka-json-viewer");
		Assert.Equal("dark", viewer.GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public void Theme_Light_Sets_DataAttribute()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.Theme, MokaJsonTheme.Light));

		IElement viewer = cut.Find(".moka-json-viewer");
		Assert.Equal("light", viewer.GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public void Height_Parameter_Applies_Style()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Height, "600px"));

		IElement viewer = cut.Find(".moka-json-viewer");
		Assert.Contains("height: 600px", viewer.GetAttribute("style"));
	}

	[Fact]
	public void Default_Height_Is_400px()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>();

		IElement viewer = cut.Find(".moka-json-viewer");
		Assert.Contains("height: 400px", viewer.GetAttribute("style"));
	}

	#endregion

	#region Event Callbacks

	[Fact]
	public void OnError_Fires_For_Invalid_Json()
	{
		JsonErrorEventArgs? receivedError = null;

		_ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, "{bad}")
			.Add(v => v.OnError, e => receivedError = e));

		Assert.NotNull(receivedError);
		Assert.NotNull(receivedError.Message);
	}

	[Fact]
	public void OnNodeSelected_Fires_When_Node_Clicked()
	{
		JsonNodeSelectedEventArgs? selectedArgs = null;

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"test","value":42}""")
			.Add(v => v.OnNodeSelected, e => selectedArgs = e));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);

		nodes[0].Click();

		Assert.NotNull(selectedArgs);
	}

	#endregion

	#region Toolbar Interaction

	[Fact]
	public void Search_Button_Toggles_Search_Overlay()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		// Search overlay should not be visible initially
		Assert.Empty(cut.FindAll(".moka-json-search"));

		// Click search button
		IElement searchButton = cut.Find(".moka-json-toolbar button");
		searchButton.Click();

		// Search overlay should now be visible
		Assert.Single(cut.FindAll(".moka-json-search"));
	}

	[Fact]
	public void Expand_Button_Expands_All_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.MaxDepthExpanded, 0));

		int initialNodeCount = cut.FindAll(".moka-json-node").Count;

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement expandButton = buttons.First(b => b.TextContent.Contains("Expand"));
		expandButton.Click();

		int expandedNodeCount = cut.FindAll(".moka-json-node").Count;
		Assert.True(expandedNodeCount > initialNodeCount);
	}

	[Fact]
	public void Collapse_Button_Collapses_All_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.MaxDepthExpanded, 5));

		int expandedNodeCount = cut.FindAll(".moka-json-node").Count;

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement collapseButton = buttons.First(b => b.TextContent.Contains("Collapse"));
		collapseButton.Click();

		int collapsedNodeCount = cut.FindAll(".moka-json-node").Count;
		Assert.True(collapsedNodeCount < expandedNodeCount);
	}

	#endregion

	#region Bottom Bar Content

	[Fact]
	public void BottomBar_Shows_DocumentSize()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		IElement bottomBar = cut.Find(".moka-json-bottom-bar");
		Assert.Contains("B", bottomBar.TextContent);
	}

	[Fact]
	public void BottomBar_Shows_NodeCount()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowBottomBar, true));

		IElement bottomBar = cut.Find(".moka-json-bottom-bar");
		Assert.Contains("nodes", bottomBar.TextContent);
	}

	[Fact]
	public void BottomBar_Shows_ParseTime()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		IElement bottomBar = cut.Find(".moka-json-bottom-bar");
		Assert.Contains("ms", bottomBar.TextContent);
	}

	#endregion

	#region Integration

	[Fact]
	public void Renders_Array_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """[1,2,3]"""));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);
	}

	[Fact]
	public void Renders_Nested_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"user":{"name":"Alice","age":30},"tags":["dev","admin"]}""")
			.Add(v => v.MaxDepthExpanded, 3));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count >= 5);
	}

	[Fact]
	public void ShowLineNumbers_Renders_Line_Numbers()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowLineNumbers, true));

		IReadOnlyList<IElement> lineNumbers = cut.FindAll(".moka-json-line-number");
		Assert.True(lineNumbers.Count > 0);
	}

	[Fact]
	public void ShowLineNumbers_False_Hides_Line_Numbers()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowLineNumbers, false));

		Assert.Empty(cut.FindAll(".moka-json-line-number"));
	}

	[Fact]
	public void Changing_Json_Rerenders_Tree()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}"""));

		int initialNodeCount = cut.FindAll(".moka-json-node").Count;

		cut.Render(p => p
			.Add(v => v.Json, """{"a":1,"b":2,"c":3,"d":4}"""));

		int newNodeCount = cut.FindAll(".moka-json-node").Count;
		Assert.True(newNodeCount > initialNodeCount);
	}

	[Fact]
	public void Keys_Are_Rendered_In_Markup()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"firstName":"John"}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> keys = cut.FindAll(".moka-json-key");
		Assert.Contains(keys, k => k.TextContent == "firstName");
	}

	[Fact]
	public void String_Values_Are_Rendered()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> values = cut.FindAll(".moka-json-value--string");
		Assert.Contains(values, v => v.TextContent.Contains("Alice"));
	}

	[Fact]
	public void Number_Values_Are_Rendered()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"count":42}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> values = cut.FindAll(".moka-json-value--number");
		Assert.Contains(values, v => v.TextContent.Contains("42"));
	}

	#endregion
}
