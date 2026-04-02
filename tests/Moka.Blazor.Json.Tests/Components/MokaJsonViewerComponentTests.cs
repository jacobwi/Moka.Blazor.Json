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

	public ValueTask InitializeAsync()
	{
		_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		_ctx.Services.AddMokaJsonViewer();
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

	/// <summary>
	///     Renders a <see cref="MokaJsonViewer" /> and waits for async loading to complete.
	///     Parsing, flattening, and stats are offloaded to Task.Run, so bUnit needs to wait
	///     until the tree viewport or error element appears in the DOM.
	/// </summary>
	private IRenderedComponent<MokaJsonViewer> RenderViewer(
		Action<ComponentParameterCollectionBuilder<MokaJsonViewer>> configure)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render(configure);
		// Wait for the component to finish all async work (parse, flatten, stats)
		// and render the tree with at least one node, or show an error.
		cut.WaitForState(() =>
				cut.FindAll(".moka-json-node").Count > 0 ||
				cut.FindAll(".moka-json-error").Count > 0,
			TimeSpan.FromSeconds(5));
		return cut;
	}

	#region Disposal

	[Fact]
	public async Task AggressiveCleanup_Enabled_Does_Not_Throw_On_Dispose()
	{
		await using BunitContext ctx = new();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMokaJsonViewer(opts => opts.AggressiveCleanup = true);

		IRenderedComponent<MokaJsonViewer> cut = ctx.Render<MokaJsonViewer>(p => p
			.Add(v => v.Json, """{"name":"test","items":[1,2,3]}""")
			.Add(v => v.MaxDepthExpanded, 2));

		cut.WaitForState(() => cut.FindAll(".moka-json-loading").Count == 0, TimeSpan.FromSeconds(3));

		// Disposing should trigger GC.Collect without throwing
		await ctx.DisposeAsync();
	}

	#endregion

	#region Context Menu Data

	[Fact]
	public void Context_Menu_RawValue_Is_Not_Truncated()
	{
		JsonNodeSelectedEventArgs? selectedArgs = null;

		// Build a JSON with a value longer than 500 chars
		string longValue = new('x', 1000);
		string json = $$"""{"data":"{{longValue}}"}""";

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, json)
			.Add(v => v.OnNodeSelected, e => selectedArgs = e)
			.Add(v => v.MaxDepthExpanded, 2));

		// Click on the "data" node to trigger OnNodeSelected
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement dataNode = nodes.First(n => n.TextContent.Contains("data"));
		dataNode.Click();
		cut.WaitForState(() => selectedArgs is not null, TimeSpan.FromSeconds(3));

		Assert.Contains(longValue, selectedArgs!.RawValue);
		Assert.True(selectedArgs.RawValuePreview.Length <= 503); // 500 + "..."
	}

	[Fact]
	public void Context_Menu_RawValue_Is_Populated_For_Container_Nodes()
	{
		string? capturedRawValue = null;

		var customAction = new MokaJsonContextAction
		{
			Id = "capture-raw",
			Label = "Capture",
			OnExecute = ctx =>
			{
				capturedRawValue = ctx.RawValue;
				return ValueTask.CompletedTask;
			}
		};

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"nested":{"a":1,"b":2}}""")
			.Add(v => v.MaxDepthExpanded, 2)
			.Add(v => v.ContextMenuActions, new List<MokaJsonContextAction> { customAction }));

		// Right-click the "nested" object node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement nestedNode = nodes.First(n => n.TextContent.Contains("nested") && n.TextContent.Contains('{'));
		nestedNode.ContextMenu();
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, TimeSpan.FromSeconds(3));

		// Click the custom action
		IReadOnlyList<IElement> menuItems = cut.FindAll(".moka-json-context-item");
		IElement captureItem = menuItems.First(m => m.TextContent.Contains("Capture"));
		captureItem.Click();
		cut.WaitForState(() => capturedRawValue is not null, TimeSpan.FromSeconds(3));
		Assert.NotEmpty(capturedRawValue!);
		Assert.Contains("\"a\"", capturedRawValue);
		Assert.Contains("\"b\"", capturedRawValue);
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"test"}"""));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);
	}

	[Fact]
	public void Shows_Error_For_Invalid_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, "{invalid json}"));

		IElement error = cut.Find(".moka-json-error");
		Assert.NotNull(error);
	}

	[Fact]
	public void ShowToolbar_False_Hides_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, false));

		Assert.Empty(cut.FindAll(".moka-json-toolbar"));
	}

	[Fact]
	public void ShowToolbar_True_Shows_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		Assert.Single(cut.FindAll(".moka-json-toolbar"));
	}

	[Fact]
	public void ShowBottomBar_False_Hides_BottomBar()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, false));

		Assert.Empty(cut.FindAll(".moka-json-bottom-bar"));
	}

	[Fact]
	public void ShowBottomBar_True_Shows_BottomBar()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		Assert.Single(cut.FindAll(".moka-json-bottom-bar"));
	}

	[Fact]
	public void Theme_Dark_Sets_DataAttribute()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.Theme, MokaJsonTheme.Dark));

		IElement viewer = cut.Find(".moka-json-viewer");
		Assert.Equal("dark", viewer.GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public void Theme_Light_Sets_DataAttribute()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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

		RenderViewer(p => p
			.Add(v => v.Json, "{bad}")
			.Add(v => v.OnError, e => receivedError = e));

		Assert.NotNull(receivedError);
		Assert.NotNull(receivedError.Message);
	}

	[Fact]
	public void OnNodeSelected_Fires_When_Node_Clicked()
	{
		JsonNodeSelectedEventArgs? selectedArgs = null;

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"test","value":42}""")
			.Add(v => v.OnNodeSelected, e => selectedArgs = e));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);

		nodes[0].Click();
		cut.WaitForState(() => selectedArgs is not null, TimeSpan.FromSeconds(3));
	}

	#endregion

	#region Toolbar Interaction

	[Fact]
	public void Search_Button_Toggles_Search_Overlay()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.MaxDepthExpanded, 0));

		int initialNodeCount = cut.FindAll(".moka-json-node").Count;

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement expandButton = buttons.First(b => b.TextContent.Contains("Expand"));
		expandButton.Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > initialNodeCount, TimeSpan.FromSeconds(5));
	}

	[Fact]
	public void Collapse_Button_Collapses_All_Except_Root()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.MaxDepthExpanded, 5));

		int expandedNodeCount = cut.FindAll(".moka-json-node").Count;

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		IElement collapseButton = buttons.First(b => b.TextContent.Contains("Collapse"));
		collapseButton.Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count < expandedNodeCount, TimeSpan.FromSeconds(5));
		int collapsedNodeCount = cut.FindAll(".moka-json-node").Count;
		// Root bracket + collapsed children + closing bracket should still be visible
		Assert.True(collapsedNodeCount >= 2);
	}

	#endregion

	#region Bottom Bar Content

	[Fact]
	public void BottomBar_Shows_DocumentSize()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		IElement bottomBar = cut.Find(".moka-json-bottom-bar");
		Assert.Contains("B", bottomBar.TextContent);
	}

	[Fact]
	public void BottomBar_Shows_NodeCount()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowBottomBar, true));

		// Stats are computed on a background thread; wait for them to appear
		cut.WaitForState(() => cut.Find(".moka-json-bottom-bar").TextContent.Contains("nodes"),
			TimeSpan.FromSeconds(5));
	}

	[Fact]
	public void BottomBar_Shows_ParseTime()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowBottomBar, true));

		IElement bottomBar = cut.Find(".moka-json-bottom-bar");
		Assert.Contains("ms", bottomBar.TextContent);
	}

	[Fact]
	public void Export_Button_Exists_In_Toolbar()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}""")
			.Add(v => v.ShowToolbar, true));

		IReadOnlyList<IElement> buttons = cut.FindAll(".moka-json-toolbar button");
		Assert.Contains(buttons, b => b.TextContent.Contains("Export"));
	}

	[Fact]
	public void Export_Button_Invokes_JS_Download()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> editableNodes = cut.FindAll(".moka-json-node--editable");
		Assert.True(editableNodes.Count > 0);
	}

	[Fact]
	public void ReadOnly_True_Does_Not_Add_Editable_Class()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		Assert.Empty(cut.FindAll(".moka-json-node--editable"));
	}

	[Fact]
	public void DoubleClick_On_Value_Node_Shows_Inline_Edit()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		// Find the value node (not the root object node or closing bracket)
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();

		// Should show inline edit input
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, TimeSpan.FromSeconds(3));
	}

	[Fact]
	public void DoubleClick_On_ReadOnly_Does_Not_Show_Edit()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();

		// Small delay to ensure no async side effects
		Thread.Sleep(50);
		Assert.Empty(cut.FindAll(".moka-json-inline-edit"));
	}

	[Fact]
	public void Edit_Context_Menu_Actions_Visible_When_Not_ReadOnly()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click a value node to open context menu
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.ContextMenu();
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, TimeSpan.FromSeconds(3));

		// Context menu should contain edit-related actions
		string markup = cut.Markup;
		Assert.Contains("Edit Value", markup);
		Assert.Contains("Delete", markup);
	}

	[Fact]
	public void Edit_Context_Menu_Actions_Hidden_When_ReadOnly()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, true)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click a node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.ContextMenu();
		// Wait for context menu to render (read-only shows Copy Path, Copy Value, etc.)
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, TimeSpan.FromSeconds(3));

		string markup = cut.Markup;
		Assert.DoesNotContain("Edit Value", markup);
		Assert.DoesNotContain("Delete", markup);
	}

	[Fact]
	public void JsonChanged_Fires_After_Edit_Commit()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Double-click the string value node to enter edit mode
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("Alice"));
		valueNode.DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, TimeSpan.FromSeconds(3));

		// Find the inline edit input and change its value
		IElement input = cut.Find(".moka-json-inline-edit");
		input.Input("Bob");
		input.KeyDown("Enter");
		cut.WaitForState(() => changedJson is not null, TimeSpan.FromSeconds(3));

		Assert.Contains("Bob", changedJson!);
	}

	[Fact]
	public void Bind_Json_Updates_After_Edit()
	{
		string? boundJson = """{"count":10}""";
		string? originalJson = boundJson;

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, boundJson)
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => boundJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Double-click the number value node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement valueNode = nodes.First(n => n.TextContent.Contains("10"));
		valueNode.DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, TimeSpan.FromSeconds(3));

		// Edit the value
		IElement input = cut.Find(".moka-json-inline-edit");
		input.Input("99");
		input.KeyDown("Enter");
		cut.WaitForState(() => boundJson != originalJson, TimeSpan.FromSeconds(3));

		Assert.NotNull(boundJson);
		Assert.Contains("99", boundJson);
	}

	[Fact]
	public void Context_Menu_Delete_Removes_Node()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice","age":30}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click the "age" node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement ageNode = nodes.First(n => n.TextContent.Contains("age") && n.TextContent.Contains("30"));
		ageNode.ContextMenu();
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, TimeSpan.FromSeconds(3));

		// Click Delete
		IReadOnlyList<IElement> menuItems = cut.FindAll(".moka-json-context-item");
		IElement deleteItem = menuItems.First(m => m.TextContent.Contains("Delete"));
		deleteItem.Click();
		cut.WaitForState(() => changedJson is not null, TimeSpan.FromSeconds(3));

		Assert.DoesNotContain("age", changedJson!);
	}

	[Fact]
	public void Context_Menu_Add_Property_Adds_To_Object()
	{
		string? changedJson = null;

		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.ReadOnly, false)
			.Add(v => v.JsonChanged, json => changedJson = json)
			.Add(v => v.MaxDepthExpanded, 2));

		// Right-click the root object node
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node--editable");
		IElement rootNode = nodes[0];
		rootNode.ContextMenu();
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, TimeSpan.FromSeconds(3));

		// Click "Add Property"
		IReadOnlyList<IElement> menuItems = cut.FindAll(".moka-json-context-item");
		IElement addItem = menuItems.First(m => m.TextContent.Contains("Add Property"));
		addItem.Click();
		cut.WaitForState(() => changedJson is not null, TimeSpan.FromSeconds(3));

		Assert.Contains("newProperty", changedJson!);
	}

	#endregion

	#region Collapse Mode

	[Fact]
	public void CollapseMode_Root_Shows_Minimal_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Root));

		// Root mode: only the root object and its closing bracket should be visible
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count <= 2);
	}

	[Fact]
	public void CollapseMode_Expanded_Shows_All_Nodes()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":1},"c":2}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Expanded));

		// Expanded mode: all nodes visible including nested
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count >= 5);
	}

	[Fact]
	public void CollapseMode_Depth_Uses_MaxDepthExpanded()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
			.Add(v => v.CollapseMode, MokaJsonCollapseMode.Depth)
			.Add(v => v.MaxDepthExpanded, 1));

		// Only root and first level expanded, not deeply nested
		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		int expandedCount = cut.FindAll(".moka-json-node").Count;

		// With depth 1, "a" object shows but "b" object is collapsed
		IRenderedComponent<MokaJsonViewer> cutFull = RenderViewer(p => p
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
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """[1,2,3]"""));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count > 0);
	}

	[Fact]
	public void Renders_Nested_Json()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"user":{"name":"Alice","age":30},"tags":["dev","admin"]}""")
			.Add(v => v.MaxDepthExpanded, 3));

		IReadOnlyList<IElement> nodes = cut.FindAll(".moka-json-node");
		Assert.True(nodes.Count >= 5);
	}

	[Fact]
	public void ShowLineNumbers_Renders_Line_Numbers()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowLineNumbers, true));

		IReadOnlyList<IElement> lineNumbers = cut.FindAll(".moka-json-line-number");
		Assert.True(lineNumbers.Count > 0);
	}

	[Fact]
	public void ShowLineNumbers_False_Hides_Line_Numbers()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ShowLineNumbers, false));

		Assert.Empty(cut.FindAll(".moka-json-line-number"));
	}

	[Fact]
	public void Changing_Json_Rerenders_Tree()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"a":1}"""));

		int initialNodeCount = cut.FindAll(".moka-json-node").Count;

		cut.Render(p => p
			.Add(v => v.Json, """{"a":1,"b":2,"c":3,"d":4}"""));

		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > initialNodeCount, TimeSpan.FromSeconds(5));
	}

	[Fact]
	public void Keys_Are_Rendered_In_Markup()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"firstName":"John"}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> keys = cut.FindAll(".moka-json-key");
		Assert.Contains(keys, k => k.TextContent == "firstName");
	}

	[Fact]
	public void String_Values_Are_Rendered()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"name":"Alice"}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> values = cut.FindAll(".moka-json-value--string");
		Assert.Contains(values, v => v.TextContent.Contains("Alice"));
	}

	[Fact]
	public void Number_Values_Are_Rendered()
	{
		IRenderedComponent<MokaJsonViewer> cut = RenderViewer(p => p
			.Add(v => v.Json, """{"count":42}""")
			.Add(v => v.MaxDepthExpanded, 2));

		IReadOnlyList<IElement> values = cut.FindAll(".moka-json-value--number");
		Assert.Contains(values, v => v.TextContent.Contains("42"));
	}

	#endregion
}
