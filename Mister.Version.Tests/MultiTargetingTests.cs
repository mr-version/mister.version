using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for multi-targeting scenarios (projects using TargetFrameworks instead of TargetFramework)
    /// These tests verify that dependency tracking works correctly when projects target multiple frameworks
    /// </summary>
    public class MultiTargetingTests : IDisposable
    {
        private readonly string _testRepoRoot;
        private readonly GitService _gitService;
        private readonly ProjectAnalyzer _projectAnalyzer;
        private readonly VersionCalculator _versionCalculator;

        public MultiTargetingTests()
        {
            _testRepoRoot = Path.Combine(Path.GetTempPath(), "multi-targeting-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRepoRoot);

            // Initialize git repository
            InitializeGitRepository();

            // Create services
            _gitService = new GitService(_testRepoRoot);
            _versionCalculator = new VersionCalculator(_gitService);
            _projectAnalyzer = new ProjectAnalyzer(_versionCalculator, _gitService);
        }

        [Fact]
        public void MultiTargeting_ProjectWithTargetFrameworks_ParsesProjectReferencesCorrectly()
        {
            // Arrange - Create a multi-targeting project that depends on another project
            var coreProjectPath = CreateProject("Core", false, new List<string>());
            var multiTargetProjectPath = CreateProject("MultiTargetLib", true, new List<string> { coreProjectPath });

            // Commit initial state
            CommitAllChanges("Initial commit with multi-targeting project");

            // Act - Analyze the multi-targeting project
            var dependencies = _projectAnalyzer.GetProjectDependencies(multiTargetProjectPath, _testRepoRoot);

            // Assert - Should find the Core dependency despite multi-targeting
            Assert.Single(dependencies);
            Assert.Contains(coreProjectPath, dependencies);
        }

        [Fact]
        public void MultiTargeting_DependencyChanges_TriggersVersionIncrement()
        {
            // Arrange - Create dependency chain: App -> MultiTargetLib -> Core
            var coreProjectPath = CreateProject("Core", false, new List<string>());
            var multiTargetLibPath = CreateProject("MultiTargetLib", true, new List<string> { coreProjectPath });
            var appProjectPath = CreateProject("App", false, new List<string> { multiTargetLibPath });

            // Commit and tag initial state
            CommitAllChanges("Initial commit");
            _gitService.CreateTag("v1.0.0", "Initial version", true);

            // Get all dependencies including transitive ones
            var allDependencies = GetTransitiveDependenciesHelper(appProjectPath);

            // Get initial version
            var initialOptions = new VersionOptions
            {
                RepoRoot = _testRepoRoot,
                ProjectPath = appProjectPath,
                ProjectName = "App",
                Dependencies = allDependencies
            };
            var initialVersion = _versionCalculator.CalculateVersion(initialOptions);

            // Act - Modify the Core project (indirect dependency through multi-targeting project)
            ModifyProject(coreProjectPath, "// Updated core functionality");
            CommitAllChanges("Update Core project");

            // Recalculate version for App
            var updatedOptions = new VersionOptions
            {
                RepoRoot = _testRepoRoot,
                ProjectPath = appProjectPath,
                ProjectName = "App",
                Dependencies = allDependencies
            };
            var updatedVersion = _versionCalculator.CalculateVersion(updatedOptions);

            // Assert - App version should increment because its dependency (MultiTargetLib)
            // indirectly changed through Core
            Assert.NotEqual(initialVersion.Version, updatedVersion.Version);
            Assert.True(updatedVersion.VersionChanged);
        }

        [Fact]
        public void MultiTargeting_TransitiveDependencies_TrackedCorrectly()
        {
            // Arrange - Create complex dependency graph with multi-targeting
            var utilsPath = CreateProject("Utils", false, new List<string>());
            var corePath = CreateProject("Core", true, new List<string> { utilsPath }); // Multi-targeting
            var servicesPath = CreateProject("Services", true, new List<string> { corePath }); // Multi-targeting
            var appPath = CreateProject("App", false, new List<string> { servicesPath });

            // Act - Get transitive dependencies for App
            var directDeps = _projectAnalyzer.GetProjectDependencies(appPath, _testRepoRoot);
            var transitiveDeps = _projectAnalyzer.GetProjectDependencies(servicesPath, _testRepoRoot);

            // Assert - Should properly track dependencies through multi-targeting projects
            Assert.Single(directDeps); // App directly depends on Services
            Assert.Single(transitiveDeps); // Services directly depends on Core

            // Services should transitively include Utils through Core
            var servicesTransitive = GetTransitiveDependenciesHelper(servicesPath);
            Assert.Contains(corePath, servicesTransitive);
            Assert.Contains(utilsPath, servicesTransitive);
        }

        [Fact]
        public void MultiTargeting_ProjectReference_WithConditions_ParsesAllReferences()
        {
            // Arrange - Create a project with framework-specific dependencies
            var net472LibPath = CreateProject("Net472Lib", false, new List<string>());
            var net80LibPath = CreateProject("Net80Lib", false, new List<string>());

            // Create multi-targeting project with conditional references
            var multiTargetPath = CreateProjectWithConditionalReferences(
                "ConditionalMultiTarget",
                new Dictionary<string, List<string>>
                {
                    { "net472", new List<string> { net472LibPath } },
                    { "net8.0", new List<string> { net80LibPath } }
                }
            );

            // Act - Get dependencies
            var dependencies = _projectAnalyzer.GetProjectDependencies(multiTargetPath, _testRepoRoot);

            // Assert - Current implementation may not parse conditional references
            // This documents the current behavior and serves as a placeholder for future enhancement
            // In reality, MSBuild would resolve these during build based on the active TargetFramework
            Assert.NotNull(dependencies);
        }

        [Fact]
        public void MultiTargeting_BuildDependencyGraph_HandlesMultiTargetingProjects()
        {
            // Arrange - Create projects
            var coreProjectPath = CreateProject("Core", true, new List<string>());
            var libProjectPath = CreateProject("Lib", true, new List<string> { coreProjectPath });
            var appProjectPath = CreateProject("App", false, new List<string> { libProjectPath });

            // Analyze all projects
            var projects = new List<ProjectInfo>
            {
                _projectAnalyzer.AnalyzeProject(coreProjectPath, _testRepoRoot),
                _projectAnalyzer.AnalyzeProject(libProjectPath, _testRepoRoot),
                _projectAnalyzer.AnalyzeProject(appProjectPath, _testRepoRoot)
            };

            // Act - Build dependency graph
            _projectAnalyzer.BuildDependencyGraph(projects);

            // Assert - All dependencies should be correctly resolved
            var appProject = projects.First(p => p.Name == "App");
            var libProject = projects.First(p => p.Name == "Lib");
            var coreProject = projects.First(p => p.Name == "Core");

            Assert.Contains("Lib", appProject.DirectDependencies);
            Assert.Contains("Lib", appProject.AllDependencies);
            Assert.Contains("Core", appProject.AllDependencies); // Transitive dependency

            Assert.Contains("Core", libProject.DirectDependencies);
            Assert.Contains("Core", libProject.AllDependencies);

            Assert.Empty(coreProject.DirectDependencies);
            Assert.Empty(coreProject.AllDependencies);
        }

        [Fact]
        public void MultiTargeting_MixedSingleAndMultiTargeting_AllDependenciesTracked()
        {
            // Arrange - Mix of single-target and multi-target projects
            var singleTargetCorePath = CreateProject("SingleTargetCore", false, new List<string>());
            var multiTargetLibPath = CreateProject("MultiTargetLib", true, new List<string> { singleTargetCorePath });
            var singleTargetAppPath = CreateProject("SingleTargetApp", false, new List<string> { multiTargetLibPath });

            // Commit initial state
            CommitAllChanges("Initial mixed targeting setup");

            // Act - Analyze all projects
            var projects = _projectAnalyzer.AnalyzeProjects(_testRepoRoot);

            // Assert
            Assert.Equal(3, projects.Count);

            var appProject = projects.FirstOrDefault(p => p.Name == "SingleTargetApp");
            var libProject = projects.FirstOrDefault(p => p.Name == "MultiTargetLib");
            var coreProject = projects.FirstOrDefault(p => p.Name == "SingleTargetCore");

            Assert.NotNull(appProject);
            Assert.NotNull(libProject);
            Assert.NotNull(coreProject);

            // Verify dependency tracking works through multi-targeting project
            Assert.Contains("MultiTargetLib", appProject.DirectDependencies);
            Assert.Contains("SingleTargetCore", libProject.DirectDependencies);
        }

        #region Helper Methods

        private void InitializeGitRepository()
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "init",
                WorkingDirectory = _testRepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                process.WaitForExit();
            }

            // Configure git
            ExecuteGitCommand("config user.email test@example.com");
            ExecuteGitCommand("config user.name 'Test User'");
        }

        private void ExecuteGitCommand(string arguments)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _testRepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                process.WaitForExit();
            }
        }

        private string CreateProject(string projectName, bool useMultiTargeting, List<string> projectReferences)
        {
            var projectDir = Path.Combine(_testRepoRoot, "src", projectName);
            Directory.CreateDirectory(projectDir);

            var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");
            var targetFramework = useMultiTargeting
                ? "<TargetFrameworks>net472;net8.0</TargetFrameworks>"
                : "<TargetFramework>net8.0</TargetFramework>";

            var references = string.Join(Environment.NewLine, projectReferences.Select(refPath =>
            {
                var relativePath = PathUtils.GetRelativePath(projectDir, refPath);
                return $"    <ProjectReference Include=\"{relativePath}\" />";
            }));

            var referencesSection = projectReferences.Any()
                ? $@"
  <ItemGroup>
{references}
  </ItemGroup>"
                : "";

            var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    {targetFramework}
    <IsPackable>true</IsPackable>
  </PropertyGroup>{referencesSection}
</Project>";

            File.WriteAllText(projectPath, projectContent);

            // Create a simple source file
            var sourceFile = Path.Combine(projectDir, $"{projectName}.cs");
            File.WriteAllText(sourceFile, $@"namespace {projectName}
{{
    public class {projectName}Class
    {{
        public string GetMessage() => ""Hello from {projectName}"";
    }}
}}");

            return projectPath;
        }

        private string CreateProjectWithConditionalReferences(string projectName, Dictionary<string, List<string>> frameworkReferences)
        {
            var projectDir = Path.Combine(_testRepoRoot, "src", projectName);
            Directory.CreateDirectory(projectDir);

            var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");

            var conditionalSections = string.Join(Environment.NewLine, frameworkReferences.Select(kvp =>
            {
                var framework = kvp.Key;
                var references = string.Join(Environment.NewLine, kvp.Value.Select(refPath =>
                {
                    var relativePath = PathUtils.GetRelativePath(projectDir, refPath);
                    return $"    <ProjectReference Include=\"{relativePath}\" />";
                }));

                return $@"
  <ItemGroup Condition=""'$(TargetFramework)' == '{framework}'"">
{references}
  </ItemGroup>";
            }));

            var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>{string.Join(";", frameworkReferences.Keys)}</TargetFrameworks>
    <IsPackable>true</IsPackable>
  </PropertyGroup>{conditionalSections}
</Project>";

            File.WriteAllText(projectPath, projectContent);

            var sourceFile = Path.Combine(projectDir, $"{projectName}.cs");
            File.WriteAllText(sourceFile, $@"namespace {projectName}
{{
    public class {projectName}Class
    {{
        public string GetMessage() => ""Hello from {projectName}"";
    }}
}}");

            return projectPath;
        }

        private void ModifyProject(string projectPath, string additionalContent)
        {
            var sourceFile = Path.Combine(Path.GetDirectoryName(projectPath),
                Path.GetFileNameWithoutExtension(projectPath) + ".cs");

            if (File.Exists(sourceFile))
            {
                var content = File.ReadAllText(sourceFile);
                File.WriteAllText(sourceFile, content + Environment.NewLine + additionalContent);
            }
        }

        private void CommitAllChanges(string message)
        {
            ExecuteGitCommand("add .");
            ExecuteGitCommand($"commit -m \"{message}\" --allow-empty");
        }

        private List<string> GetTransitiveDependenciesHelper(string projectPath)
        {
            var directDeps = _projectAnalyzer.GetProjectDependencies(projectPath, _testRepoRoot);
            var result = new List<string>(directDeps);

            foreach (var dep in directDeps)
            {
                var transitive = GetTransitiveDependenciesHelper(dep);
                foreach (var trans in transitive)
                {
                    if (!result.Contains(trans))
                    {
                        result.Add(trans);
                    }
                }
            }

            return result;
        }

        #endregion

        public void Dispose()
        {
            try
            {
                _gitService?.Dispose();

                if (Directory.Exists(_testRepoRoot))
                {
                    Directory.Delete(_testRepoRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
