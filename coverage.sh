#!/bin/bash

# Bash script to run tests with code coverage and generate HTML report

echo "Running tests with code coverage..."

# Clean previous coverage results
rm -rf coverage

# Run tests with coverage collection
# The test project uses Microsoft.Testing.Platform (see global.json), which uses --coverage instead of --collect
dotnet test --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory coverage

# Find the coverage file
COVERAGE_FILE=$(find coverage -name "coverage.cobertura.xml" | head -1)

if [ -n "$COVERAGE_FILE" ]; then
    echo "Generating HTML coverage report..."
    
    # Generate HTML report
    reportgenerator "-reports:$COVERAGE_FILE" "-targetdir:coverage/report" "-reporttypes:Html;HtmlSummary"
    
    echo "Coverage report generated at: coverage/report/index.html"
    echo ""
    echo "To view the report, open: coverage/report/index.html"
    
    # Try to open the report in the default browser (Linux/macOS)
    if command -v xdg-open > /dev/null; then
        xdg-open "coverage/report/index.html"
    elif command -v open > /dev/null; then
        open "coverage/report/index.html"
    fi
else
    echo "Coverage file not found!"
    exit 1
fi