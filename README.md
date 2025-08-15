# Mister.Version

![version](https://img.shields.io/badge/version-1.1.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

A sophisticated automatic versioning system for .NET monorepos built on MSBuild with enhanced support for development workflows and feature branches. Mister.Version (MR Version → MonoRepo Version) provides intelligent, change-based versioning that increases version numbers only when actual changes are detected in a project or its dependencies.

## ✨ What's New in v1.1.0

- **🏗️ Refactored Architecture**: Shared core library between MSBuild task and CLI tool
- **🔧 Dev Branch Support**: Development branches now increment patch versions like main branches
- **📊 Enhanced Feature Branches**: Feature branches include commit height in versioning (e.g., `v3.0.4-feature.1-{git-hash}`)
- **📄 JSON Reporting**: Full JSON report generation capability
- **🎯 Improved Change Detection**: Better detection of dependency changes and package updates
- **🧪 Enhanced Testing**: Comprehensive test coverage for core functionality
- **📊 Dependency Graph Visualization**: Generate visual dependency graphs in Mermaid, DOT, and ASCII formats

## Architecture

The solution is now structured with a shared core library:

```
Mister.Version.Core/          # Shared versioning logic
├── Models/                   # Data models and enums
├── Services/                 # Core services (Git, Versioning, Reporting)
└── Mister.Version.Core.csproj

Mister.Version/               # MSBuild task
├── MonoRepoVersionTask.cs    # MSBuild integration
└── Mister.Version.csproj

Mister.Version.CLI/           # Command-line tool
├── Program.cs                # CLI interface
└── Mister.Version.CLI.csproj
```

## Features

- **Change-Based Versioning**: Version numbers only increment when actual code changes are detected
- **Dependency-Aware**: Automatically bumps versions when dependencies change
- **Enhanced Branch Support**: Different versioning strategies for main, dev, release, and feature branches
- **Feature Branch Commit Height**: Feature branches include commit count for better traceability
- **Package Lock Detection**: Detects changes in dependencies via packages.lock.json
- **Project Type Filtering**: Skip versioning for test projects and non-packable projects
- **Multiple Output Formats**: Text, JSON, CSV, and dependency graph reporting
- **MSBuild Integration**: Seamlessly integrates with your build process
- **Zero-Commit Approach**: No need to commit version changes to your repo
- **Customizable**: Extensive configuration options

## How It Works

MonoRepo Versioning uses Git history to intelligently determine when to increment versions. At build time, it:

1. Identifies the current branch type (main, dev, release, feature)
2. Determines the base version from tags and branch context
3. Checks for changes in the project and its dependencies
4. Applies appropriate versioning rules based on context
5. Injects the calculated version into MSBuild properties

### Enhanced Versioning Rules

- **Main Branch**: Commits to main increment the patch version (8.2.0 → 8.2.1)
- **Dev Branch**: ✨ **NEW** - Dev branches also increment patch versions (8.2.0 → 8.2.1)
- **Release Branches**: Patch increments for changes in the branch (7.3.0 → 7.3.2)
- **Feature Branches**: ✨ **ENHANCED** - Include commit height and hash (8.2.0-feature-name.3-abc1234)

### Feature Branch Versioning

Feature branches now use a more sophisticated versioning scheme:

```
Format: {base-version}-{branch-name}.{commit-height}-{git-hash}
Example: v3.0.4-feature.1-abc1234
```

Where:
- `3.0.4` is the base version from the last tag
- `feature` is the normalized branch name
- `1` is the number of commits since the base tag
- `abc1234` is the short Git hash

## Getting Started

### Prerequisites

- .NET SDK 6.0+ (supports both .NET Framework 4.7.2 and .NET 8.0)
- Git installed and accessible in PATH
- A .NET solution using MSBuild

### Installation

#### Option 1: NuGet Package

```bash
dotnet add package Mister.Version --version 1.1.0
```

#### Option 2: CLI Tool

```bash
dotnet tool install --global Mister.Version.CLI --version 1.1.0
```

### Basic Setup

1. Add the targets file reference to each project that should use automatic versioning:

```xml
<Import Project="$(MSBuildThisFileDirectory)..\..\build\Mister.Version.targets" />
```

2. Create an initial version tag for your repository:

```bash
git tag v1.0.0
```

3. Build your solution:

```bash
dotnet build
```

The tool will automatically calculate and apply versions to your assemblies and packages.

## CLI Usage

The CLI tool provides comprehensive reporting and single-project version calculation:

### Generate Version Report

```bash
# Generate a text report
mr-version report

# Generate JSON report
mr-version report -o json -f versions.json

# Include test projects and detailed info
mr-version report --include-test-projects --include-commits

# Generate CSV for analysis
mr-version report -o csv -f report.csv

# Generate dependency graph
mr-version report -o graph

# Generate Mermaid graph for GitHub
mr-version report -o graph --graph-format mermaid

# Generate DOT graph for Graphviz
mr-version report -o graph --graph-format dot -f dependencies.dot

# Generate ASCII tree in console
mr-version report -o graph --graph-format ascii

# Show only changed projects in graph
mr-version report -o graph --changed-only
```

#### CLI Options

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo` | Repository root path | Current directory |
| `-p, --project-dir` | Projects directory | Repository root |
| `-o, --output` | Output format (text, json, csv, graph) | `text` |
| `-f, --file` | Output file path | Console |
| `-b, --branch` | Branch to analyze | Current branch |
| `--include-commits` | Include commit information | `true` |
| `--include-dependencies` | Include dependency information | `true` |
| `--include-test-projects` | Include test projects | `false` |
| `--include-non-packable` | Include non-packable projects | `false` |
| `--graph-format` | Graph format (mermaid, dot, ascii) | `mermaid` |
| `--show-versions` | Show version numbers in graph nodes | `true` |
| `--changed-only` | Show only projects with changes | `false` |

### Calculate Single Project Version

```bash
# Get version for specific project
mr-version version -p src/MyProject/MyProject.csproj

# Detailed output with reasoning
mr-version version -p src/MyProject/MyProject.csproj -d

# JSON output for automation
mr-version version -p src/MyProject/MyProject.csproj -j
```

## Report Formats

### Dependency Graph Visualization

MonoRepo Versioning can generate visual dependency graphs in multiple formats to help understand project relationships and dependencies:

#### Mermaid Format (GitHub Compatible)

Perfect for GitHub markdown files and documentation:

```bash
mr-version report -o graph --graph-format mermaid
```

```mermaid
graph TD
    %% MonoRepo Dependency Graph with Versions

    Core["Core<br/>1.2.3"]
    class Core changed;
    
    ProjectA["ProjectA<br/>1.2.4"]
    class ProjectA packable;
    
    ProjectB_Tests["ProjectB.Tests<br/>1.0.0"]
    class ProjectB_Tests test;

    Core --> ProjectA
    ProjectA --> ProjectB_Tests

    classDef changed fill:#ff9999,stroke:#ff0000,stroke-width:2px,color:#000;
    classDef test fill:#ccccff,stroke:#0000ff,stroke-width:1px,color:#000;
    classDef packable fill:#ccffcc,stroke:#00aa00,stroke-width:1px,color:#000;
    classDef normal fill:#f9f9f9,stroke:#333,stroke-width:1px,color:#000;
```

#### DOT Format (Graphviz)

For professional diagrams and complex visualizations:

```bash
mr-version report -o graph --graph-format dot -f dependencies.dot
```

```dot
digraph MonoRepoDependencies {
    rankdir=TD;
    node [shape=box, style=filled];
    edge [color=gray];

    Core [label="Core\n1.2.3", fillcolor="#ff9999"];
    ProjectA [label="ProjectA\n1.2.4", fillcolor="#ccffcc"];
    ProjectB_Tests [label="ProjectB.Tests\n1.0.0", fillcolor="#ccccff"];

    Core -> ProjectA [label="1.2.3"];
    ProjectA -> ProjectB_Tests [label="1.2.4"];
}
```

#### ASCII Format (Console)

For quick terminal viewing:

```bash
mr-version report -o graph --graph-format ascii
```

```
=== MonoRepo Dependency Graph ===

🔄 Core (1.2.3) [CHANGED]
  📦 ProjectA (1.2.4)
    🧪 ProjectB.Tests (1.0.0)
```

#### Graph Features

- **Visual Project Status**: 
  - 🔄 Changed projects (red in Mermaid/DOT)
  - 📦 Packable projects (green in Mermaid/DOT)
  - 🧪 Test projects (blue in Mermaid/DOT)
  - 📁 Other projects (gray in Mermaid/DOT)

- **Version Information**: Show calculated versions on nodes (`--show-versions`)
- **Dependency Edges**: Visual connections showing project dependencies
- **Filtering Options**: Show only changed projects (`--changed-only`)

### JSON Report Example

```json
{
  "repository": "/path/to/repo",
  "branch": "feature/new-feature",
  "branchType": "Feature",
  "globalVersion": "3.0.4",
  "generatedAt": "2024-01-15T10:30:00Z",
  "totalProjects": 5,
  "projectsWithChanges": 2,
  "projects": [
    {
      "name": "Core",
      "path": "src/Core/Core.csproj",
      "version": {
        "version": "3.0.4-feature.1-abc1234",
        "semVer": {
          "major": 3,
          "minor": 0,
          "patch": 4,
          "preRelease": "feature.1-abc1234"
        },
        "versionChanged": true,
        "changeReason": "Feature branch: Using pre-release version with commit height 1",
        "commitHeight": 1,
        "branchType": "Feature"
      },
      "dependencies": {
        "direct": [],
        "all": []
      },
      "isTestProject": false,
      "isPackable": true
    }
  ]
}
```

## Configuration

### MSBuild Properties

| Property | Description | Default |
|----------|-------------|---------|
| `MonoRepoVersionEnabled` | Enable/disable automatic versioning | `true` |
| `MonoRepoVersionRepoRoot` | Path to the Git repository root | Auto-detected |
| `MonoRepoVersionTagPrefix` | Prefix for version tags | `v` |
| `MonoRepoVersionUpdateProjectFile` | Update project files with versions | `false` |
| `MonoRepoVersionDebug` | Enable debug logging | `false` |
| `MonoRepoVersionExtraDebug` | Enable extra debug logging | `false` |
| `MonoRepoVersionSkipTestProjects` | Skip versioning for test projects | `true` |
| `MonoRepoVersionSkipNonPackableProjects` | Skip versioning for non-packable projects | `true` |
| `MonoRepoVersionForce` | Force a specific version | Empty |
| `MonoRepoVersionCreateTag` | Create a git tag for the calculated version | `false` |
| `MonoRepoVersionTagMessage` | Custom message for the git tag | Auto-generated |

Example configuration:

```xml
<PropertyGroup>
  <MonoRepoVersionDebug>true</MonoRepoVersionDebug>
  <MonoRepoVersionTagPrefix>release-</MonoRepoVersionTagPrefix>
</PropertyGroup>
```

Example for automated tag creation during CI/CD:

```xml
<PropertyGroup Condition="'$(CI)' == 'true' and '$(CreateReleaseTags)' == 'true'">
  <MonoRepoVersionCreateTag>true</MonoRepoVersionCreateTag>
  <MonoRepoVersionTagMessage>Automated release from CI build $(BUILD_NUMBER)</MonoRepoVersionTagMessage>
</PropertyGroup>
```

## Advanced Features

### Global vs. Project-Specific Tags

MonoRepo Versioning supports two types of tags:

1. **Project-specific tags** (default): Apply to individual projects
   - Format: `v1.2.3-projectname` (e.g., `v1.2.3-core`)
   - Used for patch and minor releases
   - Allows independent versioning of projects in the monorepo

2. **Global tags**: Apply to the entire repository
   - Format: `v1.0.0` (no project suffix)
   - Created automatically for major version releases (x.0.0)
   - Used to mark significant milestones across the entire monorepo

The system automatically creates project-specific tags by default, and only creates global tags when releasing a new major version (e.g., going from v1.x.x to v2.0.0).

### Enhanced Change Detection

The system examines multiple sources for changes:

- Direct project file modifications
- Project dependency changes
- Package dependency updates (packages.lock.json)
- Transitive dependency version changes

### Feature Branch Workflow

Feature branches provide detailed versioning for development workflows:

```bash
# Start feature branch from main (v3.0.4)
git checkout -b feature/awesome-feature

# Make changes and commit
git add .
git commit -m "Add awesome feature"

# Build shows: v3.0.4-awesome-feature.1-abc1234
dotnet build

# More commits increase the height
git commit -m "Fix awesome feature"
# Build shows: v3.0.4-awesome-feature.2-def5678
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Build and Version
on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4
      with:
        fetch-depth: 0  # Required for git history
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'
    
    - name: Install CLI tool
      run: dotnet tool install --global Mister.Version.CLI
    
    - name: Generate version report
      run: mr-version report -o json -f version-report.json
    
    - name: Build with versioning
      run: dotnet build --configuration Release
    
    - name: Upload version report
      uses: actions/upload-artifact@v4
      with:
        name: version-report
        path: version-report.json
```

## Debugging and Troubleshooting

Enable debug logging to see detailed information about the versioning process:

```bash
dotnet build /p:MonoRepoVersionDebug=true -v:n
```

For even more details:

```bash
dotnet build /p:MonoRepoVersionExtraDebug=true -v:n
```

This will display:
- Branch type detection
- Tag analysis and selection
- Change detection results
- Dependency graph analysis
- Feature branch commit height calculation
- Detailed reasoning for version decisions

### Common Issues

1. **Versions not incrementing**: Check that changes were detected in the project files
2. **Wrong version on feature branch**: Verify branch naming and check for changes
3. **Feature branch missing commit height**: Ensure there's a base tag to calculate from
4. **Package has wrong version**: Ensure MSBuild properties are correctly set

## Migration from v1.0.x

The new architecture is backward compatible, but you may want to take advantage of new features:

1. **Dev branches**: No changes needed - dev branches will now increment patch versions
2. **Feature branch enhancements**: Feature branches automatically get commit height
3. **JSON reporting**: Use the CLI tool with `-o json` for structured reports
4. **Enhanced change detection**: Automatic - better dependency change detection

## Contributing

Contributions are welcome! The new modular architecture makes it easier to contribute:

1. **Core Logic**: Add features to `Mister.Version.Core`
2. **MSBuild Integration**: Enhance the MSBuild task
3. **CLI Features**: Extend the command-line interface
4. **Testing**: Add tests for core functionality

## License

This project is licensed under the MIT License - see the LICENSE file for details.

---

**Mister.Version** - Making monorepo versioning simple, intelligent, and developer-friendly.