using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Moka.Blazor.Json.Components;
using Moka.Blazor.Json.Extensions;
using Moka.Blazor.Json.Models;
using Xunit;

namespace Moka.Blazor.Json.Tests.Components;

public sealed class MokaJsonViewerLargeDocumentTests : IAsyncLifetime
{
	private const string ModulePath = "./_content/Moka.Blazor.Json/js/moka-json.js";
	private const string SmallDocument = """{"name":"Alice","items":[1,2,3],"nested":{"deep":{"value":"needle"}}}""";

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	private readonly BunitContext _ctx = new();
	private BunitJSModuleInterop _module = null!;

	public ValueTask InitializeAsync()
	{
		_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		_module = _ctx.JSInterop.SetupModule(ModulePath);
		_module.Mode = JSRuntimeMode.Loose;
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

	private void UseOptions(Action<MokaJsonViewerOptions>? configure = null) =>
		_ctx.Services.AddMokaJsonViewer(o =>
		{
			o.LazyParsingThresholdBytes = 1;
			o.SearchDebounceMs = 0;
			configure?.Invoke(o);
		});

	private IRenderedComponent<MokaJsonViewer> RenderLoaded(Action<ComponentParameterCollectionBuilder<MokaJsonViewer>> parameters)
	{
		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render(parameters);
		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);
		return cut;
	}

	private static MemoryStream ToStream(string json) => new(Encoding.UTF8.GetBytes(json));

	private static string NumberArray(int count) => $"[{string.Join(',', Enumerable.Range(0, count))}]";

	#region Loading

	[Fact]
	public void Seekable_Stream_Over_The_Threshold_Loads_Lazily()
	{
		UseOptions();
		using MemoryStream stream = ToStream(SmallDocument);

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		Assert.Contains("Lazy Mode", cut.Find(".moka-json-bottom-bar").TextContent);
		Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "name");
		Assert.True(stream.CanRead);
	}

	[Fact]
	public void Forward_Only_Stream_Is_Spooled_And_Loads_Lazily()
	{
		UseOptions();
		using var stream = new ForwardOnlyStream(Encoding.UTF8.GetBytes(SmallDocument));

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		Assert.Contains("Lazy Mode", cut.Find(".moka-json-bottom-bar").TextContent);
	}

	[Fact]
	public void Forward_Only_Stream_Under_The_Threshold_Is_Parsed_And_Editable()
	{
		_ctx.Services.AddMokaJsonViewer();
		using var stream = new ForwardOnlyStream(Encoding.UTF8.GetBytes(SmallDocument));

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		Assert.DoesNotContain("Lazy Mode", cut.Find(".moka-json-bottom-bar").TextContent);
	}

	[Fact]
	public void Forward_Only_Stream_Over_The_Size_Limit_Shows_An_Error()
	{
		UseOptions(o => o.MaxDocumentSizeBytes = 8);
		using var stream = new ForwardOnlyStream(Encoding.UTF8.GetBytes(SmallDocument));

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p.Add(v => v.JsonStream, stream));

		cut.WaitForAssertion(() => Assert.Contains("maximum allowed size", cut.Find(".moka-json-error").TextContent), Timeout);
	}

	[Fact]
	public void Loading_Shows_Progress_And_Can_Be_Cancelled()
	{
		UseOptions();
		using var stream = new ForwardOnlyStream(new byte[1024], blockAfterData: true);

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p.Add(v => v.JsonStream, stream));

		cut.WaitForAssertion(() => Assert.Contains("KB", cut.Find(".moka-json-loading").TextContent), Timeout);
		cut.Find(".moka-json-cancel").Click();

		cut.WaitForAssertion(() => Assert.Contains("cancelled", cut.Find(".moka-json-loading").TextContent), Timeout);
		Assert.Empty(cut.FindAll(".moka-json-error"));
		Assert.Empty(cut.FindAll(".moka-json-cancel"));
	}

	[Fact]
	public async Task Disposing_While_Loading_Stops_The_Load()
	{
		UseOptions();
		using var stream = new ForwardOnlyStream(new byte[1024], blockAfterData: true);

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p.Add(v => v.JsonStream, stream));
		cut.WaitForState(() => cut.FindAll(".moka-json-cancel").Count > 0, Timeout);

		await cut.Instance.DisposeAsync();

		Assert.True(stream.WaitForCancellation(Timeout));
	}

	[Fact]
	public void Invalid_Large_Document_Shows_The_Parse_Error()
	{
		UseOptions();
		using MemoryStream stream = ToStream("""{"a":[1,2""");

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p.Add(v => v.JsonStream, stream));

		cut.WaitForState(() => cut.FindAll(".moka-json-error").Count > 0, Timeout);
	}

	[Fact]
	public void Statistics_Show_For_Large_Documents()
	{
		UseOptions();
		using MemoryStream stream = ToStream(SmallDocument);

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		cut.WaitForAssertion(() =>
		{
			string bar = cut.Find(".moka-json-bottom-bar").TextContent;
			Assert.Contains("9 nodes", bar);
			Assert.Contains("/ 3", bar);
		}, Timeout);
	}

	[Fact]
	public void A_File_That_Fails_While_Open_Shows_A_Message_And_The_Viewer_Keeps_Working()
	{
		UseOptions();
		using var stream = new FailingStream(Encoding.UTF8.GetBytes("""{"a":{"b":1},"c":{"d":2}}"""));
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.JsonStream, stream)
			.Add(v => v.MaxDepthExpanded, 0));

		cut.Find(".moka-json-node-toggle").Click();
		cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "a"), Timeout);

		stream.Fail = true;
		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains('a', StringComparison.Ordinal))
			.QuerySelector(".moka-json-node-toggle")!.Click();

		cut.WaitForAssertion(() => Assert.Contains("Could not read", cut.Find(".moka-json-toast").TextContent), Timeout);
		Assert.Empty(cut.FindAll(".moka-json-error"));
		Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "c");
	}

	[Fact]
	public void Stays_Visible_Without_An_Interactive_Renderer()
	{
		UseOptions();
		_ctx.SetRendererInfo(new RendererInfo("Static", false));

		IRenderedComponent<MokaJsonViewer> cut = _ctx.Render<MokaJsonViewer>(p => p.Add(v => v.Json, """{"a":1}"""));

		cut.WaitForState(() => cut.FindAll(".moka-json-node").Count > 0, Timeout);
		Assert.DoesNotContain("visibility:hidden", cut.Find(".moka-json-viewer").GetAttribute("style") ?? "");
	}

	#endregion

	#region Grouped rows

	[Fact]
	public void Very_Long_Array_Shows_Groups_And_Opens_One()
	{
		UseOptions();
		using MemoryStream stream = ToStream(NumberArray(100_050));

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		IReadOnlyList<IElement> groups = cut.FindAll(".moka-json-range");
		Assert.Equal(2, groups.Count);
		Assert.Contains("0 ... 99,999", groups[0].TextContent);
		Assert.Contains("100,000 ... 100,049", groups[1].TextContent);

		groups[1].Click();

		cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "100049"), Timeout);
	}

	[Fact]
	public async Task Search_Reveals_A_Match_Inside_A_Closed_Group()
	{
		UseOptions();
		using MemoryStream stream = ToStream(NumberArray(100_050));
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		int count = await cut.InvokeAsync(() => cut.Instance.SearchAsync("100049").AsTask());

		Assert.Equal(1, count);
		cut.WaitForAssertion(() => Assert.Equal("/100049", cut.Instance.SelectedPath ?? "/100049"), Timeout);
		cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".moka-json-range"),
			g => g.TextContent.Contains("100,000", StringComparison.Ordinal)), Timeout);
	}

	#endregion

	#region Expanding and selecting

	[Fact]
	public void First_View_Of_A_Large_Document_Leaves_Long_Containers_Closed()
	{
		UseOptions();
		using MemoryStream stream = ToStream($$"""{"long":{{NumberArray(5000)}},"short":[1,2]}""");

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		Assert.Contains("5000 items", cut.Find(".moka-json-collapsed-preview").TextContent);
		Assert.Contains(cut.FindAll(".moka-json-key"), k => k.TextContent == "short");
	}

	[Fact]
	public async Task Expand_All_On_A_Huge_Document_Stops_At_The_Limit_And_Says_So()
	{
		UseOptions();
		string objects = string.Join(',', Enumerable.Range(0, 60_000).Select(i => $"{{\"a\":{i},\"b\":{{\"c\":1}}}}"));
		using MemoryStream stream = ToStream($"[{objects}]");
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		await cut.InvokeAsync(() => cut.Instance.ExpandAllAsync().AsTask());

		cut.WaitForAssertion(() => Assert.Contains("too large to expand fully", cut.Find(".moka-json-toast").TextContent),
			TimeSpan.FromSeconds(60));
	}

	[Fact]
	public void Selecting_A_Node_Reports_It()
	{
		UseOptions();
		using MemoryStream stream = ToStream(SmallDocument);
		JsonNodeSelectedEventArgs? selected = null;

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.JsonStream, stream)
			.Add(v => v.OnNodeSelected, args => { selected = args; }));

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("Alice", StringComparison.Ordinal)).Click();

		cut.WaitForAssertion(() =>
		{
			Assert.NotNull(selected);
			Assert.Equal("/name", selected.Path);
			Assert.Equal("\"Alice\"", selected.RawValue);
			Assert.Equal("name", selected.PropertyName);
		}, Timeout);
	}

	[Fact]
	public async Task Context_Menu_Does_Not_Materialize_A_Large_Value()
	{
		UseOptions();
		MokaJsonNodeContext? seen = null;
		var action = new MokaJsonContextAction
		{
			Id = "capture",
			Label = "Capture",
			OnExecute = ctx =>
			{
				seen = ctx;
				return ValueTask.CompletedTask;
			}
		};

		string json = $$"""{"big":{{NumberArray(400_000)}},"small":[1]}""";
		using MemoryStream stream = ToStream(json);
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.JsonStream, stream)
			.Add(v => v.ContextMenuActions, [action]));

		await cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("big", StringComparison.Ordinal))
			.ContextMenuAsync(new MouseEventArgs());
		cut.WaitForState(() => cut.FindAll(".moka-json-context-item").Count > 0, Timeout);
		await cut.InvokeAsync(() => cut.FindAll(".moka-json-context-item")
			.First(i => i.TextContent.Contains("Capture", StringComparison.Ordinal)).Click());

		cut.WaitForAssertion(() =>
		{
			Assert.NotNull(seen);
			Assert.Equal("", seen.RawValue);
			Assert.Contains("400000 items", seen.RawValuePreview);
		}, Timeout);
	}

	#endregion

	#region Search

	[Fact]
	public void Search_Of_A_Large_Document_Marks_A_Truncated_Count()
	{
		UseOptions(o => o.MaxSearchMatches = 3);
		using MemoryStream stream = ToStream("""["hit","hit","hit","hit","hit"]""");
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.JsonStream, stream));

		cut.Find(".moka-json-toolbar button").Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-search").Count > 0, Timeout);
		cut.Find(".moka-json-search input[type=text]").Input("hit");

		cut.WaitForAssertion(() =>
			Assert.Equal("1 of 3+", cut.Find(".moka-json-search-info").TextContent.Trim()), Timeout);
	}

	[Fact]
	public void Search_Of_A_Large_Document_Reveals_A_Deep_Match()
	{
		UseOptions();
		using MemoryStream stream = ToStream(SmallDocument);
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.JsonStream, stream)
			.Add(v => v.MaxDepthExpanded, 0));

		cut.Find(".moka-json-toolbar button").Click();
		cut.WaitForState(() => cut.FindAll(".moka-json-search").Count > 0, Timeout);
		cut.Find(".moka-json-search input[type=text]").Input("needle");

		cut.WaitForAssertion(() =>
		{
			Assert.Equal("1 of 1", cut.Find(".moka-json-search-info").TextContent.Trim());
			Assert.Contains("needle", cut.Find(".moka-json-node--search-active").TextContent);
		}, Timeout);
	}

	#endregion

	#region Options

	[Fact]
	public void Default_Theme_Option_Applies_Unless_A_Theme_Is_Passed()
	{
		_ctx.Services.AddMokaJsonViewer(o => o.DefaultTheme = MokaJsonTheme.Dark);

		IRenderedComponent<MokaJsonViewer> usesOption = RenderLoaded(p => p.Add(v => v.Json, SmallDocument));
		Assert.Equal("dark", usesOption.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme"));

		IRenderedComponent<MokaJsonViewer> usesParameter = RenderLoaded(p => p
			.Add(v => v.Json, SmallDocument)
			.Add(v => v.Theme, MokaJsonTheme.Light));
		Assert.Equal("light", usesParameter.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public void Auto_Theme_Is_Left_To_The_Stylesheet()
	{
		_ctx.Services.AddMokaJsonViewer();

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.Json, SmallDocument));

		Assert.Equal("auto", cut.Find(".moka-json-viewer").GetAttribute("data-moka-json-theme"));
	}

	[Fact]
	public void Default_Expand_Depth_Option_Applies_Unless_A_Depth_Is_Passed()
	{
		_ctx.Services.AddMokaJsonViewer(o => o.DefaultExpandDepth = 0);

		IRenderedComponent<MokaJsonViewer> usesOption = RenderLoaded(p => p.Add(v => v.Json, SmallDocument));
		Assert.Single(usesOption.FindAll(".moka-json-node"));

		IRenderedComponent<MokaJsonViewer> usesParameter = RenderLoaded(p => p
			.Add(v => v.Json, SmallDocument)
			.Add(v => v.MaxDepthExpanded, 1));
		Assert.True(usesParameter.FindAll(".moka-json-node").Count > 1);
	}

	[Fact]
	public void Edit_Mode_Option_Can_Turn_Editing_Off()
	{
		_ctx.Services.AddMokaJsonViewer(o => o.EnableEditMode = false);

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, SmallDocument)
			.Add(v => v.ReadOnly, false));

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("Alice", StringComparison.Ordinal)).DoubleClick();

		Assert.Empty(cut.FindAll(".moka-json-inline-edit"));
	}

	#endregion

	#region Rendering and editing

	[Fact]
	public void Keys_That_Do_Nothing_Do_Not_Render()
	{
		_ctx.Services.AddMokaJsonViewer();
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.Json, SmallDocument));
		int renders = cut.RenderCount;

		cut.Find(".moka-json-viewer").KeyDown(new KeyboardEventArgs { Key = "a" });
		cut.Find(".moka-json-viewer").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

		Assert.Equal(renders, cut.RenderCount);
	}

	[Fact]
	public void A_Rejected_Edit_Shows_The_Error_And_Typing_Clears_It()
	{
		_ctx.Services.AddMokaJsonViewer();
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, """{"count":5}""")
			.Add(v => v.ReadOnly, false));

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("count", StringComparison.Ordinal)).DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, Timeout);

		cut.Find(".moka-json-inline-edit").Input("not a number");
		cut.Find(".moka-json-inline-edit").KeyDown(new KeyboardEventArgs { Key = "Enter" });

		cut.WaitForAssertion(() =>
			Assert.Contains("Invalid number", cut.Find(".moka-json-inline-edit-error").TextContent), Timeout);

		cut.Find(".moka-json-inline-edit").Input("7");

		cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".moka-json-inline-edit-error")), Timeout);
	}

	[Fact]
	public void A_Value_Too_Long_To_Edit_Inline_Says_So()
	{
		_ctx.Services.AddMokaJsonViewer();
		string json = $$"""{"long":"{{new string('x', 20_000)}}"}""";
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, json)
			.Add(v => v.ReadOnly, false));

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains("long", StringComparison.Ordinal)).DoubleClick();

		cut.WaitForAssertion(() => Assert.Contains("too long to edit", cut.Find(".moka-json-toast").TextContent), Timeout);
		Assert.Empty(cut.FindAll(".moka-json-inline-edit"));
	}

	[Fact]
	public void A_Very_Long_Value_Shows_Its_Start_And_How_Much_Is_Hidden()
	{
		_ctx.Services.AddMokaJsonViewer();
		string json = $$"""{"long":"{{new string('x', 25_000)}}"}""";

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.Json, json));

		Assert.Equal(10_000, cut.Find(".moka-json-value--string").TextContent.Length);
		Assert.Contains("15,000 more characters", cut.Find(".moka-json-value-more").TextContent);
	}

	[Fact]
	public void Key_And_Value_Share_A_Block_So_Wrapped_Text_Keeps_Its_Indent()
	{
		_ctx.Services.AddMokaJsonViewer();

		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p.Add(v => v.Json, """{"key":"value"}"""));

		IElement key = cut.Find(".moka-json-key");
		Assert.Equal("moka-json-node-content", key.ParentElement!.ClassName);
		Assert.NotNull(key.ParentElement.QuerySelector(".moka-json-value--string"));
	}

	[Fact]
	public void Editing_One_Row_Leaves_The_Others_Without_Edit_State()
	{
		_ctx.Services.AddMokaJsonViewer();
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, """{"a":1,"b":2}""")
			.Add(v => v.ReadOnly, false));

		cut.FindAll(".moka-json-node").First(n => n.TextContent.Contains('a', StringComparison.Ordinal)).DoubleClick();
		cut.WaitForState(() => cut.FindAll(".moka-json-inline-edit").Count > 0, Timeout);

		Assert.Single(cut.FindComponents<MokaJsonNode>(), n => n.Instance.EditState is not null);
	}

	#endregion

	#region Export

	[Fact]
	public void Export_Sends_A_Stream_To_The_Browser()
	{
		_ctx.Services.AddMokaJsonViewer();
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, SmallDocument)
			.Add(v => v.ToolbarMode, MokaJsonToolbarMode.Text));

		cut.FindAll(".moka-json-toolbar button").First(b => b.TextContent.Contains("Export", StringComparison.Ordinal)).Click();

		cut.WaitForAssertion(() =>
		{
			JSRuntimeInvocation call = _module.Invocations["downloadFile"].Single();
			Assert.IsType<DotNetStreamReference>(call.Arguments[1]);
		}, Timeout);
	}

	[Fact]
	public void Export_Refuses_A_Document_Over_The_Limit()
	{
		_ctx.Services.AddMokaJsonViewer(o => o.MaxExportSizeBytes = 8);
		IRenderedComponent<MokaJsonViewer> cut = RenderLoaded(p => p
			.Add(v => v.Json, SmallDocument)
			.Add(v => v.ToolbarMode, MokaJsonToolbarMode.Text));

		cut.FindAll(".moka-json-toolbar button").First(b => b.TextContent.Contains("Export", StringComparison.Ordinal)).Click();

		cut.WaitForAssertion(() => Assert.Contains("too large to export", cut.Find(".moka-json-toast").TextContent), Timeout);
		Assert.Empty(_module.Invocations["downloadFile"]);
	}

	#endregion

	private sealed class FailingStream(byte[] bytes) : MemoryStream(bytes)
	{
		public bool Fail { get; set; }

		public override int Read(Span<byte> buffer) =>
			Fail ? throw new IOException("The disk went away.") : base.Read(buffer);
	}

	private sealed class ForwardOnlyStream(byte[] bytes, bool blockAfterData = false) : Stream
	{
		private readonly ManualResetEventSlim _cancelled = new();
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public bool WaitForCancellation(TimeSpan timeout) => _cancelled.Wait(timeout);

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = Math.Min(count, bytes.Length - _position);
			Buffer.BlockCopy(bytes, _position, buffer, offset, read);
			_position += read;
			return read;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			int read = Math.Min(buffer.Length, bytes.Length - _position);
			if (read == 0 && blockAfterData)
			{
				try
				{
					await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					_cancelled.Set();
					throw;
				}
			}

			bytes.AsMemory(_position, read).CopyTo(buffer);
			_position += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_cancelled.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
