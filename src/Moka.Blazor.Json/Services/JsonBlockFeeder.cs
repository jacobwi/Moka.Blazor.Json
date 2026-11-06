using System.Buffers;

namespace Moka.Blazor.Json.Services;

// Hands a byte range to Utf8JsonReader one block at a time, keeping any token that straddles a block boundary.
internal sealed class JsonBlockFeeder : IDisposable
{
	private readonly IJsonByteSource _bytes;
	private readonly int _blockBytes;
	private readonly long _end;
	private readonly bool _zeroCopy;
	private byte[]? _buffer;

	// A prefix lets the reader start in the middle of a container: "[" or "{" followed by that container's
	// remaining children parses as normal JSON.
	public JsonBlockFeeder(IJsonByteSource bytes, long start, long end, int blockBytes, byte? prefix = null)
	{
		_bytes = bytes;
		_blockBytes = blockBytes;
		_end = end;
		_zeroCopy = bytes.IsInMemory && prefix is null;

		int length = (int)Math.Min(blockBytes, end - start);
		if (_zeroCopy)
		{
			WindowOffset = start;
			Window = bytes.GetMemory(start, length);
			return;
		}

		int prefixLength = prefix is null ? 0 : 1;
		_buffer = ArrayPool<byte>.Shared.Rent(length + prefixLength);
		if (prefix is { } value)
		{
			_buffer[0] = value;
		}

		bytes.Read(start, _buffer.AsSpan(prefixLength, length));
		WindowOffset = start - prefixLength;
		Window = _buffer.AsMemory(0, length + prefixLength);
	}

	public ReadOnlyMemory<byte> Window { get; private set; }

	// Add a reader's token index to this to get a source offset.
	public long WindowOffset { get; private set; }

	public bool IsFinal => WindowOffset + Window.Length >= _end;

	// If the reader made no progress the window grows, so a token larger than a block still fits.
	public void Advance(int consumed)
	{
		int held = Window.Length - consumed;
		WindowOffset += consumed;

		long wanted = Math.Max(_blockBytes, held * 2L);
		int length = (int)Math.Min(Math.Min(wanted, Array.MaxLength), _end - WindowOffset);

		if (_zeroCopy)
		{
			Window = _bytes.GetMemory(WindowOffset, length);
			return;
		}

		byte[] buffer = _buffer!;
		if (length > buffer.Length)
		{
			byte[] larger = ArrayPool<byte>.Shared.Rent(length);
			Buffer.BlockCopy(buffer, consumed, larger, 0, held);
			ArrayPool<byte>.Shared.Return(buffer);
			buffer = _buffer = larger;
		}
		else
		{
			Buffer.BlockCopy(buffer, consumed, buffer, 0, held);
		}

		_bytes.Read(WindowOffset + held, buffer.AsSpan(held, length - held));
		Window = buffer.AsMemory(0, length);
	}

	public void Dispose()
	{
		if (_buffer is not null)
		{
			ArrayPool<byte>.Shared.Return(_buffer);
			_buffer = null;
		}
	}
}
