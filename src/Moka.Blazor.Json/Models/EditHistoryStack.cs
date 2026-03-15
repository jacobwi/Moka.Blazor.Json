namespace Moka.Blazor.Json.Models;

/// <summary>
///     Snapshot-based undo/redo history for JSON editing.
///     Stores full JSON strings, capped at a maximum number of entries.
/// </summary>
public sealed class EditHistory
{
	private readonly int _maxSnapshots;
	private readonly List<string> _snapshots = new();
	private int _currentIndex = -1;

	public EditHistory(int maxSnapshots = 50)
	{
		_maxSnapshots = maxSnapshots;
	}

	public bool CanUndo => _currentIndex > 0;
	public bool CanRedo => _currentIndex < _snapshots.Count - 1;

	public void PushSnapshot(string json)
	{
		// Truncate any redo entries beyond current position
		if (_currentIndex < _snapshots.Count - 1)
		{
			_snapshots.RemoveRange(_currentIndex + 1, _snapshots.Count - _currentIndex - 1);
		}

		_snapshots.Add(json);
		_currentIndex = _snapshots.Count - 1;

		// Evict oldest if over capacity
		if (_snapshots.Count > _maxSnapshots)
		{
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
		_currentIndex = -1;
	}
}
