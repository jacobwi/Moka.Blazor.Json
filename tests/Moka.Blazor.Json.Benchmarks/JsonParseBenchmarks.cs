using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;

namespace Moka.Blazor.Json.Benchmarks;

/// <summary>
///     Benchmarks for JSON document parsing at various document sizes.
/// </summary>
[MemoryDiagnoser]
public class JsonParseBenchmarks
{
	private string _json100Kb = null!;
	private string _json1Kb = null!;
	private string _json1Mb = null!;

	[GlobalSetup]
	public void Setup()
	{
		_json1Kb = GenerateJson(10);
		_json100Kb = GenerateJson(500);
		_json1Mb = GenerateJson(5000);
	}

	[Benchmark]
	public async Task Parse_1KB()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync(_json1Kb);
	}

	[Benchmark]
	public async Task Parse_100KB()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync(_json100Kb);
	}

	[Benchmark]
	public async Task Parse_1MB()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync(_json1Mb);
	}

	[Benchmark]
	public void Flatten_1KB()
	{
		using var doc = JsonDocument.Parse(_json1Kb);
		var flattener = new JsonTreeFlattener();
		flattener.ExpandToDepth(doc.RootElement, 2);
		_ = flattener.Flatten(doc.RootElement);
	}

	[Benchmark]
	public void Flatten_100KB()
	{
		using var doc = JsonDocument.Parse(_json100Kb);
		var flattener = new JsonTreeFlattener();
		flattener.ExpandToDepth(doc.RootElement, 2);
		_ = flattener.Flatten(doc.RootElement);
	}

	private static JsonDocumentManager CreateManager()
	{
		return new JsonDocumentManager(
			NullLogger<JsonDocumentManager>.Instance,
			Options.Create(new MokaJsonViewerOptions()));
	}

	private static string GenerateJson(int itemCount)
	{
		var sb = new StringBuilder();
		sb.Append("{\"items\":[");
		for (int i = 0; i < itemCount; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}

			sb.Append(
				$"{{\"id\":{i},\"name\":\"Item {i}\",\"value\":{i * 1.5},\"active\":{(i % 2 == 0 ? "true" : "false")},\"tags\":[\"tag{i % 5}\",\"tag{i % 3}\"]}}");
		}

		sb.Append("]}");
		return sb.ToString();
	}
}
