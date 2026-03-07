# Contributing to OJS .NET Contrib

Thank you for your interest in contributing! Please see the [main contributing guide](https://github.com/openjobspec/openjobspec/blob/main/CONTRIBUTING.md) for general guidelines.

## Development Setup

```bash
# Build the project
dotnet build

# Run tests
dotnet test

# Add a new framework integration
mkdir -p src/OpenJobSpec.YourFramework
dotnet new classlib -o src/OpenJobSpec.YourFramework
```

## Adding a New Integration

1. Create a new project under `src/`
2. Reference `OpenJobSpec` SDK as a project dependency
3. Follow the patterns in `OpenJobSpec.AspNetCore` for DI extensions and hosted services
4. Add corresponding tests under `tests/`
5. Update the root `README.md` with installation and usage instructions

## Code Style

- Follow standard C# naming conventions (PascalCase for public members)
- Use nullable reference types (`<Nullable>enable</Nullable>`)
- Target both `net8.0` and `net10.0` where possible
- Add XML doc comments to all public APIs

