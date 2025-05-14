#!/bin/bash

# Test functions for Linux versioning scenarios

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
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
    echo "$message" >> "$RUNNER_TEMP/test-results.txt"
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
    
    print_status "$YELLOW" "\nRunning test: $test_name"
    print_status "$YELLOW" "Expected version: $expected_version"
    
    # Run the versioning tool
    cd "$repo_path"
    local output=$("$TOOL_PATH" calculate --repo-root . 2>&1) || true
    local actual_version=$(echo "$output" | grep -oP "Version: \K[^\s]+" | head -1)
    
    if [ -z "$actual_version" ]; then
        # Try to extract version from JSON output
        actual_version=$(echo "$output" | grep -oP '"Version":\s*"\K[^"]+' | head -1)
    fi
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
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
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init
    git config user.email "test@example.com"
    git config user.name "Test User"
    
    create_test_project "TestProject" "src/TestProject"
    
    git add .
    git commit -m "Initial commit"
    
    run_versioning_tool "$repo_dir" "0.1.0-alpha.1" "$test_name"
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
    
    run_versioning_tool "$repo_dir" "1.0.1-alpha.1" "$test_name"
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
    
    run_versioning_tool "$repo_dir" "1.1.0-new-feature.1" "$test_name"
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
    
    # Tag project-specific version
    git tag ProjectA/v1.2.0
    
    # Make changes to ProjectA
    echo "// ProjectA update" >> src/ProjectA/Program.cs
    git add .
    git commit -m "Update ProjectA"
    
    run_versioning_tool "$repo_dir/src/ProjectA" "1.2.1-alpha.1" "$test_name - ProjectA"
    run_versioning_tool "$repo_dir/src/ProjectB" "1.0.1-alpha.1" "$test_name - ProjectB"
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
    
    # Create main branch and tag
    git checkout -b main
    git tag v1.0.0
    
    # Create and switch to dev branch
    git checkout -b dev
    
    echo "// Dev feature" >> src/TestProject/Program.cs
    git add .
    git commit -m "Add dev feature"
    
    run_versioning_tool "$repo_dir" "1.1.0-dev.1" "$test_name"
}

# Test 8: Version with build metadata
test_build_metadata() {
    local test_name="Version with Build Metadata"
    local repo_dir="$TEST_DIR/test8-build-metadata"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
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
    run_versioning_tool "$repo_dir" "1.0.1-alpha.1" "$test_name"
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