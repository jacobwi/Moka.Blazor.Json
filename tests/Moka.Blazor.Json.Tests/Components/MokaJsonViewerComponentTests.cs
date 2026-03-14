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

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    #region Rendering & Parameters

    [Fact]
    public void Renders_Loading_State_When_No_Json()
    {
        var cut = _ctx.Render<MokaJsonViewer>();

        var loading = cut.Find(".moka-json-loading");
        Assert.Equal("Loading...", loading.TextContent);
    }

    [Fact]
    public void Renders_Json_Tree_When_Json_Provided()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"name":"test"}"""));

        var nodes = cut.FindAll(".moka-json-node");
        Assert.True(nodes.Count > 0);
    }

    [Fact]
    public void Shows_Error_For_Invalid_Json()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, "{invalid json}"));

        var error = cut.Find(".moka-json-error");
        Assert.NotNull(error);
    }

    [Fact]
    public void ShowToolbar_False_Hides_Toolbar()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowToolbar, false));

        Assert.Empty(cut.FindAll(".moka-json-toolbar"));
    }

    [Fact]
    public void ShowToolbar_True_Shows_Toolbar()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowToolbar, true));

        Assert.Single(cut.FindAll(".moka-json-toolbar"));
    }

    [Fact]
    public void ShowBottomBar_False_Hides_BottomBar()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowBottomBar, false));

        Assert.Empty(cut.FindAll(".moka-json-bottom-bar"));
    }

    [Fact]
    public void ShowBottomBar_True_Shows_BottomBar()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowBottomBar, true));

        Assert.Single(cut.FindAll(".moka-json-bottom-bar"));
    }

    [Fact]
    public void Theme_Dark_Sets_DataAttribute()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.Theme, MokaJsonTheme.Dark));

        var viewer = cut.Find(".moka-json-viewer");
        Assert.Equal("dark", viewer.GetAttribute("data-moka-json-theme"));
    }

    [Fact]
    public void Theme_Light_Sets_DataAttribute()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.Theme, MokaJsonTheme.Light));

        var viewer = cut.Find(".moka-json-viewer");
        Assert.Equal("light", viewer.GetAttribute("data-moka-json-theme"));
    }

    [Fact]
    public void Height_Parameter_Applies_Style()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Height, "600px"));

        var viewer = cut.Find(".moka-json-viewer");
        Assert.Contains("height: 600px", viewer.GetAttribute("style"));
    }

    [Fact]
    public void Default_Height_Is_400px()
    {
        var cut = _ctx.Render<MokaJsonViewer>();

        var viewer = cut.Find(".moka-json-viewer");
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

        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"name":"test","value":42}""")
            .Add(v => v.OnNodeSelected, e => selectedArgs = e));

        var nodes = cut.FindAll(".moka-json-node");
        Assert.True(nodes.Count > 0);

        nodes[0].Click();

        Assert.NotNull(selectedArgs);
    }

    #endregion

    #region Toolbar Interaction

    [Fact]
    public void Search_Button_Toggles_Search_Overlay()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowToolbar, true));

        // Search overlay should not be visible initially
        Assert.Empty(cut.FindAll(".moka-json-search"));

        // Click search button
        var searchButton = cut.Find(".moka-json-toolbar button");
        searchButton.Click();

        // Search overlay should now be visible
        Assert.Single(cut.FindAll(".moka-json-search"));
    }

    [Fact]
    public void Expand_Button_Expands_All_Nodes()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":{"b":{"c":1}}}""")
            .Add(v => v.MaxDepthExpanded, 0));

        var initialNodeCount = cut.FindAll(".moka-json-node").Count;

        var buttons = cut.FindAll(".moka-json-toolbar button");
        var expandButton = buttons.First(b => b.TextContent.Contains("Expand"));
        expandButton.Click();

        var expandedNodeCount = cut.FindAll(".moka-json-node").Count;
        Assert.True(expandedNodeCount > initialNodeCount);
    }

    [Fact]
    public void Collapse_Button_Collapses_All_Nodes()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":{"b":1},"c":2}""")
            .Add(v => v.MaxDepthExpanded, 5));

        var expandedNodeCount = cut.FindAll(".moka-json-node").Count;

        var buttons = cut.FindAll(".moka-json-toolbar button");
        var collapseButton = buttons.First(b => b.TextContent.Contains("Collapse"));
        collapseButton.Click();

        var collapsedNodeCount = cut.FindAll(".moka-json-node").Count;
        Assert.True(collapsedNodeCount < expandedNodeCount);
    }

    #endregion

    #region Bottom Bar Content

    [Fact]
    public void BottomBar_Shows_DocumentSize()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowBottomBar, true));

        var bottomBar = cut.Find(".moka-json-bottom-bar");
        Assert.Contains("B", bottomBar.TextContent);
    }

    [Fact]
    public void BottomBar_Shows_NodeCount()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1,"b":2}""")
            .Add(v => v.ShowBottomBar, true));

        var bottomBar = cut.Find(".moka-json-bottom-bar");
        Assert.Contains("nodes", bottomBar.TextContent);
    }

    [Fact]
    public void BottomBar_Shows_ParseTime()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}""")
            .Add(v => v.ShowBottomBar, true));

        var bottomBar = cut.Find(".moka-json-bottom-bar");
        Assert.Contains("ms", bottomBar.TextContent);
    }

    #endregion

    #region Integration

    [Fact]
    public void Renders_Array_Json()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """[1,2,3]"""));

        var nodes = cut.FindAll(".moka-json-node");
        Assert.True(nodes.Count > 0);
    }

    [Fact]
    public void Renders_Nested_Json()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"user":{"name":"Alice","age":30},"tags":["dev","admin"]}""")
            .Add(v => v.MaxDepthExpanded, 3));

        var nodes = cut.FindAll(".moka-json-node");
        Assert.True(nodes.Count >= 5);
    }

    [Fact]
    public void ShowLineNumbers_Renders_Line_Numbers()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1,"b":2}""")
            .Add(v => v.ShowLineNumbers, true));

        var lineNumbers = cut.FindAll(".moka-json-line-number");
        Assert.True(lineNumbers.Count > 0);
    }

    [Fact]
    public void ShowLineNumbers_False_Hides_Line_Numbers()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1,"b":2}""")
            .Add(v => v.ShowLineNumbers, false));

        Assert.Empty(cut.FindAll(".moka-json-line-number"));
    }

    [Fact]
    public void Changing_Json_Rerenders_Tree()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"a":1}"""));

        var initialNodeCount = cut.FindAll(".moka-json-node").Count;

        cut.Render(p => p
            .Add(v => v.Json, """{"a":1,"b":2,"c":3,"d":4}"""));

        var newNodeCount = cut.FindAll(".moka-json-node").Count;
        Assert.True(newNodeCount > initialNodeCount);
    }

    [Fact]
    public void Keys_Are_Rendered_In_Markup()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"firstName":"John"}""")
            .Add(v => v.MaxDepthExpanded, 2));

        var keys = cut.FindAll(".moka-json-key");
        Assert.Contains(keys, k => k.TextContent == "firstName");
    }

    [Fact]
    public void String_Values_Are_Rendered()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"name":"Alice"}""")
            .Add(v => v.MaxDepthExpanded, 2));

        var values = cut.FindAll(".moka-json-value--string");
        Assert.Contains(values, v => v.TextContent.Contains("Alice"));
    }

    [Fact]
    public void Number_Values_Are_Rendered()
    {
        var cut = _ctx.Render<MokaJsonViewer>(p => p
            .Add(v => v.Json, """{"count":42}""")
            .Add(v => v.MaxDepthExpanded, 2));

        var values = cut.FindAll(".moka-json-value--number");
        Assert.Contains(values, v => v.TextContent.Contains("42"));
    }

    #endregion
}