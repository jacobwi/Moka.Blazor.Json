using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Demo.Components.Pages;

public sealed partial class Home : ComponentBase
{
	#region 1. Basic sample

	private const string _sampleJson = """
	                                   {
	                                     "name": "Moka.Blazor.Json",
	                                     "version": "0.1.0",
	                                     "description": "A high-performance Blazor JSON viewer",
	                                     "features": [
	                                       "Virtualized rendering",
	                                       "Search with regex",
	                                       "Context menu",
	                                       "Theming",
	                                       "Streaming support"
	                                     ],
	                                     "config": {
	                                       "maxDepth": 256,
	                                       "maxSize": "100MB",
	                                       "themes": ["light", "dark", "auto"],
	                                       "performance": {
	                                         "virtualization": true,
	                                         "lazyParsing": true,
	                                         "objectPooling": true
	                                       }
	                                     },
	                                     "users": [
	                                       { "id": 1, "name": "Alice", "email": "alice@example.com", "active": true },
	                                       { "id": 2, "name": "Bob", "email": "bob@example.com", "active": false },
	                                       { "id": 3, "name": "Charlie", "email": "charlie@example.com", "active": true }
	                                     ],
	                                     "metadata": {
	                                       "created": "2024-01-15T10:30:00Z",
	                                       "updated": null,
	                                       "tags": ["blazor", "json", "viewer"],
	                                       "stats": { "downloads": 12345, "stars": 42, "forks": 7 }
	                                     }
	                                   }
	                                   """;

	#endregion

	#region 5. Deep nesting

	private const string _deepNestedJson = """
	                                       {
	                                         "level1": {
	                                           "description": "Top level",
	                                           "level2": {
	                                             "description": "Second level",
	                                             "items": ["a", "b", "c"],
	                                             "level3": {
	                                               "description": "Third level",
	                                               "count": 42,
	                                               "level4": {
	                                                 "description": "Fourth level",
	                                                 "enabled": true,
	                                                 "level5": {
	                                                   "description": "Fifth level",
	                                                   "level6": {
	                                                     "description": "Sixth level",
	                                                     "matrix": [[1, 2, 3], [4, 5, 6], [7, 8, 9]],
	                                                     "level7": {
	                                                       "description": "Seventh level",
	                                                       "level8": {
	                                                         "description": "Eighth level",
	                                                         "level9": {
	                                                           "description": "Ninth level",
	                                                           "level10": {
	                                                             "description": "Tenth level - the deepest!",
	                                                             "value": "You found the treasure!",
	                                                             "coordinates": { "x": 42.0, "y": -73.5, "z": 0.0 }
	                                                           }
	                                                         }
	                                                       }
	                                                     }
	                                                   }
	                                                 }
	                                               }
	                                             }
	                                           }
	                                         }
	                                       }
	                                       """;

	#endregion

	#region 7. Mixed value types

	private const string _mixedTypesJson = """
	                                       {
	                                         "strings": {
	                                           "simple": "Hello, World!",
	                                           "empty": "",
	                                           "unicode": "\u65E5\u672C\u8A9E\u30C6\u30AD\u30B9\u30C8",
	                                           "url": "https://github.com/user/repo?query=test&page=1",
	                                           "long": "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
	                                         },
	                                         "numbers": {
	                                           "integer": 42,
	                                           "negative": -17,
	                                           "zero": 0,
	                                           "float": 3.14159265358979,
	                                           "scientific": 6.022e23,
	                                           "negativeFloat": -0.001,
	                                           "large": 9007199254740991
	                                         },
	                                         "booleans": {
	                                           "yes": true,
	                                           "no": false
	                                         },
	                                         "nullValue": null,
	                                         "arrays": {
	                                           "empty": [],
	                                           "numbers": [1, 2, 3, 4, 5],
	                                           "mixed": [1, "two", true, null, { "nested": "object" }, [6, 7, 8]],
	                                           "nested": [[1, 2], [3, 4], [5, 6]]
	                                         },
	                                         "objects": {
	                                           "empty": {},
	                                           "nested": {
	                                             "child": {
	                                               "grandchild": {
	                                                 "value": "deep"
	                                               }
	                                             }
	                                           }
	                                         },
	                                         "specialCases": {
	                                           "key with spaces": "value",
	                                           "key/with/slashes": "value",
	                                           "key~with~tildes": "value",
	                                           "": "empty key"
	                                         }
	                                       }
	                                       """;

	#endregion

	#region 8. API response

	private const string _apiResponseJson = """
	                                        {
	                                          "status": 200,
	                                          "message": "OK",
	                                          "pagination": {
	                                            "page": 1,
	                                            "perPage": 25,
	                                            "totalItems": 1253,
	                                            "totalPages": 51,
	                                            "hasNextPage": true,
	                                            "hasPreviousPage": false
	                                          },
	                                          "links": {
	                                            "self": "https://api.example.com/v2/products?page=1",
	                                            "next": "https://api.example.com/v2/products?page=2",
	                                            "last": "https://api.example.com/v2/products?page=51"
	                                          },
	                                          "data": [
	                                            {
	                                              "id": "prod_8Xk2mN",
	                                              "sku": "WDG-001-BLK",
	                                              "name": "Premium Widget",
	                                              "slug": "premium-widget",
	                                              "description": "A high-quality widget for professional use",
	                                              "category": { "id": "cat_3Fp", "name": "Widgets", "path": "/electronics/widgets" },
	                                              "pricing": {
	                                                "currency": "USD",
	                                                "listPrice": 49.99,
	                                                "salePrice": 39.99,
	                                                "discount": { "type": "percentage", "value": 20, "validUntil": "2025-03-01T00:00:00Z" },
	                                                "taxRate": 0.08
	                                              },
	                                              "inventory": { "inStock": true, "quantity": 342, "warehouse": "US-WEST-2", "reorderPoint": 50 },
	                                              "variants": [
	                                                { "id": "var_1", "color": "Black", "size": "M", "sku": "WDG-001-BLK-M", "additionalPrice": 0 },
	                                                { "id": "var_2", "color": "Silver", "size": "M", "sku": "WDG-001-SLV-M", "additionalPrice": 5.00 },
	                                                { "id": "var_3", "color": "Black", "size": "L", "sku": "WDG-001-BLK-L", "additionalPrice": 10.00 }
	                                              ],
	                                              "images": [
	                                                { "url": "https://cdn.example.com/products/wdg001-1.jpg", "alt": "Front view", "isPrimary": true },
	                                                { "url": "https://cdn.example.com/products/wdg001-2.jpg", "alt": "Side view", "isPrimary": false }
	                                              ],
	                                              "ratings": { "average": 4.7, "count": 238, "distribution": { "5": 180, "4": 35, "3": 15, "2": 5, "1": 3 } },
	                                              "tags": ["bestseller", "premium", "professional"],
	                                              "createdAt": "2023-06-15T08:30:00Z",
	                                              "updatedAt": "2025-01-20T14:22:00Z"
	                                            },
	                                            {
	                                              "id": "prod_9Yl3nP",
	                                              "sku": "GDG-002-WHT",
	                                              "name": "Basic Gadget",
	                                              "slug": "basic-gadget",
	                                              "description": "An affordable gadget for everyday use",
	                                              "category": { "id": "cat_7Gq", "name": "Gadgets", "path": "/electronics/gadgets" },
	                                              "pricing": {
	                                                "currency": "USD",
	                                                "listPrice": 19.99,
	                                                "salePrice": null,
	                                                "discount": null,
	                                                "taxRate": 0.08
	                                              },
	                                              "inventory": { "inStock": false, "quantity": 0, "warehouse": "US-EAST-1", "reorderPoint": 100, "backorderDate": "2025-02-15" },
	                                              "variants": [],
	                                              "images": [
	                                                { "url": "https://cdn.example.com/products/gdg002-1.jpg", "alt": "Product photo", "isPrimary": true }
	                                              ],
	                                              "ratings": { "average": 3.9, "count": 47, "distribution": { "5": 15, "4": 12, "3": 10, "2": 6, "1": 4 } },
	                                              "tags": ["budget", "everyday"],
	                                              "createdAt": "2024-03-01T12:00:00Z",
	                                              "updatedAt": "2025-01-18T09:15:00Z"
	                                            }
	                                          ],
	                                          "meta": {
	                                            "apiVersion": "2.1.0",
	                                            "requestId": "req_aB3cD4eF5g",
	                                            "responseTimeMs": 42,
	                                            "serverRegion": "us-west-2",
	                                            "rateLimit": { "limit": 1000, "remaining": 997, "resetAt": "2025-01-25T00:00:00Z" }
	                                          }
	                                        }
	                                        """;

	#endregion

	#region 6. Large array (generated at startup)

	private string _largeArrayJson = "[]";

	#endregion

	private void BuildCustomContextActions()
	{
		_customContextActions =
		[
			// Show on any string that looks like a URL
			new MokaJsonContextAction
			{
				Id = "open-url",
				Label = "Open URL",
				ShortcutHint = "Enter",
				Order = 500,
				HasSeparatorBefore = true,
				IsVisible = ctx => ctx.ValueKind == JsonValueKind.String
				                   && ctx.RawValuePreview.StartsWith("http", StringComparison.OrdinalIgnoreCase),
				OnExecute = ctx =>
				{
					_lastActionMessage = $"Would open: {ctx.RawValuePreview}";
					StateHasChanged();
					return ValueTask.CompletedTask;
				}
			},

			// Show only on properties named "email" or values containing @
			new MokaJsonContextAction
			{
				Id = "copy-email",
				Label = "Send Email",
				Order = 510,
				IsVisible = ctx => ctx.ValueKind == JsonValueKind.String
				                   && (ctx.PropertyName is "email" or "support"
				                       || ctx.RawValuePreview.Contains('@', StringComparison.Ordinal)),
				OnExecute = ctx =>
				{
					_lastActionMessage = $"Would mailto: {ctx.RawValuePreview}";
					StateHasChanged();
					return ValueTask.CompletedTask;
				}
			},

			// Show on number nodes — demonstrates type-based filtering
			new MokaJsonContextAction
			{
				Id = "format-number",
				Label = "Format as Currency",
				Order = 520,
				IsVisible = ctx => ctx.ValueKind == JsonValueKind.Number
				                   && ctx.PropertyName is "salary" or "revenue",
				OnExecute = ctx =>
				{
					if (double.TryParse(ctx.RawValuePreview, out double val))
					{
						_lastActionMessage = $"{ctx.PropertyName}: {val:C}";
					}

					StateHasChanged();
					return ValueTask.CompletedTask;
				}
			},

			// Available on every node — shows type info
			new MokaJsonContextAction
			{
				Id = "type-info",
				Label = "Show Node Info",
				Order = 900,
				HasSeparatorBefore = true,
				OnExecute = ctx =>
				{
					_lastActionMessage =
						$"Path: {ctx.Path} | Type: {ctx.ValueKind} | Property: {ctx.PropertyName ?? "(none)"} | Depth: {ctx.Depth}";
					StateHasChanged();
					return ValueTask.CompletedTask;
				}
			}
		];
	}

	protected override void OnInitialized()
	{
		// Generate the 10k array
		var sb = new StringBuilder("[");
		for (int i = 0; i < 10_000; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}

			sb.Append(i);
		}

		sb.Append(']');
		_largeArrayJson = sb.ToString();

		BuildCustomContextActions();
	}

	private async Task GenerateStressTest()
	{
		_isGenerating = true;
		_stressTestStream?.Dispose();
		_stressTestStream = null;
		_generationProgress = "0%";
		StateHasChanged();

		await Task.Yield();

		var sw = Stopwatch.StartNew();
		long targetBytes = (long)_targetSizeMb * 1024 * 1024;
		int nodeCount = 0;

		// Write to a file-backed stream for large sizes to avoid LOH pressure
		string tempPath = Path.GetTempFileName();
		var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite,
			FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.SequentialScan);

		// SkipValidation = true for performance; we control the structure
		using var writer = new Utf8JsonWriter(fileStream,
			new JsonWriterOptions { Indented = true, SkipValidation = true });

		writer.WriteStartObject();
		writer.WriteString("_description", $"Auto-generated stress test ({_targetSizeMb} MB target)");
		writer.WriteString("_generated", DateTime.UtcNow.ToString("O"));
		writer.WriteNumber("_targetSizeMb", _targetSizeMb);

		writer.WriteStartArray("departments");
		nodeCount += 3;

		int deptIndex = 0;
		var rng = new Random(42); // deterministic seed
		int lastProgress = 0;

		while (fileStream.Position < targetBytes)
		{
			writer.WriteStartObject();
			writer.WriteNumber("id", deptIndex + 1);
			writer.WriteString("name", $"Department {deptIndex + 1:D4}");
			writer.WriteString("code", $"DEPT-{deptIndex + 1:D4}");
			writer.WriteString("location",
				PickRandom(rng, "New York", "London", "Tokyo", "Berlin", "Sydney", "Toronto", "Mumbai"));
			writer.WriteString("manager", $"Manager {deptIndex + 1}");
			writer.WriteNumber("budget", Math.Round(rng.NextDouble() * 10_000_000, 2));
			writer.WriteBoolean("active", rng.NextDouble() > 0.1);
			nodeCount += 7;

			writer.WriteStartObject("metadata");
			writer.WriteString("createdAt", DateTime.UtcNow.AddDays(-rng.Next(365 * 5)).ToString("O"));
			writer.WriteString("region", PickRandom(rng, "NA", "EU", "APAC", "LATAM"));
			writer.WriteNumber("floor", rng.Next(1, 50));
			writer.WriteEndObject();
			nodeCount += 4;

			int empCount = rng.Next(20, 51);
			writer.WriteStartArray("employees");
			nodeCount++;

			for (int e = 0; e < empCount; e++)
			{
				writer.WriteStartObject();
				writer.WriteNumber("id", deptIndex * 1000 + e + 1);
				writer.WriteString("firstName",
					PickRandom(rng, "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Hank", "Ivy", "Jack",
						"Kate", "Liam", "Mia", "Noah", "Olivia", "Paul"));
				writer.WriteString("lastName",
					PickRandom(rng, "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
						"Wilson", "Moore", "Taylor", "Anderson", "Thomas", "Jackson"));
				writer.WriteString("email", $"emp{deptIndex * 1000 + e + 1}@company.com");
				writer.WriteString("role",
					PickRandom(rng, "Engineer", "Designer", "Manager", "Analyst", "Director", "VP", "Intern", "Lead",
						"Senior Engineer", "Staff Engineer", "Principal"));
				writer.WriteNumber("salary", Math.Round(50000 + rng.NextDouble() * 200000, 2));
				writer.WriteBoolean("isRemote", rng.NextDouble() > 0.6);
				writer.WriteString("startDate", DateTime.UtcNow.AddDays(-rng.Next(365 * 10)).ToString("yyyy-MM-dd"));
				nodeCount += 9;

				writer.WriteStartObject("address");
				writer.WriteString("street",
					$"{rng.Next(1, 9999)} {PickRandom(rng, "Main", "Oak", "Elm", "Pine", "Maple", "Cedar")} {PickRandom(rng, "St", "Ave", "Blvd", "Dr", "Ln")}");
				writer.WriteString("city",
					PickRandom(rng, "Springfield", "Portland", "Austin", "Denver", "Seattle", "Boston", "Miami",
						"Chicago"));
				writer.WriteString("state", PickRandom(rng, "CA", "TX", "NY", "WA", "OR", "CO", "FL", "IL", "MA"));
				writer.WriteString("zip", $"{rng.Next(10000, 99999)}");
				writer.WriteEndObject();
				nodeCount += 5;

				writer.WriteStartArray("skills");
				int skillCount = rng.Next(2, 8);
				for (int s = 0; s < skillCount; s++)
				{
					writer.WriteStringValue(PickRandom(rng, "C#", "JavaScript", "Python", "Go", "Rust", "TypeScript",
						"Java", "SQL", "React", "Blazor", "Docker", "K8s", "AWS", "Azure", "GraphQL", "REST"));
					nodeCount++;
				}

				writer.WriteEndArray();
				nodeCount++;

				writer.WriteStartArray("projects");
				int projCount = rng.Next(1, 5);
				for (int p = 0; p < projCount; p++)
				{
					writer.WriteStartObject();
					writer.WriteString("name", $"Project-{rng.Next(1000, 9999)}");
					writer.WriteString("status", PickRandom(rng, "active", "completed", "on-hold", "planning"));
					writer.WriteNumber("hoursLogged", rng.Next(10, 2000));
					writer.WriteEndObject();
					nodeCount += 4;
				}

				writer.WriteEndArray();
				nodeCount++;

				writer.WriteEndObject();

				// Flush after each employee to keep Utf8JsonWriter buffer small
				writer.Flush();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
			deptIndex++;

			// Flush after each department
			writer.Flush();

			int progress = (int)((double)fileStream.Position / targetBytes * 100);
			if (progress >= lastProgress + 5)
			{
				lastProgress = progress;
				_generationProgress = $"{Math.Min(progress, 99)}%";
				StateHasChanged();
				await Task.Yield();
			}
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();

		sw.Stop();

		// Seek to beginning so the viewer can parse from the stream
		fileStream.Seek(0, SeekOrigin.Begin);

		_stressTestStream = fileStream;
		_stressTestSize = fileStream.Length;
		_stressTestNodeCount = nodeCount;
		_stressTestGenTime = $"{sw.Elapsed.TotalSeconds:F1}s";
		_isGenerating = false;
		_generationProgress = "";
		StateHasChanged();
	}

	private static string PickRandom(Random rng, params string[] options) => options[rng.Next(options.Length)];

	private static string FormatBytes(long bytes)
	{
		return bytes switch
		{
			>= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
			>= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
			>= 1024 => $"{bytes / 1024.0:F1} KB",
			_ => $"{bytes} B"
		};
	}

	#region 9. GeoJSON

	private const string _geoJson = """
	                                {
	                                  "type": "FeatureCollection",
	                                  "features": [
	                                    {
	                                      "type": "Feature",
	                                      "geometry": { "type": "Point", "coordinates": [-73.9857, 40.7484] },
	                                      "properties": {
	                                        "name": "Empire State Building",
	                                        "city": "New York",
	                                        "height_m": 443,
	                                        "floors": 102,
	                                        "built": 1931,
	                                        "style": "Art Deco"
	                                      }
	                                    },
	                                    {
	                                      "type": "Feature",
	                                      "geometry": { "type": "Point", "coordinates": [2.2945, 48.8584] },
	                                      "properties": {
	                                        "name": "Eiffel Tower",
	                                        "city": "Paris",
	                                        "height_m": 330,
	                                        "floors": 3,
	                                        "built": 1889,
	                                        "style": "Iron lattice"
	                                      }
	                                    },
	                                    {
	                                      "type": "Feature",
	                                      "geometry": { "type": "Point", "coordinates": [139.7454, 35.6586] },
	                                      "properties": {
	                                        "name": "Tokyo Tower",
	                                        "city": "Tokyo",
	                                        "height_m": 333,
	                                        "floors": 4,
	                                        "built": 1958,
	                                        "style": "Steel lattice"
	                                      }
	                                    },
	                                    {
	                                      "type": "Feature",
	                                      "geometry": {
	                                        "type": "Polygon",
	                                        "coordinates": [
	                                          [
	                                            [-73.9876, 40.7661],
	                                            [-73.9580, 40.8006],
	                                            [-73.9499, 40.7968],
	                                            [-73.9730, 40.7644],
	                                            [-73.9876, 40.7661]
	                                          ]
	                                        ]
	                                      },
	                                      "properties": {
	                                        "name": "Central Park",
	                                        "city": "New York",
	                                        "area_acres": 843,
	                                        "established": 1857,
	                                        "type": "Urban park"
	                                      }
	                                    },
	                                    {
	                                      "type": "Feature",
	                                      "geometry": {
	                                        "type": "LineString",
	                                        "coordinates": [
	                                          [13.3777, 52.5163],
	                                          [13.3888, 52.5170],
	                                          [13.3950, 52.5205],
	                                          [13.4050, 52.5220]
	                                        ]
	                                      },
	                                      "properties": {
	                                        "name": "Unter den Linden",
	                                        "city": "Berlin",
	                                        "length_km": 1.4,
	                                        "type": "Boulevard"
	                                      }
	                                    }
	                                  ],
	                                  "crs": {
	                                    "type": "name",
	                                    "properties": {
	                                      "name": "urn:ogc:def:crs:EPSG::4326"
	                                    }
	                                  }
	                                }
	                                """;

	private const string _contextMenuDemoJson = """
	                                            {
	                                              "company": "Acme Corp",
	                                              "website": "https://acme.example.com",
	                                              "employees": [
	                                                {
	                                                  "name": "Alice Johnson",
	                                                  "email": "alice@acme.example.com",
	                                                  "role": "Engineer",
	                                                  "github": "https://github.com/alice",
	                                                  "salary": 125000,
	                                                  "active": true
	                                                },
	                                                {
	                                                  "name": "Bob Smith",
	                                                  "email": "bob@acme.example.com",
	                                                  "role": "Designer",
	                                                  "portfolio": "https://bob.design",
	                                                  "salary": 110000,
	                                                  "active": false
	                                                }
	                                              ],
	                                              "links": {
	                                                "docs": "https://docs.acme.example.com",
	                                                "api": "https://api.acme.example.com/v2",
	                                                "support": "support@acme.example.com"
	                                              },
	                                              "metrics": {
	                                                "revenue": 5200000,
	                                                "growth": 0.23,
	                                                "customers": 1250
	                                              }
	                                            }
	                                            """;

	#endregion

	#region 10. Custom context menu actions demo

	private List<MokaJsonContextAction> _customContextActions = null!;
	private string? _lastActionMessage;

	#endregion

	#region 11. Stress test state

	private string _generationProgress = "";
	private bool _isGenerating;
	private string _stressTestGenTime = "";
	private int _stressTestNodeCount;
	private long _stressTestSize;
	private Stream? _stressTestStream;
	private int _targetSizeMb = 50;

	#endregion
}
