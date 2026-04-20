# Contributing to OJS .NET Contrib

Thank you for your interest in contributing!

## Development Setup

```bash
# Restore dependencies from NuGet (after OpenJobSpec 0.5.0 is published)
dotnet restore

# Build and run tests
dotnet build tests/OpenJobSpec.AspNetCore.Tests/OpenJobSpec.AspNetCore.Tests.csproj -c Release --no-restore --warnaserror
dotnet build tests/OpenJobSpec.WorkerService.Tests/OpenJobSpec.WorkerService.Tests.csproj -c Release --no-restore --warnaserror
dotnet test tests/OpenJobSpec.AspNetCore.Tests/OpenJobSpec.AspNetCore.Tests.csproj -c Release --no-build
dotnet test tests/OpenJobSpec.WorkerService.Tests/OpenJobSpec.WorkerService.Tests.csproj -c Release --no-build

# Add a new framework integration
mkdir -p src/OpenJobSpec.YourFramework
dotnet new classlib -o src/OpenJobSpec.YourFramework
```

## Adding a New Integration

1. Create a new project under `src/`
2. Reference the coordinated `OpenJobSpec` package version
3. Follow the patterns in `OpenJobSpec.AspNetCore` for DI extensions and hosted services
4. Add corresponding tests under `tests/`
5. Update the root `README.md` with installation and usage instructions

## Code Style

- Follow standard C# naming conventions (PascalCase for public members)
- Use nullable reference types (`<Nullable>enable</Nullable>`)
- Target `net10.0`, matching the current package target
- Add XML doc comments to all public APIs

Before the coordinated SDK package is published, pack the sibling SDK into a
local feed and include that feed during restore:

```bash
dotnet pack ../ojs-dotnet-sdk/src/OpenJobSpec/OpenJobSpec.csproj -c Release -o artifacts/packages
dotnet restore OpenJobSpec.Contrib.sln --no-http-cache -p:RestorePackagesWithLockFile=false --source artifacts/packages --source https://api.nuget.org/v3/index.json
```

Committed package manifests always consume `OpenJobSpec` through its package
coordinate; they do not rely on sibling project replacements.
Dependency locks for contrib are intentionally deferred until the canonical
0.5.0 SDK package is published, because locally repacked `.nupkg` archives do
not have a stable content hash.
