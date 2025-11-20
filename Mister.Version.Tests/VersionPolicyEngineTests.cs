using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class VersionPolicyEngineTests
    {
        private readonly VersionPolicyEngine _engine;

        public VersionPolicyEngineTests()
        {
            _engine = new VersionPolicyEngine();
        }

        #region GetProjectGroup Tests

        [Fact]
        public void GetProjectGroup_ProjectInGroup_ReturnsGroup()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core", "Mister.Version" }
                    }
                }
            };

            // Act
            var group = _engine.GetProjectGroup("Mister.Version.Core", config);

            // Assert
            Assert.NotNull(group);
            Assert.Equal("core-libs", group.Name);
        }

        [Fact]
        public void GetProjectGroup_ProjectWithWildcard_ReturnsGroup()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["all-libs"] = new VersionGroup
                    {
                        Name = "all-libs",
                        Projects = new List<string> { "Mister.Version.*" }
                    }
                }
            };

            // Act
            var group1 = _engine.GetProjectGroup("Mister.Version.Core", config);
            var group2 = _engine.GetProjectGroup("Mister.Version.CLI", config);
            var group3 = _engine.GetProjectGroup("Other.Project", config);

            // Assert
            Assert.NotNull(group1);
            Assert.NotNull(group2);
            Assert.Null(group3);
            Assert.Equal("all-libs", group1.Name);
            Assert.Equal("all-libs", group2.Name);
        }

        [Fact]
        public void GetProjectGroup_ProjectNotInGroup_ReturnsNull()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core" }
                    }
                }
            };

            // Act
            var group = _engine.GetProjectGroup("Other.Project", config);

            // Assert
            Assert.Null(group);
        }

        [Fact]
        public void GetProjectGroup_NullConfig_ReturnsNull()
        {
            // Act
            var group = _engine.GetProjectGroup("Mister.Version.Core", null);

            // Assert
            Assert.Null(group);
        }

        #endregion

        #region IsIndependent Tests

        [Fact]
        public void IsIndependent_IndependentPolicy_ProjectNotInGroup_ReturnsTrue()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Independent,
                Groups = new Dictionary<string, VersionGroup>()
            };

            // Act
            var isIndependent = _engine.IsIndependent("Mister.Version.Core", config);

            // Assert
            Assert.True(isIndependent);
        }

        [Fact]
        public void IsIndependent_IndependentPolicy_ProjectInGroup_ReturnsFalse()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Independent,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core" },
                        Strategy = VersionPolicy.LockStep
                    }
                }
            };

            // Act
            var isIndependent = _engine.IsIndependent("Mister.Version.Core", config);

            // Assert
            Assert.False(isIndependent);
        }

        [Fact]
        public void IsIndependent_LockStepPolicy_ReturnsFalse()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.LockStep
            };

            // Act
            var isIndependent = _engine.IsIndependent("Mister.Version.Core", config);

            // Assert
            Assert.False(isIndependent);
        }

        [Fact]
        public void IsIndependent_GroupedPolicy_ProjectNotInGroup_ReturnsTrue()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core" }
                    }
                }
            };

            // Act
            var isIndependent = _engine.IsIndependent("Other.Project", config);

            // Assert
            Assert.True(isIndependent);
        }

        [Fact]
        public void IsIndependent_GroupedPolicy_ProjectInGroupWithIndependentStrategy_ReturnsTrue()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["independent-group"] = new VersionGroup
                    {
                        Name = "independent-group",
                        Projects = new List<string> { "Mister.Version.Core" },
                        Strategy = VersionPolicy.Independent
                    }
                }
            };

            // Act
            var isIndependent = _engine.IsIndependent("Mister.Version.Core", config);

            // Assert
            Assert.True(isIndependent);
        }

        [Fact]
        public void IsIndependent_NullConfig_ReturnsTrue()
        {
            // Act
            var isIndependent = _engine.IsIndependent("Mister.Version.Core", null);

            // Assert
            Assert.True(isIndependent);
        }

        #endregion

        #region GetLinkedProjects Tests

        [Fact]
        public void GetLinkedProjects_LockStepPolicy_ReturnsAllProjects()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.LockStep
            };
            var allProjects = new List<string>
            {
                "Mister.Version.Core",
                "Mister.Version.CLI",
                "Mister.Version.Tests"
            };

            // Act
            var linkedProjects = _engine.GetLinkedProjects("Mister.Version.Core", allProjects, config);

            // Assert
            Assert.Equal(3, linkedProjects.Count);
            Assert.Contains("Mister.Version.Core", linkedProjects);
            Assert.Contains("Mister.Version.CLI", linkedProjects);
            Assert.Contains("Mister.Version.Tests", linkedProjects);
        }

        [Fact]
        public void GetLinkedProjects_IndependentPolicy_ReturnsOnlySelf()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Independent
            };
            var allProjects = new List<string>
            {
                "Mister.Version.Core",
                "Mister.Version.CLI",
                "Mister.Version.Tests"
            };

            // Act
            var linkedProjects = _engine.GetLinkedProjects("Mister.Version.Core", allProjects, config);

            // Assert
            Assert.Single(linkedProjects);
            Assert.Contains("Mister.Version.Core", linkedProjects);
        }

        [Fact]
        public void GetLinkedProjects_GroupedPolicy_ReturnsProjectsInSameGroup()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core", "Mister.Version" },
                        Strategy = VersionPolicy.LockStep
                    },
                    ["tools"] = new VersionGroup
                    {
                        Name = "tools",
                        Projects = new List<string> { "Mister.Version.CLI" },
                        Strategy = VersionPolicy.Independent
                    }
                }
            };
            var allProjects = new List<string>
            {
                "Mister.Version.Core",
                "Mister.Version",
                "Mister.Version.CLI"
            };

            // Act
            var linkedProjectsCore = _engine.GetLinkedProjects("Mister.Version.Core", allProjects, config);
            var linkedProjectsCli = _engine.GetLinkedProjects("Mister.Version.CLI", allProjects, config);

            // Assert
            Assert.Equal(2, linkedProjectsCore.Count);
            Assert.Contains("Mister.Version.Core", linkedProjectsCore);
            Assert.Contains("Mister.Version", linkedProjectsCore);

            Assert.Single(linkedProjectsCli);
            Assert.Contains("Mister.Version.CLI", linkedProjectsCli);
        }

        [Fact]
        public void GetLinkedProjects_GroupedPolicyWithWildcards_ReturnsMatchingProjects()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["all-libs"] = new VersionGroup
                    {
                        Name = "all-libs",
                        Projects = new List<string> { "Mister.Version.*" },
                        Strategy = VersionPolicy.LockStep
                    }
                }
            };
            var allProjects = new List<string>
            {
                "Mister.Version.Core",
                "Mister.Version.CLI",
                "Other.Project"
            };

            // Act
            var linkedProjects = _engine.GetLinkedProjects("Mister.Version.Core", allProjects, config);

            // Assert
            Assert.Equal(2, linkedProjects.Count);
            Assert.Contains("Mister.Version.Core", linkedProjects);
            Assert.Contains("Mister.Version.CLI", linkedProjects);
            Assert.DoesNotContain("Other.Project", linkedProjects);
        }

        [Fact]
        public void GetLinkedProjects_NullConfig_ReturnsOnlySelf()
        {
            // Arrange
            var allProjects = new List<string>
            {
                "Mister.Version.Core",
                "Mister.Version.CLI"
            };

            // Act
            var linkedProjects = _engine.GetLinkedProjects("Mister.Version.Core", allProjects, null);

            // Assert
            Assert.Single(linkedProjects);
            Assert.Contains("Mister.Version.Core", linkedProjects);
        }

        #endregion

        #region ValidateConfiguration Tests

        [Fact]
        public void ValidateConfiguration_ValidConfig_ReturnsNoErrors()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core", "Mister.Version" },
                        BaseVersion = "1.0.0"
                    }
                }
            };
            var allProjects = new List<string> { "Mister.Version.Core", "Mister.Version", "Mister.Version.CLI" };

            // Act
            var errors = _engine.ValidateConfiguration(config, allProjects);

            // Assert
            Assert.Empty(errors);
        }

        [Fact]
        public void ValidateConfiguration_GroupedPolicyNoGroups_ReturnsError()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>()
            };
            var allProjects = new List<string> { "Mister.Version.Core" };

            // Act
            var errors = _engine.ValidateConfiguration(config, allProjects);

            // Assert
            Assert.Single(errors);
            Assert.Contains("Grouped", errors[0]);
            Assert.Contains("no groups", errors[0]);
        }

        [Fact]
        public void ValidateConfiguration_GroupWithNoProjects_ReturnsError()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["empty-group"] = new VersionGroup
                    {
                        Name = "empty-group",
                        Projects = new List<string>()
                    }
                }
            };
            var allProjects = new List<string> { "Mister.Version.Core" };

            // Act
            var errors = _engine.ValidateConfiguration(config, allProjects);

            // Assert
            Assert.Single(errors);
            Assert.Contains("empty-group", errors[0]);
            Assert.Contains("no projects", errors[0]);
        }

        [Fact]
        public void ValidateConfiguration_InvalidBaseVersion_ReturnsError()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["core-libs"] = new VersionGroup
                    {
                        Name = "core-libs",
                        Projects = new List<string> { "Mister.Version.Core" },
                        BaseVersion = "invalid-version"
                    }
                }
            };
            var allProjects = new List<string> { "Mister.Version.Core" };

            // Act
            var errors = _engine.ValidateConfiguration(config, allProjects);

            // Assert
            Assert.Single(errors);
            Assert.Contains("core-libs", errors[0]);
            Assert.Contains("invalid base version", errors[0]);
        }

        [Fact]
        public void ValidateConfiguration_ProjectInMultipleGroups_ReturnsError()
        {
            // Arrange
            var config = new VersionPolicyConfig
            {
                Policy = VersionPolicy.Grouped,
                Groups = new Dictionary<string, VersionGroup>
                {
                    ["group1"] = new VersionGroup
                    {
                        Name = "group1",
                        Projects = new List<string> { "Mister.Version.Core" }
                    },
                    ["group2"] = new VersionGroup
                    {
                        Name = "group2",
                        Projects = new List<string> { "Mister.Version.Core" }
                    }
                }
            };
            var allProjects = new List<string> { "Mister.Version.Core" };

            // Act
            var errors = _engine.ValidateConfiguration(config, allProjects);

            // Assert
            Assert.Single(errors);
            Assert.Contains("Mister.Version.Core", errors[0]);
            Assert.Contains("multiple groups", errors[0]);
        }

        [Fact]
        public void ValidateConfiguration_NullConfig_ReturnsNoErrors()
        {
            // Arrange
            var allProjects = new List<string> { "Mister.Version.Core" };

            // Act
            var errors = _engine.ValidateConfiguration(null, allProjects);

            // Assert
            Assert.Empty(errors);
        }

        #endregion

        #region CoordinateGroupVersion Tests

        [Fact]
        public void CoordinateGroupVersion_UsesBas eVersion_WhenSpecified()
        {
            // Arrange
            var group = new VersionGroup
            {
                Name = "core-libs",
                Projects = new List<string> { "Mister.Version.Core" },
                BaseVersion = "2.0.0"
            };
            var projectVersions = new Dictionary<string, VersionResult>();

            // Act
            var version = _engine.CoordinateGroupVersion(projectVersions, group);

            // Assert
            Assert.Equal("2.0.0", version);
        }

        [Fact]
        public void CoordinateGroupVersion_UsesHighestVersion_WhenNoBaseVersion()
        {
            // Arrange
            var group = new VersionGroup
            {
                Name = "core-libs",
                Projects = new List<string> { "Mister.Version.Core", "Mister.Version" }
            };
            var projectVersions = new Dictionary<string, VersionResult>
            {
                ["Mister.Version.Core"] = new VersionResult
                {
                    Version = "1.2.0",
                    SemVer = new SemVer { Major = 1, Minor = 2, Patch = 0 }
                },
                ["Mister.Version"] = new VersionResult
                {
                    Version = "1.5.3",
                    SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 }
                }
            };

            // Act
            var version = _engine.CoordinateGroupVersion(projectVersions, group);

            // Assert
            Assert.Equal("1.5.3", version);
        }

        [Fact]
        public void CoordinateGroupVersion_UsesDefaultVersion_WhenNoProjectVersions()
        {
            // Arrange
            var group = new VersionGroup
            {
                Name = "core-libs",
                Projects = new List<string> { "Mister.Version.Core" }
            };
            var projectVersions = new Dictionary<string, VersionResult>();

            // Act
            var version = _engine.CoordinateGroupVersion(projectVersions, group);

            // Assert
            Assert.Equal("0.1.0", version);
        }

        [Fact]
        public void CoordinateGroupVersion_IgnoresProjectsNotInGroup()
        {
            // Arrange
            var group = new VersionGroup
            {
                Name = "core-libs",
                Projects = new List<string> { "Mister.Version.Core" }
            };
            var projectVersions = new Dictionary<string, VersionResult>
            {
                ["Mister.Version.Core"] = new VersionResult
                {
                    Version = "1.0.0",
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 }
                },
                ["Other.Project"] = new VersionResult
                {
                    Version = "5.0.0",
                    SemVer = new SemVer { Major = 5, Minor = 0, Patch = 0 }
                }
            };

            // Act
            var version = _engine.CoordinateGroupVersion(projectVersions, group);

            // Assert
            Assert.Equal("1.0.0", version);
        }

        #endregion
    }
}
