# shared
Simple, 0x0.st-like file sharing server.

## Running
`docker compose up -d` in the project directory.

## Configuration
Edit the `environment` section in the Docker Compose YML file.
```yml
services:
  shared:
    image: shared:latest
    build:
      context: .
      dockerfile: shared/Dockerfile
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - shared:/app/shared
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - FileSharing__BaseUrl=https://files.example.com
      - FileSharing__StoragePath=/app/shared
      - FileSharing__MaxFileSizeBytes=524288000

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
shared - simple file sharing
inspired by the great 0x0.st

licensed under the GNU AGPL v3 license <https://fsf.org/>
https://github.com/uncognic/shared

UPLOAD (remove ?ttl= for no expiry)
  curl -X POST https://files.example.com/upload?ttl=<N[s|m|h|d]> \
    -H "Authorization: Bearer <token>" \
    -F "file=@/path/to/file" | jq

DOWNLOAD
  curl https://files.example.com/f/<id>

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
