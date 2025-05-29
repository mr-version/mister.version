# Mister.Version Configuration Guide

## MSBuild Properties

Mister.Version can be configured using MSBuild properties in your project file or through a YAML configuration file.

### Available MSBuild Properties

- `MonoRepoPrereleaseType` - Prerelease type for main/dev branches (default: `none`)
  - Options: `none`, `alpha`, `beta`, `rc`
- `MonoRepoConfigFile` - Path to YAML configuration file
- `MonoRepoRoot` - Starting directory for Git repository discovery (default: `$(ProjectDir)../`)
- `MonoRepoTagPrefix` - Prefix for version tags (default: `v`)
- `MonoRepoUpdateProjectFile` - Whether to update project files (default: `false`)
- `MonoRepoDebug` - Enable debug logging (default: `false`)
- `MonoRepoExtraDebug` - Enable extra debug logging (default: `false`)
- `MonoRepoSkipTestProjects` - Skip versioning test projects (default: `true`)
- `MonoRepoSkipNonPackableProjects` - Skip non-packable projects (default: `true`)

### Example Project File Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Use alpha prereleases for this project -->
    <MonoRepoPrereleaseType>alpha</MonoRepoPrereleaseType>
    
    <!-- Use a YAML configuration file -->
    <MonoRepoConfigFile>$(MSBuildProjectDirectory)/../mr-version.yml</MonoRepoConfigFile>
  </PropertyGroup>
</Project>
```

## YAML Configuration

You can use a YAML configuration file for more complex scenarios, including project-specific overrides.

### Example YAML Configuration

```yaml
# Base global version used as fallback when no tags or versions are found
# Applies to all projects including test projects and artifacts normally ignored
baseVersion: "8.2.0"

# Global settings
prereleaseType: none  # Options: none, alpha, beta, rc
tagPrefix: v
skipTestProjects: true
skipNonPackableProjects: true

# Project-specific overrides
projects:
  # Force a specific project to use alpha prereleases
  MyLibrary:
    prereleaseType: alpha
  
  # Pin a specific project to a fixed version
  LegacyProject:
    forceVersion: 1.0.0
  
  # Project that should use beta prereleases
  ExperimentalFeature:
    prereleaseType: beta
```

### Using YAML Configuration

1. Create a `mr-version.yml` file in your repository root (also supports `mr-version.yaml` and `mister-version.yaml` for backward compatibility)
2. The configuration file is automatically detected, or you can reference it explicitly in your project files:

```xml
<PropertyGroup>
  <MonoRepoConfigFile>$(MSBuildProjectDirectory)/../mr-version.yml</MonoRepoConfigFile>
</PropertyGroup>
```

Or set it globally in a Directory.Build.props file:

```xml
<Project>
  <PropertyGroup>
    <MonoRepoConfigFile>$(MSBuildThisFileDirectory)/mr-version.yml</MonoRepoConfigFile>
  </PropertyGroup>
</Project>
```

### Configuration Precedence

Configuration values are applied in this order (later values override earlier ones):

1. Default values in Mister.Version.props
2. MSBuild properties in project files
3. Global settings from YAML configuration file
4. Project-specific settings from YAML configuration file

## Command Line Usage

When using the CLI tool, you can specify the prerelease type:

```bash
# Use alpha prereleases
mister-version --prerelease-type alpha

# Use a configuration file
mister-version --config-file ./mr-version.yml
```

## Prerelease Types

- `none` - No prerelease suffix (e.g., `1.0.0`)
- `alpha` - Alpha prerelease (e.g., `1.0.0-alpha.1`)
- `beta` - Beta prerelease (e.g., `1.0.0-beta.1`)
- `rc` - Release candidate (e.g., `1.0.0-rc.1`)

The tool will automatically increment prerelease numbers when changes are detected.