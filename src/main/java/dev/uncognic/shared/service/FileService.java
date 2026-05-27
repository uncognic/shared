package dev.uncognic.shared.service;

import dev.uncognic.shared.config.FileSharingProperties;
import dev.uncognic.shared.model.FileRecord;
import jakarta.annotation.PostConstruct;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.core.RowMapper;
import org.springframework.stereotype.Service;
import org.springframework.web.multipart.MultipartFile;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.UUID;

@Service
public class FileService {
    private final JdbcTemplate db;
    private final FileSharingProperties props;

    private static final RowMapper<FileRecord> FILE_MAPPER = (rs, rowNum) -> {
        String expiresAtStr = rs.getString("ExpiresAt");
        return FileRecord.builder()
            .id(rs.getString("Id"))
            .originalName(rs.getString("OriginalName"))
            .mimeType(rs.getString("MimeType"))
            .sizeBytes(rs.getLong("SizeBytes"))
            .uploaderIp(rs.getString("UploaderIp"))
            .uploadedAt(Instant.parse(rs.getString("UploadedAt")))
            .expiresAt(expiresAtStr != null ? Instant.parse(expiresAtStr) : null)
            .tokenLabel(rs.getString("TokenLabel"))
            .build();
    };

    public FileService(JdbcTemplate db, FileSharingProperties props) {
        this.db = db;
        this.props = props;
    }

    @PostConstruct
    public void initDb() {
        db.execute("""
            CREATE TABLE IF NOT EXISTS Files (
                Id TEXT PRIMARY KEY,
                OriginalName TEXT NOT NULL,
                MimeType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                UploaderIp TEXT NOT NULL,
                UploadedAt TEXT NOT NULL,
                ExpiresAt TEXT,
                TokenLabel TEXT NOT NULL DEFAULT ''
            )
            """);
    }

    public record FileResult(FileRecord record, Path filePath) {}

    // save file into a FileRecord then return it
    public FileRecord save(MultipartFile file, String uploaderIp, String tokenLabel, Duration ttl) throws IOException {
        // get metadata
        String id = UUID.randomUUID().toString().replace("-", "");
        Path storagePath = Path.of(props.getStoragePath()).toAbsolutePath();
        Path dest = storagePath.resolve(id);

        file.transferTo(dest);

        String originalFilename = file.getOriginalFilename();
        String safeName = originalFilename != null
            ? Path.of(originalFilename).getFileName().toString()
            : "unknown";

        // create the file record
        Instant now = Instant.now();
        FileRecord record = FileRecord.builder()
            .id(id)
            .originalName(safeName)
            .mimeType(file.getContentType())
            .sizeBytes(file.getSize())
            .uploaderIp(uploaderIp)
            .uploadedAt(now)
            .expiresAt(ttl != null ? now.plus(ttl) : null)
            .tokenLabel(tokenLabel)
            .build();

        // put it into the db
        db.update("""
            INSERT INTO Files (Id, OriginalName, MimeType, SizeBytes, UploaderIp, UploadedAt, ExpiresAt, TokenLabel)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            record.getId(), record.getOriginalName(), record.getMimeType(),
            record.getSizeBytes(), record.getUploaderIp(),
            record.getUploadedAt().toString(),
            record.getExpiresAt() != null ? record.getExpiresAt().toString() : null,
            record.getTokenLabel());

        return record;
    }

    // return the file if it exists, otherwise return null
    public FileResult get(String id) {
        List<FileRecord> records = db.query("SELECT * FROM Files WHERE Id = ?", FILE_MAPPER, id);
        if (records.isEmpty()) return null;

        FileRecord record = records.getFirst();
        if (record.getExpiresAt() != null && record.getExpiresAt().isBefore(Instant.now())) return null;

        Path filePath = Path.of(props.getStoragePath()).toAbsolutePath().resolve(id);
        return Files.exists(filePath) ? new FileResult(record, filePath) : null;
    }

    // list all files
    public List<FileRecord> list() {
        return db.query("SELECT * FROM Files ORDER BY UploadedAt DESC", FILE_MAPPER);
    }

    // delete a file
    public boolean delete(String id) {
        List<FileRecord> records = db.query("SELECT * FROM Files WHERE Id = ?", FILE_MAPPER, id);
        if (records.isEmpty()) return false;

        Path filePath = Path.of(props.getStoragePath()).toAbsolutePath().resolve(id);
        try { Files.deleteIfExists(filePath); } catch (IOException ignored) {}

        db.update("DELETE FROM Files WHERE Id = ?", id);
        return true;
    }

    // look at files that the ExpiresAt is from before the current time and then delete them
    public int deleteExpired() {
        List<FileRecord> expired = db.query(
            "SELECT * FROM Files WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= ?",
            FILE_MAPPER, Instant.now().toString());

        for (FileRecord record : expired) {
            Path filePath = Path.of(props.getStoragePath()).toAbsolutePath().resolve(record.getId());
            try { Files.deleteIfExists(filePath); } catch (IOException ignored) {}
            db.update("DELETE FROM Files WHERE Id = ?", record.getId());
        }
        return expired.size();
    }
}
