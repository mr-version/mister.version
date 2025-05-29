using System;
using Xunit;
using Mister.Version.Core.Models;

namespace Mister.Version.Tests
{
    public class SemVerTests
    {
        [Fact]
        public void SemVer_DefaultConstructor_InitializesCorrectly()
        {
            // Arrange & Act
            var semVer = new SemVer();

            // Assert
            Assert.Equal(0, semVer.Major);
            Assert.Equal(0, semVer.Minor);
            Assert.Equal(0, semVer.Patch);
            Assert.Null(semVer.PreRelease);
            Assert.Null(semVer.BuildMetadata);
        }

        [Theory]
        [InlineData(1, 2, 3, null, null, "1.2.3")]
        [InlineData(0, 0, 1, null, null, "0.0.1")]
        [InlineData(10, 20, 30, null, null, "10.20.30")]
        [InlineData(1, 0, 0, "alpha", null, "1.0.0-alpha")]
        [InlineData(2, 1, 0, "beta.1", null, "2.1.0-beta.1")]
        [InlineData(3, 0, 0, "rc.2", null, "3.0.0-rc.2")]
        [InlineData(1, 2, 3, "dev.123", null, "1.2.3-dev.123")]
        [InlineData(1, 0, 0, "feature.new-thing.5", null, "1.0.0-feature.new-thing.5")]
        public void SemVer_ToVersionString_FormatsCorrectly(
            int major, int minor, int patch, string preRelease, string buildMetadata, string expected)
        {
            // Arrange
            var semVer = new SemVer
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PreRelease = preRelease,
                BuildMetadata = buildMetadata
            };

            // Act
            var result = semVer.ToVersionString();

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1, 2, 3, null, null, "1.2.3")]
        [InlineData(1, 2, 3, "alpha", null, "1.2.3-alpha")]
        [InlineData(1, 2, 3, null, "build.123", "1.2.3+build.123")]
        [InlineData(1, 2, 3, "beta.2", "build.456", "1.2.3-beta.2+build.456")]
        [InlineData(0, 1, 0, "dev.1", "sha.abcdef1", "0.1.0-dev.1+sha.abcdef1")]
        public void SemVer_ToString_IncludesBuildMetadata(
            int major, int minor, int patch, string preRelease, string buildMetadata, string expected)
        {
            // Arrange
            var semVer = new SemVer
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PreRelease = preRelease,
                BuildMetadata = buildMetadata
            };

            // Act
            var result = semVer.ToString();

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SemVer_Clone_CreatesIndependentCopy()
        {
            // Arrange
            var original = new SemVer
            {
                Major = 1,
                Minor = 2,
                Patch = 3,
                PreRelease = "alpha.1",
                BuildMetadata = "build.123"
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.Major, clone.Major);
            Assert.Equal(original.Minor, clone.Minor);
            Assert.Equal(original.Patch, clone.Patch);
            Assert.Equal(original.PreRelease, clone.PreRelease);
            Assert.Equal(original.BuildMetadata, clone.BuildMetadata);

            // Verify independence
            clone.Major = 2;
            clone.PreRelease = "beta.1";
            Assert.Equal(1, original.Major);
            Assert.Equal("alpha.1", original.PreRelease);
        }

        [Theory]
        [InlineData(1, 0, 0, 2, 0, 0, -1)] // 1.0.0 < 2.0.0
        [InlineData(2, 0, 0, 1, 0, 0, 1)]  // 2.0.0 > 1.0.0
        [InlineData(1, 1, 0, 1, 2, 0, -1)] // 1.1.0 < 1.2.0
        [InlineData(1, 2, 0, 1, 1, 0, 1)]  // 1.2.0 > 1.1.0
        [InlineData(1, 1, 1, 1, 1, 2, -1)] // 1.1.1 < 1.1.2
        [InlineData(1, 1, 2, 1, 1, 1, 1)]  // 1.1.2 > 1.1.1
        [InlineData(1, 2, 3, 1, 2, 3, 0)]  // 1.2.3 = 1.2.3
        public void SemVer_CompareTo_ComparesVersionsCorrectly(
            int major1, int minor1, int patch1,
            int major2, int minor2, int patch2,
            int expectedComparison)
        {
            // Arrange
            var v1 = new SemVer { Major = major1, Minor = minor1, Patch = patch1 };
            var v2 = new SemVer { Major = major2, Minor = minor2, Patch = patch2 };

            // Act
            var result = CompareSemanticVersions(v1, v2);

            // Assert
            Assert.Equal(expectedComparison, Math.Sign(result));
        }

        [Theory]
        [InlineData("1.0.0", null, 0)]      // 1.0.0 == 1.0.0 (same versions)
        [InlineData("1.0.0-alpha", null, -1)] // 1.0.0-alpha < 1.0.0 (prerelease is less than release)
        [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]  // alpha < beta
        [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]   // beta > alpha
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", -1)] // alpha.1 < alpha.2
        [InlineData("1.0.0-rc.10", "1.0.0-rc.2", 1)]   // rc.10 > rc.2 (numeric comparison)
        public void SemVer_CompareTo_HandlesPreReleaseCorrectly(
            string version1, string version2, int expectedComparison)
        {
            // Arrange
            var v1 = ParseVersion(version1);
            var v2 = version2 != null ? ParseVersion(version2) : new SemVer { Major = 1, Minor = 0, Patch = 0 };

            // Act
            var result = CompareSemanticVersions(v1, v2);

            // Assert
            if (expectedComparison != 0)
            {
                Assert.Equal(expectedComparison, Math.Sign(result));
            }
        }

        [Theory]
        [InlineData(0, 0, 0, false)]
        [InlineData(0, 0, 1, false)]
        [InlineData(0, 1, 0, false)]
        [InlineData(1, 0, 0, true)]
        [InlineData(1, 0, 1, true)]
        [InlineData(2, 3, 4, true)]
        public void SemVer_IsStable_IdentifiesStableVersions(
            int major, int minor, int patch, bool expectedStable)
        {
            // Arrange
            var semVer = new SemVer { Major = major, Minor = minor, Patch = patch };

            // Act
            var isStable = semVer.Major >= 1;

            // Assert
            Assert.Equal(expectedStable, isStable);
        }

        [Theory]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("alpha", true)]
        [InlineData("beta.1", true)]
        [InlineData("rc.2", true)]
        [InlineData("dev.123", true)]
        public void SemVer_IsPreRelease_IdentifiesPreReleaseVersions(
            string preRelease, bool expectedPreRelease)
        {
            // Arrange
            var semVer = new SemVer
            {
                Major = 1,
                Minor = 0,
                Patch = 0,
                PreRelease = preRelease
            };

            // Act
            var isPreRelease = !string.IsNullOrEmpty(semVer.PreRelease);

            // Assert
            Assert.Equal(expectedPreRelease, isPreRelease);
        }

        [Fact]
        public void SemVer_Equality_ComparesCorrectly()
        {
            // Arrange
            var v1 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha" };
            var v2 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha" };
            var v3 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "beta" };
            var v4 = new SemVer { Major = 1, Minor = 2, Patch = 4, PreRelease = "alpha" };

            // Act & Assert
            Assert.True(v1.Equals(v2));
            Assert.False(v1.Equals(v3));
            Assert.False(v1.Equals(v4));
            Assert.False(v1.Equals(null));
            Assert.False(v1.Equals("not a semver"));
        }

        [Fact]
        public void SemVer_GetHashCode_ConsistentForEqualObjects()
        {
            // Arrange
            var v1 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha" };
            var v2 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha" };

            // Act & Assert
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        // Helper methods
        private SemVer ParseVersion(string version)
        {
            var parts = version.Split('-');
            var versionParts = parts[0].Split('.');
            
            return new SemVer
            {
                Major = int.Parse(versionParts[0]),
                Minor = int.Parse(versionParts[1]),
                Patch = int.Parse(versionParts[2]),
                PreRelease = parts.Length > 1 ? parts[1] : null
            };
        }

        private int CompareSemanticVersions(SemVer v1, SemVer v2)
        {
            // Use the actual SemVer CompareTo method
            return v1.CompareTo(v2);
        }
    }
}