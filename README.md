# shared
Simple, 0x0.st-like file sharing server.

## Running
Get the example docker-compose.yml file and edit it as needed, then run:
```bash
docker compose up -d
```

## Configuration
Edit the `environment` section in the Docker Compose YML file.
```yml
sservices:
  shared:
    image: ghcr.io/uncognic/shared:latest
    build:
      context: .
      dockerfile: Dockerfile
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - shared:/app/shared
    environment:
      FILE_SHARING_BASE_URL: https://files.example.com
      FILE_SHARING_STORAGE_PATH: /app/shared
      FILE_SHARING_MAX_FILE_SIZE_BYTES: 524288000
      FILE_SHARING_TOKENS_0: default
      # FILE_SHARING_TOKENS_1: another-token
      # FILE_SHARING_TOKENS_2: yet-another-token

volumes:
  shared:

```
Or edit appsettings.json directly if you are not using Docker Compose/are running from Visual Studio.

## Usage
You can find some basic instructions by cURLing the base URL:
```bash
curl https://shared.example.com
```
```
shared <dev> - simple file sharing
inspired by the great 0x0.st

licensed under the GNU AGPL v3 license <https://fsf.org/>
https://github.com/uncognic/shared

windows users: use curl.exe instead of curl
UPLOAD (remove ?ttl= for no expiry)
  curl -X POST https://files.example.com/upload?ttl=<N[s|m|h|d]> \
    -H "Authorization: Bearer <token>" \
    -F "file=@/path/to/file" | jq

DOWNLOAD
  curl https://files.example.com/f/<id> -o <output_file>

LISTING
  curl https://files.example.com/list -H "Authorization: Bearer <token>" | jq

DELETE
  curl -X DELETE https://files.example.com/f/<id> \
    -H "Authorization: Bearer <token>"

BLACKLIST
  BLACKLISTING 
      curl -X POST https://files.example.com/blacklist/<ip> -H "Authorization: Bearer <token>"

  UNBLACKLISTING 
      curl -X DELETE https://files.example.com/blacklist/<ip> -H "Authorization: Bearer <token>"

  LISTING 
      curl https://files.example.com/blacklist -H "Authorization: Bearer <token>" | jq
```
