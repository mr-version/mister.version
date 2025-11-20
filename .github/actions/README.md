# Mister.Version GitHub Actions

This directory contains a comprehensive suite of GitHub Actions for integrating Mister.Version into your CI/CD workflows, as well as testing actions for validating the tool across different platforms.

## Architecture

The production actions are implemented as **separate repositories** and referenced as **git submodules** in this directory. This architecture provides:

- **Independent versioning**: Each action can be versioned and released separately
- **Modular development**: Actions can be developed and tested independently
- **Marketplace ready**: Each action can be published to GitHub Actions Marketplace
- **Better separation**: Clear boundaries between action responsibilities

### Action Repositories

All production actions are hosted under the `mr-version` GitHub organization:

| Action | Repository | Status |
|--------|------------|--------|
| **setup** | [mr-version/setup](https://github.com/mr-version/setup) | ✅ Implemented (TypeScript) |
| **calculate** | [mr-version/calculate](https://github.com/mr-version/calculate) | ✅ Implemented (TypeScript) + Validation |
| **report** | [mr-version/report](https://github.com/mr-version/report) | ✅ Implemented (TypeScript) |
| **tag** | [mr-version/tag](https://github.com/mr-version/tag) | ✅ Implemented (TypeScript) |
| **release** | [mr-version/release](https://github.com/mr-version/release) | ✅ Implemented (Composite) + Validation |
| **changelog** | [mr-version/changelog](https://github.com/mr-version/changelog) | ✅ Implemented (TypeScript) |

### Submodule Configuration

The actions are referenced in `.gitmodules` with HTTPS URLs for broad accessibility:

```
[submodule ".github/actions/setup"]
    path = .github/actions/setup
    url = https://github.com/mr-version/setup.git
```

To initialize submodules after cloning:
```bash
git submodule update --init --recursive
```

## Production Actions

> **Note:** These actions can be referenced using either the submodule path (`./.github/actions/setup`) when used within this repository, or the GitHub repository reference (`mr-version/setup@main`) when used externally.

### 1. **setup** - Install Mister.Version CLI
**Status:** ✅ Implemented
**Repository:** [mr-version/setup](https://github.com/mr-version/setup)

Sets up the Mister.Version CLI tool in your GitHub Actions workflow.

**Usage:**
```yaml
# Reference via submodule (within this repo)
- uses: ./.github/actions/setup
  with:
    version: 'latest'

# Reference via GitHub (external repos)
- uses: mr-version/setup@main
  with:
    version: 'latest'
```

### 2. **calculate** - Calculate Versions
**Status:** ✅ Implemented
**Repository:** [mr-version/calculate](https://github.com/mr-version/calculate)

Calculates versions for multiple projects based on changes and dependencies.

**Usage:**
```yaml
- uses: ./.github/actions/calculate
  with:
    projects: '**/*.csproj'
    prerelease-type: 'beta'
```

### 3. **report** - Generate Version Reports
**Status:** ✅ Implemented
**Repository:** [mr-version/report](https://github.com/mr-version/report)

Generates comprehensive version reports in multiple formats.

**Usage:**
```yaml
- uses: ./.github/actions/report
  with:
    output-format: 'markdown'
    post-to-pr: true
```

### 4. **tag** - Create Git Tags
**Status:** ✅ Implemented
**Repository:** [mr-version/tag](https://github.com/mr-version/tag)

Creates git tags for projects with version changes.

**Usage:**
```yaml
- uses: ./.github/actions/tag
  with:
    create-global-tags: true
    global-tag-strategy: 'major-only'
```

### 5. **release** - Complete Release Workflow
**Status:** ✅ Implemented (Composite Action)
**Repository:** [mr-version/release](https://github.com/mr-version/release)

Complete release workflow that orchestrates all the above actions: setup → calculate → tag → report.

**Usage:**
```yaml
- uses: ./.github/actions/release
  with:
    create-tags: true
    generate-report: true
    post-report-to-pr: true
```

### 6. **changelog** - Generate Changelogs
**Status:** ✅ Implemented
**Repository:** [mr-version/changelog](https://github.com/mr-version/changelog)

Generates changelogs from conventional commits with support for multiple output formats.

**Usage:**
```yaml
- uses: ./.github/actions/changelog
  with:
    output-format: 'markdown'
    output-file: 'CHANGELOG.md'
    from-version: '1.0.0'
    to-version: '1.1.0'
    include-authors: true
    post-to-pr: true
```

**Key Features:**
- Multiple output formats (markdown, text, json)
- Conventional commit parsing with breaking change detection
- Issue and PR reference linking
- Post changelog directly to PR comments
- Support for monorepo project filtering

## Version Validation Support

The **calculate** and **release** actions now include comprehensive version validation support:

### Features
- **Automatic Validation**: Version constraints configured in `mr-version.yml` are automatically validated
- **GitHub Annotations**: Validation errors and warnings appear as annotations in PR checks
- **Job Summary**: Validation results are included in the action summary
- **Fail on Error**: Actions fail automatically when version constraints are violated

### Validation Constraints
Configure validation in your `mr-version.yml`:
```yaml
constraints:
  minimumVersion: "2.0.0"          # Never go below this version
  maximumVersion: "5.0.0"          # Never exceed this version
  allowedRange: "3.x.x"            # Stay within version range
  requireMonotonicIncrease: true   # Versions must always increase
  blockedVersions: ["1.2.3"]       # Skip specific versions
  requireMajorApproval: true       # Prevent accidental major bumps
```

### Validation Outputs
Both **calculate** and **release** actions now provide:
- `has-validation-errors`: Boolean indicating if any validation errors occurred
- `has-validation-warnings`: Boolean indicating if any validation warnings occurred
- `validation-errors-count`: Number of projects with validation errors
- `validation-warnings-count`: Number of projects with validation warnings

### Example Usage
```yaml
- name: Calculate Versions
  id: calculate
  uses: ./.github/actions/calculate
  with:
    projects: '**/*.csproj'

- name: Check Validation
  if: steps.calculate.outputs.has-validation-errors == 'true'
  run: |
    echo "❌ Version validation failed!"
    echo "Projects with errors: ${{ steps.calculate.outputs.validation-errors-count }}"
    exit 1
```

## Testing Actions

### test-versioning-linux

Tests the versioning tool on Linux/macOS platforms using bash scripts.

**Usage:**
```yaml
- name: Run Linux Versioning Tests
  uses: ./.github/actions/test-versioning-linux
  with:
    dotnet-version: '8.0.x'  # Optional, defaults to 8.0.x
    working-directory: '.'    # Optional, defaults to current directory
```

### test-versioning-windows

Tests the versioning tool on Windows platforms using PowerShell scripts.

**Usage:**
```yaml
- name: Run Windows Versioning Tests
  uses: ./.github/actions/test-versioning-windows
  with:
    dotnet-version: '8.0.x'  # Optional, defaults to 8.0.x
    working-directory: '.'    # Optional, defaults to current directory
```

## Complete Workflow Examples

> **Note:** These examples use submodule references (`./.github/actions/...`). For external repositories, replace with `mr-version/action-name@main`.

### Basic PR Validation
```yaml
name: Version Check
on:
  pull_request:
    branches: [main]

jobs:
  version-check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          submodules: recursive  # Initialize action submodules

      - uses: ./.github/actions/release
        with:
          create-tags: false
          generate-report: true
          post-report-to-pr: true
          dry-run: true
```

### Release on Main Branch
```yaml
name: Release
on:
  push:
    branches: [main]

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          submodules: recursive  # Initialize action submodules

      - uses: ./.github/actions/release
        with:
          create-tags: true
          create-global-tags: true
          global-tag-strategy: 'major-only'
          generate-report: true
          sign-tags: true
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Custom Version Calculation
```yaml
name: Custom Versioning
on:
  workflow_dispatch:
    inputs:
      prerelease-type:
        description: 'Prerelease type'
        required: false
        default: 'none'
        type: choice
        options: ['none', 'alpha', 'beta', 'rc']

jobs:
  custom-version:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          submodules: recursive  # Initialize action submodules

      - name: Setup Mister.Version
        uses: ./.github/actions/setup

      - name: Calculate Versions
        id: versions
        uses: ./.github/actions/calculate
        with:
          projects: 'src/**/*.csproj'
          prerelease-type: ${{ github.event.inputs.prerelease-type }}
          include-test-projects: false

      - name: Create Tags
        if: steps.versions.outputs.has-changes == 'true'
        uses: ./.github/actions/tag
        with:
          projects: 'src/**/*.csproj'
          include-test-projects: false
          only-changed: true
      
      - name: Generate Report
        uses: ./.github/actions/report
        with:
          output-format: 'json'
          output-file: 'version-report.json'

      - name: Upload Report
        uses: actions/upload-artifact@v4
        with:
          name: version-report
          path: version-report.json
```

### Multi-Format Reporting
```yaml
name: Comprehensive Reporting
on:
  schedule:
    - cron: '0 0 * * 0'  # Weekly

jobs:
  weekly-report:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          submodules: recursive  # Initialize action submodules

      - uses: ./.github/actions/setup

      - name: Generate Markdown Report
        uses: ./.github/actions/report
        with:
          output-format: 'markdown'
          output-file: 'reports/weekly-report.md'
          include-test-projects: true
          include-non-packable: true

      - name: Generate CSV Report
        uses: ./.github/actions/report
        with:
          output-format: 'csv'
          output-file: 'reports/weekly-report.csv'
          include-test-projects: true
          include-non-packable: true
      
      - name: Generate JSON Report
        uses: ./.github/actions/report
        with:
          output-format: 'json'
          output-file: 'reports/weekly-report.json'
          include-test-projects: true
          include-non-packable: true
      
      - name: Upload Reports
        uses: actions/upload-artifact@v4
        with:
          name: weekly-reports
          path: reports/
```

## Common Input Parameters

### Project Selection
- `projects`: Glob pattern for project files (default: `**/*.csproj`)
- `repository-path`: Path to repository root (default: `.`)
- `include-test-projects`: Include test projects (default: `false`)
- `include-non-packable`: Include non-packable projects (default: `false`)

### Version Control
- `tag-prefix`: Prefix for tags (default: `v`)
- `prerelease-type`: Type of prerelease (`none`, `alpha`, `beta`, `rc`)
- `force-version`: Force specific version for all projects

### Behavior
- `dry-run`: Show what would be done without making changes (default: `false`)
- `only-changed`: Only process projects with changes (default: `true`)
- `fail-on-no-changes`: Fail if no changes detected (default: `false`)

### GitHub Integration
- `token`: GitHub token for API access (default: `${{ github.token }}`)
- `post-to-pr`: Post reports as PR comments (default: `false`)

## Output Parameters

### Version Information
- `projects`: JSON array of all analyzed projects
- `changed-projects`: JSON array of projects with changes
- `has-changes`: Boolean indicating if any changes were detected

### Tagging Results
- `tags-created`: JSON array of created tags
- `tags-count`: Number of tags created
- `global-tags-created`: JSON array of global tags created
- `project-tags-created`: JSON array of project-specific tags

### Reports
- `report-content`: Generated report content
- `report-file`: Path to saved report file

## Test Scenarios

The testing actions run comprehensive test scenarios:

1. **Initial Repository (No Tags)** - Tests default versioning behavior
2. **Single Release Tag** - Tests patch version incrementing
3. **Feature Branch Versioning** - Tests feature branch naming conventions
4. **Release Branch Versioning** - Tests release candidate versioning
5. **Monorepo Multiple Projects** - Tests project-specific versioning
6. **Pre-release Versions** - Tests alpha/beta/rc progression
7. **Dev Branch Versioning** - Tests development branch versioning
8. **Build Metadata** - Tests version metadata handling

## Best Practices

1. **Initialize submodules**: Use `submodules: recursive` in checkout action to initialize action submodules (when using local references)
2. **Always fetch full history**: Use `fetch-depth: 0` in checkout action for accurate version calculation
3. **Use appropriate permissions**: Ensure your token has necessary permissions for tag creation and PR comments
4. **Filter projects wisely**: Use filtering options to focus on relevant projects
5. **Use dry-run for testing**: Test your workflow with `dry-run: true` before production use
6. **Sign important tags**: Use `sign-tags: true` for release tags in production

## Troubleshooting

### Common Issues

1. **"Action not found"**: Ensure you've initialized submodules with `submodules: recursive` in your checkout action, or use the GitHub repository reference (`mr-version/action-name@main`) instead
2. **"No project files found"**: Check your `projects` glob pattern
3. **"Permission denied"**: Ensure GitHub token has appropriate permissions
4. **"Tag already exists"**: Use `fail-on-existing-tags: false` or clean up existing tags
5. **"No changes detected"**: Verify that you have commits since the last tag

### Debug Mode
Enable Actions debug logging by setting the `ACTIONS_STEP_DEBUG` secret to `true` in your repository.

### Local Testing
You can test the CLI commands locally before using the actions:
```bash
# Install Mister.Version CLI
dotnet tool install -g Mister.Version.CLI

# Test version calculation
mr-version version --repo . --project MyProject/MyProject.csproj --json

# Test report generation
mr-version report --repo . --output markdown
```

## Contributing

### Contributing to Production Actions

The production actions are maintained in **separate repositories**. To contribute:

1. Clone the specific action repository (e.g., `git clone https://github.com/mr-version/setup.git`)
2. Update the TypeScript source files in the action's `src/` directory
3. Run `npm install` and `npm run build` to compile the action
4. Test with example workflows
5. Submit a pull request to the specific action repository
6. After merging, update the submodule reference in this repository if needed

### Contributing to Testing Actions

The testing actions (test-versioning-linux, test-versioning-windows) are maintained in this repository:

1. Add the test function to both `test-functions.sh` and `test-functions.ps1`
2. Add a new step in both `action.yml` files to call your test
3. Follow the existing naming conventions:
   - Bash: `test_scenario_name`
   - PowerShell: `Test-ScenarioName`

### Updating Submodule References

To update a submodule to the latest version from its repository:

```bash
cd .github/actions/<action-name>
git pull origin main
cd ../../..
git add .github/actions/<action-name>
git commit -m "Update <action-name> submodule to latest version"
```

## Technical Architecture

### Production Actions
- **Separate repositories**: Each action is versioned independently
- **Referenced as submodules**: Using HTTPS URLs for broad accessibility
- **TypeScript-based**: Modern Node.js actions with proper type safety
- **Modular design**: Each action has a specific responsibility
- **Composable**: Actions can be used individually or together (via the release composite action)
- **Error handling**: Comprehensive error handling and reporting

### Testing Actions
- **Platform-specific implementations**: Optimized for each OS
- **Detailed test summaries**: Results are added to GitHub Step Summary
- **Artifact collection**: Failed test repos are uploaded for debugging
- **Isolated test environments**: Each test runs in its own git repository

## License

These actions are part of the Mister.Version project and are licensed under the same terms.