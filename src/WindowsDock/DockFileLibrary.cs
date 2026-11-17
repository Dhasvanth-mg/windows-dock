using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WindowsDock;

internal sealed record LibraryFile(string Name, string Text, string Tags, string Analysis, bool Image, long Size);
internal sealed record LibraryResult(Guid Id, string Name, string Snippet);
internal sealed record LibraryPreview(LibraryFile File, byte[]? Image);

internal static class DockFileLibrary
{
    internal const int MaxUploadBytes = 32 * 1024 * 1024;
    private const int PrefixLength = 12;
    internal static string DatabasePath => DockData.FilePath("file-library.db");
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".html", ".log", ".cs", ".py", ".ts", ".tsx",
        ".js", ".jsx", ".css", ".sql", ".c", ".cpp", ".h", ".ps1", ".java", ".rs", ".go", ".docx", ".pdf", ".xlsx"
    };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    internal static async Task<bool> UploadAsync(string path, CancellationToken token)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        var image = Images.Contains(extension);
        if (!image && !Documents.Contains(extension))
            throw new ArgumentException("Choose a supported image, text file, Word document, PDF, or .xlsx spreadsheet.");

        byte[] bytes;
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
        {
            if (source.Length is < 1 or > MaxUploadBytes)
                throw new ArgumentException("Choose a file between 1 byte and 32 MB.");
            bytes = new byte[(int)source.Length];
            try { await source.ReadExactlyAsync(bytes, token).ConfigureAwait(false); }
            catch { CryptographicOperations.ZeroMemory(bytes); throw; }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            using var library = LibraryDatabase.Open();
            var fingerprint = library.Digest(SHA256.HashData(bytes));
            using (var exists = library.Command("SELECT 1 FROM uploads WHERE fingerprint = $hash", ("$hash", fingerprint)))
                if (exists.ExecuteScalar() != null)
                    return false;

            var analysis = image
                ? await LibraryImageAnalysis.ReadAsync(bytes, token).ConfigureAwait(false)
                : AnalyzeDocument(name, bytes, token);
            token.ThrowIfCancellationRequested();
            var file = new LibraryFile(name, analysis.Text, analysis.Tags, analysis.Summary, image, bytes.Length);
            var id = Guid.NewGuid().ToString("N");
            var metadata = library.EncryptMetadata(id, file);
            var payload = library.Encrypt(id + ":file", bytes);
            using var transaction = library.Connection.BeginTransaction();
            using (var insert = library.Command("""
                INSERT INTO uploads(id, fingerprint, metadata, payload)
                VALUES ($id, $hash, $metadata, $payload) ON CONFLICT(fingerprint) DO NOTHING
                """, ("$id", id), ("$hash", fingerprint), ("$metadata", metadata), ("$payload", payload)))
            {
                insert.Transaction = transaction;
                if (insert.ExecuteNonQuery() == 0)
                    return false;
            }
            using var index = library.Command("INSERT OR IGNORE INTO terms(term, upload) VALUES ($term, $upload)", ("$upload", id));
            index.Transaction = transaction;
            var term = index.Parameters.Add("$term", SqliteType.Blob);
            index.Prepare();
            foreach (var word in Words(Searchable(file)).Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                for (var length = 1; length <= Math.Min(PrefixLength, word.Length); length++)
                {
                    if (char.IsHighSurrogate(word[length - 1]))
                        continue;
                    term.Value = library.Term(word[..length]);
                    index.ExecuteNonQuery();
                }
            }
            token.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static LibraryResult[] Search(string query, CancellationToken token)
    {
        if (!File.Exists(DatabasePath))
            return [];
        var words = Words(query).Distinct(StringComparer.Ordinal).Take(16).ToArray();
        if (words.Length == 0 && !string.IsNullOrWhiteSpace(query))
            return [];
        using var library = LibraryDatabase.Open();
        using var lookup = library.Command("SELECT id, metadata FROM uploads ORDER BY rowid DESC LIMIT 30");
        if (words.Length > 0)
        {
            var keys = words.Select(Prefix).Distinct(StringComparer.Ordinal).ToArray();
            var names = Enumerable.Range(0, keys.Length).Select(index => "$t" + index).ToArray();
            for (var index = 0; index < keys.Length; index++)
                lookup.Parameters.AddWithValue(names[index], library.Term(keys[index]));
            lookup.Parameters.AddWithValue("$count", keys.Length);
            lookup.CommandText = $"""
                SELECT id, metadata FROM uploads WHERE id IN (
                    SELECT upload FROM terms WHERE term IN ({string.Join(", ", names)})
                    GROUP BY upload HAVING count(*) = $count
                ) ORDER BY rowid DESC
                """;
        }
        using var rows = lookup.ExecuteReader();
        var results = new List<LibraryResult>();
        while (results.Count < 30 && rows.Read())
        {
            token.ThrowIfCancellationRequested();
            var id = rows.GetString(0);
            var file = library.ReadMetadata(id, (byte[])rows.GetValue(1));
            if (words.Length > 0 && words.Any(term => term.Length > PrefixLength))
            {
                var indexed = Words(Searchable(file)).ToArray();
                if (words.Any(term => !indexed.Any(word => word.StartsWith(term, StringComparison.Ordinal))))
                    continue;
            }
            var body = string.Join("\n", new[] { file.Tags, file.Text }.Where(part => part.Length > 0));
            var start = words.Select(word => body.IndexOf(word, StringComparison.OrdinalIgnoreCase))
                .Where(offset => offset >= 0).DefaultIfEmpty(0).Min();
            start = Math.Max(0, start - 45);
            if (start > 0 && char.IsLowSurrogate(body[start]))
                start--;
            var excerpt = IndexedDocumentText.Crop(body[start..], 260).Replace('\n', ' ').Trim();
            results.Add(new(Guid.ParseExact(id, "N"), file.Name, excerpt.Length == 0 ? file.Analysis : excerpt));
        }
        return results.ToArray();
    }

    internal static LibraryPreview? Preview(Guid upload)
    {
        if (!File.Exists(DatabasePath))
            return null;
        using var library = LibraryDatabase.Open();
        var id = upload.ToString("N");
        using var command = library.Command("SELECT metadata FROM uploads WHERE id = $id", ("$id", id));
        if (command.ExecuteScalar() is not byte[] metadata)
            return null;
        var file = library.ReadMetadata(id, metadata);
        if (!file.Image)
            return new(file, null);
        command.CommandText = "SELECT payload FROM uploads WHERE id = $id";
        var payload = command.ExecuteScalar() as byte[];
        return new(file, payload == null ? null : library.Decrypt(id + ":file", payload));
    }

    internal static void Remove(Guid upload)
    {
        if (!File.Exists(DatabasePath))
            return;
        using var library = LibraryDatabase.Open();
        using var command = library.Command("DELETE FROM uploads WHERE id = $id", ("$id", upload.ToString("N")));
        command.ExecuteNonQuery();
    }

    private static LibraryAnalysis AnalyzeDocument(string name, byte[] data, CancellationToken token)
    {
        try
        {
            var text = IndexedDocumentText.Read(name, data, token);
            return new(text, "", text.Length == 0 ? "No text could be extracted." :
                text.Length >= IndexedDocumentText.TextLimit - 1 ? "Text indexed up to the extraction limit." : "Document text indexed");
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException or System.Xml.XmlException or
            InvalidOperationException or NotSupportedException or UglyToad.PdfPig.Core.PdfDocumentFormatException or
            UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
        { return new("", "", "The file is saved. Its text could not be extracted."); }
    }

    private static string Searchable(LibraryFile file) => $"{file.Name}\n{file.Text}\n{file.Tags}";

    private static string Prefix(string word) => IndexedDocumentText.Crop(word, PrefixLength);

    private static IEnumerable<string> Words(string input)
    {
        var word = new StringBuilder();
        foreach (var rune in input.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark)
                word.Append(Rune.ToLowerInvariant(rune).ToString());
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }
        if (word.Length > 0)
            yield return word.ToString();
    }

    private sealed class LibraryDatabase(SqliteConnection connection, byte[] keys) : IDisposable
    {
        private const int TagSize = 16;
        private const int NonceSize = 12;
        private const int Header = TagSize + NonceSize;
        internal SqliteConnection Connection { get; } = connection;

        internal static LibraryDatabase Open()
        {
            var path = DatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false, DefaultTimeout = 5, ForeignKeys = true }.ConnectionString);
            byte[]? keys = null;
            try
            {
                connection.Open();
                using var init = connection.CreateCommand();
                init.CommandText = """
                    PRAGMA secure_delete = ON;
                    CREATE TABLE IF NOT EXISTS library_key(id INTEGER PRIMARY KEY CHECK(id = 1), protected BLOB NOT NULL);
                    CREATE TABLE IF NOT EXISTS uploads(
                        id TEXT NOT NULL UNIQUE,
                        fingerprint BLOB NOT NULL UNIQUE,
                        metadata BLOB NOT NULL,
                        payload BLOB NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS terms(
                        term BLOB NOT NULL,
                        upload TEXT NOT NULL REFERENCES uploads(id) ON DELETE CASCADE,
                        PRIMARY KEY(term, upload)
                    ) WITHOUT ROWID;
                    CREATE INDEX IF NOT EXISTS terms_upload ON terms(upload);
                    """;
                init.ExecuteNonQuery();
                using var read = connection.CreateCommand();
                read.CommandText = "SELECT protected FROM library_key WHERE id = 1";
                if (read.ExecuteScalar() is not byte[] sealedKey)
                {
                    using var transaction = connection.BeginTransaction();
                    read.Transaction = transaction;
                    if (read.ExecuteScalar() is not byte[] saved)
                    {
                        keys = RandomNumberGenerator.GetBytes(64);
                        sealedKey = ProtectedData.Protect(keys, "WindowsDock.FileLibrary.v1"u8.ToArray(), DataProtectionScope.CurrentUser);
                        using var write = connection.CreateCommand();
                        write.Transaction = transaction;
                        write.CommandText = "INSERT INTO library_key(id, protected) VALUES (1, $key)";
                        write.Parameters.AddWithValue("$key", sealedKey);
                        write.ExecuteNonQuery();
                    }
                    else
                        sealedKey = saved;
                    transaction.Commit();
                }
                keys ??= ProtectedData.Unprotect(sealedKey, "WindowsDock.FileLibrary.v1"u8.ToArray(), DataProtectionScope.CurrentUser);
                if (keys.Length != 64)
                    throw new CryptographicException("The upload library could not be opened.");
                return new(connection, keys);
            }
            catch
            {
                if (keys != null)
                    CryptographicOperations.ZeroMemory(keys);
                connection.Dispose();
                throw;
            }
        }

        internal SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = Connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return command;
        }

        internal byte[] Digest(byte[] hash) => HMACSHA256.HashData(keys.AsSpan(32, 32), hash);

        internal byte[] Term(string prefix) => HMACSHA256.HashData(keys.AsSpan(32, 32), Encoding.UTF8.GetBytes("term:" + prefix));

        internal byte[] EncryptMetadata(string id, LibraryFile file)
        {
            var clear = JsonSerializer.SerializeToUtf8Bytes(file);
            try { return Encrypt(id + ":metadata", clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }

        internal LibraryFile ReadMetadata(string id, byte[] encrypted)
        {
            var clear = Decrypt(id + ":metadata", encrypted);
            try { return JsonSerializer.Deserialize<LibraryFile>(clear) ?? throw new InvalidDataException("A library entry is unreadable."); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }

        internal byte[] Encrypt(string context, byte[] clear)
        {
            var encoded = new byte[clear.Length + Header];
            RandomNumberGenerator.Fill(encoded.AsSpan(0, NonceSize));
            using var cipher = new AesGcm(keys.AsSpan(0, 32), TagSize);
            cipher.Encrypt(encoded.AsSpan(0, NonceSize), clear, encoded.AsSpan(Header), encoded.AsSpan(NonceSize, TagSize),
                Encoding.UTF8.GetBytes(context));
            return encoded;
        }

        internal byte[] Decrypt(string context, byte[] encrypted)
        {
            if (encrypted.Length < Header)
                throw new CryptographicException("A library entry is unreadable.");
            var clear = new byte[encrypted.Length - Header];
            using var cipher = new AesGcm(keys.AsSpan(0, 32), TagSize);
            try
            {
                cipher.Decrypt(encrypted.AsSpan(0, NonceSize), encrypted.AsSpan(Header),
                    encrypted.AsSpan(NonceSize, TagSize), clear, Encoding.UTF8.GetBytes(context));
                return clear;
            }
            catch { CryptographicOperations.ZeroMemory(clear); throw; }
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(keys);
            Connection.Dispose();
        }
    }
}
