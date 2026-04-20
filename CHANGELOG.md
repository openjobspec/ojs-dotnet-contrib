# Changelog

All notable changes to the OJS .NET Contrib packages are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-09-02

### Changed

- Aligned both integration packages and their `OpenJobSpec` dependency with the
  coordinated 0.5.0 release
- Pre-release CI now rejects its SDK revision placeholder until a 40-character
  immutable SDK commit and private-repository read token are supplied
- Added deterministic package metadata, XML documentation, release automation,
  publish dry-run validation, and packaged-consumer smoke tests
- Packaged-consumer validation now performs an unlocked restore from rebuilt
  local packages in a completely isolated NuGet cache instead of pinning local-package hashes
- Pre-tag validation consumes the coordinated SDK through a local NuGet feed;
  contrib lock files are deferred until the canonical 0.5.0 package hash exists
- NuGet publication and GitHub release creation are restricted to verified
  `v*` tag refs; manual dispatch performs validation only

### Fixed

- Event-listener hosted-service startup now waits until worker event subscription is active

## [0.4.0] - 2026-04-20

### Added

- Expanded API reference, workflow integration, retry configuration, and test coverage

### Fixed

- Timestamp serialization, middleware edge cases, and worker shutdown behavior

## [0.3.0] - 2026-03-09

### Added

- ASP.NET Core and Worker Service packages with dependency injection, health checks,
  hosted worker lifecycle, configuration binding, and typed job handlers

[Unreleased]: https://github.com/openjobspec/ojs-dotnet-contrib/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/openjobspec/ojs-dotnet-contrib/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/openjobspec/ojs-dotnet-contrib/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/openjobspec/ojs-dotnet-contrib/releases/tag/v0.3.0
