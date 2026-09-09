namespace MacroClicker;

// A fixed, app-owned origin. Its response is supplied from the embedded resource,
// never from a server. Keep request, navigation and native-message checks aligned.
internal sealed class WebContentPolicy
{
    public const string PageUrl = "https://macroclicker.invalid/index.html";
    private ulong? _navigationId;

    public static bool IsPage(string? uri) => string.Equals(uri, PageUrl, StringComparison.Ordinal);
    public static bool CanServe(string? uri, string? method) => IsPage(uri) && method == "GET";

    public bool TryStart(string? uri, ulong navigationId, bool redirected)
    {
        if (!IsPage(uri) || redirected || (_navigationId.HasValue && _navigationId != navigationId)) return false;
        _navigationId = navigationId;
        return true;
    }

    public bool IsPageNavigation(ulong navigationId) => _navigationId == navigationId;
}
