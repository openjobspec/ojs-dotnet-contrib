# Changelog

All notable changes to the OJS .NET Contrib packages are documented here.

## [0.1.0] — Unreleased

### Added

- `OpenJobSpec.AspNetCore` package with ASP.NET Core integration
  - `AddOjs()` DI extension for registering `OJSClient` and `OJSWorker`
  - `AddOjsHandler<T>()` for typed job handler registration
  - `AddOjs()` health check for backend connectivity monitoring
  - `OjsWorkerHostedService` for background worker lifecycle management
  - Configuration binding from `IConfiguration` and environment variables
  - `IOjsJobHandler` interface for DI-friendly job handlers

