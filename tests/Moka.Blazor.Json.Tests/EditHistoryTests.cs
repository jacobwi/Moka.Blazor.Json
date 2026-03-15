using Moka.Blazor.Json.Models;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class EditHistoryTests
{
	[Fact]
	public void New_History_Cannot_Undo_Or_Redo()
	{
		var history = new EditHistory();
		Assert.False(history.CanUndo);
		Assert.False(history.CanRedo);
	}

	[Fact]
	public void Single_Snapshot_Cannot_Undo()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		Assert.False(history.CanUndo);
		Assert.False(history.CanRedo);
	}

	[Fact]
	public void Two_Snapshots_Can_Undo()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		history.PushSnapshot("""{"a":2}""");
		Assert.True(history.CanUndo);
		Assert.False(history.CanRedo);
	}

	[Fact]
	public void Undo_Returns_Previous_Snapshot()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		history.PushSnapshot("""{"a":2}""");

		string? result = history.Undo();
		Assert.Equal("""{"a":1}""", result);
	}

	[Fact]
	public void Redo_Returns_Next_Snapshot()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		history.PushSnapshot("""{"a":2}""");

		history.Undo();
		string? result = history.Redo();
		Assert.Equal("""{"a":2}""", result);
	}

	[Fact]
	public void Push_After_Undo_Truncates_Redo()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		history.PushSnapshot("""{"a":2}""");
		history.PushSnapshot("""{"a":3}""");

		history.Undo(); // back to {"a":2}
		history.PushSnapshot("""{"a":4}"""); // should truncate {"a":3}

		Assert.False(history.CanRedo);
		string? result = history.Undo();
		Assert.Equal("""{"a":2}""", result);
	}

	[Fact]
	public void Undo_Returns_Null_When_Empty()
	{
		var history = new EditHistory();
		Assert.Null(history.Undo());
	}

	[Fact]
	public void Redo_Returns_Null_When_At_End()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		Assert.Null(history.Redo());
	}

	[Fact]
	public void Capacity_Is_Enforced()
	{
		var history = new EditHistory(3);
		history.PushSnapshot("1");
		history.PushSnapshot("2");
		history.PushSnapshot("3");
		history.PushSnapshot("4"); // should evict "1"

		// Can undo twice (4 -> 3 -> 2), but not to "1"
		Assert.Equal("3", history.Undo());
		Assert.Equal("2", history.Undo());
		Assert.False(history.CanUndo);
	}

	[Fact]
	public void Clear_Resets_State()
	{
		var history = new EditHistory();
		history.PushSnapshot("""{"a":1}""");
		history.PushSnapshot("""{"a":2}""");

		history.Clear();

		Assert.False(history.CanUndo);
		Assert.False(history.CanRedo);
		Assert.Null(history.Undo());
	}

	[Fact]
	public void Multiple_Undo_Redo_Cycles()
	{
		var history = new EditHistory();
		history.PushSnapshot("A");
		history.PushSnapshot("B");
		history.PushSnapshot("C");

		Assert.Equal("B", history.Undo());
		Assert.Equal("A", history.Undo());
		Assert.Equal("B", history.Redo());
		Assert.Equal("C", history.Redo());
		Assert.False(history.CanRedo);
	}
}
