# Mister.Version Integration Test Script (PowerShell)
# This script wraps the GitHub Action test functions for local execution.
# Exit code: 0 when every scenario passed, 1 otherwise.

$ErrorActionPreference = "Stop"

# Set up environment variables
# Use system temp directory to ensure we're outside the git tree
$env:TEST_DIR = if ($env:TEST_DIR) { $env:TEST_DIR } else {
    Join-Path ([System.IO.Path]::GetTempPath()) ("mister-version-tests-" + [System.Guid]::NewGuid().ToString().Substring(0, 8))
}
$env:TOOL_PATH = if ($env:TOOL_PATH) { $env:TOOL_PATH } else {
    $toolDir = Join-Path $PSScriptRoot (Join-Path "Mister.Version.CLI" (Join-Path "bin" (Join-Path "Debug" "net8.0")))
    $exePath = Join-Path $toolDir "mr-version.exe"
    if (Test-Path $exePath) { $exePath } else { Join-Path $toolDir "mr-version" }
}
$env:RUNNER_TEMP = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
# GITHUB_STEP_SUMMARY is only written to when set (i.e. on GitHub Actions)

# Store the original directory
$OriginalDir = Get-Location

# Source the test functions from GitHub Actions
. (Join-Path $PSScriptRoot (Join-Path ".github" (Join-Path "actions" (Join-Path "test-versioning-windows" "test-functions.ps1"))))

# Scenarios to run (all scenarios defined in test-functions.ps1)
$Scenarios = @(
    "Test-InitialRepo"
    "Test-SingleReleaseTag"
    "Test-FeatureBranch"
    "Test-ReleaseBranch"
    "Test-Monorepo"
    "Test-MonorepoWithDependencies"
    "Test-PrereleaseVersions"
    "Test-DevBranch"
    "Test-BuildMetadata"
    "Test-ConfigAlpha"
    "Test-ConfigBeta"
    "Test-ConfigRC"
    "Test-YamlConfig"
    "Test-ForceVersion"
    "Test-TagPrefix"
    "Test-DependencyTracking"
    "Test-DetachedHead"
    "Test-CrossPlatformPaths"
)

# Main test runner
function Main {
    Write-Host "Starting Mister.Version Integration Tests"
    Write-Host "========================================="
    Write-Host ""
    Write-Host "Test directory: $env:TEST_DIR"
    Write-Host ""

    # Clean up any previous test runs
    if (Test-Path $env:TEST_DIR) {
        Remove-Item -Path $env:TEST_DIR -Recurse -Force
    }
    New-Item -ItemType Directory -Path $env:TEST_DIR -Force | Out-Null

    # Initialize results file
    "" | Out-File -FilePath (Join-Path $env:RUNNER_TEMP "test-results.txt") -Encoding utf8

    # Build the CLI tool first
    Write-Host "Building Mister.Version CLI tool..."
    Set-Location $OriginalDir
    dotnet build (Join-Path $PSScriptRoot (Join-Path "Mister.Version.CLI" "Mister.Version.CLI.csproj")) -c Debug
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: dotnet build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }

    # Verify tool exists
    if (-not (Test-Path $env:TOOL_PATH)) {
        Write-Host "Error: CLI tool not found at $env:TOOL_PATH" -ForegroundColor Red
        exit 1
    }

    # Run all tests
    Write-Host ""
    Write-Host "Running test scenarios..."
    Write-Host ""

    # Every scenario runs even if an earlier one failed; failures are collected in the ledger
    foreach ($scenario in $Scenarios) {
        $null = Invoke-TestScenario $scenario
    }

    # Print the summary; exits 1 when any test failed or nothing was recorded
    Export-TestSummary
    exit 0
}

# Cleanup function
function Cleanup {
    Set-Location $OriginalDir
    if ($env:TEST_DIR -and (Test-Path $env:TEST_DIR)) {
        Write-Host "Cleaning up test directory: $env:TEST_DIR"
        Remove-Item -Path $env:TEST_DIR -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Run main if script is executed directly
if ($MyInvocation.InvocationName -ne '.') {
    try {
        Main
    }
    finally {
        Cleanup
    }
}
