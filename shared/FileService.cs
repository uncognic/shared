using Dapper;
using shared.Models;
using Microsoft.Data.Sqlite;


namespace shared.Services;

public class FileService
{
    private readonly string _storage_path;
    public string ConnectionString { get; private set; } = string.Empty;


    public FileService(IConfiguration configuration)
    {
        _storage_path = configuration["FileSharing:StoragePath"] ?? throw new InvalidOperationException("FileSharing:StoragePath configuration is missing.");
        Directory.CreateDirectory(_storage_path);
        var dbPath = Path.Combine(_storage_path, "shared.db");
        ConnectionString = $"Data Source={dbPath}";
        InitDb();
    }

    private void InitDb()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Files (
            Id TEXT PRIMARY KEY,
            OriginalName TEXT NOT NULL,
            MimeType TEXT NOT NULL,
            SizeBytes INTEGER NOT NULL,
            UploaderIp TEXT NOT NULL,
            UploadedAt TEXT NOT NULL,
            ExpiresAt TEXT,
            TokenLabel TEXT NOT NULL DEFAULT '')
            """);

        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Downloads (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FileId TEXT NOT NULL,
            Ip TEXT NOT NULL,
            DownloadedAt TEXT NOT NULL,
            FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE)
            """);

    }

    public async Task<FileRecord> SaveAsync(IFormFile file, string uploaderIp, string tokenLabel, TimeSpan? ttl = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var destPath = Path.Combine(_storage_path, id);

        await using (var dest = File.Create(destPath))
        {
            await file.CopyToAsync(dest);
        }

        var record = new FileRecord
        {
            Id = id,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            UploaderIp = uploaderIp,
            UploadedAt = DateTime.UtcNow,
            ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null,
            TokenLabel = tokenLabel,
        };

        using var connection = new SqliteConnection(ConnectionString);
        await connection.ExecuteAsync("""
                                      INSERT INTO Files (Id, OriginalName, MimeType, SizeBytes, UploaderIp, UploadedAt, ExpiresAt, TokenLabel)
                                      VALUES (@Id, @OriginalName, @MimeType, @SizeBytes, @UploaderIp, @UploadedAt, @ExpiresAt, @TokenLabel)
                                      """, new
        {
            Id = record.Id,
            OriginalName = record.OriginalName,
            MimeType = record.MimeType,
            SizeBytes = record.SizeBytes,
            UploaderIp = record.UploaderIp,
            UploadedAt = record.UploadedAt.ToString("O"),
            ExpiresAt = record.ExpiresAt?.ToString("O"),
            TokenLabel = record.TokenLabel
        });

        return record;
    }

    public async Task<(FileRecord? Record, string? FilePath)> GetAsync(string id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        var record = await connection.QuerySingleOrDefaultAsync<FileRecord>("SELECT * FROM Files WHERE Id = @Id", new { Id = id });
        if (record is null)
            return (null, null);

        if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.UtcNow)
            return (null, null);

        var filePath = Path.Combine(_storage_path, id);
        return File.Exists(filePath) ? (record, filePath) : (null, null);
    }

    public async Task<FileRecord?> GetInfoAsync(string id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        var record = await connection.QuerySingleOrDefaultAsync<FileRecord>("SELECT * FROM Files WHERE Id = @Id", new { Id = id });
        if (record is null)
            return null;

        record.Downloads = (await connection.QueryAsync<Download>(
            "SELECT Ip, DownloadedAt FROM Downloads WHERE FileId = @Id ORDER BY DownloadedAt DESC",
            new { Id = id })).ToList();
        record.DownloadCount = record.Downloads.Count;

        return record;
    }

    public async Task<IEnumerable<FileRecord>> ListAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        var files = (await connection.QueryAsync<FileRecord>("""
            SELECT f.*, COUNT(d.Id) AS DownloadCount
            FROM Files f LEFT JOIN Downloads d ON f.Id = d.FileId
            GROUP BY f.Id
            ORDER BY f.UploadedAt DESC
            """)).ToList();

        return files;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        var record = await conn.QuerySingleOrDefaultAsync<FileRecord>(
            "SELECT * FROM Files WHERE Id = @Id", new { Id = id });

        if (record is null)
            return false;

        var filePath = Path.Combine(_storage_path, id);
        if (File.Exists(filePath))
            File.Delete(filePath);

        await conn.ExecuteAsync("DELETE FROM Files WHERE Id = @Id", new { Id = id });
        return true;
    }

    public async Task<object> GetStatsAsync()
    {
        using var conn = new SqliteConnection(ConnectionString);
        var totalFiles = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Files");
        var totalBytes = await conn.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(SizeBytes), 0) FROM Files");
        var totalDownloads = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Downloads");
        var filesByToken = await conn.QueryAsync("SELECT TokenLabel, COUNT(*) AS FileCount, SUM(SizeBytes) AS TotalBytes FROM Files GROUP BY TokenLabel");
        return new
        {
            TotalFiles = totalFiles,
            TotalBytes = totalBytes,
            TotalDownloads = totalDownloads,
            ByToken = filesByToken
        };
    }

    public async Task RecordDownloadAsync(string fileId, string ip)
    {
        using var conn = new SqliteConnection(ConnectionString);
        await conn.ExecuteAsync("""
            INSERT INTO Downloads (FileId, Ip, DownloadedAt)
            VALUES (@FileId, @Ip, @DownloadedAt)
            """, new { FileId = fileId, Ip = ip, DownloadedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task<int> DeleteExpiredAsync()
    {
        using var conn = new SqliteConnection(ConnectionString);
        var expired = await conn.QueryAsync<FileRecord>(
            "SELECT * FROM Files WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= @Now",
            new { Now = DateTime.UtcNow.ToString("O") });

        int count = 0;
        foreach (var record in expired)
        {
            var filePath = Path.Combine(_storage_path, record.Id);
            if (File.Exists(filePath))
                File.Delete(filePath);

            await conn.ExecuteAsync("DELETE FROM Files WHERE Id = @Id", new { record.Id });
            count++;
        }
        return count;
    }
}