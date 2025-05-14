# Mister.Version Integration Test Script (PowerShell)
# This script creates mock git repositories with different versioning scenarios
# and tests the versioning tool against them

$ErrorActionPreference = "Stop"

# Colors for output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

# Test directory
$TestDir = Join-Path $PSScriptRoot "test-repos"
$ToolPath = Join-Path $PSScriptRoot "Mister.Version.CLI\bin\Debug\net8.0\mr-version.exe"
if (-not (Test-Path $ToolPath)) {
    $ToolPath = Join-Path $PSScriptRoot "Mister.Version.CLI\bin\Debug\net8.0\mr-version"
}

# Function to create a test project
function New-TestProject {
    param(
        [string]$ProjectName,
        [string]$ProjectDir
    )
    
    New-Item -ItemType Directory -Path $ProjectDir -Force | Out-Null
    
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>
"@ | Out-File -FilePath "$ProjectDir\$ProjectName.csproj" -Encoding UTF8
    
    @"
namespace $ProjectName
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello from $ProjectName");
        }
    }
}
"@ | Out-File -FilePath "$ProjectDir\Program.cs" -Encoding UTF8
}

# Function to run versioning tool and capture output
function Test-VersioningTool {
    param(
        [string]$RepoPath,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "`nRunning test: $TestName" -Color Yellow
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Yellow
    
    # Build the tool if it doesn't exist
    if (-not (Test-Path $ToolPath)) {
        Write-ColorOutput "Building Mister.Version CLI tool..." -Color Yellow
        dotnet build (Join-Path $PSScriptRoot "Mister.Version.CLI\Mister.Version.CLI.csproj") -c Debug
    }
    
    # Run the versioning tool
    Push-Location $RepoPath
    try {
        $output = & $ToolPath calculate --repo-root . 2>&1 | Out-String
        $actualVersion = if ($output -match 'Version:\s*([^\s]+)') { $matches[1] }
        if (-not $actualVersion) {
            # Try to extract version from JSON output
            if ($output -match '"Version":\s*"([^"]+)"') {
                $actualVersion = $matches[1]
            }
        }
        
        if ($actualVersion -eq $ExpectedVersion) {
            Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
            return $true
        } else {
            Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
            Write-Host "Full output:"
            Write-Host $output
            return $false
        }
    }
    finally {
        Pop-Location
    }
}

# Clean up previous test runs
function Clear-TestRepos {
    if (Test-Path $TestDir) {
        Remove-Item -Path $TestDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $TestDir -Force | Out-Null
}

# Test 1: Initial repository with no tags
function Test-InitialRepo {
    $testName = "Initial Repository (No Tags)"
    $repoDir = Join-Path $TestDir "test1-initial"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "0.1.0-alpha.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 2: Repository with a single release tag
function Test-SingleReleaseTag {
    $testName = "Single Release Tag"
    $repoDir = Join-Path $TestDir "test2-single-tag"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Make another commit
        Add-Content -Path "src\TestProject\Program.cs" -Value "// New feature"
        git add . | Out-Null
        git commit -m "Add new feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-alpha.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 3: Feature branch versioning
function Test-FeatureBranch {
    $testName = "Feature Branch Versioning"
    $repoDir = Join-Path $TestDir "test3-feature-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Create and switch to feature branch
        git checkout -b feature/new-feature | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Feature code"
        git add . | Out-Null
        git commit -m "Add feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.1.0-new-feature.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 4: Release branch versioning
function Test-ReleaseBranch {
    $testName = "Release Branch Versioning"
    $repoDir = Join-Path $TestDir "test4-release-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Create and switch to release branch
        git checkout -b release/1.1 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Release prep"
        git add . | Out-Null
        git commit -m "Prepare release" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.1.0-rc.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 5: Multiple projects in monorepo
function Test-Monorepo {
    $testName = "Monorepo with Multiple Projects"
    $repoDir = Join-Path $TestDir "test5-monorepo"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        # Create multiple projects
        New-TestProject -ProjectName "ProjectA" -ProjectDir "src\ProjectA"
        New-TestProject -ProjectName "ProjectB" -ProjectDir "src\ProjectB"
        New-TestProject -ProjectName "ProjectC" -ProjectDir "src\ProjectC"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        # Tag global version
        git tag v1.0.0 | Out-Null
        
        # Tag project-specific version
        git tag ProjectA/v1.2.0 | Out-Null
        
        # Make changes to ProjectA
        Add-Content -Path "src\ProjectA\Program.cs" -Value "// ProjectA update"
        git add . | Out-Null
        git commit -m "Update ProjectA" | Out-Null
        
        $result1 = Test-VersioningTool -RepoPath "$repoDir\src\ProjectA" -ExpectedVersion "1.2.1-alpha.1" -TestName "$testName - ProjectA"
        $result2 = Test-VersioningTool -RepoPath "$repoDir\src\ProjectB" -ExpectedVersion "1.0.1-alpha.1" -TestName "$testName - ProjectB"
        
        return $result1 -and $result2
    }
    finally {
        Pop-Location
    }
}

# Test 6: Pre-release versions
function Test-PrereleaseVersions {
    $testName = "Pre-release Version Progression"
    $repoDir = Join-Path $TestDir "test6-prerelease"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0-alpha.1 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Alpha 2"
        git add . | Out-Null
        git commit -m "Alpha 2 changes" | Out-Null
        
        $result1 = Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.0-alpha.2" -TestName "$testName - Alpha progression"
        
        git tag v1.0.0-alpha.2 | Out-Null
        git tag v1.0.0-beta.1 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Beta 2"
        git add . | Out-Null
        git commit -m "Beta 2 changes" | Out-Null
        
        $result2 = Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.0-beta.2" -TestName "$testName - Beta progression"
        
        return $result1 -and $result2
    }
    finally {
        Pop-Location
    }
}

# Test 7: Dev branch versioning
function Test-DevBranch {
    $testName = "Dev Branch Versioning"
    $repoDir = Join-Path $TestDir "test7-dev-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        # Create main branch and tag
        git checkout -b main | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Create and switch to dev branch
        git checkout -b dev | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Dev feature"
        git add . | Out-Null
        git commit -m "Add dev feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.1.0-dev.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 8: Version with build metadata
function Test-BuildMetadata {
    $testName = "Version with Build Metadata"
    $repoDir = Join-Path $TestDir "test8-build-metadata"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0+build.123 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// New build"
        git add . | Out-Null
        git commit -m "New build" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-alpha.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Main test runner
function Main {
    Write-ColorOutput "Starting Mister.Version Integration Tests" -Color Green
    Write-ColorOutput "=========================================" -Color Green
    Write-Host ""
    
    # Clean up any previous test runs
    Clear-TestRepos
    
    # Build the CLI tool first
    Write-ColorOutput "Building Mister.Version CLI tool..." -Color Yellow
    dotnet build (Join-Path $PSScriptRoot "Mister.Version.CLI\Mister.Version.CLI.csproj") -c Debug
    
    # Run all tests
    $totalTests = 0
    $passedTests = 0
    
    # Array of test functions
    $tests = @(
        "Test-InitialRepo",
        "Test-SingleReleaseTag",
        "Test-FeatureBranch",
        "Test-ReleaseBranch",
        "Test-Monorepo",
        "Test-PrereleaseVersions",
        "Test-DevBranch",
        "Test-BuildMetadata"
    )
    
    # Run each test
    foreach ($test in $tests) {
        $totalTests++
        if (& $test) {
            $passedTests++
        }
    }
    
    # Print summary
    Write-Host ""
    Write-ColorOutput "=========================================" -Color Green
    Write-ColorOutput "Test Summary" -Color Green
    Write-ColorOutput "=========================================" -Color Green
    
    if ($passedTests -eq $totalTests) {
        Write-ColorOutput "✓ All tests passed! ($passedTests/$totalTests)" -Color Green
        exit 0
    } else {
        Write-ColorOutput "✗ Some tests failed! ($passedTests/$totalTests passed)" -Color Red
        exit 1
    }
}

# Run main if script is executed directly
if ($MyInvocation.InvocationName -ne '.') {
    Main
}