using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using LibGit2Sharp;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for transitive dependency tracking and version propagation
    /// </summary>
    public class TransitiveDependencyTests
    {
        private readonly MockProjectAnalyzerWithTransitive _mockProjectAnalyzer;
        private readonly MockGitService _mockGitService;
        private readonly VersionCalculator _versionCalculator;

        public TransitiveDependencyTests()
        {
            _mockProjectAnalyzer = new MockProjectAnalyzerWithTransitive();
            _mockGitService = new MockGitService();
            _versionCalculator = new VersionCalculator(_mockGitService, null);
        }

        [Fact]
        public void GetTransitiveDependencies_SimpleChain_ReturnsAllDependencies()
        {
            // Arrange: A → B → C (A depends on B, B depends on C)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" },
                ["ProjectB"] = new List<string> { "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectC"] = new List<string>()
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var analyzer = new ProjectAnalyzer(_versionCalculator, _mockGitService, null);

            // Act
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectA/ProjectA.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj" });

            // Assert
            Assert.Equal(2, transitiveDeps.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDeps);
        }

        [Fact]
        public void GetTransitiveDependencies_LongChain_ReturnsAllDependencies()
        {
            // Arrange: A → B → C → D → E (5-level chain)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" },
                ["ProjectB"] = new List<string> { "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectD/ProjectD.csproj" },
                ["ProjectD"] = new List<string> { "/repo/ProjectE/ProjectE.csproj" },
                ["ProjectE"] = new List<string>()
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectA/ProjectA.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj" });

            // Assert
            Assert.Equal(4, transitiveDeps.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectD/ProjectD.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectE/ProjectE.csproj", transitiveDeps);
        }

        [Fact]
        public void GetTransitiveDependencies_DiamondStructure_HandlesCorrectly()
        {
            // Arrange: A → B, A → C, B → D, C → D (diamond dependency)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string> { "/repo/ProjectB/ProjectB.csproj", "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectB"] = new List<string> { "/repo/ProjectD/ProjectD.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectD/ProjectD.csproj" },
                ["ProjectD"] = new List<string>()
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectA/ProjectA.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj", "/repo/ProjectC/ProjectC.csproj" });

            // Assert
            Assert.Equal(3, transitiveDeps.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectD/ProjectD.csproj", transitiveDeps);

            // ProjectD should only appear once despite being reached through multiple paths
            Assert.Single(transitiveDeps.Where(d => d.Contains("ProjectD")));
        }

        [Fact]
        public void GetTransitiveDependencies_CircularDependency_HandlesGracefully()
        {
            // Arrange: A → B → C → A (circular dependency)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" },
                ["ProjectB"] = new List<string> { "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectA/ProjectA.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj" });

            // Assert - should not loop infinitely and should include all reachable projects
            Assert.Equal(3, transitiveDeps.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDeps);
            Assert.Contains("/repo/ProjectA/ProjectA.csproj", transitiveDeps);
        }

        [Fact]
        public void VersionPropagation_SimpleChain_AllProjectsGetVersionBumps()
        {
            // Arrange: C → B → A (A changes, B and C should get version bumps)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Set up git service to indicate changes in ProjectA
            var mockGitServiceWithChanges = new MockGitServiceWithChanges
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                DirectChanges = true // ProjectA has direct changes
            };

            var versionCalculator = new VersionCalculator(mockGitServiceWithChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceWithChanges, null);

            // Act - Calculate versions for all projects
            var projectAInfo = analyzer.AnalyzeProject("/repo/ProjectA/ProjectA.csproj", "/repo");
            var projectBInfo = analyzer.AnalyzeProject("/repo/ProjectB/ProjectB.csproj", "/repo");
            var projectCInfo = analyzer.AnalyzeProject("/repo/ProjectC/ProjectC.csproj", "/repo");

            // Assert
            Assert.True(projectAInfo.Version.VersionChanged, "ProjectA should have version change (direct changes)");
            Assert.True(projectBInfo.Version.VersionChanged, "ProjectB should have version change (depends on changed ProjectA)");
            Assert.True(projectCInfo.Version.VersionChanged, "ProjectC should have version change (transitively depends on changed ProjectA)");

            // Verify the dependency relationships are captured
            Assert.Empty(projectAInfo.DirectDependencies);
            Assert.Single(projectBInfo.DirectDependencies);
            Assert.Contains("ProjectA", projectBInfo.DirectDependencies);
            Assert.Single(projectCInfo.DirectDependencies);
            Assert.Contains("ProjectB", projectCInfo.DirectDependencies);
        }

        [Fact]
        public void VersionPropagation_LongChain_AllDependentsGetVersionBumps()
        {
            // Arrange: E → D → C → B → A (A changes, all should get version bumps)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" },
                ["ProjectD"] = new List<string> { "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectE"] = new List<string> { "/repo/ProjectD/ProjectD.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var mockGitServiceWithChanges = new MockGitServiceWithChanges
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                DirectChanges = true
            };

            var versionCalculator = new VersionCalculator(mockGitServiceWithChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceWithChanges, null);

            // Act
            var projects = new[]
            {
                analyzer.AnalyzeProject("/repo/ProjectA/ProjectA.csproj", "/repo"),
                analyzer.AnalyzeProject("/repo/ProjectB/ProjectB.csproj", "/repo"),
                analyzer.AnalyzeProject("/repo/ProjectC/ProjectC.csproj", "/repo"),
                analyzer.AnalyzeProject("/repo/ProjectD/ProjectD.csproj", "/repo"),
                analyzer.AnalyzeProject("/repo/ProjectE/ProjectE.csproj", "/repo")
            };

            // Assert - All projects should have version changes
            foreach (var project in projects)
            {
                Assert.True(project.Version.VersionChanged, 
                    $"{project.Name} should have version change due to dependency chain");
            }
        }

        [Fact]
        public void VersionPropagation_DiamondStructure_CorrectPropagation()
        {
            // Arrange: D depends on B and C, B and C depend on A, A changes
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectD"] = new List<string> { "/repo/ProjectB/ProjectB.csproj", "/repo/ProjectC/ProjectC.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var mockGitServiceWithChanges = new MockGitServiceWithChanges
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                DirectChanges = true
            };

            var versionCalculator = new VersionCalculator(mockGitServiceWithChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceWithChanges, null);

            // Act
            var projectA = analyzer.AnalyzeProject("/repo/ProjectA/ProjectA.csproj", "/repo");
            var projectB = analyzer.AnalyzeProject("/repo/ProjectB/ProjectB.csproj", "/repo");
            var projectC = analyzer.AnalyzeProject("/repo/ProjectC/ProjectC.csproj", "/repo");
            var projectD = analyzer.AnalyzeProject("/repo/ProjectD/ProjectD.csproj", "/repo");

            // Assert
            Assert.True(projectA.Version.VersionChanged, "ProjectA should have version change (direct changes)");
            Assert.True(projectB.Version.VersionChanged, "ProjectB should have version change (depends on A)");
            Assert.True(projectC.Version.VersionChanged, "ProjectC should have version change (depends on A)");
            Assert.True(projectD.Version.VersionChanged, "ProjectD should have version change (depends on B and C)");

            // Verify dependency structure
            Assert.Empty(projectA.DirectDependencies);
            Assert.Single(projectB.DirectDependencies);
            Assert.Contains("ProjectA", projectB.DirectDependencies);
            Assert.Single(projectC.DirectDependencies);
            Assert.Contains("ProjectA", projectC.DirectDependencies);
            Assert.Equal(2, projectD.DirectDependencies.Count);
            Assert.Contains("ProjectB", projectD.DirectDependencies);
            Assert.Contains("ProjectC", projectD.DirectDependencies);
        }

        [Fact]
        public void VersionPropagation_NoChanges_NoVersionBumps()
        {
            // Arrange: Simple chain with no changes anywhere
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var mockGitServiceNoChanges = new MockGitServiceWithChanges
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                DirectChanges = false, // No changes anywhere
                DependencyChanges = false
            };

            var versionCalculator = new VersionCalculator(mockGitServiceNoChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceNoChanges, null);

            // Act
            var projectA = analyzer.AnalyzeProject("/repo/ProjectA/ProjectA.csproj", "/repo");
            var projectB = analyzer.AnalyzeProject("/repo/ProjectB/ProjectB.csproj", "/repo");
            var projectC = analyzer.AnalyzeProject("/repo/ProjectC/ProjectC.csproj", "/repo");

            // Assert - No projects should have version changes
            Assert.False(projectA.Version.VersionChanged, "ProjectA should not have version change (no changes)");
            Assert.False(projectB.Version.VersionChanged, "ProjectB should not have version change (no dependency changes)");
            Assert.False(projectC.Version.VersionChanged, "ProjectC should not have version change (no dependency changes)");
        }

        [Fact]
        public void VersionPropagation_MiddleProjectChanges_OnlyDependentsGetBumps()
        {
            // Arrange: A → B → C, B changes (A unchanged, C should get bump)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Custom git service that returns changes only for ProjectB
            var mockGitServiceSelectiveChanges = new MockGitServiceWithSelectiveChanges(
                changedProjects: new[] { "ProjectB" })
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var versionCalculator = new VersionCalculator(mockGitServiceSelectiveChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceSelectiveChanges, null);

            // Act
            var projectA = analyzer.AnalyzeProject("/repo/ProjectA/ProjectA.csproj", "/repo");
            var projectB = analyzer.AnalyzeProject("/repo/ProjectB/ProjectB.csproj", "/repo");
            var projectC = analyzer.AnalyzeProject("/repo/ProjectC/ProjectC.csproj", "/repo");

            // Assert
            Assert.False(projectA.Version.VersionChanged, "ProjectA should not have version change (no direct changes)");
            Assert.True(projectB.Version.VersionChanged, "ProjectB should have version change (direct changes)");
            Assert.True(projectC.Version.VersionChanged, "ProjectC should have version change (dependency B changed)");
        }

        [Theory]
        [InlineData(3)] // Small chain: A → B → C
        [InlineData(5)] // Medium chain: A → B → C → D → E
        [InlineData(10)] // Large chain: A → B → C → D → E → F → G → H → I → J
        public void VersionPropagation_VariableChainLength_HandlesCorrectly(int chainLength)
        {
            // Arrange: Create a chain of specified length
            var projectStructure = new Dictionary<string, List<string>>();
            var projectNames = Enumerable.Range(0, chainLength)
                .Select(i => $"Project{(char)('A' + i)}")
                .ToList();

            for (int i = 0; i < projectNames.Count; i++)
            {
                if (i == 0)
                {
                    projectStructure[projectNames[i]] = new List<string>();
                }
                else
                {
                    projectStructure[projectNames[i]] = new List<string> 
                    { 
                        $"/repo/{projectNames[i - 1]}/{projectNames[i - 1]}.csproj" 
                    };
                }
            }

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var mockGitServiceWithChanges = new MockGitServiceWithChanges
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                DirectChanges = true
            };

            var versionCalculator = new VersionCalculator(mockGitServiceWithChanges, null);
            var analyzer = new ProjectAnalyzer(versionCalculator, mockGitServiceWithChanges, null);

            // Act - Analyze all projects
            var projects = projectNames.Select(name => 
                analyzer.AnalyzeProject($"/repo/{name}/{name}.csproj", "/repo")).ToList();

            // Assert - All projects should have version changes (since first project changed)
            for (int i = 0; i < projects.Count; i++)
            {
                Assert.True(projects[i].Version.VersionChanged, 
                    $"{projectNames[i]} should have version change in chain of length {chainLength}");
            }

            // Verify the chain structure is correct
            Assert.Empty(projects[0].DirectDependencies); // First project has no dependencies
            for (int i = 1; i < projects.Count; i++)
            {
                Assert.Single(projects[i].DirectDependencies);
                Assert.Contains(projectNames[i - 1], projects[i].DirectDependencies);
            }
        }

        [Fact]
        public void Performance_LargeTransitiveChain_HandlesEfficiently()
        {
            // Test performance with a reasonably large transitive chain (20 projects)
            var projectStructure = new Dictionary<string, List<string>>();
            var projectNames = Enumerable.Range(0, 20)
                .Select(i => $"Project{i:D2}")
                .ToList();

            for (int i = 0; i < projectNames.Count; i++)
            {
                if (i == 0)
                {
                    projectStructure[projectNames[i]] = new List<string>();
                }
                else
                {
                    projectStructure[projectNames[i]] = new List<string> 
                    { 
                        $"/repo/{projectNames[i - 1]}/{projectNames[i - 1]}.csproj" 
                    };
                }
            }

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            var startTime = DateTime.Now;

            // Act - Calculate transitive dependencies for the last project in the chain
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                $"/repo/{projectNames.Last()}/{projectNames.Last()}.csproj", 
                "/repo", 
                new List<string> { $"/repo/{projectNames[projectNames.Count - 2]}/{projectNames[projectNames.Count - 2]}.csproj" });

            var duration = DateTime.Now - startTime;

            // Assert - Should complete quickly and return all dependencies in the chain
            Assert.Equal(19, transitiveDeps.Count); // All projects except self
            Assert.True(duration.TotalMilliseconds < 1000, "Performance test should complete in under 1 second");
        }
    }

    #region Mock Classes for Testing

    /// <summary>
    /// Extended mock project analyzer that exposes transitive dependency functionality for testing
    /// </summary>
    public class MockProjectAnalyzerWithTransitive : IProjectAnalyzer
    {
        private Dictionary<string, List<string>> _projectStructure = new();

        public void SetProjectStructure(Dictionary<string, List<string>> structure)
        {
            _projectStructure = structure;
        }

        public List<string> GetTransitiveDependenciesPublic(string projectPath, string repositoryPath, List<string> directDependencies)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var allProjects = _projectStructure.ToDictionary(
                kvp => kvp.Key,
                kvp => (Path: $"/repo/{kvp.Key}/{kvp.Key}.csproj", Dependencies: kvp.Value),
                StringComparer.OrdinalIgnoreCase);
            
            var transitiveDeps = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            BuildTransitiveDependencyList(projectName, allProjects, transitiveDeps, visited);
            return transitiveDeps;
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

        #region IProjectAnalyzer Implementation

        public List<ProjectInfo> AnalyzeProjects(string repositoryPath, string projectDirectory = null)
        {
            throw new NotImplementedException("Use real ProjectAnalyzer for integration tests");
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath)
        {
            throw new NotImplementedException("Use real ProjectAnalyzer for integration tests");
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath, VersionOptions additionalOptions)
        {
            throw new NotImplementedException("Use real ProjectAnalyzer for integration tests");
        }

        public List<string> GetProjectDependencies(string projectPath, string repositoryPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            return _projectStructure.TryGetValue(projectName, out var deps) ? deps : new List<string>();
        }

        public void BuildDependencyGraph(List<ProjectInfo> projects)
        {
            throw new NotImplementedException("Use real ProjectAnalyzer for integration tests");
        }

        #endregion
    }

    /// <summary>
    /// Mock git service that can selectively return changes for specific projects
    /// </summary>
    public class MockGitServiceWithSelectiveChanges : MockGitService
    {
        private readonly HashSet<string> _changedProjects;

        public MockGitServiceWithSelectiveChanges(IEnumerable<string> changedProjects)
        {
            _changedProjects = new HashSet<string>(changedProjects, StringComparer.OrdinalIgnoreCase);
        }

        public override bool ProjectHasChangedSinceTag(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, bool debug = false)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            // Return true if this project is in the changed projects list
            if (_changedProjects.Contains(projectName))
                return true;

            // Check if any dependencies have changed
            if (dependencies != null)
            {
                foreach (var dep in dependencies)
                {
                    var depName = Path.GetFileNameWithoutExtension(dep);
                    if (_changedProjects.Contains(depName))
                        return true;
                }
            }

            return false;
        }
    }

    #endregion
}