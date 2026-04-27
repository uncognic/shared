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
      - shared:/app/filestore
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - FileSharing__BaseUrl=https://files.example.com
      - FileSharing__StoragePath=./filestore
      - FileSharing__BearerToken=CHANGE_ME
      - FileSharing__MaxFileSizeBytes=524288000

volumes:
  filestore:
```

## Usage

```bash
curl -X POST https://shared.example.com/upload \
 -H "Authorization: Bearer TOKEN_HERE" \
 -F "file=@/PATH/TO/FILE"
 ```
 
You will receive the link to download it in a format like this:
```bash
{
  "link": "https://shared.example.com/f/example",
  "id": "id_here",
  "originalName": "somefile.txt",
  "mimeType": "text/plain",
  "sizeBytes": 1234,
  "uploadedAt": "2026-04-26T12:00:00Z"
}
```

You can download it back like this:
```bash
curl https://shared.example.com/f/some_id -o file.txt
```
