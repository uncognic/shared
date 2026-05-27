package dev.uncognic.shared.controller;

import dev.uncognic.shared.config.FileSharingProperties;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
public class AboutController {
    private final FileSharingProperties props;
    private final String version;

    public AboutController(FileSharingProperties props) {
        this.props = props;
        this.version = System.getenv().getOrDefault("APP_VERSION", "dev");
    }

    // print this message when fetching bare domain
    @GetMapping(value = "/", produces = MediaType.TEXT_PLAIN_VALUE)
    public ResponseEntity<String> about() {
        String baseUrl = props.getBaseUrl().replaceAll("/$", "");
        String text = String.format("""
            shared <%s> - simple file sharing
            inspired by the great 0x0.st

            licensed under the GNU AGPL v3 license <https://fsf.org/>
            https://github.com/uncognic/shared

            windows users: use curl.exe instead of curl
            UPLOAD (remove ?ttl= for no expiry)
              curl -X POST %s/upload?ttl=<N[s|m|h|d]> \\
                -H "Authorization: Bearer <token>" \\
                -F "file=@/path/to/file" | jq

            DOWNLOAD
              curl %s/f/<id> -o <output_file>

            LISTING
              curl %s/list -H "Authorization: Bearer <token>" | jq

            DELETE
              curl -X DELETE %s/f/<id> \\
                -H "Authorization: Bearer <token>"

            BLACKLIST
              BLACKLISTING
                  curl -X POST %s/blacklist/<ip> -H "Authorization: Bearer <token>"

              UNBLACKLISTING
                  curl -X DELETE %s/blacklist/<ip> -H "Authorization: Bearer <token>"

              LISTING
                  curl %s/blacklist -H "Authorization: Bearer <token>" | jq
            """, version, baseUrl, baseUrl, baseUrl, baseUrl, baseUrl, baseUrl, baseUrl);
        return ResponseEntity.ok()
            .contentType(MediaType.TEXT_PLAIN)
            .body(text);
    }

    // disallow all bots
    @GetMapping(value = "/robots.txt", produces = MediaType.TEXT_PLAIN_VALUE)
    public ResponseEntity<String> robots() {
        return ResponseEntity.ok()
            .contentType(MediaType.TEXT_PLAIN)
            .body("User-agent: *\nDisallow: /\n");
    }
}
