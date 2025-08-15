using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public interface IProjectAnalyzer
    {
        List<ProjectInfo> AnalyzeProjects(string repositoryPath, string projectDirectory = null);
        ProjectInfo AnalyzeProject(string projectPath, string repositoryPath);
        ProjectInfo AnalyzeProject(string projectPath, string repositoryPath, VersionOptions additionalOptions);
        List<string> GetProjectDependencies(string projectPath, string repositoryPath);
        void BuildDependencyGraph(List<ProjectInfo> projects);
    }

    public class ProjectAnalyzer : IProjectAnalyzer
    {
        private readonly IVersionCalculator _versionCalculator;
        private readonly IGitService _gitService;
        private readonly Action<string, string> _logger;

        public ProjectAnalyzer(IVersionCalculator versionCalculator, IGitService gitService, Action<string, string> logger = null)
        {
            _versionCalculator = versionCalculator;
            _gitService = gitService;
            _logger = logger ?? ((level, message) => { });
        }

        public List<ProjectInfo> AnalyzeProjects(string repositoryPath, string projectDirectory = null)
        {
            var projects = new List<ProjectInfo>();
            
            // Default to scanning the entire repository if no project directory specified
            var searchPath = string.IsNullOrEmpty(projectDirectory) 
                ? repositoryPath 
                : Path.Combine(repositoryPath, projectDirectory);

            if (!Directory.Exists(searchPath))
            {
                _logger("Warning", $"Project directory does not exist: {searchPath}");
                return projects;
            }

            // Find all project files
            var projectFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(searchPath, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(searchPath, "*.fsproj", SearchOption.AllDirectories))
                .ToList();

            _logger("Info", $"Found {projectFiles.Count} project files");

            foreach (var projectFile in projectFiles)
            {
                try
                {
                    var projectInfo = AnalyzeProject(projectFile, repositoryPath);
                    if (projectInfo != null)
                    {
                        projects.Add(projectInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger("Error", $"Failed to analyze project {projectFile}: {ex.Message}");
                }
            }

            // Build dependency graph
            BuildDependencyGraph(projects);

            return projects;
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath)
        {
            return AnalyzeProject(projectPath, repositoryPath, null);
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath, VersionOptions additionalOptions)
        {
            if (!File.Exists(projectPath))
            {
                _logger("Error", $"Project file does not exist: {projectPath}");
                return null;
            }

            var projectContent = File.ReadAllText(projectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            // Extract project properties
            var isTestProject = IsTestProject(projectContent);
            var isPackable = IsPackable(projectContent);

            // Get dependencies (including transitive)
            var dependencies = GetProjectDependencies(projectPath, repositoryPath);
            var transitiveDependencies = GetTransitiveDependencies(projectPath, repositoryPath, dependencies);

            // Calculate version using the version calculator
            var versionOptions = new VersionOptions
            {
                RepoRoot = repositoryPath,
                ProjectPath = projectPath,
                ProjectName = projectName,
                Dependencies = transitiveDependencies,
                IsTestProject = isTestProject,
                IsPackable = isPackable,
                BaseVersion = additionalOptions.BaseVersion
            };

            // Apply additional options if provided
            if (additionalOptions != null)
            {
                versionOptions.PrereleaseType = additionalOptions.PrereleaseType;
                versionOptions.Debug = additionalOptions.Debug;
                versionOptions.TagPrefix = additionalOptions.TagPrefix;
                versionOptions.ForceVersion = additionalOptions.ForceVersion;
                
                // Override dependencies if provided in additional options
                if (additionalOptions.Dependencies != null && additionalOptions.Dependencies.Count > 0)
                {
                    versionOptions.Dependencies = additionalOptions.Dependencies;
                }
            }

            var versionResult = _versionCalculator.CalculateVersion(versionOptions);

            var projectInfo = new ProjectInfo
            {
                Name = projectName,
                Path = PathUtils.GetRelativePath(repositoryPath, projectPath),
                FullPath = projectPath,
                Version = versionResult,
                DirectDependencies = dependencies.Select(d => Path.GetFileNameWithoutExtension(d)).ToList(),
                IsTestProject = isTestProject,
                IsPackable = isPackable
            };

            return projectInfo;
        }

        public List<string> GetProjectDependencies(string projectPath, string repositoryPath)
        {
            var dependencies = new List<string>();
            
            try
            {
                var projectContent = File.ReadAllText(projectPath);
                var projectDir = Path.GetDirectoryName(projectPath);

                // Find ProjectReference elements
                var matches = Regex.Matches(projectContent, @"<ProjectReference\s+Include\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var dependencyPath = match.Groups[1].Value;
                    
                    // Normalize path separators for cross-platform compatibility
                    dependencyPath = dependencyPath.Replace('\\', Path.DirectorySeparatorChar);
                    
                    // Resolve relative path to absolute path
                    if (!Path.IsPathRooted(dependencyPath))
                    {
                        dependencyPath = Path.GetFullPath(Path.Combine(projectDir, dependencyPath));
                    }
                    else
                    {
                        // Even for rooted paths, ensure they're fully resolved
                        dependencyPath = Path.GetFullPath(dependencyPath);
                    }
                    
                    if (File.Exists(dependencyPath))
                    {
                        dependencies.Add(dependencyPath);
                    }
                    else
                    {
                        _logger("Warning", $"Referenced project not found: {dependencyPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger("Error", $"Failed to get dependencies for {projectPath}: {ex.Message}");
            }

            return dependencies;
        }

        private List<string> GetTransitiveDependencies(string projectPath, string repositoryPath, List<string> directDependencies)
        {
            try
            {
                // For performance, we'll do a lightweight discovery of all projects in the repo
                var allProjects = DiscoverAllProjects(repositoryPath);
                // Build a lookup of project name -> project info
                var projectLookup = new Dictionary<string, (string Path, List<string> Dependencies)>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var proj in allProjects)
                {
                    var projName = Path.GetFileNameWithoutExtension(proj);
                    var projDeps = GetProjectDependencies(proj, repositoryPath);
                    projectLookup[projName] = (proj, projDeps);
                }
                
                // Build transitive dependencies for our target project
                var targetProjectName = Path.GetFileNameWithoutExtension(projectPath);
                var transitiveDeps = new List<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                BuildTransitiveDependencyList(targetProjectName, projectLookup, transitiveDeps, visited);
                
                return transitiveDeps;
            }
            catch (Exception ex)
            {
                _logger("Warning", $"Failed to calculate transitive dependencies for {projectPath}: {ex.Message}");
                // Fallback to direct dependencies only
                return directDependencies;
            }
        }

        private void BuildTransitiveDependencyList(string projectName, Dictionary<string, (string Path, List<string> Dependencies)> projectLookup, List<string> result, HashSet<string> visited)
        {
            if (visited.Contains(projectName))
                return; // Avoid circular dependencies

            visited.Add(projectName);

            if (projectLookup.TryGetValue(projectName, out var projectInfo))
            {
                foreach (var depPath in projectInfo.Dependencies)
                {
                    if (!result.Contains(depPath))
                    {
                        result.Add(depPath);
                    }
                    
                    // Recursively add transitive dependencies
                    var depName = Path.GetFileNameWithoutExtension(depPath);
                    BuildTransitiveDependencyList(depName, projectLookup, result, visited);
                }
            }
        }

        private List<string> DiscoverAllProjects(string repositoryPath)
        {
            var projects = new List<string>();
            var projectExtensions = new[] { "*.csproj", "*.fsproj", "*.vbproj" };
            
            foreach (var extension in projectExtensions)
            {
                var foundProjects = Directory.GetFiles(repositoryPath, extension, SearchOption.AllDirectories);
                projects.AddRange(foundProjects);
            }
            
            return projects;
        }

        public void BuildDependencyGraph(List<ProjectInfo> projects)
        {
            var projectLookup = projects.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var project in projects)
            {
                project.AllDependencies = BuildTransitiveDependencies(project.Name, projectLookup, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        private List<string> BuildTransitiveDependencies(string projectName, Dictionary<string, ProjectInfo> projectLookup, HashSet<string> visited)
        {
            if (visited.Contains(projectName))
            {
                return new List<string>(); // Avoid circular dependencies
            }

            visited.Add(projectName);
            var result = new List<string>();

            if (projectLookup.TryGetValue(projectName, out var project))
            {
                foreach (var dependency in project.DirectDependencies)
                {
                    if (!result.Contains(dependency, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(dependency);
                    }

                    // Add transitive dependencies
                    var transitiveDeps = BuildTransitiveDependencies(dependency, projectLookup, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase));
                    foreach (var transitiveDep in transitiveDeps)
                    {
                        if (!result.Contains(transitiveDep, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(transitiveDep);
                        }
                    }
                }
            }

            return result;
        }

        private bool IsTestProject(string projectContent)
        {
            // Check for explicit test project marker
            if (Regex.IsMatch(projectContent, @"<IsTestProject\s*>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Check for common test frameworks
            var testFrameworks = new[]
            {
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "NUnit",
                "MSTest",
                "nunit",
                "MSTest.TestFramework"
            };

            return testFrameworks.Any(framework => 
                Regex.IsMatch(projectContent, $@"<PackageReference[^>]+Include\s*=\s*""{Regex.Escape(framework)}""", RegexOptions.IgnoreCase));
        }

        private bool IsPackable(string projectContent)
        {
            // Check for explicit IsPackable setting
            var isPackableMatch = Regex.Match(projectContent, @"<IsPackable\s*>\s*(true|false)\s*</IsPackable>", RegexOptions.IgnoreCase);
            if (isPackableMatch.Success)
            {
                return string.Equals(isPackableMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }

            // Check project type - some types are not packable by default
            if (Regex.IsMatch(projectContent, @"<OutputType\s*>\s*Exe\s*</OutputType>", RegexOptions.IgnoreCase))
            {
                // Console applications are typically not packable unless explicitly set
                return false;
            }

            // Default to packable for library projects
            return true;
        }
    }
}