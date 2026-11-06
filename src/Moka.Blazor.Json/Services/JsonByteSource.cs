namespace Moka.Blazor.Json.Services;

// Search and tree reads run at the same time, so implementations must allow concurrent reads.
internal interface IJsonByteSource : IDisposable
{
	long Length { get; }

	bool IsInMemory { get; }

	void Read(long offset, Span<byte> destination);

	ReadOnlyMemory<byte> GetMemory(long offset, int length);
}

internal sealed class MemoryJsonByteSource(ReadOnlyMemory<byte> bytes) : IJsonByteSource
{
	public long Length => bytes.Length;

	public bool IsInMemory => true;

	public void Read(long offset, Span<byte> destination) =>
		bytes.Span.Slice(checked((int)offset), destination.Length).CopyTo(destination);

	public ReadOnlyMemory<byte> GetMemory(long offset, int length) => bytes.Slice(checked((int)offset), length);

	public void Dispose()
	{
	}
}

internal sealed class StreamJsonByteSource(Stream stream, bool ownsStream) : IJsonByteSource
{
	private readonly Lock _gate = new();

	public long Length { get; } = stream.Length;

	public bool IsInMemory => false;

	public void Read(long offset, Span<byte> destination)
	{
		lock (_gate)
		{
			stream.Position = offset;
			stream.ReadExactly(destination);
		}
	}

	public ReadOnlyMemory<byte> GetMemory(long offset, int length)
	{
		byte[] bytes = new byte[length];
		Read(offset, bytes);
		return bytes;
	}

	public void Dispose()
	{
		if (ownsStream)
		{
			lock (_gate)
			{
				stream.Dispose();
			}
		}
	}
}
