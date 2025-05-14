# Mister.Version Testing Guide

This directory contains test scripts and documentation for testing the Mister.Version tool.

## Integration Tests

The integration tests create mock git repositories with various versioning scenarios to ensure the versioning tool works correctly across different use cases.

### Running Tests

#### Using Make (Recommended)
```bash
# Run all tests (unit + integration)
make test

# Run only integration tests
make integration-test

# Run only unit tests
make unit-test
```

#### Using Scripts Directly

**Linux/macOS:**
```bash
./test-versioning-scenarios.sh
```

**Windows (PowerShell):**
```powershell
.\test-versioning-scenarios.ps1
```

**Windows (Git Bash):**
```bash
./test-versioning-scenarios.sh
```

### Test Scenarios

The integration tests cover the following scenarios:

1. **Initial Repository (No Tags)**
   - Tests versioning in a new repository without any version tags
   - Expected: `0.1.0-alpha.1`

2. **Single Release Tag**
   - Repository with one release tag and subsequent commits
   - Expected: Patch version increment with alpha prerelease

3. **Feature Branch Versioning**
   - Tests versioning on feature branches
   - Expected: Minor version increment with feature branch name in prerelease

4. **Release Branch Versioning**
   - Tests versioning on release branches
   - Expected: Release candidate (rc) prerelease versions

5. **Monorepo with Multiple Projects**
   - Tests project-specific versioning in a monorepo setup
   - Supports both global and project-specific version tags

6. **Pre-release Version Progression**
   - Tests progression through alpha, beta, and rc versions
   - Ensures correct prerelease version incrementing

7. **Dev Branch Versioning**
   - Tests versioning on development branches
   - Expected: Minor version increment with dev prerelease

8. **Version with Build Metadata**
   - Tests handling of versions with build metadata
   - Ensures metadata is properly parsed and handled

## CI/CD Integration

The tests are automatically run in GitHub Actions on:
- Every push to main, dev, and abstract branches
- Every pull request to main
- Manual workflow dispatch

### GitHub Actions Workflows

1. **CI Build and Test** (`.github/workflows/ci.yml`)
   - Runs on multiple OS (Ubuntu, Windows, macOS)
   - Tests with multiple .NET versions (6.0, 8.0)
   - Includes code coverage reporting

2. **NuGet Publish** (`.github/workflows/nuget-publish.yml`)
   - Runs tests before publishing
   - Only publishes on version tags (v*.*.*)

## Writing New Tests

To add a new test scenario:

1. Create a new test function in the test script
2. Follow the naming convention: `test_scenario_name` (bash) or `Test-ScenarioName` (PowerShell)
3. Use the provided helper functions:
   - `create_test_project` / `New-TestProject` - Creates a test C# project
   - `run_versioning_tool` / `Test-VersioningTool` - Runs the tool and validates output
4. Add the test function to the tests array in the main function
5. Ensure the test cleans up after itself

Example test structure:
```bash
test_my_scenario() {
    local test_name="My Test Scenario"
    local repo_dir="$TEST_DIR/test-my-scenario"
    
    # Setup repository
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    
    # Create project and commits
    create_test_project "TestProject" "src/TestProject"
    git add .
    git commit -m "Initial commit"
    
    # Test the versioning
    run_versioning_tool "$repo_dir" "1.0.0" "$test_name"
}
```

## Troubleshooting

### Common Issues

1. **Tool not found**
   - Ensure you've built the CLI tool: `dotnet build ./Mister.Version.CLI/Mister.Version.CLI.csproj`

2. **Git not available**
   - The tests require git to be installed and available in PATH

3. **Permission denied**
   - Make scripts executable: `chmod +x test-versioning-scenarios.sh`

4. **Tests fail on Windows**
   - Use Git Bash or PowerShell version of the script
   - Ensure line endings are correct (LF not CRLF)

### Debug Mode

To run tests with more verbose output:
```bash
# Set debug environment variable
DEBUG=1 ./test-versioning-scenarios.sh
```

## Local Development

For rapid iteration during development:

1. Use the Makefile for quick test runs
2. Run specific test scenarios by commenting out others in the tests array
3. Check test-repos directory for test artifacts after failures
4. Use `make clean` to remove all test artifacts

## Contributing

When adding new versioning features:
1. Add corresponding test scenarios
2. Ensure tests pass on all platforms
3. Update this README with new test documentation
4. Add tests to both bash and PowerShell scripts