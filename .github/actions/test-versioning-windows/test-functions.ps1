# Test functions for Windows versioning scenarios

# Results tracking
$script:TotalTests = 0
$script:PassedTests = 0

# Function to write colored output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
    
    # Also log to results file
    if ($env:RUNNER_TEMP) {
        $Message | Out-File -FilePath "$env:RUNNER_TEMP\test-results.txt" -Encoding utf8 -Append
    } elseif ($env:TEST_DIR) {
        $Message | Out-File -FilePath "$env:TEST_DIR\test-results.txt" -Encoding utf8 -Append
    }
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
    
    # Run the versioning tool
    Push-Location $RepoPath
    try {
        # Find the first .csproj file
        $projectFile = Get-ChildItem -Path . -Filter "*.csproj" -Recurse | Select-Object -First 1
        if (-not $projectFile) {
            Write-ColorOutput "No .csproj file found in $RepoPath" -Color Red
            return $false
        }
        
        # Run the version command
        $output = & $env:TOOL_PATH version --repo $RepoPath --project $projectFile.FullName 2>&1 | Out-String
        
        # Extract version from "Version: X.X.X" format
        if ($output -match 'Version:\s*([^\r\n]+)') {
            $actualVersion = $matches[1].Trim()
        }
        
        # Handle case where version might be "Unknown"
        if ($actualVersion -eq "Unknown") {
            $actualVersion = $null
        }
        
        $script:TotalTests++
        
        # Debug output
        if (-not $actualVersion) {
            Write-ColorOutput "Debug: Could not extract version from output" -Color Yellow
            Write-ColorOutput "Debug: Raw output:" -Color Yellow
            Write-Host $output
        }
        
        if ($actualVersion -eq $ExpectedVersion) {
            Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
            $script:PassedTests++
            
            # Add to GitHub summary
            "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
            return $true
        } else {
            Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
            Write-Host "Full output:"
            Write-Host $output
            
            # Add to GitHub summary
            "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
            return $false
        }
    }
    finally {
        Pop-Location
    }
}

# Function to run versioning tool for monorepo projects
function Test-MonorepoVersioningTool {
    param(
        [string]$RepoPath,
        [string]$ProjectPath,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "Running test: $TestName" -Color Yellow
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Yellow
    Write-ColorOutput "Repo path: $RepoPath" -Color Yellow
    Write-ColorOutput "Project path: $ProjectPath" -Color Yellow
    
    # Run the version command
    $output = & $env:TOOL_PATH version --repo $RepoPath --project $ProjectPath 2>&1 | Out-String
    
    # Extract version from "Version: X.X.X" format
    if ($output -match 'Version:\s*([^\r\n]+)') {
        $actualVersion = $matches[1].Trim()
    }
    
    # Handle case where version might be "Unknown"
    if ($actualVersion -eq "Unknown") {
        $actualVersion = $null
    }
    
    $script:TotalTests++
    
    # Debug output
    if (-not $actualVersion) {
        Write-ColorOutput "Debug: Could not extract version from output" -Color Yellow
        Write-ColorOutput "Debug: Raw output:" -Color Yellow
        Write-Host $output
    }
    
    if ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        $script:PassedTests++
        
        # Add to GitHub summary
        "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $true
    }
    else {
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $output
        
        # Add to GitHub summary
        "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $false
    }
}

# Test 1: Initial repository with no tags
function Test-InitialRepo {
    $testName = "Initial Repository (No Tags)"
    $repoDir = "$env:TEST_DIR\test1-initial"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git config --global init.defaultBranch main
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "0.1.0" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 2: Repository with a single release tag
function Test-SingleReleaseTag {
    $testName = "Single Release Tag"
    $repoDir = "$env:TEST_DIR\test2-single-tag"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Make another commit
        Add-Content -Path "src\TestProject\Program.cs" -Value "// New feature"
        git add . | Out-Null
        git commit -m "Add new feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 3: Feature branch versioning
function Test-FeatureBranch {
    $testName = "Feature Branch Versioning"
    $repoDir = "$env:TEST_DIR\test3-feature-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Create and switch to feature branch
        git checkout -b feature/new-feature | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Feature code"
        git add . | Out-Null
        git commit -m "Add feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-new-feature.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 4: Release branch versioning
function Test-ReleaseBranch {
    $testName = "Release Branch Versioning"
    $repoDir = "$env:TEST_DIR\test4-release-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
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
    $repoDir = "$env:TEST_DIR\test5-monorepo"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        # Create multiple projects
        New-TestProject -ProjectName "ProjectA" -ProjectDir "src\ProjectA"
        New-TestProject -ProjectName "ProjectB" -ProjectDir "src\ProjectB"
        New-TestProject -ProjectName "ProjectC" -ProjectDir "src\ProjectC"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        # Tag global version
        git tag v1.0.0 | Out-Null
        
        # Tag project-specific versions using new format
        git tag ProjectA-v1.2.0 | Out-Null
        git tag ProjectB-v1.0.0 | Out-Null
        git tag ProjectC-v1.0.0 | Out-Null
        
        # Make changes to ProjectA
        Add-Content -Path "src\ProjectA\Program.cs" -Value "// ProjectA update"
        git add . | Out-Null
        git commit -m "Update ProjectA" | Out-Null
        
        $result1 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectA\ProjectA.csproj" -ExpectedVersion "1.2.1" -TestName "$testName - ProjectA"
        $result2 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectB\ProjectB.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectB"
        $result3 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectC\ProjectC.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectC"
        
        return $result1 -and $result2 -and $result3
    }
    finally {
        Pop-Location
    }
}

# Test 5b: Multiple projects in monorepo with dependencies
function Test-MonorepoWithDependencies {
    $testName = "Monorepo with Dependencies"
    $repoDir = "$env:TEST_DIR\test5b-monorepo-deps"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        # Create ProjectA (base library)
        New-TestProject -ProjectName "ProjectA" -ProjectDir "src\ProjectA"
        
        # Create ProjectB with dependency on ProjectA
        New-Item -ItemType Directory -Path "src\ProjectB" -Force | Out-Null
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ProjectA/ProjectA.csproj" />
  </ItemGroup>
</Project>
"@ | Out-File -FilePath "src\ProjectB\ProjectB.csproj" -Encoding UTF8
        
        @"
namespace ProjectB
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello from ProjectB");
            ProjectA.Program.Main(args);
        }
    }
}
"@ | Out-File -FilePath "src\ProjectB\Program.cs" -Encoding UTF8

        # Create ProjectC (standalone)
        New-TestProject -ProjectName "ProjectC" -ProjectDir "src\ProjectC"
        
        git add . | Out-Null
        git commit -m "Initial commit with dependencies" | Out-Null
        
        # Tag project-specific versions using new format
        git tag ProjectA-v1.0.0 | Out-Null
        git tag ProjectB-v1.0.0 | Out-Null
        git tag ProjectC-v1.0.0 | Out-Null
        
        # Make changes to ProjectA (which should trigger ProjectB version bump)
        Add-Content -Path "src\ProjectA\Program.cs" -Value "// ProjectA update"
        git add . | Out-Null
        git commit -m "Update ProjectA" | Out-Null
        
        $result1 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectA\ProjectA.csproj" -ExpectedVersion "1.0.1" -TestName "$testName - ProjectA"
        $result2 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectB\ProjectB.csproj" -ExpectedVersion "1.0.1" -TestName "$testName - ProjectB (depends on A)"
        $result3 = Test-MonorepoVersioningTool -RepoPath $repoDir -ProjectPath ".\src\ProjectC\ProjectC.csproj" -ExpectedVersion "1.0.0" -TestName "$testName - ProjectC (independent)"
        
        return $result1 -and $result2 -and $result3
    }
    finally {
        Pop-Location
    }
}

# Test 6: Pre-release versions
function Test-PrereleaseVersions {
    $testName = "Pre-release Version Progression"
    $repoDir = "$env:TEST_DIR\test6-prerelease"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
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
    $repoDir = "$env:TEST_DIR\test7-dev-branch"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        # Tag on the current branch (which is already main/master)
        git tag v1.0.0 | Out-Null
        
        # Create and switch to dev branch
        git checkout -b dev | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Dev feature"
        git add . | Out-Null
        git commit -m "Add dev feature" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1-dev.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 8: Version with build metadata
function Test-BuildMetadata {
    $testName = "Version with Build Metadata"
    $repoDir = "$env:TEST_DIR\test8-build-metadata"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0+build.123 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// New build"
        git add . | Out-Null
        git commit -m "New build" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 9: Configuration Tests - PrereleaseType=alpha
function Test-ConfigAlpha {
    $testName = "Configuration: PrereleaseType=alpha"
    $repoDir = "$env:TEST_DIR\test9-config-alpha"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        
        # Test with alpha prerelease type
        $result1 = Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "alpha" -ExpectedVersion "0.1.0-alpha.1" -TestName "$testName - Initial"
        
        git tag v0.1.0-alpha.1 | Out-Null
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Update"
        git add . | Out-Null
        git commit -m "Update code" | Out-Null
        
        $result2 = Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "alpha" -ExpectedVersion "0.1.0-alpha.2" -TestName "$testName - Increment"
        
        return $result1 -and $result2
    }
    finally {
        Pop-Location
    }
}

# Test 10: Configuration Tests - PrereleaseType=beta
function Test-ConfigBeta {
    $testName = "Configuration: PrereleaseType=beta"
    $repoDir = "$env:TEST_DIR\test10-config-beta"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Beta feature"
        git add . | Out-Null
        git commit -m "Add beta feature" | Out-Null
        
        return Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "beta" -ExpectedVersion "1.0.1-beta.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 11: Configuration Tests - PrereleaseType=rc
function Test-ConfigRC {
    $testName = "Configuration: PrereleaseType=rc"
    $repoDir = "$env:TEST_DIR\test11-config-rc"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v2.0.0 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// RC feature"
        git add . | Out-Null
        git commit -m "Add RC feature" | Out-Null
        
        return Test-VersioningToolWithPrerelease -RepoPath $repoDir -PrereleaseType "rc" -ExpectedVersion "2.0.1-rc.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 12: YAML Configuration Tests
function Test-YamlConfig {
    $testName = "YAML Configuration File"
    $repoDir = "$env:TEST_DIR\test12-yaml-config"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        # Create YAML config file
        @"
tagPrefix: "v"
prereleaseType: "beta"
"@ | Out-File -FilePath "mister-version.yml" -Encoding UTF8
        
        git add . | Out-Null
        git commit -m "Initial commit with config" | Out-Null
        
        return Test-VersioningTool -RepoPath $repoDir -ExpectedVersion "0.1.0-beta.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 13: Force Version Tests
function Test-ForceVersion {
    $testName = "Force Version Override"
    $repoDir = "$env:TEST_DIR\test13-force-version"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Some changes"
        git add . | Out-Null
        git commit -m "Some changes" | Out-Null
        
        return Test-VersioningToolForce -RepoPath $repoDir -ForceVersion "2.5.0" -ExpectedVersion "2.5.0" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 14: Tag Prefix Variations
function Test-TagPrefix {
    $testName = "Tag Prefix Variations"
    $repoDir = "$env:TEST_DIR\test14-tag-prefix"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        New-TestProject -ProjectName "TestProject" -ProjectDir "src\TestProject"
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag release-1.0.0 | Out-Null  # Different prefix
        
        Add-Content -Path "src\TestProject\Program.cs" -Value "// Changes"
        git add . | Out-Null
        git commit -m "Changes" | Out-Null
        
        return Test-VersioningToolWithTagPrefix -RepoPath $repoDir -TagPrefix "release-" -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Test 15: Dependency Tracking
function Test-DependencyTracking {
    $testName = "Dependency Tracking in Monorepo"
    $repoDir = "$env:TEST_DIR\test15-dependencies"
    
    New-Item -ItemType Directory -Path $repoDir -Force | Out-Null
    Push-Location $repoDir
    try {
        git init | Out-Null
        git config user.email "test@example.com"
        git config user.name "Test User"
        
        # Create projects with dependencies
        New-TestProject -ProjectName "SharedLib" -ProjectDir "src\SharedLib"
        New-TestProject -ProjectName "App" -ProjectDir "src\App"
        
        # Add shared dependency file
        "// Shared utility" | Out-File -FilePath "src\SharedLib\Utils.cs" -Encoding UTF8
        
        git add . | Out-Null
        git commit -m "Initial commit" | Out-Null
        git tag v1.0.0 | Out-Null
        
        # Update shared lib
        Add-Content -Path "src\SharedLib\Utils.cs" -Value "// Updated utility"
        git add . | Out-Null
        git commit -m "Update shared lib" | Out-Null
        
        return Test-VersioningToolWithDependencies -RepoPath $repoDir -ProjectPath ".\src\App\App.csproj" -Dependencies "src\SharedLib" -ExpectedVersion "1.0.1" -TestName $testName
    }
    finally {
        Pop-Location
    }
}

# Helper function for prerelease type tests
function Test-VersioningToolWithPrerelease {
    param(
        [string]$RepoPath,
        [string]$PrereleaseType,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "" -Color White
    Write-ColorOutput "Running test: $TestName" -Color Cyan
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Blue
    Write-ColorOutput "Prerelease Type: $PrereleaseType" -Color Magenta
    
    $output = & $env:TOOL_PATH version --repo $RepoPath --project ".\src\TestProject\TestProject.csproj" --prerelease-type $PrereleaseType 2>&1 | Out-String
    
    if ($output -match 'Version:\s*([^\r\n]+)') {
        $actualVersion = $matches[1].Trim()
    }
    
    $script:TotalTests++
    
    if ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        $script:PassedTests++
        "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $true
    }
    else {
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $output
        "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $false
    }
}

# Helper function for force version tests
function Test-VersioningToolForce {
    param(
        [string]$RepoPath,
        [string]$ForceVersion,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "" -Color White
    Write-ColorOutput "Running test: $TestName" -Color Cyan
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Blue
    Write-ColorOutput "Force Version: $ForceVersion" -Color Magenta
    
    $output = & $env:TOOL_PATH version --repo $RepoPath --project ".\src\TestProject\TestProject.csproj" --force-version $ForceVersion 2>&1 | Out-String
    
    if ($output -match 'Version:\s*([^\r\n]+)') {
        $actualVersion = $matches[1].Trim()
    }
    
    $script:TotalTests++
    
    if ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        $script:PassedTests++
        "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $true
    }
    else {
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $output
        "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $false
    }
}

# Helper function for tag prefix tests
function Test-VersioningToolWithTagPrefix {
    param(
        [string]$RepoPath,
        [string]$TagPrefix,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "" -Color White
    Write-ColorOutput "Running test: $TestName" -Color Cyan
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Blue
    Write-ColorOutput "Tag Prefix: $TagPrefix" -Color Magenta
    
    $output = & $env:TOOL_PATH version --repo $RepoPath --project ".\src\TestProject\TestProject.csproj" --tag-prefix $TagPrefix 2>&1 | Out-String
    
    if ($output -match 'Version:\s*([^\r\n]+)') {
        $actualVersion = $matches[1].Trim()
    }
    
    $script:TotalTests++
    
    if ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        $script:PassedTests++
        "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $true
    }
    else {
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $output
        "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $false
    }
}

# Helper function for dependency tracking tests
function Test-VersioningToolWithDependencies {
    param(
        [string]$RepoPath,
        [string]$ProjectPath,
        [string]$Dependencies,
        [string]$ExpectedVersion,
        [string]$TestName
    )
    
    Write-ColorOutput "" -Color White
    Write-ColorOutput "Running test: $TestName" -Color Cyan
    Write-ColorOutput "Expected version: $ExpectedVersion" -Color Blue
    Write-ColorOutput "Dependencies: $Dependencies" -Color Magenta
    
    $output = & $env:TOOL_PATH version --repo $RepoPath --project $ProjectPath --dependencies $Dependencies 2>&1 | Out-String
    
    if ($output -match 'Version:\s*([^\r\n]+)') {
        $actualVersion = $matches[1].Trim()
    }
    
    $script:TotalTests++
    
    if ($actualVersion -eq $ExpectedVersion) {
        Write-ColorOutput "✓ PASSED: Got version $actualVersion" -Color Green
        $script:PassedTests++
        "✅ **$TestName**: $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $true
    }
    else {
        Write-ColorOutput "✗ FAILED: Expected $ExpectedVersion but got $actualVersion" -Color Red
        Write-Host "Full output:"
        Write-Host $output
        "❌ **$TestName**: Expected $ExpectedVersion but got $actualVersion" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        return $false
    }
}

# Export test summary at the end
function Export-TestSummary {
    "" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    "### Test Summary" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    "- Total Tests: $script:TotalTests" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    "- Passed: $script:PassedTests" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    "- Failed: $($script:TotalTests - $script:PassedTests)" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    
    if ($script:PassedTests -eq $script:TotalTests) {
        "" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        "✅ **All tests passed!**" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    } else {
        "" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        "❌ **Some tests failed!**" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
        exit 1
    }
}