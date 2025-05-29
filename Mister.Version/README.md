# Mister.Version MSBuild Integration

This package provides MSBuild integration for Mister.Version, enabling automatic version calculation for .NET projects in monorepos.

## Features

- **Automatic Version Calculation**: Generates semantic versions based on git history and changes
- **Monorepo Support**: Independent versioning for multiple projects in a single repository
- **MSBuild Integration**: Seamless integration with the .NET build process
- **Cross-Platform**: Works on Windows, macOS, and Linux
- **Git Tag Support**: Creates and manages git tags for releases

## Installation

Install the package via NuGet Package Manager:

```bash
dotnet add package Mister.Version
```

Or add it to your project file:

```xml
<PackageReference Include="Mister.Version" Version="1.1.0-rc.1" />
```

## Configuration

Create a `mr-version.yml` configuration file in your repository root:

```yaml
projects:
  - name: "MyProject"
    path: "src/MyProject"
    dependencies: []
```

## Usage

Once installed, Mister.Version will automatically calculate and inject version information during the build process. The version will be available in:

- Assembly version attributes
- Package version (for packable projects)
- MSBuild properties

### MSBuild Properties

The following MSBuild properties are available:

- `$(Version)`: The calculated semantic version
- `$(AssemblyVersion)`: Assembly version (major.minor.0.0)
- `$(FileVersion)`: File version (major.minor.patch.0)
- `$(InformationalVersion)`: Full version with metadata

### Creating Tags

To create git tags during build, set the `CreateTag` property:

```xml
<PropertyGroup>
  <CreateTag>true</CreateTag>
  <TagMessage>Release $(Version)</TagMessage>
</PropertyGroup>
```

## Advanced Configuration

### Custom Version Calculation

You can customize version calculation by configuring the MSBuild task properties:

```xml
<PropertyGroup>
  <MisterVersionConfigPath>custom-config.yaml</MisterVersionConfigPath>
  <MisterVersionTagPrefix>v</MisterVersionTagPrefix>
  <MisterVersionCreateGlobalTags>true</MisterVersionCreateGlobalTags>
</PropertyGroup>
```

### Branch-Specific Versioning

Mister.Version supports different versioning strategies based on branch types:

- **Feature branches**: `1.0.0-feature-branch-name.1`
- **Release branches**: `1.0.0-rc.1`
- **Development branches**: `1.0.0-dev.1`
- **Main/master branches**: `1.0.0`

## Requirements

- .NET Framework 4.7.2 or .NET 8.0+
- Git repository
- MSBuild 15.0+

## Documentation

For complete documentation and advanced usage scenarios, visit the [Mister.Version repository](https://github.com/discrete-sharp/Mister.Version).

## License

MIT License - see the LICENSE file for details.