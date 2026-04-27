namespace shared.Models;

public class FileRecord
{
    public string Id { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string UploaderIp { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
