# Mister.Version Makefile

.PHONY: all build test clean restore unit-test integration-test

# Default target
all: build test

# Restore NuGet packages
restore:
	dotnet restore

# Build the solution
build: restore
	dotnet build --configuration Release

# Build debug version for testing
build-debug: restore
	dotnet build --configuration Debug

# Run all tests
test: unit-test integration-test

# Run unit tests
unit-test: build
	dotnet test --no-build --configuration Release --verbosity normal

# Run integration tests
integration-test: build-debug
	@echo "Running integration tests..."
	@chmod +x ./test-versioning-scenarios.sh
	@./test-versioning-scenarios.sh

# Clean build artifacts
clean:
	dotnet clean
	rm -rf test-repos/
	find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
	find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

# Run the CLI tool
run-cli: build-debug
	./Mister.Version.CLI/bin/Debug/net8.0/mr-version

# Package for NuGet
pack: build
	dotnet pack --no-build --configuration Release --output ./nupkg

# Run a quick smoke test
smoke-test: build-debug
	@echo "Running smoke test..."
	@./Mister.Version.CLI/bin/Debug/net8.0/mr-version --help
	@echo "Smoke test passed!"

# Install as global tool (for testing)
install-global: pack
	dotnet tool install --global --add-source ./nupkg Mister.Version

# Uninstall global tool
uninstall-global:
	dotnet tool uninstall --global Mister.Version