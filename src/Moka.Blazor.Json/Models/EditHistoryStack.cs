namespace Moka.Blazor.Json.Models;

/// <summary>
///     Snapshot-based undo/redo history for JSON editing.
///     Stores full JSON strings, capped at a maximum number of entries and a maximum total size.
/// </summary>
/// <param name="maxSnapshots">The most snapshots to keep.</param>
/// <param name="maxTotalChars">
///     The most characters to keep across all snapshots. Fifty copies of a large document would otherwise
///     hold several GB for every open viewer. The current snapshot and the one before it are always kept.
/// </param>
public sealed class EditHistory(int maxSnapshots = 50, long maxTotalChars = 256 * 1024 * 1024)
{
	private readonly List<string> _snapshots = [];
	private long _totalChars;
	private int _currentIndex = -1;

	public bool CanUndo => _currentIndex > 0;
	public bool CanRedo => _currentIndex < _snapshots.Count - 1;

	public void PushSnapshot(string json)
	{
		ArgumentNullException.ThrowIfNull(json);

		// Truncate any redo entries beyond current position
		if (_currentIndex < _snapshots.Count - 1)
		{
			for (int i = _currentIndex + 1; i < _snapshots.Count; i++)
			{
				_totalChars -= _snapshots[i].Length;
			}

			_snapshots.RemoveRange(_currentIndex + 1, _snapshots.Count - _currentIndex - 1);
		}

		_snapshots.Add(json);
		_totalChars += json.Length;
		_currentIndex = _snapshots.Count - 1;

		while (_snapshots.Count > 2 && (_snapshots.Count > maxSnapshots || _totalChars > maxTotalChars))
		{
			_totalChars -= _snapshots[0].Length;
			_snapshots.RemoveAt(0);
			_currentIndex--;
		}
	}

	public string? Undo()
	{
		if (!CanUndo)
		{
			return null;
		}

		_currentIndex--;
		return _snapshots[_currentIndex];
	}

	public string? Redo()
	{
		if (!CanRedo)
		{
			return null;
		}

		_currentIndex++;
		return _snapshots[_currentIndex];
	}

	public void Clear()
	{
		_snapshots.Clear();
		_totalChars = 0;
		_currentIndex = -1;
	}
}
