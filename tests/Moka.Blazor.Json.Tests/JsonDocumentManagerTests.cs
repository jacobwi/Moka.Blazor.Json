using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonDocumentManagerTests : IAsyncDisposable
{
	private readonly JsonDocumentManager _manager = new(
		NullLogger<JsonDocumentManager>.Instance,
		Options.Create(new MokaJsonViewerOptions()));

	public async ValueTask DisposeAsync() => await _manager.DisposeAsync();

	[Fact]
	public async Task ParseAsync_ValidJson_LoadsDocument()
	{
		await _manager.ParseAsync("""{"name":"test","value":42}""");

		Assert.True(_manager.IsLoaded);
		Assert.True(_manager.DocumentSizeBytes > 0);
	}

	[Fact]
	public async Task ParseAsync_InvalidJson_ThrowsJsonException()
	{
		JsonException ex = await Assert.ThrowsAnyAsync<JsonException>(() => _manager.ParseAsync("{invalid}").AsTask());
		Assert.NotNull(ex);
	}

	[Fact]
	public async Task NavigateToElement_ValidPath_ReturnsElement()
	{
		await _manager.ParseAsync("""{"users":[{"name":"Alice"}]}""");

		JsonElement element = _manager.NavigateToElement("/users/0/name");

		Assert.Equal(JsonValueKind.String, element.ValueKind);
		Assert.Equal("Alice", element.GetString());
	}

	[Fact]
	public async Task NavigateToElement_InvalidPath_ThrowsKeyNotFoundException()
	{
		await _manager.ParseAsync("""{"name":"test"}""");

		Assert.Throws<KeyNotFoundException>(() => _manager.NavigateToElement("/nonexistent"));
	}

	[Fact]
	public async Task CountNodes_ReturnsCorrectCount()
	{
		await _manager.ParseAsync("""{"a":1,"b":[2,3]}""");

		// root object + a + 1 + b + array + 2 + 3 = 5 (root + a:1 + b:[2,3])
		// Object{a:1, b:Array[2,3]} => root(1) + a(1) + b(1) + 2(1) + 3(1) = 5
		int count = _manager.CountNodes();
		Assert.Equal(5, count);
	}

	[Fact]
	public async Task GetMaxDepth_ReturnsCorrectDepth()
	{
		await _manager.ParseAsync("""{"a":{"b":{"c":1}}}""");

		Assert.Equal(3, _manager.GetMaxDepth());
	}

	[Fact]
	public async Task FormatBytes_FormatsCorrectly()
	{
		Assert.Equal("512 B", JsonDocumentManager.FormatBytes(512));
		Assert.Equal("1.0 KB", JsonDocumentManager.FormatBytes(1024));
		Assert.Equal("1.5 MB", JsonDocumentManager.FormatBytes((long)(1.5 * 1024 * 1024)));
	}

	[Fact]
	public async Task GetJsonString_ReturnsValidJson()
	{
		string input = """{"name":"test"}""";
		await _manager.ParseAsync(input);

		string output = _manager.GetJsonString(false);
		Assert.Contains("\"name\"", output);
		Assert.Contains("\"test\"", output);
	}
}
