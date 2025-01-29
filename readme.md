# Overview
This project provides semantic diffing of document versions.

# Instructions

Deploy infra using the following commands:
```bash
azd auth login
azd up
```

Copy sample.env to .env.
Fill in the <redacted> values.

In order, run the scripts using the following command(s):

```
dotnet run --project ./Upload.csproj
dotnet run --project ./Create.csproj
dotnet run --project ./Ingest.csproj
dotnet run --project ./Query.csproj
```