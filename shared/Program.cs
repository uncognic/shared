using shared.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<FileService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BlacklistService>();
builder.Services.AddHostedService<ExpiryService>();
builder.WebHost.ConfigureKestrel(options =>
{
    var maxSize = builder.Configuration.GetValue<long>("FileSharing:MaxFileSizeBytes", 524_288_000);
    options.Limits.MaxRequestBodySize = maxSize;
});

var app = builder.Build();
app.Services.GetRequiredService<TokenService>();
app.UseForwardedHeaders();

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

// POST /upload

app.MapPost("/upload", async (HttpContext ctx, FileService fs) =>
{
    if (!await IsAuthorized(ctx))
        return Results.Unauthorized();

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
        

    var record = await fs.SaveAsync(file, ip, ttl);
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
});

// GET /f/{id}

app.MapGet("/f/{id}", async (string id, FileService fs) =>
{
    var (record, filePath) = await fs.GetAsync(id);

    if (record is null || filePath is null)
        return Results.NotFound();

    var stream = File.OpenRead(filePath);
    return Results.File(stream, record.MimeType, record.OriginalName);
});

// GET /list (auth)
app.MapGet("/list", async (HttpContext ctx, FileService fs) =>
{
    if (!await IsAuthorized(ctx))
        return Results.Unauthorized();

    var files = await fs.ListAsync();
    return Results.Ok(files);
});

app.MapDelete("/f/{id}", async (string id, HttpContext ctx, FileService fs) =>
{
    if (!await IsAuthorized(ctx))
        return Results.Unauthorized();

    var deleted = await fs.DeleteAsync(id);
    return deleted ? Results.Ok() : Results.NotFound();
});

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
        return Results.Unauthorized();

    await bl.AddAsync(ip);
    return Results.Ok();
});

app.MapDelete("/blacklist/{ip}", async (string ip, HttpContext ctx, BlacklistService bl) =>
{
    if (!await IsAuthorized(ctx))
        return Results.Unauthorized();

    await bl.RemoveAsync(ip);
    return Results.Ok();
});

app.MapGet("/blacklist", async (HttpContext ctx, BlacklistService bl) =>
{
    if (!await IsAuthorized(ctx))
        return Results.Unauthorized();

    var ips = await bl.ListAsync();
    return Results.Ok(ips);
});

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
