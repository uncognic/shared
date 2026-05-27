package dev.uncognic.shared.model;

import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Data;
import lombok.NoArgsConstructor;

import java.time.Instant;

@Data
@Builder
@NoArgsConstructor
@AllArgsConstructor
// sqlite file record
public class FileRecord {
    private String id;
    private String originalName;
    private String mimeType;
    private long sizeBytes;
    private String uploaderIp;
    private Instant uploadedAt;
    private Instant expiresAt;
    private String tokenLabel;
}
