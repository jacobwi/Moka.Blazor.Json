using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Displays the JSON Pointer path as a clickable breadcrumb trail.
/// </summary>
public sealed partial class MokaJsonBreadcrumb : ComponentBase
{
    #region Private Methods

    private static bool NeedsQuoting(string propertyName)
    {
        if (propertyName.Length == 0) return true;
        foreach (var c in propertyName)
            if (c is '.' or ' ' or '[' or ']' or '"' or '\'' or '/')
                return true;
        return false;
    }

    #endregion

    #region Nested Types

    private sealed class BreadcrumbSegment
    {
        public required string Label { get; init; }
        public required string DisplayLabel { get; init; }
        public required string Path { get; init; }
        public string? TypeIcon { get; init; }
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
            if (Path == _cachedPath) return _cachedSegments;
            _cachedPath = Path;

            if (string.IsNullOrEmpty(Path))
            {
                _cachedSegments = [];
                return _cachedSegments;
            }

            var parts = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var segments = new List<BreadcrumbSegment>(parts.Length);
            var sb = new StringBuilder(Path.Length);

            foreach (var part in parts)
            {
                var unescaped = part.Replace("~1", "/").Replace("~0", "~");
                sb.Append('/');
                sb.Append(part);

                string displayLabel;
                if (int.TryParse(unescaped, out var index))
                    displayLabel = $"[{index}]";
                else if (NeedsQuoting(unescaped))
                    displayLabel = $"[\"{unescaped}\"]";
                else
                    displayLabel = unescaped;

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