# Mister.Version MSBuild Integration

![version](https://img.shields.io/badge/version-3.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

MSBuild integration package for Mister.Version, enabling automatic version calculation for .NET projects in monorepos.

## Features

- **Automatic Version Calculation**: Generates semantic versions based on git history and changes
- **Conventional Commits Support**: Intelligent semantic versioning based on commit message conventions
- **Automatic Changelog Generation**: Generate changelogs from conventional commits during build
- **File Pattern-Based Change Detection**: Smart versioning based on which files changed
- **Monorepo Support**: Independent versioning for multiple projects in a single repository
- **MSBuild Integration**: Seamless integration with the .NET build process
- **Cross-Platform**: Works on Windows, macOS, and Linux
- **Multi-Targeting Support**: Works with both .NET Framework 4.7.2 and .NET 8.0+

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package Mister.Version
```

Or add to your project file:

```xml
<PackageReference Include="Mister.Version" Version="3.0.0" />
```

## Usage

Once installed, Mister.Version automatically calculates and injects version information during the build process. The version will be available in:

- Assembly version attributes
- Package version (for packable projects)
- MSBuild properties (`$(Version)`, `$(AssemblyVersion)`, `$(FileVersion)`, `$(InformationalVersion)`)

### Quick Start

1. Install the package in your project
2. Create an initial version tag:
   ```bash
   git tag v1.0.0
   ```
3. Build your project:
   ```bash
   dotnet build
   ```

The tool will automatically calculate versions based on your git history and changes.

## What's New in v3.0

- **Conventional Commits**: Enable semantic versioning based on commit message conventions
- **Changelog Generation**: Automatically generate changelogs during build
- **File Pattern Detection**: Control version bumps based on which files changed
- **Git Enhancements**: Shallow clone support, custom tag patterns, submodule detection
- **Additional Directory Monitoring**: Track changes in shared libraries outside project directory

## Configuration

Configure behavior using MSBuild properties:

```xml
<PropertyGroup>
  <MonoRepoDebug>true</MonoRepoDebug>
  <MonoRepoTagPrefix>v</MonoRepoTagPrefix>
  <MonoRepoPrereleaseType>beta</MonoRepoPrereleaseType>

  <!-- Enable conventional commits for semantic versioning -->
  <MonoRepoConventionalCommitsEnabled>true</MonoRepoConventionalCommitsEnabled>

  <!-- Enable file pattern-based change detection -->
  <MonoRepoChangeDetectionEnabled>true</MonoRepoChangeDetectionEnabled>
  <MonoRepoIgnoreFilePatterns>**/*.md;**/docs/**</MonoRepoIgnoreFilePatterns>

  <!-- Enable automatic changelog generation -->
  <MonoRepoGenerateChangelog>true</MonoRepoGenerateChangelog>
  <MonoRepoChangelogFormat>markdown</MonoRepoChangelogFormat>
</PropertyGroup>
```

Common properties:

- `MonoRepoDebug` - Enable debug logging
- `MonoRepoTagPrefix` - Version tag prefix (default: `v`)
- `MonoRepoPrereleaseType` - Prerelease type (none, alpha, beta, rc)
- `MonoRepoConfigFile` - Path to YAML configuration file
- `MonoRepoConventionalCommitsEnabled` - Enable conventional commits analysis
- `MonoRepoChangeDetectionEnabled` - Enable file pattern-based change detection
- `MonoRepoGenerateChangelog` - Generate changelog during build

## Documentation

For complete documentation, including:
- Configuration options
- Versioning rules and strategies
- Feature branch support
- YAML configuration
- Advanced features

See the [main README](../README.md).

## Requirements

- .NET Framework 4.7.2 or .NET 8.0+
- Git repository
- MSBuild 15.0+

## License

MIT License - see the LICENSE file for details.
