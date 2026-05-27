package dev.uncognic.shared.service;

import jakarta.annotation.PostConstruct;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.util.List;

@Service
public class BlacklistService {
    private final JdbcTemplate db;

    public BlacklistService(JdbcTemplate db) {
        this.db = db;
    }

    @PostConstruct
    public void initDb() {
        db.execute("""
            CREATE TABLE IF NOT EXISTS Blacklist (
                Ip TEXT PRIMARY KEY,
                AddedAt TEXT NOT NULL
            )
            """);
    }

    // check if an ip is in the table
    public boolean isBlocked(String ip) {
        Integer count = db.queryForObject("SELECT COUNT(*) FROM Blacklist WHERE Ip = ?", Integer.class, ip);
        return count != null && count > 0;
    }

    // add into the table
    public void add(String ip) {
        db.update("INSERT OR IGNORE INTO Blacklist (Ip, AddedAt) VALUES (?, ?)",
            ip, Instant.now().toString());
    }

    // remove
    public void remove(String ip) {
        db.update("DELETE FROM Blacklist WHERE Ip = ?", ip);
    }

    // list all
    public List<String> list() {
        return db.queryForList("SELECT Ip FROM Blacklist ORDER BY AddedAt DESC", String.class);
    }
}
