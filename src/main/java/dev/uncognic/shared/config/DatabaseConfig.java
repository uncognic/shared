package dev.uncognic.shared.config;

import com.zaxxer.hikari.HikariConfig;
import com.zaxxer.hikari.HikariDataSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import javax.sql.DataSource;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

// class for configuration loading
@Configuration
public class DatabaseConfig {
    private final FileSharingProperties props;

    public DatabaseConfig(FileSharingProperties props) {
        this.props = props;
    }

    // open sqlite database
    @Bean
    public DataSource dataSource() throws IOException {
        Path storagePath = Path.of(props.getStoragePath()).toAbsolutePath();
        Files.createDirectories(storagePath);

        HikariConfig config = new HikariConfig();
        config.setDriverClassName("org.sqlite.JDBC");
        config.setJdbcUrl("jdbc:sqlite:" + storagePath.resolve("shared.db"));
        config.setMaximumPoolSize(1);
        config.setConnectionTimeout(30_000);
        return new HikariDataSource(config);
    }
}
