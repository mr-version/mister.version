# Mister.Version Composite GitHub Actions

This directory contains reusable composite actions for testing the Mister.Version tool across different platforms.

## Available Actions

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

## Test Scenarios

Both actions run the same set of test scenarios:

1. **Initial Repository (No Tags)** - Tests default versioning behavior
2. **Single Release Tag** - Tests patch version incrementing
3. **Feature Branch Versioning** - Tests feature branch naming conventions
4. **Release Branch Versioning** - Tests release candidate versioning
5. **Monorepo Multiple Projects** - Tests project-specific versioning
6. **Pre-release Versions** - Tests alpha/beta/rc progression
7. **Dev Branch Versioning** - Tests development branch versioning
8. **Build Metadata** - Tests version metadata handling

## Features

- **Platform-specific implementations** - Optimized for each OS
- **Detailed test summaries** - Results are added to GitHub Step Summary
- **Artifact collection** - Failed test repos are uploaded for debugging
- **Isolated test environments** - Each test runs in its own git repository
- **Comprehensive logging** - All test output is captured and reported

## Implementation Details

### Linux Action
- Uses bash scripts for test execution
- Test functions are stored in `test-functions.sh`
- Uses ANSI color codes for terminal output
- Compatible with Linux and macOS runners

### Windows Action
- Uses PowerShell scripts for test execution
- Test functions are stored in `test-functions.ps1`
- Uses PowerShell color output capabilities
- Optimized for Windows runners

## Adding New Tests

To add a new test scenario:

1. Add the test function to both `test-functions.sh` and `test-functions.ps1`
2. Add a new step in both `action.yml` files to call your test
3. Follow the existing naming conventions:
   - Bash: `test_scenario_name`
   - PowerShell: `Test-ScenarioName`

## Debugging

If tests fail:
1. Check the GitHub Step Summary for detailed results
2. Download the test artifacts to inspect the git repositories
3. Run the tests locally using the standalone scripts:
   - Linux/macOS: `./test-versioning-scenarios.sh`
   - Windows: `.\test-versioning-scenarios.ps1`

## Maintenance

When updating test scenarios:
1. Update both Linux and Windows implementations
2. Ensure test expectations match across platforms
3. Test locally on both platforms if possible
4. Update this README if adding new features