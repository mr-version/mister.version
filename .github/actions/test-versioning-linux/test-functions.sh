#!/bin/bash

# Test functions for Linux versioning scenarios

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE="\033[0;34m"
PURPLE="\033[0;35m"
CYAN="\033[0;36m"
NC='\033[0m' # No Color

# Results tracking
TOTAL_TESTS=0
PASSED_TESTS=0

# Function to print colored output
print_status() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
    
    # Also log to results file
    if [ -n "$RUNNER_TEMP" ]; then
        echo "$message" >> "$RUNNER_TEMP/test-results.txt"
    elif [ -n "$TEST_DIR" ]; then
        echo "$message" >> "$TEST_DIR/test-results.txt"
    fi
}

# Function to create a versioning repo
create_versioning_repo() {
    local repo_dir=$1
    
    if [ -d "$repo_dir" ]; then
        rm -rf "$repo_dir"
    fi
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    
    # Initialize git repo
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
}

# Function to create a test project
create_test_project() {
    local project_name=$1
    local project_dir=$2
    
    mkdir -p "$project_dir"
    cat > "$project_dir/${project_name}.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>
EOF
    
    cat > "$project_dir/Program.cs" << EOF
namespace $project_name
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello from $project_name");
        }
    }
}
EOF
}

# Function to run versioning tool and capture output
run_versioning_tool() {
    local repo_path=$1
    local expected_version=$2
    local test_name=$3
    
    print_status "$YELLOW" "Running test: $test_name"
    print_status "$YELLOW" "Expected version: $expected_version"
    print_status "$YELLOW" "Repo path: $repo_path"

    # Run the versioning tool
    print_status "$PURPLE" "Current Directory: $PWD"

    # Find the first .csproj file
    local project_file=$(find . -name "*.csproj" -type f | head -1)
    if [ -z "$project_file" ]; then
        print_status "$RED" "No .csproj file found in $repo_path"
        return 1
    fi
    
    # Build command with prerelease type if set
    # Build command with prerelease type if set
    if [ "$USE_DOTNET" = "true" ]; then
        local display_cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_file\""
    else
        local display_cmd="\"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_file\""
    fi
    if [ -n "$PRERELEASE_TYPE" ] && [ "$PRERELEASE_TYPE" != "none" ]; then
        display_cmd="$display_cmd --prerelease-type $PRERELEASE_TYPE"
    fi
    print_status "$BLUE" "Running command: $display_cmd"

    # Run the version command (with prerelease type if set)
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_file\""
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_file\""
    fi
    if [ -n "$PRERELEASE_TYPE" ] && [ "$PRERELEASE_TYPE" != "none" ]; then
        cmd="$cmd --prerelease-type $PRERELEASE_TYPE"
    fi
    local output=$(eval "$cmd 2>&1") || true
    
    # Extract version from "Version: X.X.X" format
    local actual_version=$(echo "$output" | grep "^Version: " | sed 's/Version: //' | tr -d '\r\n' | xargs)
    
    # Handle case where version might be "Unknown"
    if [ "$actual_version" == "Unknown" ]; then
        actual_version=""
    fi
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    # Debug output
    if [ -z "$actual_version" ]; then
        print_status "$YELLOW" "Debug: Could not extract version from output"
        print_status "$YELLOW" "Debug: Raw output:"
        echo "$output"
    fi
    
    if [ "$actual_version" == "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        
        # Add to GitHub summary
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
        return 0
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        
        # Add to GitHub summary
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
        return 1
    fi
}

# Function to run versioning tool for monorepo projects
run_monorepo_versioning_tool() {
    local repo_path=$1
    local project_path=$2
    local expected_version=$3
    local test_name=$4
    
    print_status "$YELLOW" "Running test: $test_name"
    print_status "$YELLOW" "Expected version: $expected_version"
    print_status "$YELLOW" "Repo path: $repo_path"
    print_status "$YELLOW" "Project path: $project_path"

    # Run the versioning tool
    print_status "$PURPLE" "Current Directory: $PWD"
    
    # Build command with prerelease type if set
    # Build command with prerelease type if set
    if [ "$USE_DOTNET" = "true" ]; then
        local display_cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_path\""
    else
        local display_cmd="\"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_path\""
    fi
    if [ -n "$PRERELEASE_TYPE" ] && [ "$PRERELEASE_TYPE" != "none" ]; then
        display_cmd="$display_cmd --prerelease-type $PRERELEASE_TYPE"
    fi
    print_status "$BLUE" "Running command: $display_cmd"

    # Run the version command (with prerelease type if set)
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_path\""
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_path\" --project \"$project_path\""
    fi
    if [ -n "$PRERELEASE_TYPE" ] && [ "$PRERELEASE_TYPE" != "none" ]; then
        cmd="$cmd --prerelease-type $PRERELEASE_TYPE"
    fi
    local output=$(eval "$cmd 2>&1") || true
    
    # Extract version from "Version: X.X.X" format
    local actual_version=$(echo "$output" | grep "^Version: " | sed 's/Version: //' | tr -d '\r\n' | xargs)
    
    # Handle case where version might be "Unknown"
    if [ "$actual_version" == "Unknown" ]; then
        actual_version=""
    fi
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    # Debug output
    if [ -z "$actual_version" ]; then
        print_status "$YELLOW" "Debug: Could not extract version from output"
        print_status "$YELLOW" "Debug: Raw output:"
        echo "$output"
    fi
    
    if [ "$actual_version" == "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        
        # Add to GitHub summary
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
        return 0
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        
        # Add to GitHub summary
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
        return 1
    fi
}

# Test 1: Initial repository with no tags
test_initial_repo() {
    local test_name="Initial Repository (No Tags)"
    local repo_dir="$TEST_DIR/test1-initial"
    
    print_status "$YELLOW" "Creating test repo at: $repo_dir"
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    
    run_versioning_tool "$repo_dir" "0.1.0" "$test_name"
}

# Test 2: Repository with a single release tag
test_single_release_tag() {
    local test_name="Single Release Tag"
    local repo_dir="$TEST_DIR/test2-single-tag"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    # Make another commit
    echo "// New feature" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add new feature"
    
    run_versioning_tool "$repo_dir" "1.0.1" "$test_name"
}

# Test 3: Feature branch versioning
test_feature_branch() {
    local test_name="Feature Branch Versioning"
    local repo_dir="$TEST_DIR/test3-feature-branch"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    # Create and switch to feature branch
    git checkout -b feature/new-feature
    
    echo "// Feature code" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add feature"
    
    run_versioning_tool "$repo_dir" "1.0.1-new-feature.1" "$test_name"
}

# Test 4: Release branch versioning
test_release_branch() {
    local test_name="Release Branch Versioning"
    local repo_dir="$TEST_DIR/test4-release-branch"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    # Create and switch to release branch
    git checkout -b release/1.1
    
    echo "// Release prep" >> src/TestProject/Program.cs
    git add .
    git commit -m "Prepare release"
    
    run_versioning_tool "$repo_dir" "1.1.0-rc.1" "$test_name"
}

# Test 5: Multiple projects in monorepo
test_monorepo() {
    local test_name="Monorepo with Multiple Projects"
    local repo_dir="$TEST_DIR/test5-monorepo"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    # Create multiple projects
    create_test_project "ProjectA" "src/ProjectA"
    create_test_project "ProjectB" "src/ProjectB"
    create_test_project "ProjectC" "src/ProjectC"
    
    git add .
    git commit -m "Initial commit"
    
    # Tag global version
    git tag v1.0.0
    
    # Tag project-specific versions using new format
    git tag ProjectA-v1.2.0
    git tag ProjectB-v1.0.0
    git tag ProjectC-v1.0.0
    
    # Make changes to ProjectA
    echo "// ProjectA update" >> src/ProjectA/Program.cs
    git add .
    git commit -m "Update ProjectA"
    
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectA/ProjectA.csproj" "1.2.1" "$test_name - ProjectA"
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectB/ProjectB.csproj" "1.0.0" "$test_name - ProjectB"
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectC/ProjectC.csproj" "1.0.0" "$test_name - ProjectC"
}

# Test 5b: Multiple projects in monorepo with dependencies
test_monorepo_with_dependencies() {
    local test_name="Monorepo with Dependencies"
    local repo_dir="$TEST_DIR/test5b-monorepo-deps"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    # Create ProjectA (base library)
    create_test_project "ProjectA" "src/ProjectA"
    
    # Create ProjectB with dependency on ProjectA
    mkdir -p "src/ProjectB"
    cat > "src/ProjectB/ProjectB.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ProjectA/ProjectA.csproj" />
  </ItemGroup>
</Project>
EOF
    
    cat > "src/ProjectB/Program.cs" << EOF
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
EOF

    # Create ProjectC (standalone)
    create_test_project "ProjectC" "src/ProjectC"
    
    git add .
    git commit -m "Initial commit with dependencies"
    
    # Tag project-specific versions using new format
    git tag ProjectA-v1.0.0
    git tag ProjectB-v1.0.0
    git tag ProjectC-v1.0.0
    
    # Make changes to ProjectA (which should trigger ProjectB version bump)
    echo "// ProjectA update" >> src/ProjectA/Program.cs
    git add .
    git commit -m "Update ProjectA"
    
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectA/ProjectA.csproj" "1.0.1" "$test_name - ProjectA"
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectB/ProjectB.csproj" "1.0.1" "$test_name - ProjectB (depends on A)"
    run_monorepo_versioning_tool "$repo_dir" "./src/ProjectC/ProjectC.csproj" "1.0.0" "$test_name - ProjectC (independent)"
}

# Test 6: Pre-release versions
test_prerelease_versions() {
    local test_name="Pre-release Version Progression"
    local repo_dir="$TEST_DIR/test6-prerelease"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0-alpha.1
    
    echo "// Alpha 2" >> src/TestProject/Program.cs
    git add .
    git commit -m "Alpha 2 changes"
    
    run_versioning_tool "$repo_dir" "1.0.0-alpha.2" "$test_name - Alpha progression"
    
    git tag v1.0.0-alpha.2
    git tag v1.0.0-beta.1
    
    echo "// Beta 2" >> src/TestProject/Program.cs
    git add .
    git commit -m "Beta 2 changes"
    
    run_versioning_tool "$repo_dir" "1.0.0-beta.2" "$test_name - Beta progression"
}

# Test 7: Dev branch versioning
test_dev_branch() {
    local test_name="Dev Branch Versioning"
    local repo_dir="$TEST_DIR/test7-dev-branch"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    
    # Tag on the current branch (which is already main)
    git tag v1.0.0
    
    # Create and switch to dev branch
    git checkout -b dev
    
    echo "// Dev feature" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add dev feature"
    
    run_versioning_tool "$repo_dir" "1.0.1-dev.1" "$test_name"
}

# Test 8: Version with build metadata
test_build_metadata() {
    local test_name="Version with Build Metadata"
    local repo_dir="$TEST_DIR/test8-build-metadata"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git config --global init.defaultBranch main
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0+build.123
    
    echo "// New build" >> src/TestProject/Program.cs
    git add .
    git commit -m "New build"
    
    # The tool should increment patch and add its own metadata
    run_versioning_tool "$repo_dir" "1.0.1" "$test_name"
}

# Test 8: Configuration Tests - PrereleaseType=alpha
test_config_alpha() {
    local test_name="Configuration: PrereleaseType=alpha"
    local repo_dir="$TEST_DIR/test8-config-alpha"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    
    # Test with alpha prerelease type - should get 0.1.0-alpha.1 for initial repo
    run_versioning_tool_with_prerelease "$repo_dir" "alpha" "0.1.0-alpha.1" "$test_name - Initial"
    
    # Tag and make another commit
    git tag v0.1.0-alpha.1
    echo "// Update" >> src/TestProject/Program.cs
    git add .
    git commit -m "Update code"
    
    # Should increment to alpha.2
    run_versioning_tool_with_prerelease "$repo_dir" "alpha" "0.1.0-alpha.2" "$test_name - Increment"
}

# Test 9: Configuration Tests - PrereleaseType=beta
test_config_beta() {
    local test_name="Configuration: PrereleaseType=beta"
    local repo_dir="$TEST_DIR/test9-config-beta"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    echo "// Beta feature" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add beta feature"
    
    # Should get 1.0.1-beta.1 with beta prerelease type
    run_versioning_tool_with_prerelease "$repo_dir" "beta" "1.0.1-beta.1" "$test_name"
}

# Test 10: Configuration Tests - PrereleaseType=rc
test_config_rc() {
    local test_name="Configuration: PrereleaseType=rc"
    local repo_dir="$TEST_DIR/test10-config-rc"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v2.0.0
    
    echo "// RC feature" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add RC feature"
    
    # Should get 2.0.1-rc.1 with rc prerelease type
    run_versioning_tool_with_prerelease "$repo_dir" "rc" "2.0.1-rc.1" "$test_name"
}

# Test 11: YAML Configuration Tests
test_yaml_config() {
    local test_name="YAML Configuration File"
    local repo_dir="$TEST_DIR/test11-yaml-config"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    # Create YAML config file
    cat > mister-version.yml << EOF
tagPrefix: "v"
prereleaseType: "beta"
EOF
    
    git add .
    git commit -m "Initial commit with config"
    
    # Should use beta from config file
    run_versioning_tool "$repo_dir" "0.1.0-beta.1" "$test_name"
}

# Test 12: Force Version Tests
test_force_version() {
    local test_name="Force Version Override"
    local repo_dir="$TEST_DIR/test12-force-version"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    echo "// Some changes" >> src/TestProject/Program.cs
    git add .
    git commit -m "Some changes"
    
    # Force specific version
    run_versioning_tool_force "$repo_dir" "2.5.0" "2.5.0" "$test_name"
}

# Test 13: Tag Prefix Variations
test_tag_prefix() {
    local test_name="Tag Prefix Variations"
    local repo_dir="$TEST_DIR/test13-tag-prefix"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    git tag release-1.0.0  # Different prefix
    
    echo "// Changes" >> src/TestProject/Program.cs
    git add .
    git commit -m "Changes"
    
    # Test with custom tag prefix
    run_versioning_tool_with_tag_prefix "$repo_dir" "release-" "1.0.1" "$test_name"
}

# Test 14: Dependency Tracking
test_dependency_tracking() {
    local test_name="Dependency Tracking in Monorepo"
    local repo_dir="$TEST_DIR/test14-dependencies"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    # Create SharedLib project
    create_test_project "SharedLib" "src/SharedLib"
    
    # Create App project with dependency on SharedLib
    mkdir -p "src/App"
    cat > "src/App/App.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../SharedLib/SharedLib.csproj" />
  </ItemGroup>
</Project>
EOF
    
    cat > "src/App/Program.cs" << EOF
namespace App
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello from App");
        }
    }
}
EOF
    
    # Add shared dependency file
    echo "// Shared utility" > src/SharedLib/Utils.cs
    
    git add .
    git commit -m "Initial commit"
    git tag v1.0.0
    
    # Update shared lib
    echo "// Updated utility" >> src/SharedLib/Utils.cs
    git add .
    git commit -m "Update shared lib"
    
    # App should be affected by shared lib changes via transitive dependency detection
    run_versioning_tool_with_dependencies "$repo_dir" "./src/App/App.csproj" "src/SharedLib" "1.0.1" "$test_name"
}

# Helper function for prerelease type tests
run_versioning_tool_with_prerelease() {
    local repo_dir=$1
    local prerelease_type=$2
    local expected_version=$3
    local test_name=$4
    
    echo ""
    print_status "$CYAN" "Running test: $test_name"
    print_status "$BLUE" "Expected version: $expected_version"
    print_status "$PURPLE" "Repo path: $repo_dir"
    print_status "$PURPLE" "Current Directory: $(pwd)"
    print_status "$PURPLE" "Prerelease Type: $prerelease_type"
    
    # Verify tool exists
    if [ ! -f "$TOOL_PATH" ]; then
        print_status "$RED" "Tool not found at: $TOOL_PATH"
        return 1
    fi
    
    # Check if we need to use dotnet
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --prerelease-type \"$prerelease_type\" --debug"
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --prerelease-type \"$prerelease_type\" --debug"
    fi
    
    print_status "$YELLOW" "Running command: $cmd"
    
    local output
    local stderr_file=$(mktemp)
    output=$(eval $cmd 2>"$stderr_file")
    local exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Command failed with exit code $exit_code"
        echo "Standard output:"
        echo "$output"
        echo ""
        echo "Standard error:"
        cat "$stderr_file"
        rm -f "$stderr_file"
        ((TOTAL_TESTS++))
        return 1
    fi
    rm -f "$stderr_file"
    
    # Extract version from output
    local actual_version=$(echo "$output" | grep "^Version:" | cut -d' ' -f2- | tr -d '\r\n' | xargs)
    
    ((TOTAL_TESTS++))
    
    if [ "$actual_version" = "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
    fi
}

# Helper function for force version tests
run_versioning_tool_force() {
    local repo_dir=$1
    local force_version=$2
    local expected_version=$3
    local test_name=$4
    
    echo ""
    print_status "$CYAN" "Running test: $test_name"
    print_status "$BLUE" "Expected version: $expected_version"
    print_status "$PURPLE" "Force Version: $force_version"
    
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --force-version \"$force_version\""
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --force-version \"$force_version\""
    fi
    print_status "$YELLOW" "Running command: $cmd"
    
    local output
    output=$(eval $cmd 2>&1)
    local exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Command failed with exit code $exit_code"
        echo "Error output:"
        echo "$output"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    local actual_version=$(echo "$output" | grep "^Version:" | cut -d' ' -f2- | tr -d '\r\n' | xargs)
    
    ((TOTAL_TESTS++))
    
    if [ "$actual_version" = "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
    fi
}

# Helper function for tag prefix tests
run_versioning_tool_with_tag_prefix() {
    local repo_dir=$1
    local tag_prefix=$2
    local expected_version=$3
    local test_name=$4
    
    echo ""
    print_status "$CYAN" "Running test: $test_name"
    print_status "$BLUE" "Expected version: $expected_version"
    print_status "$PURPLE" "Tag Prefix: $tag_prefix"
    
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --tag-prefix \"$tag_prefix\""
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --tag-prefix \"$tag_prefix\""
    fi
    print_status "$YELLOW" "Running command: $cmd"
    
    local output
    output=$(eval $cmd 2>&1)
    local exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Command failed with exit code $exit_code"
        echo "Error output:"
        echo "$output"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    local actual_version=$(echo "$output" | grep "^Version:" | cut -d' ' -f2- | tr -d '\r\n' | xargs)
    
    ((TOTAL_TESTS++))
    
    if [ "$actual_version" = "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
    fi
}

# Helper function for dependency tracking tests
run_versioning_tool_with_dependencies() {
    local repo_dir=$1
    local project_path=$2
    local dependencies=$3
    local expected_version=$4
    local test_name=$5
    
    echo ""
    print_status "$CYAN" "Running test: $test_name"
    print_status "$BLUE" "Expected version: $expected_version"
    print_status "$PURPLE" "Dependencies: $dependencies (auto-detected)"
    
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"$project_path\""
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"$project_path\""
    fi
    print_status "$YELLOW" "Running command: $cmd"
    
    local output
    output=$(eval $cmd 2>&1)
    local exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Command failed with exit code $exit_code"
        echo "Error output:"
        echo "$output"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    local actual_version=$(echo "$output" | grep "^Version:" | cut -d' ' -f2- | tr -d '\r\n' | xargs)
    
    ((TOTAL_TESTS++))
    
    if [ "$actual_version" = "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
    fi
}

# Export test summary at the end
export_test_summary() {
    echo "" >> $GITHUB_STEP_SUMMARY
    echo "### Test Summary" >> $GITHUB_STEP_SUMMARY
    echo "- Total Tests: $TOTAL_TESTS" >> $GITHUB_STEP_SUMMARY
    echo "- Passed: $PASSED_TESTS" >> $GITHUB_STEP_SUMMARY
    echo "- Failed: $((TOTAL_TESTS - PASSED_TESTS))" >> $GITHUB_STEP_SUMMARY
    
    if [ $PASSED_TESTS -eq $TOTAL_TESTS ]; then
        echo "" >> $GITHUB_STEP_SUMMARY
        echo "✅ **All tests passed!**" >> $GITHUB_STEP_SUMMARY
    else
        echo "" >> $GITHUB_STEP_SUMMARY
        echo "❌ **Some tests failed!**" >> $GITHUB_STEP_SUMMARY
        exit 1
    fi
}

# Test global vs project tag priority scenarios
test_global_vs_project_tags() {
    local test_name="Global vs Project Tag Priority"
    local repo_dir="$TEST_DIR/global-vs-project-test"
    
    print_status "$CYAN" "=== Testing $test_name ==="
    
    # Create a fresh repo
    create_versioning_repo "$repo_dir"
    cd "$repo_dir"
    
    # Create test project
    create_test_project "TestProject" "src/TestProject"
    
    # Create global version tag (v2.0.0)
    git add .
    git commit -m "Initial commit"
    git tag v2.0.0
    
    # Create project-specific tag that is higher (using prefix format)
    git tag TestProject-v2.5.0
    
    # Add changes to test project
    echo "// Updated" >> src/TestProject/Program.cs
    git add .
    git commit -m "Update TestProject"
    
    # Should use project tag (higher version) -> 2.5.1
    run_versioning_tool "$repo_dir" "2.5.1" "Project tag takes precedence when higher"
    
    # Create a global tag that is higher than project tag
    git tag v3.0.0
    
    # Make another change
    echo "// Another update" >> src/TestProject/Program.cs
    git add .
    git commit -m "Another update"
    
    # Should use global tag (higher version) -> 3.0.1
    run_versioning_tool "$repo_dir" "3.0.1" "Global tag takes precedence when higher"
}

# Test new release cycle detection
test_new_release_cycle_detection() {
    local test_name="New Release Cycle Detection"
    local repo_dir="$TEST_DIR/new-release-cycle-test"
    
    print_status "$CYAN" "=== Testing $test_name ==="
    
    # Create a fresh repo
    create_versioning_repo "$repo_dir"
    cd "$repo_dir"
    
    # Create test project
    create_test_project "TestProject" "src/TestProject"
    
    # Simulate existing project tags (older release cycle)
    git add .
    git commit -m "Initial commit"
    git tag v1.5.3-testproject
    
    # Create global tag for new release cycle (major version bump)
    git tag v2.0.0
    
    # Add changes to test project
    echo "// Updated for v2" >> src/TestProject/Program.cs
    git add .
    git commit -m "Update for v2.0.0"
    
    # Should detect new release cycle and use global tag -> 2.0.1
    run_versioning_tool "$repo_dir" "2.0.1" "New major release cycle detected"
    
    # Test minor version new release cycle
    git tag v2.1.0  # New minor version
    
    echo "// Updated for v2.1" >> src/TestProject/Program.cs
    git add .
    git commit -m "Update for v2.1.0"
    
    # Should use new minor release cycle -> 2.1.1
    run_versioning_tool "$repo_dir" "2.1.1" "New minor release cycle detected"
}

# Test mr-version.yml baseVersion scenarios
test_config_baseversion_scenarios() {
    local test_name="Config BaseVersion Scenarios"
    local repo_dir="$TEST_DIR/config-baseversion-test"
    
    print_status "$CYAN" "=== Testing $test_name ==="
    
    # Create a fresh repo
    create_versioning_repo "$repo_dir"
    cd "$repo_dir"
    
    # Create test project
    create_test_project "TestProject" "src/TestProject"
    
    # Create project tag
    git add .
    git commit -m "Initial commit"
    git tag v1.2.3-testproject
    
    # Create mr-version.yml with higher baseVersion
    cat > mr-version.yml << EOF
baseVersion: "2.0.0"
prereleaseType: none
defaultIncrement: patch
EOF
    
    # Add changes
    echo "// Updated" >> src/TestProject/Program.cs
    git add .
    git commit -m "Update project"
    
    # Should use config baseVersion (new release cycle) -> 2.0.0 (first change gets exact baseVersion)
    run_versioning_tool "$repo_dir" "2.0.0" "Config baseVersion creates new release cycle"
    
    # Test with prerelease
    cat > mr-version.yml << EOF
baseVersion: "3.0.0"
prereleaseType: alpha
defaultIncrement: patch
EOF
    
    echo "// Alpha update" >> src/TestProject/Program.cs
    git add .
    git commit -m "Alpha update"
    
    # Should use config baseVersion -> 3.0.0 (first change gets exact baseVersion, no prerelease on first use)
    run_versioning_tool_with_config "$repo_dir" "alpha" "3.0.0" "Config baseVersion with prerelease"
}

# Helper function for config-based tests
run_versioning_tool_with_config() {
    local repo_dir=$1
    local prerelease_type=$2
    local expected_version=$3
    local test_name=$4
    
    echo ""
    print_status "$CYAN" "Running test: $test_name"
    print_status "$BLUE" "Expected version: $expected_version"
    print_status "$PURPLE" "Prerelease type: $prerelease_type"
    
    if [ "$USE_DOTNET" = "true" ]; then
        local cmd="dotnet \"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --prerelease-type \"$prerelease_type\" --debug"
    else
        local cmd="\"$TOOL_PATH\" version --repo \"$repo_dir\" --project \"./src/TestProject/TestProject.csproj\" --prerelease-type \"$prerelease_type\" --debug"
    fi
    
    print_status "$YELLOW" "Running command: $cmd"
    
    local output
    local stderr_file=$(mktemp)
    output=$(eval $cmd 2>"$stderr_file")
    local exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Command failed with exit code $exit_code"
        echo "Standard output:"
        echo "$output"
        echo ""
        echo "Standard error:"
        cat "$stderr_file"
        rm -f "$stderr_file"
        ((TOTAL_TESTS++))
        return 1
    fi
    rm -f "$stderr_file"
    
    # Extract version from output
    local actual_version=$(echo "$output" | grep "^Version:" | cut -d' ' -f2- | tr -d '\r\n' | xargs)
    
    ((TOTAL_TESTS++))
    
    if [ "$actual_version" = "$expected_version" ]; then
        print_status "$GREEN" "✓ PASSED: Got version $actual_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: $actual_version" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected $expected_version but got $actual_version"
        echo "Full output:"
        echo "$output"
        echo "❌ **$test_name**: Expected $expected_version but got $actual_version" >> $GITHUB_STEP_SUMMARY
    fi
}
# Test NuGet package generation with correct dependency versions
test_nuget_package_dependencies() {
    local test_name="NuGet Package Dependency Versions"
    local repo_dir="$TEST_DIR/nuget-package-test"
    
    print_status "$CYAN" "=== Testing $test_name ==="
    
    # Create a fresh repo
    create_versioning_repo "$repo_dir"
    cd "$repo_dir"
    
    # Create Core library project
    mkdir -p src/Core
    cat > src/Core/Core.csproj << EOCSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <Version>2.5.0</Version>
    <AssemblyVersion>2.5.0.0</AssemblyVersion>
  </PropertyGroup>
</Project>
EOCSPROJ
    echo "namespace Core { public class CoreClass { } }" > src/Core/CoreClass.cs
    
    # Create Service project that references Core
    mkdir -p src/Service
    cat > src/Service/Service.csproj << EOCSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <Version>3.1.0</Version>
    <AssemblyVersion>3.1.0.0</AssemblyVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Core/Core.csproj" />
  </ItemGroup>
</Project>
EOCSPROJ
    echo "namespace Service { public class ServiceClass { } }" > src/Service/ServiceClass.cs
    
    # Commit
    git add .
    git commit -m "Add Core and Service projects"
    
    # Build Core first
    print_status "$YELLOW" "Building Core project..."
    dotnet build src/Core/Core.csproj --configuration Release
    if [ $? -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Core project build failed"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    # Build and pack Service
    print_status "$YELLOW" "Building and packing Service project..."
    dotnet build src/Service/Service.csproj --configuration Release
    dotnet pack src/Service/Service.csproj --configuration Release --no-build --output ./nupkg
    
    if [ $? -ne 0 ]; then
        print_status "$RED" "✗ FAILED: Service project pack failed"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    # Extract and verify the nuspec file
    print_status "$YELLOW" "Verifying package dependencies..."
    
    # Find the generated package
    local package_file=$(find ./nupkg -name "Service.*.nupkg" | head -1)
    if [ -z "$package_file" ]; then
        print_status "$RED" "✗ FAILED: No package file found"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    # Extract nuspec and check dependency version
    local temp_extract="/tmp/nuget-extract-$$"
    mkdir -p "$temp_extract"
    unzip -q "$package_file" -d "$temp_extract"
    
    local nuspec_file=$(find "$temp_extract" -name "*.nuspec" | head -1)
    if [ -z "$nuspec_file" ]; then
        print_status "$RED" "✗ FAILED: No nuspec file found in package"
        rm -rf "$temp_extract"
        ((TOTAL_TESTS++))
        return 1
    fi
    
    # Check if Core dependency has version 2.5.0 (not 1.0.0)
    local core_version=$(grep -o 'id="Core" version="[^"]*"' "$nuspec_file" | grep -o 'version="[^"]*"' | cut -d'"' -f2)
    
    rm -rf "$temp_extract"
    
    ((TOTAL_TESTS++))
    
    if [ "$core_version" = "2.5.0" ]; then
        print_status "$GREEN" "✓ PASSED: Core dependency has correct version $core_version"
        ((PASSED_TESTS++))
        echo "✅ **$test_name**: Core dependency version correct ($core_version)" >> $GITHUB_STEP_SUMMARY
    else
        print_status "$RED" "✗ FAILED: Expected Core dependency version 2.5.0 but got $core_version"
        echo "❌ **$test_name**: Expected Core version 2.5.0 but got $core_version" >> $GITHUB_STEP_SUMMARY
    fi
}
