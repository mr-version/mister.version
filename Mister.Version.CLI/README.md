# Mister.Version CLI

![version](https://img.shields.io/badge/version-3.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

Command-line tool for analyzing, reporting, and managing versions across your .NET monorepo.

## Features

- **Version Reports**: Generate reports in multiple formats (text, JSON, CSV, dependency graphs)
- **Version Calculation**: Calculate versions for individual projects with detailed reasoning
- **Changelog Generation**: Automatically generate changelogs from conventional commits
- **Dependency Visualization**: Visualize dependency graphs (Mermaid, DOT, ASCII)
- **Conventional Commits**: Smart semantic versioning based on commit message conventions
- **Branch-Aware Versioning**: Different strategies for main, dev, release, and feature branches

## Installation

### Global Tool

```bash
dotnet tool install --global Mister.Version.CLI --version 3.0.0
```

### Local Tool

```bash
dotnet new tool-manifest  # if you don't have one already
dotnet tool install --local Mister.Version.CLI --version 3.0.0
```

## Quick Reference

### Generate Version Report

```bash
# Text report
mr-version report

# JSON report
mr-version report -o json -f versions.json

# CSV report
mr-version report -o csv -f report.csv

# Dependency graph (Mermaid)
mr-version report -o graph --graph-format mermaid

# Dependency graph (DOT for Graphviz)
mr-version report -o graph --graph-format dot -f dependencies.dot

# ASCII tree
mr-version report -o graph --graph-format ascii
```

### Calculate Single Project Version

```bash
# Get version for a specific project
mr-version version -p src/MyProject/MyProject.csproj

# Detailed output with reasoning
mr-version version -p src/MyProject/MyProject.csproj -d

# JSON output
mr-version version -p src/MyProject/MyProject.csproj -j
```

### Generate Changelog

```bash
# Generate changelog from last tag to HEAD
mr-version changelog

# Generate for specific version range
mr-version changelog --from 2.2.0 --to 2.3.0

# Output to file
mr-version changelog --output-file CHANGELOG.md

# Different formats
mr-version changelog --output markdown  # Markdown (default)
mr-version changelog --output text      # Plain text
mr-version changelog --output json      # JSON
```

### Common Options

| Option | Description |
|--------|-------------|
| `-r, --repo` | Repository root path |
| `-o, --output` | Output format (text, json, csv, graph) |
| `-f, --file` | Output file path |
| `--include-test-projects` | Include test projects |
| `--graph-format` | Graph format (mermaid, dot, ascii) |
| `--changed-only` | Show only projects with changes |

## Documentation

For complete documentation, configuration options, and usage examples, see the [main README](../README.md).

## CI/CD Integration

### GitHub Actions

```yaml
- name: Install CLI tool
  run: dotnet tool install --global Mister.Version.CLI

- name: Generate version report
  run: mr-version report -o json -f version-report.json

- name: Upload version report
  uses: actions/upload-artifact@v4
  with:
    name: version-report
    path: version-report.json
```

## License

MIT License - see the LICENSE file for details.
