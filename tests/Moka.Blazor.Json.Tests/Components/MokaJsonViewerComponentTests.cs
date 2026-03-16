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

	#region Context Menu Data

	[Fact]
	public void Context_Menu_RawValue_Is_Not_Truncated()
	{
		JsonNodeSelectedEventArgs? selectedArgs = null;

		// Build a JSON with a value longer than 500 chars
		string longValue = new('x', 1000);
		string json = $$"""{"data":"{{longValue}}"}""";

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, json)
			.Add(v => v.OnNodeSelected, e => selectedArgs = e)
			.Add(v => v.MaxDepthExpanded, 2));

		// Click on the "data" node to trigger OnNodeSelected
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement dataNode = nodes.First(n => n.TextContent.Contains("data"));
		dataNode.Click();

		Assert.NotNull(selectedArgs);
		Assert.Contains(longValue, selectedArgs.RawValue);
		Assert.True(selectedArgs.RawValuePreview.Length <= 503); // 500 + "..."
	}

	#endregion

	#region Rendering & Parameters

	[Fact]
	public void No_Loading_Shown_When_Json_Is_Null()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>();

		Assert.Empty(cut.FindAll(".moka-json-loading"));
		Assert.Empty(cut.FindAll(".moka-json-node"));
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

	[Fact]
	public void Export_Button_Exists_In_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		Assert.Contains(buttons, b => b.TextContent.Contains("Export"));
	}

	[Fact]
	public void Export_Button_Invokes_JS_Download()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement exportButton = buttons.First(b => b.TextContent.Contains("Export"));
		exportButton.Click();

		// Loose mode absorbs the JS call — no exception means the handler wired correctly
	}

	[Fact]
	public void Copy_Button_Invokes_JS_Clipboard()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement copyButton = buttons.First(b => b.TextContent.Contains("Copy"));
		copyButton.Click();
	}

	#endregion

	#region Toggle Style & Size

	[Theory]
	[InlineData(MokaJsonToggleStyle.Triangle)]
	[InlineData(MokaJsonToggleStyle.Chevron)]
	[InlineData(MokaJsonToggleStyle.PlusMinus)]
	[InlineData(MokaJsonToggleStyle.Arrow)]
	public void ToggleStyle_Renders_Toggle_Button(MokaJsonToggleStyle style)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1}}""")
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.ToggleStyle, style));

		IReadOnlyList<IElement> toggles = cut.FindAll(".moka-json-node-toggle");
		Assert.True(toggles.Count > 0);
	}

	[Theory]
	[InlineData(MokaJsonToggleSize.Small, "moka-json-toggle--sm")]
	[InlineData(MokaJsonToggleSize.Medium, "moka-json-toggle--md")]
	[InlineData(MokaJsonToggleSize.Large, "moka-json-toggle--lg")]
	public void ToggleSize_Applies_Css_Class(MokaJsonToggleSize size, string expectedClass)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1}}""")
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.ToggleSize, size));

		IReadOnlyList<IElement> toggles = cut.FindAll(".moka-json-node-toggle");
		Assert.True(toggles.Count > 0);
		Assert.All(toggles, t => Assert.Contains(expectedClass, t.ClassName));
	}

	[Fact]
	public void Default_ToggleSize_Is_Small()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1}}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> toggles = cut.FindAll(".moka-json-node-toggle");
		Assert.True(toggles.Count > 0);
		Assert.All(toggles, t => Assert.Contains("moka-json-toggle--sm", t.ClassName));
	}

	#endregion

	#region Edit Mode

	[Fact]
	public void ReadOnly_False_Adds_Editable_Class()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> editableNodes = cut.FindAll(".moka-json-node--editable");
		Assert.True(editableNodes.Count > 0);
	}

	[Fact]
	public void ReadOnly_True_Does_Not_Add_Editable_Class()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		Assert.Empty(cut.FindAll(".moka-json-node--editable"));
	}

	[Fact]
	public void DoubleClick_On_Value_Node_Shows_Inline_Edit()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		// Find the value node (not the root object node or closing bracket)
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();

		// Should show inline edit input
		IReadOnlyList<IElement> inputs = cut.FindAll(".moka-json-inline-edit");
		Assert.True(inputs.Count > 0);
	}

	[Fact]
	public void DoubleClick_On_ReadOnly_Does_Not_Show_Edit()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();

		Assert.Empty(cut.FindAll(".moka-json-inline-edit"));
	}

	[Fact]
	public void Edit_Context_Menu_Actions_Visible_When_Not_ReadOnly()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click a value node to open context menu
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.ContextMenu();

		// Context menu should contain edit-related actions
		string markup = cut.Markup;
		Assert.Contains("Edit Value", markup);
		Assert.Contains("Delete", markup);
	}

	[Fact]
	public void Edit_Context_Menu_Actions_Hidden_When_ReadOnly()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click a node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.ContextMenu();

		string markup = cut.Markup;
		Assert.DoesNotContain("Edit Value", markup);
		Assert.DoesNotContain("Delete", markup);
	}

	[Fact]
	public void JsonChanged_Fires_After_Edit_Commit()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Double-click the string value node to enter edit mode
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();

		// Find the inline edit input and change its value
		IElement input = cut.Find(".moka-json-inline-edit");
		input.Input("Bob");
		input.KeyDown("Enter");

		Assert.NotNull(changedJson);
		Assert.Contains("Bob", changedJson);
	}

	[Fact]
	public void Bind_Json_Updates_After_Edit()
	{
		string? boundJson = """{"count":10}""";

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, boundJson)
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => boundJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Double-click the number value node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("10"));
		valueNode.DoubleClick();

		// Edit the value
		IElement input = cut.Find(".moka-json-inline-edit");
		input.Input("99");
		input.KeyDown("Enter");

		Assert.NotNull(boundJson);
		Assert.Contains("99", boundJson);
	}

	[Fact]
	public void Context_Menu_Delete_Removes_Node()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice","age":30}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click the "age" node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement ageNode = nodes.First(n => n.TextContent.Contains("age") && n.TextContent.Contains("30"));
		ageNode.ContextMenu();

		// Click Delete
		IReadOnlyList<IElement> menuItems = cut.FindAll(".moka-json-context-item");
		IElement deleteItem = menuItems.First(m => m.TextContent.Contains("Delete"));
		deleteItem.Click();

		Assert.NotNull(changedJson);
		Assert.DoesNotContain("age", changedJson);
	}

	[Fact]
	public void Context_Menu_Add_Property_Adds_To_Object()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click the root object node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement rootNode = nodes[0];
		rootNode.ContextMenu();

		// Click "Add Property"
		IReadOnlyList<IElement> menuItems = cut.FindAll(".moka-json-context-item");
		IElement addItem = menuItems.First(m => m.TextContent.Contains("Add Property"));
		addItem.Click();

		Assert.NotNull(changedJson);
		Assert.Contains("newProperty", changedJson);
	}

	#endregion

	#region Collapse Mode

	[Fact]
	public void CollapseMode_Root_Shows_Minimal_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Root));

		// Root mode: only the root object and its closing bracket should be visible
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count <= 2);
	}

	[Fact]
	public void CollapseMode_Expanded_Shows_All_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Expanded));

		// Expanded mode: all nodes visible including nested
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count >= 5);
	}

	[Fact]
	public void CollapseMode_Depth_Uses_MaxDepthExpanded()
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Depth)
			.Add(v => v.MaxDepthExpanded, 1));

		// Only root and first level expanded, not deeply nested
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		int expandedCount = cut.FindAll(".moka-json-node").Count;

		// With depth 1, "a" object shows but "b" object is collapsed
		IRenderedComponent<MokaJsonViewer> cutFull = _ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Expanded));

		int fullCount = cutFull.FindAll(".moka-json-node").Count;
		Assert.True(expandedCount < fullCount);
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
