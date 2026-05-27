package dev.uncognic.shared.filter;

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
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

@Component
@Order(-200)
public class RateLimitFilter extends OncePerRequestFilter {
    private static final Logger log = LoggerFactory.getLogger(RateLimitFilter.class);
    private static final long WINDOW_MS = 60_000L;

    private record Policy(int limit) {}
    private static final Map<String, Policy> POLICIES = Map.of(
        "upload", new Policy(10),
        "download", new Policy(60),
        "api", new Policy(30)
    );

    // per ip: long[0] start ms, long[1] count
    private final Map<String, ConcurrentHashMap<String, long[]>> windows = Map.of(
        "upload", new ConcurrentHashMap<>(),
        "download", new ConcurrentHashMap<>(),
        "api", new ConcurrentHashMap<>()
    );

    // check if rate limited
    @Override
    protected void doFilterInternal(HttpServletRequest req, HttpServletResponse res, FilterChain chain)
            throws ServletException, IOException {
        String policy = policyFor(req);
        if (policy != null) {
            String ip = req.getRemoteAddr();
            if (!checkAndIncrement(policy, ip)) {
                log.warn("Rate limit exceeded for {} on {}", ip, req.getRequestURI());
                res.setStatus(429);
                res.getWriter().write("Too many requests.");
                return;
            }
        }
        chain.doFilter(req, res);
    }

    // check which policy we need to use
    private String policyFor(HttpServletRequest req) {
        String method = req.getMethod();
        String uri = req.getRequestURI();
        if ("POST".equals(method) && "/upload".equals(uri)) return "upload";
        if ("GET".equals(method) && uri.startsWith("/f/")) return "download";
        if (uri.equals("/list") || uri.startsWith("/blacklist") ||
            ("DELETE".equals(method) && uri.startsWith("/f/"))) return "api";
        return null;
    }

    // check if it is rate limited, and return if it is or not
    private boolean checkAndIncrement(String policy, String ip) {
        long now = System.currentTimeMillis();
        int limit = POLICIES.get(policy).limit();
        long[] result = windows.get(policy).compute(ip, (k, entry) -> {
            if (entry == null || now - entry[0] >= WINDOW_MS) return new long[]{now, 1};
            entry[1]++;
            return entry;
        });
        return result[1] <= limit;
    }
}
