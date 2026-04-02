using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Displays the JSON Pointer path as a clickable breadcrumb trail.
/// </summary>
public sealed partial class MokaJsonBreadcrumb : ComponentBase
{
	#region Nested Types

	private sealed class BreadcrumbSegment
	{
		public required string Label { get; init; }
		public required string DisplayLabel { get; init; }
		public required string Path { get; init; }
		public string? TypeIcon { get; init; }
	}

	#endregion

	#region Private Methods

	private Task HandleRootClick() => OnNavigate.InvokeAsync(string.Empty);

	private static bool NeedsQuoting(string propertyName)
	{
		if (propertyName.Length == 0)
		{
			return true;
		}

		foreach (char c in propertyName)
		{
			if (c is '.' or ' ' or '[' or ']' or '"' or '\'' or '/')
			{
				return true;
			}
		}

		return false;
	}

	#endregion

	#region Parameters

	/// <summary>The currently selected JSON Pointer path.</summary>
	[Parameter]
	public string? Path { get; set; }

	/// <summary>The root element's value kind for type icon display.</summary>
	[Parameter]
	public JsonValueKind RootValueKind { get; set; }

	/// <summary>Callback when a breadcrumb segment is clicked.</summary>
	[Parameter]
	public EventCallback<string> OnNavigate { get; set; }

	#endregion

	#region State Fields

	private string? _cachedPath;
	private List<BreadcrumbSegment> _cachedSegments = [];

	#endregion

	#region Computed Properties

	private string RootTypeIcon => RootValueKind switch
	{
		JsonValueKind.Object => "{}",
		JsonValueKind.Array => "[]",
		_ => ""
	};

	private List<BreadcrumbSegment> Segments
	{
		get
		{
			if (Path == _cachedPath)
			{
				return _cachedSegments;
			}

			_cachedPath = Path;

			if (string.IsNullOrEmpty(Path))
			{
				_cachedSegments = [];
				return _cachedSegments;
			}

			string[] parts = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			var segments = new List<BreadcrumbSegment>(parts.Length);
			var sb = new StringBuilder(Path.Length);

			foreach (string part in parts)
			{
				string unescaped = JsonPointerHelper.UnescapeSegment(part);
				sb.Append('/');
				sb.Append(part);

				string displayLabel;
				if (int.TryParse(unescaped, out int index))
				{
					displayLabel = $"[{index}]";
				}
				else if (NeedsQuoting(unescaped))
				{
					displayLabel = $"[\"{unescaped}\"]";
				}
				else
				{
					displayLabel = unescaped;
				}

				segments.Add(new BreadcrumbSegment
				{
					Label = unescaped,
					DisplayLabel = displayLabel,
					Path = sb.ToString(),
					TypeIcon = null
				});
			}

			_cachedSegments = segments;
			return _cachedSegments;
		}
	}

	#endregion
}
