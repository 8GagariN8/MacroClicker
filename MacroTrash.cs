using System.Globalization;

namespace MacroClicker;

internal sealed record DeletedMacro(string Id, string Name, DateTimeOffset? DeletedAt, DateTimeOffset? ExpiresAt);

internal static class MacroTrash
{
    public const int RetentionDays = 30;
    internal const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";

    private static string Folder(string libraryPath)
    {
        var folder = Path.Combine(libraryPath, "Deleted");
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Папка корзины не должна быть символической ссылкой.");
        return folder;
    }
    private static string FilePath(string libraryPath, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['/', '\\', ':']) >= 0 ||
            Path.GetFileName(id) != id || !id.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Некорректный идентификатор корзины.");
        var path = Path.Combine(Folder(libraryPath), id);
        if (!File.Exists(path)) throw new FileNotFoundException("Макрос уже удалён или восстановлен.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Файл корзины не должен быть символической ссылкой.");
        return path;
    }
    public static List<DeletedMacro> List(string libraryPath)
    {
        var folder = Folder(libraryPath);
        if (!Directory.Exists(folder)) return [];
        var result = new List<DeletedMacro>();
        foreach (var path in Directory.GetFiles(folder, "*.json").Order())
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            var id = Path.GetFileName(path);
            DateTimeOffset? deleted = DateTimeOffset.TryParseExact(id.Split('_')[0], TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) ? parsed : null;
            var name = id;
            try { name = MacroStorage.Read(path).Name; }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
            { /* A damaged macro must still be visible and removable from the trash. */ }
            result.Add(new(id, name, deleted, deleted?.AddDays(RetentionDays)));
        }
        return result;
    }
    public static string Restore(string libraryPath, string id)
    {
        var source = FilePath(libraryPath, id);
        MacroStorage.Read(source); // Do not restore corrupt JSON into the library.
        var destination = Path.Combine(libraryPath, Guid.NewGuid() + ".json");
        File.Move(source, destination);
        return destination;
    }
    public static void DeletePermanently(string libraryPath, string id) => File.Delete(FilePath(libraryPath, id));
    public static int PurgeExpired(string libraryPath, DateTimeOffset now, Action<Exception>? onError = null)
    {
        var count = 0;
        foreach (var item in List(libraryPath).Where(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now))
            try { DeletePermanently(libraryPath, item.Id); count++; }
            catch (Exception error) when (onError is not null) { onError(error); }
        return count;
    }
}
