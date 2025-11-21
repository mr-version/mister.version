#!/bin/bash

# Mister.Version Integration Test Script
# This script wraps the GitHub Action test functions for local execution

set -e

# Store the original directory first
ORIGINAL_DIR="$(pwd)"

# Set up environment variables
# Force use of system temp directory to ensure we're outside the git tree
if [ -z "$TEST_DIR" ]; then
    # Create temp directory explicitly in /tmp
    export TEST_DIR="$(mktemp -d /tmp/mister-version-tests-XXXXXX)" || {
        echo "Failed to create temp directory"
        exit 1
    }
fi

# Ensure TOOL_PATH is absolute and set correct extension based on environment
if [ "$USE_DOTNET" = "true" ]; then
    export TOOL_PATH="${TOOL_PATH:-$ORIGINAL_DIR/Mister.Version.CLI/bin/Debug/net8.0/mr-version.dll}"
else
    export TOOL_PATH="${TOOL_PATH:-$ORIGINAL_DIR/Mister.Version.CLI/bin/Debug/net8.0/mr-version}"
fi
export RUNNER_TEMP="${RUNNER_TEMP:-/tmp}"
export GITHUB_STEP_SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/null}"

# Set prerelease type for tests (none by default, as per the project's default configuration)
export PRERELEASE_TYPE="${PRERELEASE_TYPE:-none}"

# Source the test functions from GitHub Actions
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/.github/actions/test-versioning-linux/test-functions.sh"

# Main test runner
main() {
    echo "Starting Mister.Version Integration Tests"
    echo "========================================="
    echo ""
    echo "Test directory: $TEST_DIR"
    echo "Current directory: $(pwd)"
    echo ""
    
    # Ensure test directory exists and is outside the repo
    if [ ! -d "$TEST_DIR" ]; then
        mkdir -p "$TEST_DIR"
    fi
    
    # Verify we're using a temp directory
    if [[ ! "$TEST_DIR" =~ ^/tmp/ ]] && [[ ! "$TEST_DIR" =~ ^/var/tmp/ ]]; then
        echo "ERROR: Test directory is not in temp location: $TEST_DIR"
        echo "Please unset TEST_DIR or set it to a location under /tmp"
        exit 1
    fi
    
    # Initialize results file
    echo "" > "$RUNNER_TEMP/test-results.txt"
    
    # Build the CLI tool first
    echo "Building Mister.Version CLI tool..."
    cd "$ORIGINAL_DIR"
    dotnet build ./Mister.Version.CLI/Mister.Version.CLI.csproj -c Debug
    
    # Verify tool exists
    if [ ! -f "$TOOL_PATH" ]; then
        echo "Error: CLI tool not found at $TOOL_PATH"
        exit 1
    fi
    
    # Run all tests
    echo ""
    echo "Running test scenarios..."
    echo ""
    
    # Test functions from the GitHub Action
    test_initial_repo
    test_single_release_tag
    test_feature_branch
    test_release_branch
    test_monorepo
    test_monorepo_with_dependencies
    test_prerelease_versions
    test_dev_branch
    test_build_metadata
    test_config_alpha
    test_config_beta
    test_config_rc
    test_yaml_config
    test_force_version
    test_tag_prefix
    test_dependency_tracking
    test_detached_head
    test_shallow_clone
    test_cross_platform_paths
    test_global_vs_project_tags
    test_new_release_cycle_detection
    test_config_baseversion_scenarios
    test_nuget_package_dependencies
    test_multi_targeting_dependency_tracking
    test_cross_targeting_build_isolation

    # Export summary
    export_test_summary
    
    # Print local summary
    echo ""
    echo "========================================="
    echo "Test Summary"
    echo "========================================="
    echo "Total Tests: $TOTAL_TESTS"
    echo "Passed: $PASSED_TESTS"
    echo "Failed: $((TOTAL_TESTS - PASSED_TESTS))"
    
    if [ $PASSED_TESTS -eq $TOTAL_TESTS ]; then
        echo ""
        echo "✓ All tests passed!"
        exit 0
    else
        echo ""
        echo "✗ Some tests failed!"
        exit 1
    fi
}

# Cleanup function
cleanup() {
    if [ -n "$TEST_DIR" ]; then
        # Get absolute path of TEST_DIR
        local abs_test_dir="$(cd "$TEST_DIR" 2>/dev/null && pwd || echo "$TEST_DIR")"
        
        # Only clean up if it's in a temp directory
        if [[ "$abs_test_dir" == /tmp/* ]] || [[ "$abs_test_dir" == /var/tmp/* ]]; then
            echo "Cleaning up test directory: $abs_test_dir"
            rm -rf "$abs_test_dir"
        else
            echo "WARNING: Not cleaning up $abs_test_dir - not in temp directory"
        fi
    fi
}

# Set trap to cleanup on exit
trap cleanup EXIT

# Run main if script is executed directly
if [ "${BASH_SOURCE[0]}" == "${0}" ]; then
    main "$@"
fi