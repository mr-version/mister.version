# PowerShell script to run tests with code coverage and generate HTML report

Write-Host "Running tests with code coverage..." -ForegroundColor Green

# Clean previous coverage results
if (Test-Path "coverage") {
    Remove-Item -Recurse -Force "coverage"
}

# Run tests with coverage collection
# The test project uses Microsoft.Testing.Platform (see global.json), which uses --coverage instead of --collect
dotnet test --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory coverage

# Find the coverage file
$coverageFile = Get-ChildItem -Path "coverage" -Recurse -Filter "coverage.cobertura.xml" | Select-Object -First 1

if ($coverageFile) {
    Write-Host "Generating HTML coverage report..." -ForegroundColor Green
    
    # Generate HTML report
    reportgenerator "-reports:$($coverageFile.FullName)" "-targetdir:coverage/report" "-reporttypes:Html;HtmlSummary"
    
    Write-Host "Coverage report generated at: coverage/report/index.html" -ForegroundColor Green
    Write-Host ""
    Write-Host "To view the report, open: coverage/report/index.html" -ForegroundColor Yellow
    
    # Try to open the report in the default browser (Windows)
    if ($IsWindows -or [System.Environment]::OSVersion.Platform -eq "Win32NT") {
        $reportPath = Join-Path (Get-Location) "coverage/report/index.html"
        Start-Process $reportPath
    }
} else {
    Write-Host "Coverage file not found!" -ForegroundColor Red
}