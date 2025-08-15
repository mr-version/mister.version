#!/bin/bash

# Performance test script for mr-version
set -e

echo "=== Mister.Version Performance Tests ==="

# Configuration
TEMP_DIR="/tmp/mr-version-perf-test"
TOOL_PATH="./Mister.Version.CLI/bin/Release/net8.0/mr-version"
if [[ "$USE_DOTNET" == "true" ]]; then
    TOOL_PATH="dotnet ./Mister.Version.CLI/bin/Release/net8.0/mr-version.dll"
fi

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to measure execution time
measure_time() {
    local description="$1"
    shift
    local command="$@"
    
    log_info "Measuring: $description"
    echo "Command: $command"
    
    local start_time=$(date +%s.%N)
    eval "$command" > /dev/null 2>&1
    local exit_code=$?
    local end_time=$(date +%s.%N)
    
    local duration=$(echo "$end_time - $start_time" | bc -l)
    
    if [ $exit_code -eq 0 ]; then
        printf "✅ %-50s %8.3f seconds\n" "$description" "$duration"
    else
        printf "❌ %-50s %8.3f seconds (FAILED)\n" "$description" "$duration"
        return $exit_code
    fi
}

# Create test scenarios
create_small_repo() {
    local repo_dir="$1"
    local project_count="$2"
    
    log_info "Creating small repo with $project_count projects"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init --quiet
    git config user.email "test@example.com"
    git config user.name "Performance Test"
    
    mkdir -p src
    for ((i=1; i<=project_count; i++)); do
        mkdir -p "src/Project$i"
        cat > "src/Project$i/Project$i.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>
EOF
        
        cat > "src/Project$i/Class$i.cs" << EOF
using System;
namespace Project$i
{
    public class Class$i
    {
        public void Method$i() => Console.WriteLine("Project $i");
    }
}
EOF
    done
    
    git add . --quiet
    git commit -m "Initial commit" --quiet
    git tag v1.0.0 --quiet
}

create_large_repo() {
    local repo_dir="$1"
    local project_count="$2"
    
    log_info "Creating large repo with $project_count projects and dependencies"
    
    mkdir -p "$repo_dir"
    cd "$repo_dir"
    git init --quiet
    git config user.email "test@example.com"
    git config user.name "Performance Test"
    
    mkdir -p src tests
    
    # Create projects with realistic dependency structure
    for ((i=1; i<=project_count; i++)); do
        local project_type="src"
        local is_test="false"
        local is_packable="true"
        
        # Make every 5th project a test project
        if [ $((i % 5)) -eq 0 ]; then
            project_type="tests"
            is_test="true"
            is_packable="false"
        fi
        
        mkdir -p "$project_type/Project$i"
        cat > "$project_type/Project$i/Project$i.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>$is_test</IsTestProject>
    <IsPackable>$is_packable</IsPackable>
  </PropertyGroup>
EOF
        
        # Add dependencies (each project depends on 1-3 previous projects)
        if [ $i -gt 3 ]; then
            echo "  <ItemGroup>" >> "$project_type/Project$i/Project$i.csproj"
            local dep_count=$((RANDOM % 3 + 1))
            for ((j=1; j<=dep_count; j++)); do
                local dep_id=$((i - j - 1))
                if [ $dep_id -gt 0 ] && [ $dep_id -lt $i ]; then
                    echo "    <ProjectReference Include=\"../../src/Project$dep_id/Project$dep_id.csproj\" />" >> "$project_type/Project$i/Project$i.csproj"
                fi
            done
            echo "  </ItemGroup>" >> "$project_type/Project$i/Project$i.csproj"
        fi
        
        echo "</Project>" >> "$project_type/Project$i/Project$i.csproj"
        
        cat > "$project_type/Project$i/Class$i.cs" << EOF
using System;
namespace Project$i
{
    public class Class$i
    {
        public void Method$i() => Console.WriteLine("Project $i");
    }
}
EOF
    done
    
    git add . --quiet
    git commit -m "Initial large repo" --quiet
    git tag v1.0.0 --quiet
    
    # Create some changes to make performance testing more realistic
    echo "// Modified" >> "src/Project1/Class1.cs"
    git add . --quiet
    git commit -m "Modify Project1" --quiet
    
    if [ $project_count -gt 10 ]; then
        echo "// Modified" >> "src/Project10/Class10.cs"
        git add . --quiet
        git commit -m "Modify Project10" --quiet
    fi
}

# Performance test scenarios
run_performance_tests() {
    log_info "Starting performance tests..."
    echo ""
    echo "Results:"
    echo "========================================"
    
    # Test 1: Small repo (10 projects)
    local small_repo="$TEMP_DIR/small-repo"
    rm -rf "$small_repo"
    create_small_repo "$small_repo" 10
    cd "$small_repo"
    
    measure_time "Small repo (10 projects) - Text report" "$TOOL_PATH report"
    measure_time "Small repo (10 projects) - JSON report" "$TOOL_PATH report -o json"
    measure_time "Small repo (10 projects) - Mermaid graph" "$TOOL_PATH report -o graph --graph-format mermaid"
    measure_time "Small repo (10 projects) - DOT graph" "$TOOL_PATH report -o graph --graph-format dot"
    measure_time "Small repo (10 projects) - ASCII graph" "$TOOL_PATH report -o graph --graph-format ascii"
    
    # Test 2: Medium repo (50 projects)
    local medium_repo="$TEMP_DIR/medium-repo"
    rm -rf "$medium_repo"
    create_large_repo "$medium_repo" 50
    cd "$medium_repo"
    
    measure_time "Medium repo (50 projects) - Text report" "$TOOL_PATH report"
    measure_time "Medium repo (50 projects) - JSON report" "$TOOL_PATH report -o json"
    measure_time "Medium repo (50 projects) - Mermaid graph" "$TOOL_PATH report -o graph --graph-format mermaid"
    measure_time "Medium repo (50 projects) - DOT graph" "$TOOL_PATH report -o graph --graph-format dot"
    measure_time "Medium repo (50 projects) - ASCII graph" "$TOOL_PATH report -o graph --graph-format ascii"
    
    # Test 3: Large repo (100 projects) - only if we have time
    if [[ "${SKIP_LARGE_TESTS:-false}" != "true" ]]; then
        local large_repo="$TEMP_DIR/large-repo"
        rm -rf "$large_repo"
        create_large_repo "$large_repo" 100
        cd "$large_repo"
        
        measure_time "Large repo (100 projects) - Text report" "$TOOL_PATH report"
        measure_time "Large repo (100 projects) - JSON report" "$TOOL_PATH report -o json"
        measure_time "Large repo (100 projects) - Mermaid graph" "$TOOL_PATH report -o graph --graph-format mermaid"
        measure_time "Large repo (100 projects) - DOT graph" "$TOOL_PATH report -o graph --graph-format dot"
        measure_time "Large repo (100 projects) - ASCII graph" "$TOOL_PATH report -o graph --graph-format ascii"
    fi
    
    echo "========================================"
    log_info "Performance tests completed!"
}

# Memory usage test
run_memory_tests() {
    log_info "Running memory usage tests..."
    
    local large_repo="$TEMP_DIR/memory-test-repo"
    rm -rf "$large_repo"
    create_large_repo "$large_repo" 100
    cd "$large_repo"
    
    # Monitor memory usage during execution
    if command -v valgrind >/dev/null 2>&1; then
        log_info "Running memory leak detection with Valgrind..."
        valgrind --tool=memcheck --leak-check=full $TOOL_PATH report > /dev/null 2>&1 || log_warn "Valgrind test completed with warnings"
    else
        log_warn "Valgrind not available, skipping memory leak detection"
    fi
    
    # Basic memory monitoring
    log_info "Running basic memory monitoring..."
    /usr/bin/time -v $TOOL_PATH report > /dev/null 2>&1 || log_warn "Memory monitoring completed"
}

# Main execution
main() {
    log_info "Mister.Version Performance Test Suite"
    echo "Tool path: $TOOL_PATH"
    echo "Temp directory: $TEMP_DIR"
    echo ""
    
    # Check if tool exists
    if [[ "$USE_DOTNET" == "true" ]]; then
        if ! command -v dotnet >/dev/null 2>&1; then
            log_error "dotnet command not found"
            exit 1
        fi
    else
        if [ ! -f "./Mister.Version.CLI/bin/Release/net8.0/mr-version" ]; then
            log_error "mr-version tool not found. Please build the project first."
            exit 1
        fi
    fi
    
    # Install bc for calculations if not available
    if ! command -v bc >/dev/null 2>&1; then
        log_warn "bc not found, installing..."
        if command -v apt-get >/dev/null 2>&1; then
            sudo apt-get update -qq && sudo apt-get install -y bc
        elif command -v yum >/dev/null 2>&1; then
            sudo yum install -y bc
        else
            log_error "Cannot install bc calculator"
            exit 1
        fi
    fi
    
    # Create temp directory
    rm -rf "$TEMP_DIR"
    mkdir -p "$TEMP_DIR"
    
    # Run tests
    run_performance_tests
    
    if [[ "${SKIP_MEMORY_TESTS:-false}" != "true" ]]; then
        run_memory_tests
    fi
    
    # Cleanup
    rm -rf "$TEMP_DIR"
    
    log_info "All performance tests completed successfully!"
}

# Run main function
main "$@"