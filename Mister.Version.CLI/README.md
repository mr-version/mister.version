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

# Boolean options take an optional value
mr-version report --include-commits false --changed-only
```

Every project in a report is versioned exactly as `mr-version version` (and the MSBuild task) would version it,
so `mr-version.yml` settings such as `tagPrefix`, `commitConventions`, `changeDetection`, `gitIntegration`,
`constraints` and `scheme` apply.

Graph output: Mermaid edges are `Dependency -->|version| Dependent`, DOT edges `Dependency -> Dependent [label="version"]`,
and the ASCII tree starts from the projects with no dependencies, listing below each project the projects that depend on
it (a project with several dependencies is expanded once and marked `(see above)` elsewhere; cycles are listed under
"Circular Dependencies").

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo` | Repository root path | `.` |
| `-p, --project-dir` | Directory containing the projects | Repository root |
| `-o, --output` | Output format (`text`, `json`, `csv`, `graph`) | `text` |
| `-f, --file` | Output file path | Console |
| `-b, --branch` | Branch name to version as (the checked-out commit is still what is versioned) | Checked-out branch |
| `-t, --tag-prefix` | Prefix for version tags | `tagPrefix` from `mr-version.yml`, else `v` |
| `--include-commits [true\|false]` | Include commit information | `true` |
| `--include-dependencies [true\|false]` | Include dependency information | `true` |
| `--include-test-projects [true\|false]` | Include test projects | `false` |
| `--include-non-packable [true\|false]` | Include non-packable projects | `false` |
| `--graph-format` | Graph format (`mermaid`, `dot`, `ascii`) | `mermaid` |
| `--show-versions [true\|false]` | Show version numbers in the graph | `true` |
| `--changed-only [true\|false]` | Show only projects with version changes | `false` |
| `--config-file` | Path to `mr-version.yml` | Auto-detected |

A boolean option given without a value means `true`.

### Calculate Single Project Version

```bash
# Get version for a specific project
mr-version version -p src/MyProject/MyProject.csproj

# Detailed output with reasoning (branch, previous version, conventional commit analysis, validation)
mr-version version -p src/MyProject/MyProject.csproj -d

# JSON output
mr-version version -p src/MyProject/MyProject.csproj -j

# Version as a given branch (e.g. a detached-HEAD CI checkout)
mr-version version -p src/MyProject/MyProject.csproj -b release/2.0

# Also count changes in other paths (in addition to the project's ProjectReferences)
mr-version version -p src/MyProject/MyProject.csproj --dependencies "shared;build/common.props"
```

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo` | Repository root path; `-p` and `--dependencies` are relative to it | `.` |
| `-p, --project` | Project file (required) | |
| `-t, --tag-prefix` | Prefix for version tags | `tagPrefix` from `mr-version.yml`, else `v` |
| `-b, --branch` | Branch name to version as (overrides the checked-out branch; for a detached HEAD the branch is otherwise taken from the CI environment or a branch whose tip is HEAD) | Checked-out branch |
| `--dependencies` | Extra paths (projects or directories) whose changes count for the project, separated by `;`, `,` or spaces | None |
| `--prerelease-type` | Prerelease type for main/dev branches (`none`, `alpha`, `beta`, `rc`) | `none` |
| `--force-version` | Use this version instead of the calculated one | |
| `-d, --detailed` | Detailed text output | `false` |
| `-j, --json` | JSON output (warnings and debug output go to stderr) | `false` |
| `--dry-run` | Accepted for compatibility; `version` never changes anything | `false` |
| `--config-file` | Path to `mr-version.yml` | Auto-detected |
| `--enable-file-cache`, `--file-cache-path` | Cache results in `.mrversion` | Off |

The version is calculated the same way the MSBuild task calculates it: the project's direct and transitive
`ProjectReference`s are tracked, and the project type is detected as MSBuild evaluates it:

- a project is **packable** unless it sets `<IsPackable>false</IsPackable>` (console apps are packable, as in the .NET SDK);
- a project is a **test project** when it sets `<IsTestProject>true</IsTestProject>` or references `Microsoft.NET.Test.Sdk`
  (referencing only xunit/NUnit/MSTest does not make a library a test project); test projects are not packable unless
  they set `<IsPackable>true</IsPackable>`;
- unconditional values in the project's `Directory.Build.props` (and the ones it imports further up, up to the
  repository root) apply, and the project file wins over them. Conditional properties are ignored.

Test and non-packable projects are not versioned (they report the base version) unless `mr-version.yml` sets
`skipTestProjects: false` / `skipNonPackableProjects: false`.

**Tag prefix:** `-t/--tag-prefix` if given, else `tagPrefix` from `mr-version.yml`, else `v`. Note that when
`mr-version.yml` sets `tagPrefix`, the version calculation itself uses the YAML value even if `-t` is given (YAML
settings take precedence over command-line and MSBuild values in the core); the CLI prints a warning in that case.

JSON output fields: `project`, `path`, `version`, `versionChanged`, `changeReason`, `previousVersion`,
`previousCommitSha`, `commitSha`, `commitDate`, `commitMessage`, `branchType`, `branchName`, `commitHeight`,
`isTestProject`, `isPackable`, `dependencies`, `scheme`, `calver`, `conventionalCommits`, `validation`. When the
version violates configured constraints the command exits with 1 (with `-j` the JSON, including `validation`, is
still written).

### Generate Changelog

```bash
# Generate changelog from last tag to HEAD
mr-version changelog

# Generate for specific version range
mr-version changelog --from 2.2.0 --to 2.3.0

# Only the commits that touch one project (name or project file path)
mr-version changelog --project MyProject
mr-version changelog --project src/MyProject/MyProject.csproj

# Output to file
mr-version changelog --output-file CHANGELOG.md

# Different formats
mr-version changelog --output markdown  # Markdown (default)
mr-version changelog --output text      # Plain text
mr-version changelog --output json      # JSON
```

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo` | Repository root path | `.` |
| `-p, --project` | Project name or project file path: only commits touching the project's directory are included, and project tags (`MyProject/v1.2.3`, `MyProject-v1.2.3`) are used | All commits |
| `-f, --from` / `-t, --to` | Version range (tags) | Previous version tag / HEAD |
| `-o, --output` | `markdown`, `text` or `json` | `markdown` |
| `--output-file` | Output file path | Console |
| `--tag-prefix` | Prefix for version tags | `tagPrefix` from `mr-version.yml`, else `v` |
| `--repo-url` | Repository URL for links | `changelog.repositoryUrl` |
| `--include-authors [true\|false]` | Include commit authors | `changelog.includeAuthors`, else `false` |
| `--include-commits [true\|false]` | Include commit links | `changelog.includeCommitLinks`, else `true` |
| `--include-issues [true\|false]` | Include issue references | `changelog.includeIssueReferences`, else `true` |
| `--include-prs [true\|false]` | Include PR references | `changelog.includePullRequestReferences`, else `true` |
| `--group-breaking [true\|false]` | Group breaking changes | `changelog.groupBreakingChanges`, else `true` |

`mr-version.yml` `changelog:` settings apply unless the corresponding flag is given. When the end of the range is not
tagged, the header shows the project's calculated version (with `--project`) or `Unreleased`.

### Output Streams

Results go to stdout; warnings, errors and command-line parse errors go to stderr (`--help` goes to stdout). Output
is UTF-8 on every platform.

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
