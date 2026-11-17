using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace WindowsDock;

internal sealed record SearchDocument(string Path, string Name, string Snippet);
internal sealed record FileScanResult(int Count, int Unreadable, bool Limited);

internal static class DockFileSearch
{
    private const int MaxFiles = 3000;
    private const int MaxVisited = 25_000;
    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".html", ".log", ".cs", ".py", ".ts", ".tsx",
        ".js", ".jsx", ".css", ".sql", ".c", ".cpp", ".h", ".ps1", ".java", ".rs", ".go", ".docx", ".pdf", ".xlsx"
    };
    private static readonly HashSet<string> NameTypes = new(StringComparer.OrdinalIgnoreCase)
    { ".doc", ".xls", ".pptx", ".png", ".jpg", ".jpeg", ".webp", ".wav", ".mp3" };

    private static SqliteConnection Open()
    {
        var path = DockData.FilePath("file-search.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var initializing = !File.Exists(path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false, DefaultTimeout = 3 }.ConnectionString);
        try
        {
            connection.Open();
            using var init = Command(connection, """
                PRAGMA secure_delete = ON;
                CREATE TABLE IF NOT EXISTS folders(path TEXT PRIMARY KEY COLLATE NOCASE);
                CREATE TABLE IF NOT EXISTS files(path TEXT UNIQUE COLLATE NOCASE, folder TEXT COLLATE NOCASE,
                    stamp INTEGER, size INTEGER);
                CREATE INDEX IF NOT EXISTS idx_files_folder ON files(folder);
                CREATE VIRTUAL TABLE IF NOT EXISTS document_text USING fts5(name, content);
                """);
            init.ExecuteNonQuery();
            if (initializing)
            {
                using var secure = Command(connection, "INSERT INTO document_text(document_text, rank) VALUES ('secure-delete', 1)");
                secure.ExecuteNonQuery();
            }
            using var version = Command(connection, "PRAGMA user_version");
            if (Convert.ToInt64(version.ExecuteScalar()) < 1)
            {
                using var migration = connection.BeginTransaction();
                version.Transaction = migration;
                if (Convert.ToInt64(version.ExecuteScalar()) < 1)
                {
                    using var upgrade = Command(connection, """
                        UPDATE files SET stamp = -1 WHERE lower(path) LIKE '%.pdf' OR lower(path) LIKE '%.xlsx';
                        PRAGMA user_version = 1;
                        """);
                    upgrade.Transaction = migration;
                    upgrade.ExecuteNonQuery();
                }
                migration.Commit();
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static string[] Folders()
    {
        if (!File.Exists(DockData.FilePath("file-search.db")))
            return [];
        using var connection = Open();
        return Folders(connection);
    }

    private static string[] Folders(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var cmd = Command(connection, "SELECT path FROM folders ORDER BY path COLLATE NOCASE");
        cmd.Transaction = transaction;
        using var rows = cmd.ExecuteReader();
        var folders = new List<string>();
        while (rows.Read())
            folders.Add(rows.GetString(0));
        return folders.ToArray();
    }

    internal static string AddFolder(string input)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        ValidateFolder(path);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var folders = Folders(connection, transaction);
        if (folders.Any(folder => Contains(folder, path) || Contains(path, folder)))
            throw new ArgumentException("This folder or an overlapping folder is already included.");
        if (folders.Length >= 8)
            throw new ArgumentException("Choose up to eight folders.");
        using var add = Command(connection, "INSERT INTO folders(path) VALUES ($path)", ("$path", path));
        add.Transaction = transaction;
        add.ExecuteNonQuery();
        transaction.Commit();
        return path;
    }

    internal static void RemoveFolder(string folder)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var delete = Command(connection, """
            INSERT INTO document_text(document_text, rank) VALUES ('secure-delete', 1);
            DELETE FROM document_text WHERE rowid IN (SELECT rowid FROM files WHERE folder = $folder);
            DELETE FROM files WHERE folder = $folder;
            DELETE FROM folders WHERE path = $folder;
            """, ("$folder", folder));
        delete.Transaction = transaction;
        delete.ExecuteNonQuery();
        transaction.Commit();
    }

    internal static FileScanResult Update(string folder, CancellationToken cancellation)
    {
        ValidateFolder(folder);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (!Folders(connection, transaction).Contains(folder, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Add this folder before indexing it.");
        var existing = new Dictionary<string, (long Id, long Stamp, long Size)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = Command(connection, "SELECT rowid, path, stamp, size FROM files WHERE folder = $folder", ("$folder", folder)))
        {
            cmd.Transaction = transaction;
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
                existing.Add(rows.GetString(1), (rows.GetInt64(0), rows.GetInt64(2), rows.GetInt64(3)));
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
            MaxRecursionDepth = 24
        };
        var unreadable = 0;
        var count = 0;
        var visited = 0;
        var limited = false;
        foreach (var path in Directory.EnumerateFiles(folder, "*", enumeration))
        {
            cancellation.ThrowIfCancellationRequested();
            if (++visited > MaxVisited)
            {
                limited = true;
                break;
            }
            var ext = Path.GetExtension(path);
            var text = TextTypes.Contains(ext);
            if (!text && !NameTypes.Contains(ext))
                continue;
            if (count == MaxFiles)
            {
                limited = true;
                break;
            }
            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (existing.Remove(path, out var previous) && previous.Stamp == info.LastWriteTimeUtc.Ticks &&
                    previous.Size == info.Length)
                {
                    count++;
                    continue;
                }
                var content = "";
                if (text && info.Length is > 0 and <= 4_194_304)
                {
                    try { content = IndexedDocumentText.Read(path, cancellation); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                        InvalidDataException or System.Xml.XmlException or InvalidOperationException or NotSupportedException or
                        UglyToad.PdfPig.Core.PdfDocumentFormatException or UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
                    { unreadable++; }
                }
                if (previous.Id != 0)
                    DeleteFile(connection, transaction, previous.Id);
                using var store = Command(connection, """
                    INSERT INTO files(path, folder, stamp, size) VALUES ($path, $folder, $stamp, $size);
                    INSERT INTO document_text(rowid, name, content) VALUES (last_insert_rowid(), $name, $content);
                    """, ("$path", path), ("$folder", folder), ("$stamp", info.LastWriteTimeUtc.Ticks),
                    ("$size", info.Length), ("$name", info.Name), ("$content", content));
                store.Transaction = transaction;
                store.ExecuteNonQuery();
                count++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { unreadable++; }
        }
        foreach (var leftover in existing)
        {
            cancellation.ThrowIfCancellationRequested();
            // Results omitted by a scan limit belong to the previous snapshot. Drop them to keep each index bounded.
            DeleteFile(connection, transaction, leftover.Value.Id);
        }
        transaction.Commit();
        return new(count, unreadable, limited);
    }

    internal static SearchDocument[] Search(string query)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{M}\p{Nd}_]+").Select(match => match.Value).Take(12).ToArray();
        if (terms.Length == 0 || !File.Exists(DockData.FilePath("file-search.db")))
            return [];
        var match = string.Join(" AND ", terms.Select(term => $"\"{term}\"*"));
        using var connection = Open();
        using var search = Command(connection, """
            SELECT files.path, document_text.name, snippet(document_text, 1, '', '', '…', 18)
            FROM document_text JOIN files ON files.rowid = document_text.rowid
            WHERE document_text MATCH $match ORDER BY rank LIMIT 30
            """, ("$match", match));
        using var rows = search.ExecuteReader();
        var result = new List<SearchDocument>();
        while (rows.Read())
            result.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2)));
        return result.ToArray();
    }

    internal static string ExcerptText(IEnumerable<SearchDocument> results) => string.Join("\n\n", results.Take(5).Select(result =>
    {
        var length = Math.Min(result.Snippet.Length, 1300);
        if (length > 0 && char.IsHighSurrogate(result.Snippet[length - 1]))
            length--;
        return $"{result.Name}\n{result.Snippet[..length]}";
    }));

    private static void ValidateFolder(string path)
    {
        if (!Directory.Exists(path))
            throw new IOException("The selected folder is unavailable.");
        for (var folder = new DirectoryInfo(path); folder != null; folder = folder.Parent)
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Choose a physical folder. Linked directories are skipped.");
    }

    private static bool Contains(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static void DeleteFile(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using var delete = Command(connection, """
            DELETE FROM document_text WHERE rowid = $id;
            DELETE FROM files WHERE rowid = $id;
            """, ("$id", id));
        delete.Transaction = transaction;
        delete.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}
