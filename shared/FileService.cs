using Dapper;
using shared.Models;
using Microsoft.Data.Sqlite;


namespace shared.Services;

public class FileService
{
    private readonly string _storage_path;
    private readonly string _connectionString;

    public FileService(IConfiguration configuration)
    {
        _storage_path = configuration["FileSharing:StoragePath"] ?? throw new InvalidOperationException("FileSharing:StoragePath configuration is missing.");
        Directory.CreateDirectory(_storage_path);
        var dbPath = Path.Combine(_storage_path, "shared.db");
        _connectionString = $"Data Source={dbPath}";
        InitDb();
    }

    private void InitDb()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Files (
            Id TEXT PRIMARY KEY,
            OriginalName TEXT NOT NULL,
            MimeType TEXT NOT NULL,
            SizeBytes INTEGER NOT NULL,
            UploaderIp TEXT NOT NULL,
            UploadedAt TEXT NOT NULL)
            """);

    }

    public async Task<FileRecord> SaveAsync(IFormFile file, string uploaderIp)
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
            UploadedAt = DateTime.UtcNow
        };

        using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync("""
            INSERT INTO Files (Id, OriginalName, MimeType, SizeBytes, UploaderIp, UploadedAt)
            VALUES (@Id, @OriginalName, @MimeType, @SizeBytes, @UploaderIp, @UploadedAt)
            """, new
        {
            record.Id,
            record.OriginalName,
            record.MimeType,
            record.SizeBytes,
            record.UploaderIp,
            UploadedAt = record.UploadedAt.ToString("O")
        });

        return record;
    }

    public async Task<(FileRecord? Record, string? FilePath)> GetAsync(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        var record = await connection.QuerySingleOrDefaultAsync<FileRecord>("SELECT * FROM Files WHERE Id = @Id", new { Id = id });
        if (record is null)
        { 
            return (null, null);
        }

        var filePath = Path.Combine(_storage_path, id);
        return File.Exists(filePath) ? (record, filePath) : (null, null);
    }

    public async Task<IEnumerable<FileRecord>> ListAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        return await connection.QueryAsync<FileRecord>("SELECT * FROM Files ORDER BY UploadedAt DESC");
    }
}