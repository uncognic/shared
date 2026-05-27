package dev.uncognic.shared.controller;

import dev.uncognic.shared.service.BlacklistService;
import dev.uncognic.shared.service.TokenService;
import jakarta.servlet.http.HttpServletRequest;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/blacklist")
public class BlacklistController {
    private static final Logger log = LoggerFactory.getLogger(BlacklistController.class);
    private final BlacklistService blacklistService;
    private final TokenService tokenService;

    public BlacklistController(BlacklistService blacklistService, TokenService tokenService) {
        this.blacklistService = blacklistService;
        this.tokenService = tokenService;
    }

    // POST /blacklist/ip
    @PostMapping("/{ip}")
    public ResponseEntity<?> add(@PathVariable String ip, HttpServletRequest req) {
        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized blacklist attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        blacklistService.add(ip);
        return ResponseEntity.ok().build();
    }

    // DELETE /blacklist/ip
    @DeleteMapping("/{ip}")
    public ResponseEntity<?> remove(@PathVariable String ip, HttpServletRequest req) {
        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized unblacklist attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        blacklistService.remove(ip);
        return ResponseEntity.ok().build();
    }

    // GET /blacklist/ip
    @GetMapping
    public ResponseEntity<?> list(HttpServletRequest req) {
        if (!tokenService.isAuthorized(req)) {
            log.warn("Unauthorized blacklist list attempt from {}", req.getRemoteAddr());
            return ResponseEntity.status(401).build();
        }
        return ResponseEntity.ok(blacklistService.list());
    }
}
