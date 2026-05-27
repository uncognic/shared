package dev.uncognic.shared.service;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

// delete files when they expire
@Service
public class ExpiryService {
    private static final Logger log = LoggerFactory.getLogger(ExpiryService.class);
    private final FileService fileService;

    public ExpiryService(FileService fileService) {
        this.fileService = fileService;
    }

    @Scheduled(fixedDelay = 300_000)
    public void sweep() {
        int count = fileService.deleteExpired();
        if (count > 0) {
            log.info("expiry: deleted {} expired file(s).", count);
        }
    }
}
