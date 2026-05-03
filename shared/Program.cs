using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using shared.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var logPath = Path.Combine(
    builder.Configuration["FileSharing:StoragePath"] ?? "/app/shared",
    "logs", "shared-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddHttpContextAccessor();
var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
Log.Information("=================================================");
Log.Information("  shared {Version}", version);
Log.Information("=================================================");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<FileService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BlacklistService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy("download", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy("api", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));

    options.OnRejected = async (ctx, token) =>
    {
        Log.Warning("Rate limit exceeded for {Ip} on {Path}",
            ctx.HttpContext.Connection.RemoteIpAddress, ctx.HttpContext.Request.Path);
        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsync("Too many requests.", token);
    };
});
builder.Services.AddHostedService<ExpiryService>();
builder.WebHost.ConfigureKestrel(options =>
{
    var maxSize = builder.Configuration.GetValue<long>("FileSharing:MaxFileSizeBytes", 524_288_000);
    options.Limits.MaxRequestBodySize = maxSize;
});

var app = builder.Build();
app.Services.GetRequiredService<TokenService>();
app.UseForwardedHeaders();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diag, ctx) =>
    {
        diag.Set("Ip", ctx.Connection.RemoteIpAddress);
        diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
    };
    options.MessageTemplate = "{RequestMethod} {RequestPath} {StatusCode} {Elapsed:0}ms | {Ip} | {UserAgent}";
});
app.UseRateLimiter();

// blacklisting
app.Use(async (ctx, next) =>
{
    // allow about even if blacklisted
    if (ctx.Request.Path == "/")
    {
        await next();
        return;
    }

    var bl = ctx.RequestServices.GetRequiredService<BlacklistService>();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    if (await bl.IsBlockedAsync(ip))
    {
        Log.Warning("Blocked IP {Ip} attempted to access {Path}", ip, ctx.Request.Path);
        ctx.Response.StatusCode = 403;
        return;
    }

    await next();
});

// auth
async Task<bool> IsAuthorized(HttpContext ctx)
{
    var tokenService = ctx.RequestServices.GetRequiredService<TokenService>();
    var header = ctx.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ")) return false;
    var token = header["Bearer ".Length..].Trim();
    return await tokenService.IsValidAsync(token);
}

// fuck clankers
app.MapGet("/robots.txt", () => Results.Text(
    "User-agent: *\nDisallow: /\n",
    "text/plain"));

// POST /upload

app.MapPost("/upload", async (HttpContext ctx, FileService fs, TokenService tokenService) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized upload attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    if (!ctx.Request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest("No file provided.");

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    TimeSpan? ttl = null;
    if (ctx.Request.Query.TryGetValue("ttl", out var ttlStr))
    {
        ttl = ParseTtl(ttlStr.ToString());
    }

    var token = ctx.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
    var tokenLabel = await tokenService.GetLabelAsync(token) ?? "unknown";
    var record = await fs.SaveAsync(file, ip, tokenLabel, ttl);

    Log.Information("Upload: {OriginalName} ({Size} bytes) from {Ip}, expires {ExpiresAt}",
        record.OriginalName, record.SizeBytes, ip, record.ExpiresAt?.ToString("O") ?? "never");
    var baseUrl = app.Configuration["FileSharing:BaseUrl"]?.TrimEnd('/');
    var link = $"{baseUrl}/f/{record.Id}";

    return Results.Ok(new
    {
        link,
        record.Id,
        record.OriginalName,
        record.MimeType,
        record.SizeBytes,
        record.UploadedAt,
        record.ExpiresAt
    });
}).RequireRateLimiting("upload");

// GET /f/{id}

app.MapGet("/f/{id}", async (string id, FileService fs) =>
{
    var (record, filePath) = await fs.GetAsync(id);

    if (record is null || filePath is null)
    {
        Log.Warning("Download 404: {Id}", id);
        return Results.NotFound();
    }
    Log.Information("Download: {Id} ({OriginalName})", record.Id, record.OriginalName);

    var stream = File.OpenRead(filePath);
    return Results.File(stream, record.MimeType, record.OriginalName);
}).RequireRateLimiting("download");

// GET /list (auth)
app.MapGet("/list", async (HttpContext ctx, FileService fs) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized list attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    Log.Information("List requested from {Ip}", ctx.Connection.RemoteIpAddress);

    var files = await fs.ListAsync();
    return Results.Ok(files);
}).RequireRateLimiting("api");

app.MapDelete("/f/{id}", async (string id, HttpContext ctx, FileService fs) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized file delete attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    var deleted = await fs.DeleteAsync(id);
    if (deleted)
        Log.Information("Deleted: {Id}", id);
    else
        Log.Warning("Delete 404: {Id}", id);
    return deleted ? Results.Ok() : Results.NotFound();
}).RequireRateLimiting("api");

// GET / (about?
app.MapGet("/", () =>
{
    var baseUrl = app.Configuration["FileSharing:BaseUrl"]?.TrimEnd('/') ?? "https://example.com";
    var text =
        $"shared - simple file sharing\n" +
        $"inspired by the great 0x0.st\n\n" +
        $"licensed under the GNU AGPL v3 license <https://fsf.org/>\n" +
        $"https://github.com/uncognic/shared\n" +
        $"\n" +
        $"UPLOAD (remove ?ttl= for no expiry)\n" +
        $"  curl -X POST {baseUrl}/upload?ttl=<N[s|m|h|d]> \\\n" +
        $"    -H \"Authorization: Bearer <token>\" \\\n" +
        $"    -F \"file=@/path/to/file\" | jq\n" +
        $"\n" +
        $"DOWNLOAD\n" +
        $"  curl {baseUrl}/f/<id>\n\n" +
        $"LISTING\n" +
        $"  curl {baseUrl}/list -H \"Authorization: Bearer <token>\" | jq\n"+
        $"\n" +
        $"DELETE\n" +
        $"  curl -X DELETE {baseUrl}/f/<id> \\\n" +
        $"    -H \"Authorization: Bearer <token>\"\n" +
        $"\n" +
        $"BLACKLIST\n" +
        $"  BLACKLISTING \n" +
        $"      curl -X POST {baseUrl}/blacklist/<ip> -H \"Authorization: Bearer <token>\"\n\n" +
        $"  UNBLACKLISTING \n" +
        $"      curl -X DELETE {baseUrl}/blacklist/<ip> -H \"Authorization: Bearer <token>\"\n\n" +
        $"  LISTING \n" +
        $"      curl {baseUrl}/blacklist -H \"Authorization: Bearer <token>\" | jq\n";
    return Results.Text(text, "text/plain");
});

app.MapPost("/blacklist/{ip}", async (string ip, HttpContext ctx, BlacklistService bl) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized blacklist attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    await bl.AddAsync(ip);
    return Results.Ok();
}).RequireRateLimiting("api");

app.MapDelete("/blacklist/{ip}", async (string ip, HttpContext ctx, BlacklistService bl) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized unblacklist attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    await bl.RemoveAsync(ip);
    return Results.Ok();
}).RequireRateLimiting("api");

app.MapGet("/blacklist", async (HttpContext ctx, BlacklistService bl) =>
{
    if (!await IsAuthorized(ctx))
    {
        Log.Warning("Unauthorized blacklist list attempt from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    var ips = await bl.ListAsync();
    return Results.Ok(ips);
}).RequireRateLimiting("api");

TimeSpan? ParseTtl(string s)
{
    if (string.IsNullOrEmpty(s)) return null;
    var unit = s[^1];
    if (!int.TryParse(s[..^1], out var value) || value <= 0) return null;
    return unit switch
    {
        's' => TimeSpan.FromSeconds(value),
        'm' => TimeSpan.FromMinutes(value),
        'h' => TimeSpan.FromHours(value),
        'd' => TimeSpan.FromDays(value),
        _ => null
    };
}


app.Run();
