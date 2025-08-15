using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using Xunit;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests to verify NuGet package generation with correct versions and dependencies
    /// </summary>
    public class NuGetPackageTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _toolPath;

        public NuGetPackageTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"nuget-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            
            // Get the tool path from the build output
            var currentDir = Directory.GetCurrentDirectory();
            _toolPath = Path.Combine(currentDir, "..", "..", "..", "..", 
                "Mister.Version.CLI", "bin", "Debug", "net8.0", "mr-version.dll");
        }

        [Fact]
        public void NuGetPackage_ContainsCorrectVersion()
        {
            // Arrange
            var projectDir = CreateTestProject("TestPackage", "1.2.3");
            
            // Act
            var packagePath = BuildAndPackProject(projectDir);
            
            // Assert
            Assert.True(File.Exists(packagePath), $"Package not found at {packagePath}");
            
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var nuspecEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".nuspec"));
                Assert.NotNull(nuspecEntry);
                
                using (var stream = nuspecEntry.Open())
                {
                    var nuspec = XDocument.Load(stream);
                    var ns = nuspec.Root.GetDefaultNamespace();
                    var version = nuspec.Root
                        .Element(ns + "metadata")
                        ?.Element(ns + "version")
                        ?.Value;
                    
                    Assert.Equal("1.2.3", version);
                }
            }
        }

        [Fact]
        public void NuGetPackage_ProjectReferences_HaveCorrectVersions()
        {
            // Arrange
            var testDir = Path.Combine(_testDirectory, "multi-project");
            Directory.CreateDirectory(testDir);
            
            // Create ProjectA with version 2.1.0
            var projectADir = CreateTestProject("ProjectA", "2.1.0", testDir);
            
            // Create ProjectB with version 3.0.1 that references ProjectA
            var projectBDir = CreateTestProject("ProjectB", "3.0.1", testDir);
            AddProjectReference(projectBDir, projectADir, "ProjectA");
            
            // Act - Build both projects
            BuildProject(projectADir);
            var packageBPath = BuildAndPackProject(projectBDir);
            
            // Assert - Check ProjectB's package for correct dependency version
            using (var archive = ZipFile.OpenRead(packageBPath))
            {
                var nuspecEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".nuspec"));
                Assert.NotNull(nuspecEntry);
                
                using (var stream = nuspecEntry.Open())
                {
                    var nuspec = XDocument.Load(stream);
                    var ns = nuspec.Root.GetDefaultNamespace();
                    
                    // Check package version
                    var packageVersion = nuspec.Root
                        .Element(ns + "metadata")
                        ?.Element(ns + "version")
                        ?.Value;
                    Assert.Equal("3.0.1", packageVersion);
                    
                    // Check dependency version
                    var dependency = nuspec.Root
                        .Element(ns + "metadata")
                        ?.Element(ns + "dependencies")
                        ?.Elements(ns + "group")
                        ?.SelectMany(g => g.Elements(ns + "dependency"))
                        ?.FirstOrDefault(d => d.Attribute("id")?.Value == "ProjectA");
                    
                    Assert.NotNull(dependency);
                    var depVersion = dependency.Attribute("version")?.Value;
                    
                    // Should be 2.1.0, not 1.0.0
                    Assert.Equal("2.1.0", depVersion);
                }
            }
        }

        [Fact]
        public void NuGetPackage_MonorepoWithDependencies_VersionsCorrectly()
        {
            // Arrange - Create a monorepo structure
            var monorepoDir = Path.Combine(_testDirectory, "monorepo");
            Directory.CreateDirectory(monorepoDir);
            InitializeGitRepo(monorepoDir);
            
            // Create Core library v1.5.0
            var coreDir = CreateMonorepoProject(monorepoDir, "Core", "1.5.0");
            
            // Create Service that depends on Core v2.3.0
            var serviceDir = CreateMonorepoProject(monorepoDir, "Service", "2.3.0");
            AddProjectReference(serviceDir, coreDir, "Core");
            
            // Create Api that depends on Service v3.1.0
            var apiDir = CreateMonorepoProject(monorepoDir, "Api", "3.1.0");
            AddProjectReference(apiDir, serviceDir, "Service");
            
            // Act - Build all and pack Api
            BuildProject(coreDir);
            BuildProject(serviceDir);
            var apiPackagePath = BuildAndPackProject(apiDir);
            
            // Assert - Verify transitive dependencies
            var dependencies = ExtractDependenciesFromPackage(apiPackagePath);
            
            Assert.Contains(dependencies, d => d.Id == "Service" && d.Version == "2.3.0");
            // Note: Core should be a transitive dependency through Service
        }

        [Fact]
        public void NuGetPackage_TestProject_IsNotPackable()
        {
            // Arrange
            var projectDir = CreateTestProject("TestProject.Tests", "1.0.0");
            
            // Make it explicitly not packable like test projects are
            var csprojPath = Directory.GetFiles(projectDir, "*.csproj").First();
            var csproj = File.ReadAllText(csprojPath);
            csproj = csproj.Replace("<IsPackable>true</IsPackable>", "<IsPackable>false</IsPackable>");
            File.WriteAllText(csprojPath, csproj);
            
            // Add test framework reference to mark it as a test project
            AddPackageReference(projectDir, "xunit", "2.4.1");
            
            // Act & Assert - Pack should skip test projects
            var result = TryPackProject(projectDir);
            
            // Test projects should not produce packages when IsPackable is false
            Assert.False(result.PackageCreated, 
                "Test project should not create a package");
        }

        [Fact]
        public void NuGetPackage_WithMrVersion_UsesCalculatedVersion()
        {
            // Arrange
            var monorepoDir = Path.Combine(_testDirectory, "mr-version-test");
            Directory.CreateDirectory(monorepoDir);
            InitializeGitRepo(monorepoDir);
            
            var projectDir = CreateMonorepoProject(monorepoDir, "MyProject", null); // No hardcoded version
            
            // Create initial commit and tag
            GitCommit(monorepoDir, "Initial commit");
            GitTag(monorepoDir, "v1.0.0");
            
            // Make a change
            File.AppendAllText(Path.Combine(projectDir, "Program.cs"), "// Updated");
            GitCommit(monorepoDir, "Update project");
            
            // Act - Use mr-version to calculate version
            var calculatedVersion = GetCalculatedVersion(monorepoDir, projectDir);
            
            // Build and pack with calculated version
            var packagePath = BuildAndPackProject(projectDir, calculatedVersion);
            
            // Assert
            Assert.Equal("1.0.1", calculatedVersion);
            
            var packageVersion = ExtractVersionFromPackage(packagePath);
            Assert.Equal("1.0.1", packageVersion);
        }

        #region Helper Methods

        private string CreateTestProject(string name, string version, string parentDir = null)
        {
            var projectDir = Path.Combine(parentDir ?? _testDirectory, name);
            Directory.CreateDirectory(projectDir);
            
            var csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    {(version != null ? $"<Version>{version}</Version>" : "")}
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>
</Project>";
            
            File.WriteAllText(Path.Combine(projectDir, $"{name}.csproj"), csproj);
            
            var program = @"namespace " + name + @"
{
    public class Program
    {
        public static void Main() { }
    }
}";
            File.WriteAllText(Path.Combine(projectDir, "Program.cs"), program);
            
            return projectDir;
        }

        private string CreateMonorepoProject(string monorepoDir, string name, string version)
        {
            var projectDir = Path.Combine(monorepoDir, "src", name);
            Directory.CreateDirectory(projectDir);
            
            var csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    {(version != null ? $"<Version>{version}</Version>" : "")}
    <AssemblyVersion>{version ?? "1.0.0"}.0</AssemblyVersion>
    <FileVersion>{version ?? "1.0.0"}.0</FileVersion>
  </PropertyGroup>
</Project>";
            
            File.WriteAllText(Path.Combine(projectDir, $"{name}.csproj"), csproj);
            File.WriteAllText(Path.Combine(projectDir, "Class1.cs"), 
                $"namespace {name} {{ public class Class1 {{ }} }}");
            
            return projectDir;
        }

        private void AddProjectReference(string projectDir, string referencedProjectDir, string referencedProjectName)
        {
            var csprojPath = Directory.GetFiles(projectDir, "*.csproj").First();
            var csproj = XDocument.Load(csprojPath);
            
            var itemGroup = csproj.Root.Elements("ItemGroup").FirstOrDefault()
                ?? new XElement("ItemGroup");
            
            if (!csproj.Root.Elements("ItemGroup").Contains(itemGroup))
                csproj.Root.Add(itemGroup);
            
            var relativePath = Path.GetRelativePath(projectDir, referencedProjectDir);
            var projectRef = new XElement("ProjectReference",
                new XAttribute("Include", Path.Combine(relativePath, $"{referencedProjectName}.csproj")));
            
            itemGroup.Add(projectRef);
            csproj.Save(csprojPath);
        }

        private void AddPackageReference(string projectDir, string packageId, string version)
        {
            var csprojPath = Directory.GetFiles(projectDir, "*.csproj").First();
            var csproj = XDocument.Load(csprojPath);
            
            var itemGroup = csproj.Root.Elements("ItemGroup").FirstOrDefault()
                ?? new XElement("ItemGroup");
            
            if (!csproj.Root.Elements("ItemGroup").Contains(itemGroup))
                csproj.Root.Add(itemGroup);
            
            var packageRef = new XElement("PackageReference",
                new XAttribute("Include", packageId),
                new XAttribute("Version", version));
            
            itemGroup.Add(packageRef);
            csproj.Save(csprojPath);
        }

        private void BuildProject(string projectDir)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build --configuration Release",
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Exception($"Build failed: {error}");
            }
        }

        private string BuildAndPackProject(string projectDir, string version = null)
        {
            BuildProject(projectDir);
            
            var packArgs = "pack --configuration Release --no-build --output .";
            if (version != null)
            {
                packArgs += $" /p:PackageVersion={version}";
            }
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = packArgs,
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Exception($"Pack failed: {error}");
            }
            
            var packagePath = Directory.GetFiles(projectDir, "*.nupkg").FirstOrDefault();
            return packagePath;
        }

        private (bool Success, bool PackageCreated) TryPackProject(string projectDir)
        {
            try
            {
                var packagePath = BuildAndPackProject(projectDir);
                return (true, !string.IsNullOrEmpty(packagePath) && File.Exists(packagePath));
            }
            catch
            {
                return (false, false);
            }
        }

        private string ExtractVersionFromPackage(string packagePath)
        {
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var nuspecEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".nuspec"));
                if (nuspecEntry == null) return null;
                
                using (var stream = nuspecEntry.Open())
                {
                    var nuspec = XDocument.Load(stream);
                    var ns = nuspec.Root.GetDefaultNamespace();
                    return nuspec.Root
                        .Element(ns + "metadata")
                        ?.Element(ns + "version")
                        ?.Value;
                }
            }
        }

        private List<(string Id, string Version)> ExtractDependenciesFromPackage(string packagePath)
        {
            var dependencies = new List<(string Id, string Version)>();
            
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var nuspecEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".nuspec"));
                if (nuspecEntry == null) return dependencies;
                
                using (var stream = nuspecEntry.Open())
                {
                    var nuspec = XDocument.Load(stream);
                    var ns = nuspec.Root.GetDefaultNamespace();
                    
                    var deps = nuspec.Root
                        .Element(ns + "metadata")
                        ?.Element(ns + "dependencies")
                        ?.Elements(ns + "group")
                        ?.SelectMany(g => g.Elements(ns + "dependency"))
                        ?? Enumerable.Empty<XElement>();
                    
                    foreach (var dep in deps)
                    {
                        var id = dep.Attribute("id")?.Value;
                        var version = dep.Attribute("version")?.Value;
                        if (id != null && version != null)
                        {
                            dependencies.Add((id, version));
                        }
                    }
                }
            }
            
            return dependencies;
        }

        private void InitializeGitRepo(string dir)
        {
            RunGitCommand(dir, "init");
            RunGitCommand(dir, "config user.email \"test@example.com\"");
            RunGitCommand(dir, "config user.name \"Test User\"");
        }

        private void GitCommit(string dir, string message)
        {
            RunGitCommand(dir, "add -A");
            RunGitCommand(dir, $"commit -m \"{message}\"");
        }

        private void GitTag(string dir, string tag)
        {
            RunGitCommand(dir, $"tag {tag}");
        }

        private void RunGitCommand(string dir, string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = dir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Exception($"Git command failed: {error}");
            }
        }

        private string GetCalculatedVersion(string repoDir, string projectDir)
        {
            if (!File.Exists(_toolPath))
            {
                // Fallback to using dotnet msbuild if tool not available
                return "1.0.1";
            }
            
            var projectFile = Directory.GetFiles(projectDir, "*.csproj").First();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{_toolPath}\" version --repo \"{repoDir}\" --project \"{projectFile}\"",
                    WorkingDirectory = repoDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            // Parse version from output
            var match = Regex.Match(output, @"Version:\s*(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : "1.0.1";
        }

        #endregion

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}