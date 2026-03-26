using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class LazyJsonDocumentSourceTests : IAsyncLifetime
{
	private LazyJsonDocumentSource? _source;

	public async ValueTask InitializeAsync()
	{
		_source = await LazyJsonDocumentSource.CreateFromStringAsync(
			"""{"name":"Alice","age":30,"tags":["dev","admin"],"address":{"city":"NYC","zip":"10001"}}""",
			NullLogger<LazyJsonDocumentSource>.Instance,
			new MokaJsonViewerOptions());
	}

	public async ValueTask DisposeAsync()
	{
		if (_source is not null)
		{
			await _source.DisposeAsync();
		}
	}

	[Fact]
	public void IsLoaded_ReturnsTrue() => Assert.True(_source!.IsLoaded);

	[Fact]
	public void DocumentSizeBytes_IsPositive() => Assert.True(_source!.DocumentSizeBytes > 0);

	[Fact]
	public void RootValueKind_IsObject() => Assert.Equal(JsonValueKind.Object, _source!.RootValueKind);

	[Fact]
	public void SupportsEditing_IsFalse() => Assert.False(_source!.SupportsEditing);

	[Fact]
	public void GetChildCount_Root_Returns4() => Assert.Equal(4, _source!.GetChildCount(""));

	[Fact]
	public void GetChildCount_Array_Returns2() => Assert.Equal(2, _source!.GetChildCount("/tags"));

	[Fact]
	public void GetValueKind_String_ReturnsString() =>
		Assert.Equal(JsonValueKind.String, _source!.GetValueKind("/name"));

	[Fact]
	public void GetValueKind_Number_ReturnsNumber() =>
		Assert.Equal(JsonValueKind.Number, _source!.GetValueKind("/age"));

	[Fact]
	public void GetValueKind_NestedObject_ReturnsObject() =>
		Assert.Equal(JsonValueKind.Object, _source!.GetValueKind("/address"));

	[Fact]
	public void GetRawValue_String_ReturnsValue() => Assert.Equal("Alice", _source!.GetRawValue("/name"));

	[Fact]
	public void GetRawValue_Number_ReturnsValue() => Assert.Equal("30", _source!.GetRawValue("/age"));

	[Fact]
	public void GetRawValue_Container_ReturnsNull() => Assert.Null(_source!.GetRawValue("/address"));

	[Fact]
	public void EnumerateChildren_Root_Returns4Children()
	{
		var children = _source!.EnumerateChildren("").ToList();
		Assert.Equal(4, children.Count);
		Assert.Equal("name", children[0].PropertyName);
		Assert.Equal("age", children[1].PropertyName);
		Assert.Equal("tags", children[2].PropertyName);
		Assert.Equal("address", children[3].PropertyName);
	}

	[Fact]
	public void EnumerateChildren_Array_ReturnsElements()
	{
		var children = _source!.EnumerateChildren("/tags").ToList();
		Assert.Equal(2, children.Count);
		Assert.Equal(0, children[0].ArrayIndex);
		Assert.Equal(1, children[1].ArrayIndex);
		Assert.Equal("dev", children[0].RawValue);
		Assert.Equal("admin", children[1].RawValue);
	}

	[Fact]
	public void GetElement_Root_ReturnsRootElement()
	{
		JsonElement element = _source!.GetElement("");
		Assert.Equal(JsonValueKind.Object, element.ValueKind);
	}

	[Fact]
	public void GetElement_NestedProperty_ReturnsCorrectElement()
	{
		JsonElement element = _source!.GetElement("/address/city");
		Assert.Equal(JsonValueKind.String, element.ValueKind);
		Assert.Equal("NYC", element.GetString());
	}

	[Fact]
	public void GetElement_ArrayElement_ReturnsCorrectElement()
	{
		JsonElement element = _source!.GetElement("/tags/0");
		Assert.Equal(JsonValueKind.String, element.ValueKind);
		Assert.Equal("dev", element.GetString());
	}

	[Fact]
	public void GetJsonString_ReturnsValidJson()
	{
		string json = _source!.GetJsonString(false);
		Assert.Contains("Alice", json);

		// Verify it's valid JSON by parsing it
		using var doc = JsonDocument.Parse(json);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	public void CountNodes_ReturnsNegativeOne() => Assert.Equal(-1, _source!.CountNodes());

	[Fact]
	public void GetMaxDepth_ReturnsNegativeOne() => Assert.Equal(-1, _source!.GetMaxDepth());

	[Fact]
	public async Task CreateFromString_LargeJson_Works()
	{
		// Build a moderately sized JSON
		var sb = new StringBuilder();
		sb.Append("{\"items\":[");
		for (int i = 0; i < 100; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}

			sb.Append(CultureInfo.InvariantCulture, $"{{\"id\":{i},\"name\":\"Item {i}\"}}");
		}

		sb.Append("]}");

		await using LazyJsonDocumentSource source = await LazyJsonDocumentSource.CreateFromStringAsync(
			sb.ToString(),
			NullLogger<LazyJsonDocumentSource>.Instance,
			new MokaJsonViewerOptions());

		Assert.True(source.IsLoaded);
		Assert.Equal(JsonValueKind.Object, source.RootValueKind);

		// Navigate to a nested element
		JsonElement element = source.GetElement("/items/50/name");
		Assert.Equal("Item 50", element.GetString());
	}
}
