package dev.uncognic.shared.service;

import dev.uncognic.shared.config.FileSharingProperties;
import jakarta.annotation.PostConstruct;
import jakarta.servlet.http.HttpServletRequest;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.time.Instant;
import java.util.HexFormat;
import java.util.List;
import java.util.UUID;

@Service
public class TokenService {
    private static final Logger log = LoggerFactory.getLogger(TokenService.class);
    private final JdbcTemplate db;
    private final FileSharingProperties props;

    public TokenService(JdbcTemplate db, FileSharingProperties props) {
        this.db = db;
        this.props = props;
    }

    @PostConstruct
    public void init() {
        db.execute("""
            CREATE TABLE IF NOT EXISTS Tokens (
                Id TEXT PRIMARY KEY,
                Hash TEXT NOT NULL UNIQUE,
                Label TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )
            """);
        ensureTokens();
    }

    // ensure a token exists in the db for each token in the config
    private void ensureTokens() {
        for (String label : props.getTokens()) {
            Integer count = db.queryForObject(
                "SELECT COUNT(*) FROM Tokens WHERE Label = ?", Integer.class, label);
            if (count != null && count > 0) continue;

            String plaintext = generateToken();
            String hash = hash(plaintext);

            db.update("INSERT INTO Tokens (Id, Hash, Label, CreatedAt) VALUES (?, ?, ?, ?)",
                UUID.randomUUID().toString().replace("-", ""), hash, label, Instant.now().toString());

            log.warn("=================================================");
            log.warn("  token '{}': {}", label, plaintext);
            log.warn("  store this somewhere safe, it won't be shown again.");
            log.warn("=================================================");
        }
    }

    // check if the hash is in the db
    public boolean isValid(String token) {
        Integer count = db.queryForObject(
            "SELECT COUNT(*) FROM Tokens WHERE Hash = ?", Integer.class, hash(token));
        return count != null && count > 0;
    }

    // get the label of the token
    public String getLabel(String token) {
        List<String> labels = db.queryForList(
            "SELECT Label FROM Tokens WHERE Hash = ?", String.class, hash(token));
        return labels.isEmpty() ? null : labels.getFirst();
    }

    // check if the authorization is valid
    public boolean isAuthorized(HttpServletRequest req) {
        String header = req.getHeader("Authorization");
        if (header == null || !header.startsWith("Bearer ")) return false;
        return isValid(header.substring(7).trim());
    }

    // get the token from a request
    public String tokenFrom(HttpServletRequest req) {
        String header = req.getHeader("Authorization");
        if (header == null || !header.startsWith("Bearer ")) return null;
        return header.substring(7).trim();
    }

    // generate token
    public static String generateToken() {
        byte[] bytes = new byte[32];
        new SecureRandom().nextBytes(bytes);
        return HexFormat.of().formatHex(bytes);
    }

    // make a hash of the token to store in the db
    public static String hash(String token) {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] bytes = digest.digest(token.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(bytes);
        } catch (NoSuchAlgorithmException e) {
            throw new RuntimeException(e);
        }
    }
}
