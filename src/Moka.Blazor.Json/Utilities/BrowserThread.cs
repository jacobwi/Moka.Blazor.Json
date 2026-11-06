namespace Moka.Blazor.Json.Utilities;

internal static class BrowserThread
{
	// Browser WebAssembly has one thread, so Task.Run doesn't take work off the UI there, and Task.Yield doesn't
	// give the browser a turn to paint. Long loops call this between blocks. Elsewhere it does nothing.
	public static Task YieldAsync() => OperatingSystem.IsBrowser() ? Task.Delay(1) : Task.CompletedTask;
}
