package dev.uncognic.shared.controller;

import dev.uncognic.shared.config.FileSharingProperties;
import dev.uncognic.shared.model.FileRecord;
import dev.uncognic.shared.service.FileService;
import dev.uncognic.shared.service.TokenService;
import jakarta.servlet.http.HttpServletRequest;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.core.io.FileSystemResource;
import org.springframework.http.ContentDisposition;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.multipart.MultipartFile;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Map;

@RestController
public class FileController {
    private static final Logger log = LoggerFactory.getLogger(FileController.class);
    private final FileService fileService;
    private final TokenService tokenService;
    private final FileSharingProperties props;

    // boilerplate
    public FileController(FileService fileService, TokenService tokenService, FileSharingProperties props) {
        this.fileService = fileService;
        this.tokenService = tokenService;
        this.props = props;
    }

    // POST /upload
    @PostMapping("/upload")
    public ResponseEntity<?> upload(
            @RequestParam("file") MultipartFile file,
            @RequestParam(required = false) String ttl,
            HttpServletRequest req) throws IOException {

        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized upload attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        if (file == null || file.isEmpty()) {
            return ResponseEntity.badRequest().body("No file provided.");
        }

        // for logging
        String ip = req.getRemoteAddr();
        String token = tokenService.tokenFrom(req);
        String tokenLabel = token != null ? tokenService.getLabel(token) : "unknown";
        if (tokenLabel == null) tokenLabel = "unknown";

        FileRecord record = fileService.save(file, ip, tokenLabel, parseTtl(ttl));

        log.info("Upload: {} ({} bytes) from {}, expires {}",
            record.getOriginalName(), record.getSizeBytes(), ip,
            record.getExpiresAt() != null ? record.getExpiresAt() : "never");

        // return the info to the uploader
        String baseUrl = props.getBaseUrl().replaceAll("/$", "");
        Map<String, Object> response = new LinkedHashMap<>();
        response.put("link", baseUrl + "/f/" + record.getId());
        response.put("id", record.getId());
        response.put("originalName", record.getOriginalName());
        response.put("mimeType", record.getMimeType());
        response.put("sizeBytes", record.getSizeBytes());
        response.put("uploadedAt", record.getUploadedAt());
        response.put("expiresAt", record.getExpiresAt());
        return ResponseEntity.ok(response);
    }

    // GET /f/id
    @GetMapping("/f/{id}")
    public ResponseEntity<?> download(@PathVariable String id) {
        FileService.FileResult result = fileService.get(id);
        if (result == null) {
            log.warn("Download 404: {}", id);
            return ResponseEntity.notFound().build();
        }
        log.info("Download: {} ({})", result.record().getId(), result.record().getOriginalName());
        return ResponseEntity.ok()
            .contentType(MediaType.parseMediaType(result.record().getMimeType()))
            .header(HttpHeaders.CONTENT_DISPOSITION,
                ContentDisposition.attachment()
                    .filename(result.record().getOriginalName(), StandardCharsets.UTF_8)
                    .build().toString())
            .body(new FileSystemResource(result.filePath()));
    }

    // GET /list
    @GetMapping("/list")
    public ResponseEntity<?> list(HttpServletRequest req) {
        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized list attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        log.info("List requested from {}", req.getRemoteAddr());
        return ResponseEntity.ok(fileService.list());
    }

    // DELETE /f/id
    @DeleteMapping("/f/{id}")
    public ResponseEntity<?> delete(@PathVariable String id, HttpServletRequest req) {
        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized file delete attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        boolean deleted = fileService.delete(id);
        if (deleted) {
            log.info("Deleted: {}", id);
            return ResponseEntity.ok().build();
        }
        log.warn("Delete 404: {}", id);
        return ResponseEntity.notFound().build();
    }

    // parse expiration time
    private Duration parseTtl(String s) {
        if (s == null || s.isEmpty()) return null;
        char unit = s.charAt(s.length() - 1);
        try {
            int value = Integer.parseInt(s.substring(0, s.length() - 1));
            if (value <= 0) return null;
            return switch (unit) {
                case 's' -> Duration.ofSeconds(value);
                case 'm' -> Duration.ofMinutes(value);
                case 'h' -> Duration.ofHours(value);
                case 'd' -> Duration.ofDays(value);
                default -> null;
            };
        } catch (NumberFormatException e) {
            return null;
        }
    }
}
