# Mister.Version Integration Test Script (PowerShell)
# This script wraps the GitHub Action test functions for local execution

$ErrorActionPreference = "Stop"

# Set up environment variables
# Use system temp directory to ensure we're outside the git tree
$env:TEST_DIR = if ($env:TEST_DIR) { $env:TEST_DIR } else { 
    Join-Path ([System.IO.Path]::GetTempPath()) ("mister-version-tests-" + [System.Guid]::NewGuid().ToString().Substring(0, 8))
}
$env:TOOL_PATH = if ($env:TOOL_PATH) { $env:TOOL_PATH } else { 
    $exePath = Join-Path $PSScriptRoot "Mister.Version.CLI\bin\Debug\net8.0\mr-version.exe"
    if (Test-Path $exePath) { $exePath } else { Join-Path $PSScriptRoot "Mister.Version.CLI\bin\Debug\net8.0\mr-version" }
}
$env:RUNNER_TEMP = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
$env:GITHUB_STEP_SUMMARY = if ($env:GITHUB_STEP_SUMMARY) { $env:GITHUB_STEP_SUMMARY } else { "NUL" }

# Store the original directory
$OriginalDir = Get-Location

# Source the test functions from GitHub Actions
. (Join-Path $PSScriptRoot ".github\actions\test-versioning-windows\test-functions.ps1")

# Main test runner
function Main {
    Write-Host "Starting Mister.Version Integration Tests"
    Write-Host "========================================="
    Write-Host ""
    
    # Clean up any previous test runs
    if (Test-Path $env:TEST_DIR) {
        Remove-Item -Path $env:TEST_DIR -Recurse -Force
    }
    New-Item -ItemType Directory -Path $env:TEST_DIR -Force | Out-Null
    
    # Initialize results file
    "" | Out-File -FilePath "$env:RUNNER_TEMP\test-results.txt" -Encoding utf8
    
    # Build the CLI tool first
    Write-Host "Building Mister.Version CLI tool..."
    Set-Location $OriginalDir
    dotnet build (Join-Path $PSScriptRoot "Mister.Version.CLI\Mister.Version.CLI.csproj") -c Debug
    
    # Verify tool exists
    if (-not (Test-Path $env:TOOL_PATH)) {
        Write-Host "Error: CLI tool not found at $env:TOOL_PATH" -ForegroundColor Red
        exit 1
    }
    
    # Run all tests
    Write-Host ""
    Write-Host "Running test scenarios..."
    Write-Host ""
    
    # Test functions from the GitHub Action
    Test-InitialRepo
    Test-SingleReleaseTag
    Test-FeatureBranch
    Test-ReleaseBranch
    Test-Monorepo
    Test-MonorepoWithDependencies
    Test-PrereleaseVersions
    Test-DevBranch
    Test-BuildMetadata
    Test-ConfigAlpha
    Test-ConfigBeta
    Test-ConfigRC
    # Test-YamlConfig  # TODO: Write tests for YAML configuration (ConfigurationService.cs)
    Test-ForceVersion
    Test-TagPrefix
    Test-DependencyTracking
    
    # Export summary
    Export-TestSummary
    
    # Print local summary
    Write-Host ""
    Write-Host "========================================="
    Write-Host "Test Summary"
    Write-Host "========================================="
    Write-Host "Total Tests: $script:TotalTests"
    Write-Host "Passed: $script:PassedTests"
    Write-Host "Failed: $($script:TotalTests - $script:PassedTests)"
    
    if ($script:PassedTests -eq $script:TotalTests) {
        Write-Host ""
        Write-Host "✓ All tests passed!" -ForegroundColor Green
        exit 0
    } else {
        Write-Host ""
        Write-Host "✗ Some tests failed!" -ForegroundColor Red
        exit 1
    }
}

# Cleanup function
function Cleanup {
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