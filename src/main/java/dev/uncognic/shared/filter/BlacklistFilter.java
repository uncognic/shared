package dev.uncognic.shared.filter;

import dev.uncognic.shared.service.BlacklistService;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;

@Component
@Order(-199)
public class BlacklistFilter extends OncePerRequestFilter {
    private static final Logger log = LoggerFactory.getLogger(BlacklistFilter.class);
    private final BlacklistService blacklistService;

    public BlacklistFilter(BlacklistService blacklistService) {
        this.blacklistService = blacklistService;
    }

    // check if the IP is blocked, if so then HTTP 403
    @Override
    protected void doFilterInternal(HttpServletRequest req, HttpServletResponse res, FilterChain chain)
            throws ServletException, IOException {
        if ("/".equals(req.getRequestURI())) {
            chain.doFilter(req, res);
            return;
        }

        String ip = req.getRemoteAddr();
        if (blacklistService.isBlocked(ip)) {
            log.warn("Blocked IP {} attempted to access {}", ip, req.getRequestURI());
            res.setStatus(403);
            return;
        }

        chain.doFilter(req, res);
    }
}
