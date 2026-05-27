package dev.uncognic.shared.config;

import lombok.Data;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.stereotype.Component;

import java.util.List;

@Component
@ConfigurationProperties(prefix = "file-sharing")
@Data
// class for config
public class FileSharingProperties {
    private String baseUrl = "https://files.example.com";
    private String storagePath = "./filestore";
    private long maxFileSizeBytes = 524_288_000L;
    private List<String> tokens = List.of("default");
}
