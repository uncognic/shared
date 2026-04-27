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
            ExpiresAt TEXT)
            """);

    }

    public async Task<FileRecord> SaveAsync(IFormFile file, string uploaderIp, TimeSpan? ttl = null)
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
            ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null
        };

        using var connection = new SqliteConnection(ConnectionString);
        await connection.ExecuteAsync("""
                                      INSERT INTO Files (Id, OriginalName, MimeType, SizeBytes, UploaderIp, UploadedAt, ExpiresAt)
                                      VALUES (@Id, @OriginalName, @MimeType, @SizeBytes, @UploaderIp, @UploadedAt, @ExpiresAt)
                                      """, new
        {
            Id = record.Id,
            OriginalName = record.OriginalName,
            MimeType = record.MimeType,
            SizeBytes = record.SizeBytes,
            UploaderIp = record.UploaderIp,
            UploadedAt = record.UploadedAt.ToString("O"),
            ExpiresAt = record.ExpiresAt?.ToString("O")
        });

        return record;
    }

    public async Task<(FileRecord? Record, string? FilePath)> GetAsync(string id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        var record = await connection.QuerySingleOrDefaultAsync<FileRecord>("SELECT * FROM Files WHERE Id = @Id", new { Id = id });
        if (record is null)
        { 
            return (null, null);
        }

        if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.UtcNow)
        {
            return (null, null);
        }
        
        var filePath = Path.Combine(_storage_path, id);
        return File.Exists(filePath) ? (record, filePath) : (null, null);
    }

    public async Task<IEnumerable<FileRecord>> ListAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        return await connection.QueryAsync<FileRecord>("SELECT * FROM Files ORDER BY UploadedAt DESC");
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