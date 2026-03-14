namespace Moka.Blazor.Json.Models;

/// <summary>
///     Event arguments raised when an error occurs in the JSON viewer.
/// </summary>
public sealed class JsonErrorEventArgs : EventArgs
{
    /// <summary>
    ///     A human-readable description of the error.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    ///     The exception that caused the error, if available.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    ///     The byte position in the JSON input where the error occurred, if applicable.
    /// </summary>
    public long? BytePosition { get; init; }

    /// <summary>
    ///     The line number in the JSON input where the error occurred, if applicable.
    /// </summary>
    public long? LineNumber { get; init; }
}