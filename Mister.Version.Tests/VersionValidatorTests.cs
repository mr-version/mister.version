using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class VersionValidatorTests
    {
        private readonly VersionValidator _validator;

        public VersionValidatorTests()
        {
            _validator = new VersionValidator();
        }

        #region MinimumVersion Tests

        [Fact]
        public void ValidateVersion_MinimumVersion_PassesWhenAboveMinimum()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "1.0.0"
            };

            // Act
            var result = _validator.ValidateVersion("2.0.0", "1.5.0", constraints, VersionBumpType.Major);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_MinimumVersion_FailsWhenBelowMinimum()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "2.0.0"
            };

            // Act
            var result = _validator.ValidateVersion("1.5.0", "1.4.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.ConstraintName == "MinimumVersion");
        }

        [Fact]
        public void ValidateVersion_MinimumVersion_PassesWhenEqualToMinimum()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "1.5.0"
            };

            // Act
            var result = _validator.ValidateVersion("1.5.0", "1.4.0", constraints, VersionBumpType.Patch);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion

        #region MaximumVersion Tests

        [Fact]
        public void ValidateVersion_MaximumVersion_PassesWhenBelowMaximum()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MaximumVersion = "5.0.0"
            };

            // Act
            var result = _validator.ValidateVersion("3.0.0", "2.5.0", constraints, VersionBumpType.Major);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_MaximumVersion_FailsWhenAboveMaximum()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MaximumVersion = "3.0.0"
            };

            // Act
            var result = _validator.ValidateVersion("4.0.0", "3.5.0", constraints, VersionBumpType.Major);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.ConstraintName == "MaximumVersion");
        }

        #endregion

        #region AllowedRange Tests

        [Theory]
        [InlineData("2.1.5", "2.x.x", true)]
        [InlineData("2.3.0", "2.x.x", true)]
        [InlineData("3.0.0", "2.x.x", false)]
        [InlineData("1.9.9", "2.x.x", false)]
        [InlineData("2.1.3", "2.1.x", true)]
        [InlineData("2.2.0", "2.1.x", false)]
        [InlineData("2.1.5", "2.1.5", true)]
        [InlineData("2.1.6", "2.1.5", false)]
        public void IsVersionInRange_VariousPatterns_ReturnsExpected(string version, string pattern, bool expected)
        {
            // Act
            var result = _validator.IsVersionInRange(version, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ValidateVersion_AllowedRange_FailsWhenOutsideRange()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                AllowedRange = "2.x.x"
            };

            // Act
            var result = _validator.ValidateVersion("3.0.0", "2.5.0", constraints, VersionBumpType.Major);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ConstraintName == "AllowedRange");
        }

        #endregion

        #region BlockedVersions Tests

        [Fact]
        public void ValidateVersion_BlockedVersions_FailsWhenVersionIsBlocked()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                BlockedVersions = new List<string> { "2.0.0", "2.1.0", "3.0.0" }
            };

            // Act
            var result = _validator.ValidateVersion("2.0.0", "1.9.0", constraints, VersionBumpType.Major);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ConstraintName == "BlockedVersions");
        }

        [Fact]
        public void ValidateVersion_BlockedVersions_PassesWhenVersionIsNotBlocked()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                BlockedVersions = new List<string> { "2.0.0", "2.1.0" }
            };

            // Act
            var result = _validator.ValidateVersion("2.2.0", "2.1.5", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Theory]
        [InlineData("1.0.0", "1.0.0", true)]
        [InlineData("1.0.0", "1.0.1", false)]
        [InlineData("2.5.3", "2.5.3", true)]
        public void IsVersionBlocked_VariousVersions_ReturnsExpected(string version, string blockedVersion, bool expected)
        {
            // Arrange
            var blockedVersions = new List<string> { blockedVersion };

            // Act
            var result = _validator.IsVersionBlocked(version, blockedVersions);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region MonotonicIncrease Tests

        [Fact]
        public void ValidateVersion_MonotonicIncrease_FailsWhenVersionDoesNotIncrease()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMonotonicIncrease = true
            };

            // Act
            var result = _validator.ValidateVersion("1.5.0", "1.5.0", constraints, VersionBumpType.Patch);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ConstraintName == "RequireMonotonicIncrease");
        }

        [Fact]
        public void ValidateVersion_MonotonicIncrease_FailsWhenVersionDecreases()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMonotonicIncrease = true
            };

            // Act
            var result = _validator.ValidateVersion("1.4.0", "1.5.0", constraints, VersionBumpType.Patch);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ConstraintName == "RequireMonotonicIncrease");
        }

        [Fact]
        public void ValidateVersion_MonotonicIncrease_PassesWhenVersionIncreases()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMonotonicIncrease = true
            };

            // Act
            var result = _validator.ValidateVersion("1.6.0", "1.5.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion

        #region MajorApproval Tests

        [Fact]
        public void ValidateVersion_RequireMajorApproval_FailsWhenNotApproved()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMajorApproval = true
            };

            // Act
            var result = _validator.ValidateVersion("2.0.0", "1.5.0", constraints, VersionBumpType.Major, majorApproved: false);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ConstraintName == "RequireMajorApproval");
        }

        [Fact]
        public void ValidateVersion_RequireMajorApproval_PassesWhenApproved()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMajorApproval = true
            };

            // Act
            var result = _validator.ValidateVersion("2.0.0", "1.5.0", constraints, VersionBumpType.Major, majorApproved: true);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_RequireMajorApproval_PassesForNonMajorBump()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMajorApproval = true
            };

            // Act
            var result = _validator.ValidateVersion("1.6.0", "1.5.0", constraints, VersionBumpType.Minor, majorApproved: false);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion

        #region CustomRules Tests

        [Fact]
        public void ValidateCustomRules_PatternRule_FailsWhenDoesNotMatch()
        {
            // Arrange
            var customRules = new Dictionary<string, ValidationRule>
            {
                ["must-start-with-v"] = new ValidationRule
                {
                    Name = "must-start-with-v",
                    Type = ValidationRuleType.Pattern,
                    Severity = ValidationSeverity.Error,
                    Config = new Dictionary<string, string> { ["pattern"] = @"^v\d+\.\d+\.\d+$" }
                }
            };

            // Act
            var result = _validator.ValidateCustomRules("1.0.0", customRules);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void ValidateCustomRules_PatternRule_PassesWhenMatches()
        {
            // Arrange
            var customRules = new Dictionary<string, ValidationRule>
            {
                ["must-start-with-v"] = new ValidationRule
                {
                    Name = "must-start-with-v",
                    Type = ValidationRuleType.Pattern,
                    Severity = ValidationSeverity.Error,
                    Config = new Dictionary<string, string> { ["pattern"] = @"^v?\d+\.\d+\.\d+$" }
                }
            };

            // Act
            var result = _validator.ValidateCustomRules("1.0.0", customRules);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateCustomRules_RangeRule_FailsWhenOutsideRange()
        {
            // Arrange
            var customRules = new Dictionary<string, ValidationRule>
            {
                ["version-range"] = new ValidationRule
                {
                    Name = "version-range",
                    Type = ValidationRuleType.Range,
                    Severity = ValidationSeverity.Error,
                    Config = new Dictionary<string, string> { ["min"] = "1.0.0", ["max"] = "5.0.0" }
                }
            };

            // Act
            var result = _validator.ValidateCustomRules("6.0.0", customRules);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void ValidateCustomRules_WarningRule_CreatesWarningNotError()
        {
            // Arrange
            var customRules = new Dictionary<string, ValidationRule>
            {
                ["test-warning"] = new ValidationRule
                {
                    Name = "test-warning",
                    Type = ValidationRuleType.Pattern,
                    Severity = ValidationSeverity.Warning,
                    Config = new Dictionary<string, string> { ["pattern"] = @"^999\." }
                }
            };

            // Act
            var result = _validator.ValidateCustomRules("1.0.0", customRules);

            // Assert
            Assert.True(result.IsValid); // Warnings don't fail validation
            Assert.Empty(result.Errors);
            Assert.NotEmpty(result.Warnings);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void ValidateVersion_MultipleConstraints_AllPass()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "1.0.0",
                MaximumVersion = "10.0.0",
                AllowedRange = "2.x.x",
                RequireMonotonicIncrease = true,
                BlockedVersions = new List<string> { "2.5.0" }
            };

            // Act
            var result = _validator.ValidateVersion("2.3.0", "2.2.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_MultipleConstraints_OneFails()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "1.0.0",
                MaximumVersion = "10.0.0",
                AllowedRange = "2.x.x",
                RequireMonotonicIncrease = true,
                BlockedVersions = new List<string> { "2.3.0" }
            };

            // Act
            var result = _validator.ValidateVersion("2.3.0", "2.2.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains(result.Errors, e => e.ConstraintName == "BlockedVersions");
        }

        [Fact]
        public void ValidateVersion_DisabledConstraints_AlwaysPasses()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = false,
                MinimumVersion = "10.0.0", // Would normally fail
                BlockedVersions = new List<string> { "1.0.0" } // Would normally fail
            };

            // Act
            var result = _validator.ValidateVersion("1.0.0", "0.9.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_NullConstraints_AlwaysPasses()
        {
            // Act
            var result = _validator.ValidateVersion("1.0.0", "0.9.0", null, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ValidateVersion_InvalidVersionFormat_ReturnsError()
        {
            // Arrange
            var constraints = new VersionConstraints { Enabled = true };

            // Act
            var result = _validator.ValidateVersion("invalid", "1.0.0", constraints, VersionBumpType.Patch);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_PrereleaseVersions_HandlesCorrectly()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                MinimumVersion = "1.0.0",
                RequireMonotonicIncrease = true
            };

            // Act
            var result = _validator.ValidateVersion("1.5.0-alpha.1", "1.4.0", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateVersion_BuildMetadata_IsIgnoredInComparison()
        {
            // Arrange
            var constraints = new VersionConstraints
            {
                Enabled = true,
                RequireMonotonicIncrease = true
            };

            // Act
            var result = _validator.ValidateVersion("1.5.0+build.123", "1.4.0+build.122", constraints, VersionBumpType.Minor);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion
    }
}
