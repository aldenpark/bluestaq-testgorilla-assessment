# Verification Quickstart

```bash
dotnet tool restore
dotnet format --verify-no-changes
dotnet build --no-restore
dotnet test --no-build
dotnet test --no-build --filter 'Category=Integration'
dotnet test --no-build --filter 'Category=Smoke'
dotnet list package --vulnerable --include-transitive
```

Start the API with `dotnet run --project src/Bluestaq.Notes.Api` and authenticate
using a configured development key. New invitation and key-management responses
show a plaintext `apiKey` once; store it before discarding the response.
