using System.Buffers;

namespace Moka.Blazor.Json.Services;

internal static class JsonStreamSpooler
{
	private const int CopyBufferBytes = 256 * 1024;

	// Large documents are read at random, which an upload or a network response can't do. Keeps the copy in
	// memory under the limit, then moves to a temp file that's deleted when the returned stream closes.
	public static async Task<Stream> ToSeekableAsync(
		Stream source,
		long memoryLimit,
		long maxBytes,
		Action<long>? progress = null,
		CancellationToken cancellationToken = default)
	{
		Stream destination = new MemoryStream();
		byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
		try
		{
			long total = 0;
			while (true)
			{
				int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
				if (read == 0)
				{
					break;
				}

				total += read;
				if (total > maxBytes)
				{
					throw new InvalidOperationException(
						$"Document size exceeds the maximum allowed size ({JsonDocumentManager.FormatBytes(maxBytes)}).");
				}

				if (destination is MemoryStream memory && total > memoryLimit)
				{
					destination = CreateTempFile();
					memory.Position = 0;
					await memory.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
					await memory.DisposeAsync().ConfigureAwait(false);
				}

				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				progress?.Invoke(total);
			}

			destination.Position = 0;
			return destination;
		}
		catch
		{
			await destination.DisposeAsync().ConfigureAwait(false);
			throw;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static FileStream CreateTempFile() =>
		new(Path.Combine(Path.GetTempPath(), $"moka-json-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew,
			FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
}
