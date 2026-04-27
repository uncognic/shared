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
builder.WebHost.ConfigureKestrel(options =>
{
    var maxSize = builder.Configuration.GetValue<long>("FileSharing:MaxFileSizeBytes", 524_288_000);
    options.Limits.MaxRequestBodySize = maxSize;
});

var app = builder.Build();
app.UseForwardedHeaders();

// auth
bool IsAuthorized(HttpContext ctx)
{
    var expected = app.Configuration["FileSharing:BearerToken"];
    if (string.IsNullOrEmpty(expected))
    {
        return false;
    }

    return ctx.Request.Headers.Authorization.ToString() == $"Bearer {expected}";
}

// POST /upload

app.MapPost("/upload", async (HttpContext ctx, FileService fs) =>
{
    if (!IsAuthorized(ctx))
        return Results.Unauthorized();

    if (!ctx.Request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest("No file provided.");

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var record = await fs.SaveAsync(file, ip);
    var baseUrl = app.Configuration["FileSharing:BaseUrl"]?.TrimEnd('/');
    var link = $"{baseUrl}/f/{record.Id}";

    return Results.Ok(new
    {
        link,
        record.Id,
        record.OriginalName,
        record.MimeType,
        record.SizeBytes,
        record.UploadedAt
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
    if (!IsAuthorized(ctx))
        return Results.Unauthorized();

    var files = await fs.ListAsync();
    return Results.Ok(files);
});

app.Run();
