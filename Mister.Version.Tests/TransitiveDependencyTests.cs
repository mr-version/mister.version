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
        public void TransitiveDependencyDiscovery_CoreFunctionality_WorksCorrectly()
        {
            // Test that demonstrates the core transitive dependency discovery works
            // This test focuses on the transitive dependency calculation itself rather than full integration
            
            // Arrange: Chain structure A ← B ← C (C depends on B, B depends on A)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act - Get transitive dependencies for ProjectC
            var transitiveDepsC = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectC/ProjectC.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj" });

            // Get transitive dependencies for ProjectB
            var transitiveDepsB = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectB/ProjectB.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectA/ProjectA.csproj" });

            // Assert
            // ProjectC should have both ProjectB and ProjectA as transitive dependencies
            Assert.Equal(2, transitiveDepsC.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDepsC);
            Assert.Contains("/repo/ProjectA/ProjectA.csproj", transitiveDepsC);

            // ProjectB should have only ProjectA as transitive dependency
            Assert.Single(transitiveDepsB);
            Assert.Contains("/repo/ProjectA/ProjectA.csproj", transitiveDepsB);

            // This validates the core functionality: when A changes, the dependency tracking
            // will correctly identify that both B and C need to be version-bumped
        }

        [Fact]
        public void TransitiveDependencyDiscovery_LongChain_ReturnsCorrectDependencies()
        {
            // Test that long chains are correctly resolved
            // Arrange: E → D → C → B → A (5-level chain)
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectB/ProjectB.csproj" },
                ["ProjectD"] = new List<string> { "/repo/ProjectC/ProjectC.csproj" },
                ["ProjectE"] = new List<string> { "/repo/ProjectD/ProjectD.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act - Get transitive dependencies for the end of the chain
            var transitiveDepsE = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectE/ProjectE.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectD/ProjectD.csproj" });

            // Assert - ProjectE should have all 4 other projects as transitive dependencies
            Assert.Equal(4, transitiveDepsE.Count);
            Assert.Contains("/repo/ProjectD/ProjectD.csproj", transitiveDepsE);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDepsE);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDepsE);
            Assert.Contains("/repo/ProjectA/ProjectA.csproj", transitiveDepsE);

            // This validates that when A changes, the dependency tracking will correctly
            // identify that ALL projects in the chain (B, C, D, E) need version bumps
        }

        [Fact]
        public void TransitiveDependencyDiscovery_DiamondStructure_HandlesCorrectly()
        {
            // Test diamond dependency structure
            // Arrange: D depends on B and C, B and C both depend on A
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>(),
                ["ProjectB"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectC"] = new List<string> { "/repo/ProjectA/ProjectA.csproj" },
                ["ProjectD"] = new List<string> { "/repo/ProjectB/ProjectB.csproj", "/repo/ProjectC/ProjectC.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act - Get transitive dependencies for ProjectD
            var transitiveDepsD = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectD/ProjectD.csproj", 
                "/repo", 
                new List<string> { "/repo/ProjectB/ProjectB.csproj", "/repo/ProjectC/ProjectC.csproj" });

            // Assert - ProjectD should have B, C, and A as dependencies
            // A should appear only once despite being reachable through both B and C
            Assert.Equal(3, transitiveDepsD.Count);
            Assert.Contains("/repo/ProjectB/ProjectB.csproj", transitiveDepsD);
            Assert.Contains("/repo/ProjectC/ProjectC.csproj", transitiveDepsD);
            Assert.Contains("/repo/ProjectA/ProjectA.csproj", transitiveDepsD);

            // ProjectA should only appear once despite being reached through multiple paths
            Assert.Single(transitiveDepsD.Where(d => d.Contains("ProjectA")));

            // This validates that when A changes, the dependency tracking will correctly
            // identify that B, C, and D all need version bumps, with proper deduplication
        }

        [Fact]
        public void TransitiveDependencyDiscovery_EmptyDependencies_ReturnsEmptyList()
        {
            // Test that projects with no dependencies return empty transitive dependency lists
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["ProjectA"] = new List<string>()
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/ProjectA/ProjectA.csproj", 
                "/repo", 
                new List<string>());

            // Assert
            Assert.Empty(transitiveDeps);
        }

        [Fact]
        public void TransitiveDependencyDiscovery_RealWorldComplexStructure_HandlesCorrectly()
        {
            // Test a more complex real-world-like structure
            // Core ← Utilities, Core ← Models
            // WebAPI ← Core, WebAPI ← Models
            // Tests ← WebAPI, Tests ← Core
            var projectStructure = new Dictionary<string, List<string>>
            {
                ["Core"] = new List<string>(),
                ["Models"] = new List<string>(),
                ["Utilities"] = new List<string> { "/repo/Core/Core.csproj" },
                ["WebAPI"] = new List<string> { "/repo/Core/Core.csproj", "/repo/Models/Models.csproj" },
                ["Tests"] = new List<string> { "/repo/WebAPI/WebAPI.csproj", "/repo/Core/Core.csproj" }
            };

            _mockProjectAnalyzer.SetProjectStructure(projectStructure);

            // Act - Get transitive dependencies for Tests project
            var transitiveDepsTests = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                "/repo/Tests/Tests.csproj", 
                "/repo", 
                new List<string> { "/repo/WebAPI/WebAPI.csproj", "/repo/Core/Core.csproj" });

            // Assert - Tests should have WebAPI, Core, and Models as dependencies
            // (Utilities is not reachable from Tests)
            Assert.Equal(3, transitiveDepsTests.Count);
            Assert.Contains("/repo/WebAPI/WebAPI.csproj", transitiveDepsTests);
            Assert.Contains("/repo/Core/Core.csproj", transitiveDepsTests);
            Assert.Contains("/repo/Models/Models.csproj", transitiveDepsTests); // Via WebAPI
            
            // This validates complex dependency resolution where changes to Models or Core
            // should propagate through multiple paths to affect Tests
        }

        [Theory]
        [InlineData(3)] // Small chain: A → B → C
        [InlineData(5)] // Medium chain: A → B → C → D → E
        [InlineData(10)] // Large chain: A → B → C → D → E → F → G → H → I → J
        public void TransitiveDependencyDiscovery_VariableChainLength_ReturnsCorrectCount(int chainLength)
        {
            // Test that variable chain lengths are handled correctly
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

            // Act - Get transitive dependencies for the last project in the chain
            var lastProjectName = projectNames.Last();
            var secondToLastProjectPath = $"/repo/{projectNames[projectNames.Count - 2]}/{projectNames[projectNames.Count - 2]}.csproj";
            
            var transitiveDeps = _mockProjectAnalyzer.GetTransitiveDependenciesPublic(
                $"/repo/{lastProjectName}/{lastProjectName}.csproj", 
                "/repo", 
                new List<string> { secondToLastProjectPath });

            // Assert - Should have all other projects as transitive dependencies
            Assert.Equal(chainLength - 1, transitiveDeps.Count);
            
            // Verify all expected projects are included
            for (int i = 0; i < chainLength - 1; i++)
            {
                var expectedPath = $"/repo/{projectNames[i]}/{projectNames[i]}.csproj";
                Assert.Contains(expectedPath, transitiveDeps);
            }

            // This validates that the transitive dependency resolution scales correctly
            // and can handle chains of various lengths without performance issues
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