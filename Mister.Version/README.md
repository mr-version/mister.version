# Mister.Version MSBuild Integration

![version](https://img.shields.io/badge/version-1.1.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

MSBuild integration package for Mister.Version, enabling automatic version calculation for .NET projects in monorepos.

## Features

- **Automatic Version Calculation**: Generates semantic versions based on git history and changes
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
<PackageReference Include="Mister.Version" Version="1.1.0" />
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

## Configuration

Configure behavior using MSBuild properties:

```xml
<PropertyGroup>
  <MonoRepoDebug>true</MonoRepoDebug>
  <MonoRepoTagPrefix>v</MonoRepoTagPrefix>
  <MonoRepoPrereleaseType>beta</MonoRepoPrereleaseType>
</PropertyGroup>
```

Common properties:

- `MonoRepoDebug` - Enable debug logging
- `MonoRepoTagPrefix` - Version tag prefix (default: `v`)
- `MonoRepoPrereleaseType` - Prerelease type (none, alpha, beta, rc)
- `MonoRepoConfigFile` - Path to YAML configuration file

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
