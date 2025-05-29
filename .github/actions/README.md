# Mister.Version GitHub Actions

This directory contains a comprehensive suite of GitHub Actions for integrating Mister.Version into your CI/CD workflows, as well as testing actions for validating the tool across different platforms.

## Production Actions

### 1. `setup-mister-version`
Sets up the Mister.Version CLI tool in your GitHub Actions workflow.

**Basic Usage:**
```yaml
- uses: ./.github/actions/setup-mister-version
  with:
    version: 'latest'
```

### 2. `mister-version-calculate`
Calculates versions for multiple projects based on changes and dependencies.

**Basic Usage:**
```yaml
- uses: ./.github/actions/mister-version-calculate
  with:
    projects: '**/*.csproj'
    prerelease-type: 'beta'
```

### 3. `mister-version-report`
Generates comprehensive version reports in multiple formats.

**Basic Usage:**
```yaml
- uses: ./.github/actions/mister-version-report
  with:
    output-format: 'markdown'
    post-to-pr: true
```

### 4. `mister-version-tag`
Creates git tags for projects with version changes.

**Basic Usage:**
```yaml
- uses: ./.github/actions/mister-version-tag
  with:
    create-global-tags: true
    global-tag-strategy: 'major-only'
```

### 5. `mister-version-release` (Composite)
Complete release workflow that orchestrates all the above actions.

**Basic Usage:**
```yaml
- uses: ./.github/actions/mister-version-release
  with:
    create-tags: true
    generate-report: true
    post-report-to-pr: true
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
      
      - uses: ./.github/actions/mister-version-release
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
      
      - uses: ./.github/actions/mister-version-release
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
      
      - name: Setup Mister.Version
        uses: ./.github/actions/setup-mister-version
      
      - name: Calculate Versions
        id: versions
        uses: ./.github/actions/mister-version-calculate
        with:
          projects: 'src/**/*.csproj'
          prerelease-type: ${{ github.event.inputs.prerelease-type }}
          include-test-projects: false
      
      - name: Create Tags
        if: steps.versions.outputs.has-changes == 'true'
        uses: ./.github/actions/mister-version-tag
        with:
          projects: 'src/**/*.csproj'
          include-test-projects: false
          only-changed: true
      
      - name: Generate Report
        uses: ./.github/actions/mister-version-report
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
      
      - uses: ./.github/actions/setup-mister-version
      
      - name: Generate Markdown Report
        uses: ./.github/actions/mister-version-report
        with:
          output-format: 'markdown'
          output-file: 'reports/weekly-report.md'
          include-test-projects: true
          include-non-packable: true
      
      - name: Generate CSV Report
        uses: ./.github/actions/mister-version-report
        with:
          output-format: 'csv'
          output-file: 'reports/weekly-report.csv'
          include-test-projects: true
          include-non-packable: true
      
      - name: Generate JSON Report
        uses: ./.github/actions/mister-version-report
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

1. **Always fetch full history**: Use `fetch-depth: 0` in checkout action for accurate version calculation
2. **Use appropriate permissions**: Ensure your token has necessary permissions for tag creation and PR comments
3. **Filter projects wisely**: Use filtering options to focus on relevant projects
4. **Use dry-run for testing**: Test your workflow with `dry-run: true` before production use
5. **Sign important tags**: Use `sign-tags: true` for release tags in production

## Troubleshooting

### Common Issues

1. **"No project files found"**: Check your `projects` glob pattern
2. **"Permission denied"**: Ensure GitHub token has appropriate permissions
3. **"Tag already exists"**: Use `fail-on-existing-tags: false` or clean up existing tags
4. **"No changes detected"**: Verify that you have commits since the last tag

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

## Adding New Tests

To add a new test scenario:

1. Add the test function to both `test-functions.sh` and `test-functions.ps1`
2. Add a new step in both `action.yml` files to call your test
3. Follow the existing naming conventions:
   - Bash: `test_scenario_name`
   - PowerShell: `Test-ScenarioName`

## Contributing

When modifying these actions:

1. Update the TypeScript source files in each action's `src/` directory
2. Run `npm run build` to compile the actions
3. Test with the example workflows
4. Update this documentation if needed

## Architecture

### Production Actions
- **TypeScript-based**: Modern Node.js actions with proper type safety
- **Modular design**: Each action has a specific responsibility
- **Composable**: Actions can be used individually or together
- **Error handling**: Comprehensive error handling and reporting

### Testing Actions
- **Platform-specific implementations**: Optimized for each OS
- **Detailed test summaries**: Results are added to GitHub Step Summary
- **Artifact collection**: Failed test repos are uploaded for debugging
- **Isolated test environments**: Each test runs in its own git repository

## License

These actions are part of the Mister.Version project and are licensed under the same terms.