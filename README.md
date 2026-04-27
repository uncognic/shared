# shared
Simple, 0x0.st-like file sharing server.

## Running
`dotnet run` in the project directory.

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
