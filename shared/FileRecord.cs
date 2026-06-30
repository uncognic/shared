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
    public string TokenLabel { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public List<Download> Downloads { get; set; } = [];
}

public class Download
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileId { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
}
