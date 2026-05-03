using Dapper;
using Microsoft.Data.Sqlite;

namespace shared.Services;

public class BlacklistService
{
    private readonly string _connectionString;

    public BlacklistService(FileService fileService)
    {
        _connectionString = fileService.ConnectionString;
        InitDb();
    }

    private void InitDb()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Execute("""
                         CREATE TABLE IF NOT EXISTS Blacklist (
                             Ip        TEXT PRIMARY KEY,
                             AddedAt   TEXT NOT NULL
                         )
                     """);
    }

    public async Task<bool> IsBlockedAsync(string ip)
    {
        using var conn = new SqliteConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Blacklist WHERE Ip = @Ip", new { Ip = ip });
        return count > 0;
    }

    public async Task AddAsync(string ip)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync("""
                                    INSERT OR IGNORE INTO Blacklist (Ip, AddedAt)
                                    VALUES (@Ip, @AddedAt)
                                """, new { Ip = ip, AddedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task RemoveAsync(string ip)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync("DELETE FROM Blacklist WHERE Ip = @Ip", new { Ip = ip });
    }

    public async Task<IEnumerable<string>> ListAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryAsync<string>("SELECT Ip FROM Blacklist ORDER BY AddedAt DESC");
    }
}