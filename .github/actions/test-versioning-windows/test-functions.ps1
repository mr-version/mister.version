# Test functions for Windows versioning scenarios
#
# How results are reported:
#   * Every assertion goes through Assert-ToolVersion, which records the outcome with Add-TestResult.
#   * Add-TestResult keeps an in-process tally and appends one line per assertion to a ledger file in
#     $env:TEST_DIR, because GitHub Actions runs every step of the composite action in a separate pwsh
#     process (in-memory counters do not survive from one step to the next).
#   * Run scenarios through Invoke-TestScenario. It returns $true only when the scenario recorded at
#     least one assertion, none of them failed and nothing threw (a failing git command throws), so a
#     step can do:  if (-not (Invoke-TestScenario Test-InitialRepo)) { exit 1 }
#   * Export-TestSummary reads the ledger, writes the totals and exits 1 when anything failed or when no
#     result was recorded at all.
#
# Paths are built with Join-Path and the separators of path arguments handed to native commands are
# normalized, so the same scenarios also run under PowerShell 7 on Linux/macOS (for local debugging).

# Results tracking (this process)
$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0

# Directory for the ledger and log files
function Get-TestOutputRoot {
    if ($env:TEST_DIR) { return $env:TEST_DIR }
    return [System.IO.Path]::GetTempPath()
}

# Ledger shared by all steps of one run
function Get-TestLedgerPath {
    return (Join-Path (Get-TestOutputRoot) "test-ledger.tsv")
}

# Function to write colored output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color

    # Also log to results file
    if ($env:RUNNER_TEMP) {
        $Message | Out-File -FilePath (Join-Path $env:RUNNER_TEMP "test-results.txt") -Encoding utf8 -Append
    } elseif ($env:TEST_DIR) {
        $Message | Out-File -FilePath (Join-Path $env:TEST_DIR "test-results.txt") -Encoding utf8 -Append
    }
}

# Append a line to the GitHub step summary (no-op when running outside GitHub Actions)
function Write-StepSummary {
    param([string]$Line)
    if ($env:GITHUB_STEP_SUMMARY) {
        $Line | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
}

# Record the outcome of one assertion
function Add-TestResult {
    param(
        [Parameter(Mandatory)][string]$TestName,
        [Parameter(Mandatory)][bool]$Passed,
        [string]$Detail = ""
    )

    $script:TotalTests++
    if ($Passed) { $script:PassedTests++ } else { $script:FailedTests++ }

    $status = if ($Passed) { "PASS" } else { "FAIL" }
    $name = ($TestName -replace '[\t\r\n]+', ' ').Trim()
    $text = ($Detail -replace '[\t\r\n]+', ' ').Trim()
    "$status`t$name`t$text" | Out-File -FilePath (Get-TestLedgerPath) -Encoding utf8 -Append

    if ($Passed) {
        Write-StepSummary "✅ **$name**: $text"
    } else {
        Write-StepSummary "❌ **$name**: $text"
    }
}

# Convert / and \ to the separator of the current platform (for paths handed to native commands)
function ConvertTo-NativePath {
    param([string]$Path)
    return ($Path -replace '[\\/]', [string][System.IO.Path]::DirectorySeparatorChar)
}

# Run git and throw when it fails, so a broken scenario setup cannot go unnoticed.
# Returns git's standard output lines (e.g. for rev-parse).
function Invoke-Git {
    $ErrorActionPreference = "Continue"
    $output = & git @args 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "git $($args -join ' ') failed with exit code ${exitCode}:`n$($output | Out-String)"
    }
    $output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }
}

# Create an empty git repository with a local identity on branch main
function New-TestRepo {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    $nativePath = ConvertTo-NativePath $Path
    Invoke-Git init --initial-branch=main $nativePath | Out-Null
    Invoke-Git -C $nativePath config user.email "test@example.com" | Out-Null
    Invoke-Git -C $nativePath config user.name "Test User" | Out-Null
    Invoke-Git -C $nativePath config commit.gpgsign false | Out-Null
    Invoke-Git -C $nativePath config tag.gpgsign false | Out-Null
}

# Function to create a test project
function New-TestProject {
    param(
        [string]$ProjectName,
        [string]$ProjectDir,
        [string[]]$ProjectReferences = @()
    )

    New-Item -ItemType Directory -Path $ProjectDir -Force | Out-Null

    $referenceGroup = ""
    if ($ProjectReferences.Count -gt 0) {
        $items = ($ProjectReferences | ForEach-Object { "    <ProjectReference Include=`"$_`" />" }) -join "`n"
        $referenceGroup = "`n  <ItemGroup>`n$items`n  </ItemGroup>"
    }

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>$referenceGroup
</Project>
"@ | Out-File -FilePath (Join-Path $ProjectDir "$ProjectName.csproj") -Encoding UTF8

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
"@ | Out-File -FilePath (Join-Path $ProjectDir "Program.cs") -Encoding UTF8
}

# Run the mr-version CLI ($env:TOOL_PATH; a .dll is run through dotnet) and capture its output
function Invoke-VersionTool {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $ErrorActionPreference = "Continue"
    if (-not $env:TOOL_PATH) {
        throw "TOOL_PATH is not set"
    }

    if ($env:TOOL_PATH -like "*.dll") {
        $output = & dotnet $env:TOOL_PATH @Arguments 2>&1 | Out-String
    } else {
        $output = & $env:TOOL_PATH @Arguments 2>&1 | Out-String
    }

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = $output
    }
}

# Shared assertion: run `mr-version version` for a project and compare the reported version.
# Fails when the tool exits non-zero or reports a different (or no) version.
function Assert-ToolVersion {
    param(
        [Parameter(Mandatory)][string]$RepoPath,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$TestName,
        [string[]]$ExtraArguments = @()
    )

    Write-ColorOutput "" -Color White
    Write-ColorOutput "Running test: $TestName" -Color Cyan
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Yellow
    Write-ColorOutput "Repo path: $RepoPath" -Color Yellow
    Write-ColorOutput "Project path: $ProjectPath" -Color Yellow
    if ($ExtraArguments.Count -gt 0) {
        Write-ColorOutput "Extra arguments: $($ExtraArguments -join ' ')" -Color Magenta
    }

    $arguments = @("version", "--repo", (ConvertTo-NativePath $RepoPath), "--project", (ConvertTo-NativePath $ProjectPath)) + $ExtraArguments
    $result = Invoke-VersionTool -Arguments $arguments

    # Extract version from the "Version: X.X.X" line
    $actualVersion = $null
    if ($result.Output -match '(?m)^Version:[ \t]*(\S+)') {
        $actualVersion = $Matches[1]
    }

    # Handle case where version might be "Unknown"
    if ($actualVersion -eq "Unknown") {
        $actualVersion = $null
    }

    if ($result.ExitCode -ne 0) {
        Write-ColorOutput "✗ FAILED: mr-version exited with code $($result.ExitCode)" -Color Red
        Write-Host "Full output:"
        Write-Host $result.Output
        Add-TestResult -TestName $TestName -Passed $false -Detail "mr-version exited with code $($result.ExitCode) (expected $ExpectedVersion, got '$actualVersion')"
    } elseif ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        Add-TestResult -TestName $TestName -Passed $true -Detail $actualVersion
    } else {
        if (-not $actualVersion) {
            Write-ColorOutput "Debug: Could not extract version from output" -Color Yellow
        }
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $result.Output
        Add-TestResult -TestName $TestName -Passed $false -Detail "Expected $ExpectedVersion but got '$actualVersion'"
    }
}

# Version of the first project found in the repository
function Test-VersioningTool {
    param(
        [string]$RepoPath,
        [string]$ExpectedVersion,
        [string]$TestName
    )

    $projectFile = Get-ChildItem -LiteralPath $RepoPath -Filter "*.csproj" -Recurse | Select-Object -First 1
    if (-not $projectFile) {
        Write-ColorOutput "✗ FAILED: No .csproj file found in $RepoPath" -Color Red
        Add-TestResult -TestName $TestName -Passed $false -Detail "No .csproj file found in $RepoPath"
        return
    }

    Assert-ToolVersion -RepoPath $RepoPath -ProjectPath $projectFile.FullName -ExpectedVersion $ExpectedVersion -TestName $TestName
}

# Version of a specific project (path relative to the current directory, i.e. the repository)
function Test-MonorepoVersioningTool {
    param(
        [string]$RepoPath,
        [string]$ProjectPath,
        [string]$ExpectedVersion,
        [string]$TestName
    )

    Assert-ToolVersion -RepoPath $RepoPath -ProjectPath $ProjectPath -ExpectedVersion $ExpectedVersion -TestName $TestName
}

# Helper function for prerelease type tests
function Test-VersioningToolWithPrerelease {
    param(
        [string]$RepoPath,
        [string]$PrereleaseType,
        [string]$ExpectedVersion,
        [string]$TestName
    )

    Assert-ToolVersion -RepoPath $RepoPath -ProjectPath ".\src\TestProject\TestProject.csproj" -ExpectedVersion $ExpectedVersion -TestName $TestName -ExtraArguments @("--prerelease-type", $PrereleaseType)
}

# Helper function for force version tests
function Test-VersioningToolForce {
    param(
        [string]$RepoPath,
        [string]$ForceVersion,
        [string]$ExpectedVersion,
        [string]$TestName
    )

    Assert-ToolVersion -RepoPath $RepoPath -ProjectPath ".\src\TestProject\TestProject.csproj" -ExpectedVersion $ExpectedVersion -TestName $TestName -ExtraArguments @("--force-version", $ForceVersion)
}

# Helper function for tag prefix tests
function Test-VersioningToolWithTagPrefix {
    param(
        [string]$RepoPath,
        [string]$TagPrefix,
        [string]$ExpectedVersion,
        [string]$TestName
    )

    Assert-ToolVersion -RepoPath $RepoPath -ProjectPath ".\src\TestProject\TestProject.csproj" -ExpectedVersion $ExpectedVersion -TestName $TestName -ExtraArguments @("--tag-prefix", $TagPrefix)
}

# Run one scenario function; returns $true only if it recorded assertions, none failed and it did not throw
function Invoke-TestScenario {
    param([Parameter(Mandatory)][string]$Name)

    $totalBefore = $script:TotalTests
    $failedBefore = $script:FailedTests
    $location = Get-Location

    try {
        $null = & $Name
    }
    catch {
        Write-ColorOutput "✗ ERROR in ${Name}: $($_.Exception.Message)" -Color Red
        Write-Host $_.ScriptStackTrace
        Add-TestResult -TestName $Name -Passed $false -Detail "Scenario error: $($_.Exception.Message)"
    }
    finally {
        Set-Location $location
    }

    if ($script:TotalTests -eq $totalBefore) {
        Write-ColorOutput "✗ FAILED: $Name recorded no test results" -Color Red
        Add-TestResult -TestName $Name -Passed $false -Detail "Scenario recorded no test results"
    }

    return ($script:FailedTests -eq $failedBefore)
}

# Test 1: Initial repository with no tags
function Test-InitialRepo {
    $testName = "Initial Repository (No Tags)"
    $repoDir = Join-Path $env:TEST_DIR "test1-initial"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "0.1.0" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 2: Repository with a single release tag
function Test-SingleReleaseTag {
    $testName = "Single Release Tag"
    $repoDir = Join-Path $env:TEST_DIR "test2-single-tag"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        # Make another commit
        Add-Content -Path "src\TestProject\Program.cs" -Value "// New feature"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add new feature" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 3: Feature branch versioning
function Test-FeatureBranch {
    $testName = "Feature Branch Versioning"
    $repoDir = Join-Path $env:TEST_DIR "test3-feature-branch"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        # Create and switch to feature branch
        Invoke-Git checkout -b feature/new-feature | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Feature code"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add feature" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-new-feature.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 4: Release branch versioning
# Release branches produce the final version of the release line (no prerelease suffix),
# matching test_release_branch in the Linux suite.
function Test-ReleaseBranch {
    $testName = "Release Branch Versioning"
    $repoDir = Join-Path $env:TEST_DIR "test4-release-branch"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        # Create and switch to release branch
        Invoke-Git checkout -b release/1.1 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Release prep"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Prepare release" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.1.0" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 5: Multiple projects in monorepo
function Test-Monorepo {
    $testName = "Monorepo with Multiple Projects"
    $repoDir = Join-Path $env:TEST_DIR "test5-monorepo"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        # Create multiple projects
        New-TestProject -ProjectName "ProjectA" -ProjectDir "src\ProjectA"
        New-TestProject -ProjectName "ProjectB" -ProjectDir "src\ProjectB"
        New-TestProject -ProjectName "ProjectC" -ProjectDir "src\ProjectC"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        # Tag global version
        Invoke-Git tag v1.0.0 | Out-Null

        # Tag project-specific versions using new format
        Invoke-Git tag ProjectA-v1.2.0 | Out-Null
        Invoke-Git tag ProjectB-v1.0.0 | Out-Null
        Invoke-Git tag ProjectC-v1.0.0 | Out-Null

        # Make changes to ProjectA
        Add-Content -Path "src\ProjectA\Program.cs" -Value "// ProjectA update"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update ProjectA" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectA\ProjectA.csproj" -ExpectedVersion "1.2.1" -TestName "$testName - ProjectA"
        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectB\ProjectB.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectB"
        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectC\ProjectC.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectC"
    }
    finally {
        Pop-Location
    }
}

# Test 5b: Multiple projects in monorepo with dependencies
function Test-MonorepoWithDependencies {
    $testName = "Monorepo with Dependencies"
    $repoDir = Join-Path $env:TEST_DIR "test5b-monorepo-deps"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        # Create ProjectA (base library)
        New-TestProject -ProjectName "ProjectA" -ProjectDir "src\ProjectA"

        # Create ProjectB with dependency on ProjectA
        New-TestProject -ProjectName "ProjectB" -ProjectDir "src\ProjectB" -ProjectReferences @("../ProjectA/ProjectA.csproj")

        # Create ProjectC (standalone)
        New-TestProject -ProjectName "ProjectC" -ProjectDir "src\ProjectC"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit with dependencies" | Out-Null

        # Tag project-specific versions using new format
        Invoke-Git tag ProjectA-v1.0.0 | Out-Null
        Invoke-Git tag ProjectB-v1.0.0 | Out-Null
        Invoke-Git tag ProjectC-v1.0.0 | Out-Null

        # Make changes to ProjectA (which should trigger ProjectB version bump)
        Add-Content -Path "src\ProjectA\Program.cs" -Value "// ProjectA update"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update ProjectA" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectA\ProjectA.csproj" -ExpectedVersion "1.0.1" -TestName "$testName - ProjectA"
        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectB\ProjectB.csproj" -ExpectedVersion "1.0.1" -TestName "$testName - ProjectB (depends on A)"
        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectC\ProjectC.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectC (independent)"
    }
    finally {
        Pop-Location
    }
}

# Test 6: Pre-release versions
function Test-PrereleaseVersions {
    $testName = "Pre-release Version Progression"
    $repoDir = Join-Path $env:TEST_DIR "test6-prerelease"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0-alpha.1 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Alpha 2"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Alpha 2 changes" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.0-alpha.2" -TestName "$testName - Alpha progression"

        Invoke-Git tag v1.0.0-alpha.2 | Out-Null
        Invoke-Git tag v1.0.0-beta.1 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Beta 2"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Beta 2 changes" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.0-beta.2" -TestName "$testName - Beta progression"
    }
    finally {
        Pop-Location
    }
}

# Test 7: Dev branch versioning
function Test-DevBranch {
    $testName = "Dev Branch Versioning"
    $repoDir = Join-Path $env:TEST_DIR "test7-dev-branch"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        # Tag on the current branch (main)
        Invoke-Git tag v1.0.0 | Out-Null

        # Create and switch to dev branch
        Invoke-Git checkout -b dev | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Dev feature"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add dev feature" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-dev.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 8: Version with build metadata
function Test-BuildMetadata {
    $testName = "Version with Build Metadata"
    $repoDir = Join-Path $env:TEST_DIR "test8-build-metadata"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0+build.123 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// New build"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "New build" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 9: Configuration Tests - PrereleaseType=alpha
function Test-ConfigAlpha {
    $testName = "Configuration: PrereleaseType=alpha"
    $repoDir = Join-Path $env:TEST_DIR "test9-config-alpha"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        # Test with alpha prerelease type
        Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "alpha" -ExpectedVersion "0.1.0-alpha.1" -TestName "$testName - Initial"

        Invoke-Git tag v0.1.0-alpha.1 | Out-Null
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Update"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update code" | Out-Null

        Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "alpha" -ExpectedVersion "0.1.0-alpha.2" -TestName "$testName - Increment"
    }
    finally {
        Pop-Location
    }
}

# Test 10: Configuration Tests - PrereleaseType=beta
function Test-ConfigBeta {
    $testName = "Configuration: PrereleaseType=beta"
    $repoDir = Join-Path $env:TEST_DIR "test10-config-beta"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Beta feature"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add beta feature" | Out-Null

        Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "beta" -ExpectedVersion "1.0.1-beta.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 11: Configuration Tests - PrereleaseType=rc
function Test-ConfigRC {
    $testName = "Configuration: PrereleaseType=rc"
    $repoDir = Join-Path $env:TEST_DIR "test11-config-rc"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v2.0.0 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// RC feature"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add RC feature" | Out-Null

        Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "rc" -ExpectedVersion "2.0.1-rc.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 12: YAML Configuration Tests (same sub-cases as test_yaml_config in the Linux suite)
function Test-YamlConfig {
    $testName = "YAML Configuration File"
    $repoDir = Join-Path $env:TEST_DIR "test12-yaml-config"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        # 12a: Basic YAML config with beta prerelease
        @"
tagPrefix: "v"
prereleaseType: "beta"
"@ | Out-File -FilePath "mr-version.yml" -Encoding UTF8

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit with config" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "0.1.0-beta.1" -TestName "$testName - beta prerelease"

        # 12b: YAML config with custom tag prefix
        @"
tagPrefix: "release-"
prereleaseType: "none"
"@ | Out-File -FilePath "mr-version.yml" -Encoding UTF8

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update config with custom prefix" | Out-Null
        Invoke-Git tag release-1.0.0 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Update"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Make changes" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1" -TestName "$testName - custom tag prefix"

        # 12c: YAML config with baseVersion
        @"
baseVersion: "2.0.0"
prereleaseType: "none"
"@ | Out-File -FilePath "mr-version.yml" -Encoding UTF8

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Another update"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update with baseVersion config" | Out-Null

        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "2.0.0" -TestName "$testName - baseVersion"
    }
    finally {
        Pop-Location
    }

    # 12d: Alternative YAML filename (mister-version.yaml)
    $repoDir2 = Join-Path $env:TEST_DIR "test12d-yaml-alt-name"

    New-TestRepo $repoDir2
    Push-Location $repoDir2
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        @"
prereleaseType: "alpha"
"@ | Out-File -FilePath "mister-version.yaml" -Encoding UTF8

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Config with alternate filename" | Out-Null

        Test-VersioningTool -RepoPath $repoDir2 -ExpectedVersion "0.1.0-alpha.1" -TestName "$testName - alternate filename"
    }
    finally {
        Pop-Location
    }
}

# Test 13: Force Version Tests
function Test-ForceVersion {
    $testName = "Force Version Override"
    $repoDir = Join-Path $env:TEST_DIR "test13-force-version"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Some changes"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Some changes" | Out-Null

        Test-VersioningToolForce -RepoPath $repoDir -ForceVersion "2.5.0" -ExpectedVersion "2.5.0" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 14: Tag Prefix Variations
function Test-TagPrefix {
    $testName = "Tag Prefix Variations"
    $repoDir = Join-Path $env:TEST_DIR "test14-tag-prefix"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag release-1.0.0 | Out-Null  # Different prefix

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Changes"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Changes" | Out-Null

        Test-VersioningToolWithTagPrefix -RepoPath $repoDir -TagPrefix "release-" -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 15: Dependency Tracking
# App references SharedLib through a ProjectReference; a change in SharedLib must bump App
# (transitive dependency detection, as in test_dependency_tracking in the Linux suite).
function Test-DependencyTracking {
    $testName = "Dependency Tracking in Monorepo"
    $repoDir = Join-Path $env:TEST_DIR "test15-dependencies"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        # Create SharedLib, and App with a dependency on SharedLib
        New-TestProject -ProjectName "SharedLib" -ProjectDir "src\SharedLib"
        New-TestProject -ProjectName "App" -ProjectDir "src\App" -ProjectReferences @("../SharedLib/SharedLib.csproj")

        # Add shared dependency file
        "// Shared utility" | Out-File -FilePath "src\SharedLib\Utils.cs" -Encoding UTF8

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        # Update shared lib
        Add-Content -Path "src\SharedLib\Utils.cs" -Value "// Updated utility"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Update shared lib" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\App\App.csproj" -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 16: Detached HEAD scenarios (port of test_detached_head in the Linux suite)
function Test-DetachedHead {
    $testName = "Detached HEAD State"
    $repoDir = Join-Path $env:TEST_DIR "test16-detached-head"

    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"

        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Invoke-Git tag v1.0.0 | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Feature 1"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add feature 1" | Out-Null

        Add-Content -Path "src\TestProject\Program.cs" -Value "// Feature 2"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Add feature 2" | Out-Null
        Invoke-Git tag v1.1.0 | Out-Null

        # 16a: Detached HEAD at a tagged commit
        Invoke-Git checkout --detach v1.0.0 | Out-Null
        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.0" -TestName "$testName - at tagged commit"

        # 16b: Detached HEAD at an untagged commit (between tags)
        Invoke-Git checkout main | Out-Null
        $commitBetween = (Invoke-Git rev-parse "HEAD~1" | Select-Object -First 1).Trim()
        Invoke-Git checkout --detach $commitBetween | Out-Null
        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-no-branch.1" -TestName "$testName - between tags"

        # 16c: Detached HEAD with new commits
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Detached change"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Change in detached HEAD" | Out-Null
        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-no-branch.2" -TestName "$testName - with new commits"

        # 16d: Detached HEAD at a specific commit hash
        Invoke-Git checkout main | Out-Null
        $commitHash = (Invoke-Git rev-parse "HEAD~1" | Select-Object -First 1).Trim()
        Invoke-Git checkout --detach $commitHash | Out-Null
        Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-no-branch.1" -TestName "$testName - at commit hash"
    }
    finally {
        Pop-Location
    }
}

# Test 17: Path handling (port of test_cross_platform_paths in the Linux suite)
function Test-CrossPlatformPaths {
    $testName = "Cross-Platform Paths"

    # 17a: Paths with spaces (repository and project directory)
    $repoDir = Join-Path $env:TEST_DIR "test17 with spaces"
    New-TestRepo $repoDir
    Push-Location $repoDir
    try {
        New-TestProject -ProjectName "MyProject" -ProjectDir "src\My Project"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\My Project\MyProject.csproj" -ExpectedVersion "0.1.0" -TestName "$testName - spaces in path"
    }
    finally {
        Pop-Location
    }

    # 17b: Long project names
    $repoDir2 = Join-Path $env:TEST_DIR "test17b-long-names"
    $longName = "VeryLongProjectNameThatExceedsNormalLengthForTestingPurposes"
    New-TestRepo $repoDir2
    Push-Location $repoDir2
    try {
        New-TestProject -ProjectName $longName -ProjectDir "src\$longName"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir2 -ProjectPath ".\src\$longName\$longName.csproj" -ExpectedVersion "0.1.0" -TestName "$testName - long project name"
    }
    finally {
        Pop-Location
    }

    # 17c: Nested directory structure (deep paths)
    $repoDir3 = Join-Path $env:TEST_DIR "test17c-deep-paths"
    $deepPath = "src\level1\level2\level3\level4\DeepProject"
    New-TestRepo $repoDir3
    Push-Location $repoDir3
    try {
        New-TestProject -ProjectName "DeepProject" -ProjectDir $deepPath
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir3 -ProjectPath ".\$deepPath\DeepProject.csproj" -ExpectedVersion "0.1.0" -TestName "$testName - deep nested path"
    }
    finally {
        Pop-Location
    }

    # 17d: Special characters in project names (dashes, underscores, dots)
    $repoDir4 = Join-Path $env:TEST_DIR "test17d-special-chars"
    New-TestRepo $repoDir4
    Push-Location $repoDir4
    try {
        New-TestProject -ProjectName "My-Project_Name.Core" -ProjectDir "src\My-Project_Name.Core"
        Invoke-Git add . | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null

        Test-MonorepoVersioningTool -RepoPath $repoDir4 -ProjectPath ".\src\My-Project_Name.Core\My-Project_Name.Core.csproj" -ExpectedVersion "0.1.0" -TestName "$testName - special characters"
    }
    finally {
        Pop-Location
    }
}

# Read the results recorded by every step of this run
function Get-TestLedger {
    $path = Get-TestLedgerPath
    if (-not (Test-Path $path)) {
        return @()
    }

    return @(Get-Content -Path $path -Encoding UTF8 | Where-Object { $_.Trim() } | ForEach-Object {
        $parts = $_ -split "`t", 3
        [pscustomobject]@{
            Status = $parts[0].Trim([char]0xFEFF)
            Name   = $(if ($parts.Count -gt 1) { $parts[1] } else { "" })
            Detail = $(if ($parts.Count -gt 2) { $parts[2] } else { "" })
        }
    })
}

# Export test summary at the end; exits 1 when any recorded test failed or nothing was recorded
function Export-TestSummary {
    $entries = @(Get-TestLedger)
    $failedEntries = @($entries | Where-Object { $_.Status -ne "PASS" })
    $total = $entries.Count
    $failed = $failedEntries.Count
    $passed = $total - $failed

    Write-StepSummary ""
    Write-StepSummary "### Test Summary"
    Write-StepSummary "- Total Tests: $total"
    Write-StepSummary "- Passed: $passed"
    Write-StepSummary "- Failed: $failed"
    Write-StepSummary ""

    Write-Host ""
    Write-Host "========================================="
    Write-Host "Test Summary"
    Write-Host "========================================="
    Write-Host "Total Tests: $total"
    Write-Host "Passed: $passed"
    Write-Host "Failed: $failed"

    if ($total -eq 0) {
        Write-StepSummary "❌ **No test results were recorded!**"
        Write-Host ""
        Write-Host "✗ No test results were recorded!" -ForegroundColor Red
        exit 1
    }

    if ($failed -gt 0) {
        Write-StepSummary "❌ **Some tests failed!**"
        Write-Host ""
        Write-Host "✗ Some tests failed:" -ForegroundColor Red
        foreach ($entry in $failedEntries) {
            Write-StepSummary "- ❌ $($entry.Name): $($entry.Detail)"
            Write-Host "  - $($entry.Name): $($entry.Detail)" -ForegroundColor Red
        }
        exit 1
    }

    Write-StepSummary "✅ **All tests passed!**"
    Write-Host ""
    Write-Host "✓ All tests passed!" -ForegroundColor Green
}
