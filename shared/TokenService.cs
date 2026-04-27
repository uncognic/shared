using System.Runtime.CompilerServices;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace shared.Services;

public class TokenService
{
    private readonly string _connectionString;
    private readonly ILogger<TokenService> _logger;

    public TokenService(FileService fileService, ILogger<TokenService> logger)
    {
        _connectionString = fileService.ConnectionString;
        _logger = logger;

        InitDb();
        EnsureTokenExists();
    }

    private void InitDb()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Execute("""
                         CREATE TABLE IF NOT EXISTS Tokens (
                             Id        TEXT PRIMARY KEY,
                             Hash      TEXT NOT NULL UNIQUE,
                             Label     TEXT NOT NULL,
                             CreatedAt TEXT NOT NULL
                         )
                     """);
    }

    private void EnsureTokenExists()
    {
        using var conn = new SqliteConnection(_connectionString);
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Tokens");
        if (count > 0) return;

        var (plaintext, hash) = GenerateToken();

        conn.Execute("""
                         INSERT INTO Tokens (Id, Hash, Label, CreatedAt)
                         VALUES (@Id, @Hash, @Label, @CreatedAt)
                     """, new
        {
            Id = Guid.NewGuid().ToString("N"),
            Hash = hash,
            Label = "default",
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        _logger.LogWarning("=================================================");
        _logger.LogWarning("  Generated token: {Token}", plaintext);
        _logger.LogWarning("  Store this somewhere safe, it won't be shown again.");
        _logger.LogWarning("=================================================");
    }

    public async Task<bool> IsValidAsync(string token)
    {
        var hash = Hash(token);
        using var conn = new SqliteConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Tokens WHERE Hash = @Hash", new { Hash = hash });
        return count > 0;
    }

    public static (string Plaintext, string Hash) GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToHexString(bytes).ToLowerInvariant();
        return (plaintext, Hash(plaintext));
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}