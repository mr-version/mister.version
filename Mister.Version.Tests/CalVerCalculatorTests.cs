using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class CalVerCalculatorTests
    {
        private readonly CalVerCalculator _calculator;

        public CalVerCalculatorTests()
        {
            _calculator = new CalVerCalculator();
        }

        #region Format Validation Tests

        [Theory]
        [InlineData("YYYY.MM.PATCH", true)]
        [InlineData("YY.0M.PATCH", true)]
        [InlineData("YYYY.WW.PATCH", true)]
        [InlineData("YYYY.0M.PATCH", true)]
        [InlineData("INVALID.FORMAT", false)]
        [InlineData("YY.MM", false)]
        [InlineData("", false)]
        public void CalVerConfig_IsValidFormat_ValidatesCorrectly(string format, bool expectedValid)
        {
            // Arrange
            var config = new CalVerConfig { Format = format };

            // Act
            var result = config.IsValidFormat();

            // Assert
            Assert.Equal(expectedValid, result);
        }

        #endregion

        #region YYYY.MM.PATCH Format Tests

        [Fact]
        public void CalculateVersion_YYYY_MM_PATCH_Format_GeneratesCorrectVersion()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YYYY_MM_PATCH_Format_SameMonth_IncrementsPatc()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 11, 20);
            var existingVersion = new SemVer { Major = 2025, Minor = 11, Patch = 3 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(4, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YYYY_MM_PATCH_Format_NewMonth_ResetsPatch()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", ResetPatchPeriodically = true };
            var date = new DateTime(2025, 11, 20);
            var existingVersion = new SemVer { Major = 2025, Minor = 10, Patch = 5 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YYYY_MM_PATCH_Format_NewMonth_NoReset_ContinuesPatch()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", ResetPatchPeriodically = false };
            var date = new DateTime(2025, 11, 20);
            var existingVersion = new SemVer { Major = 2025, Minor = 10, Patch = 5 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(6, result.Patch);
        }

        #endregion

        #region YY.0M.PATCH Format Tests

        [Fact]
        public void CalculateVersion_YY_0M_PATCH_Format_GeneratesCorrectVersion()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YY.0M.PATCH" };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(25, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YY_0M_PATCH_Format_January_GeneratesCorrectVersion()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YY.0M.PATCH" };
            var date = new DateTime(2025, 1, 15);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(25, result.Major);
            Assert.Equal(1, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        #endregion

        #region YYYY.WW.PATCH Format Tests

        [Fact]
        public void CalculateVersion_YYYY_WW_PATCH_Format_GeneratesCorrectVersion()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.WW.PATCH" };
            var date = new DateTime(2025, 11, 20); // Week 47 in 2025

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.True(result.Minor >= 1 && result.Minor <= 53); // Week number should be valid
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YYYY_WW_PATCH_Format_SameWeek_IncrementsPatc()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.WW.PATCH" };
            var date = new DateTime(2025, 11, 20);
            var weekNumber = GetIsoWeekNumber(date);
            var existingVersion = new SemVer { Major = 2025, Minor = weekNumber, Patch = 2 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(weekNumber, result.Minor);
            Assert.Equal(3, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YYYY_WW_PATCH_Format_NewWeek_ResetsPatch()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.WW.PATCH", ResetPatchPeriodically = true };
            var date = new DateTime(2025, 11, 20);
            var weekNumber = GetIsoWeekNumber(date);
            var existingVersion = new SemVer { Major = 2025, Minor = weekNumber - 1, Patch = 7 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(weekNumber, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        #endregion

        #region YYYY.0M.PATCH Format Tests

        [Fact]
        public void CalculateVersion_YYYY_0M_PATCH_Format_GeneratesCorrectVersion()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.0M.PATCH" };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(11, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        #endregion

        #region Year Transition Tests

        [Fact]
        public void CalculateVersion_YearTransition_HandlesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", ResetPatchPeriodically = true };
            var date = new DateTime(2026, 1, 5);
            var existingVersion = new SemVer { Major = 2025, Minor = 12, Patch = 10 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2026, result.Major);
            Assert.Equal(1, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_YY_Format_YearTransition_HandlesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YY.0M.PATCH", ResetPatchPeriodically = true };
            var date = new DateTime(2026, 1, 5);
            var existingVersion = new SemVer { Major = 25, Minor = 12, Patch = 10 };

            // Act
            var result = _calculator.CalculateVersion(config, date, existingVersion);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(26, result.Major);
            Assert.Equal(1, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        #endregion

        #region Parse CalVer Tests

        [Theory]
        [InlineData("2025.11.0", 2025, 11, 0)]
        [InlineData("2025.11.5", 2025, 11, 5)]
        [InlineData("25.11.0", 25, 11, 0)]
        [InlineData("2025.47.3", 2025, 47, 3)]
        [InlineData("v2025.11.0", 2025, 11, 0)]
        public void ParseCalVer_ValidVersions_ParsesCorrectly(string versionString, int expectedMajor, int expectedMinor, int expectedPatch)
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };

            // Act
            var result = _calculator.ParseCalVer(versionString, config);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedMajor, result.Major);
            Assert.Equal(expectedMinor, result.Minor);
            Assert.Equal(expectedPatch, result.Patch);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ParseCalVer_NullOrEmpty_ReturnsNull(string versionString)
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };

            // Act
            var result = _calculator.ParseCalVer(versionString, config);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Format CalVer Tests

        [Fact]
        public void FormatCalVer_DefaultSeparator_FormatsCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", Separator = "." };
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5 };

            // Act
            var result = _calculator.FormatCalVer(version, config);

            // Assert
            Assert.Equal("2025.11.5", result);
        }

        [Fact]
        public void FormatCalVer_WithPrerelease_FormatsCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", Separator = "." };
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5, PreRelease = "alpha" };

            // Act
            var result = _calculator.FormatCalVer(version, config);

            // Assert
            Assert.Equal("2025.11.5-alpha", result);
        }

        [Fact]
        public void FormatCalVer_WithBuildMetadata_FormatsCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", Separator = "." };
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5, BuildMetadata = "feature-123" };

            // Act
            var result = _calculator.FormatCalVer(version, config);

            // Assert
            Assert.Equal("2025.11.5+feature-123", result);
        }

        [Fact]
        public void FormatCalVer_WithPrereleaseAndBuildMetadata_FormatsCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", Separator = "." };
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5, PreRelease = "beta", BuildMetadata = "feature-123" };

            // Act
            var result = _calculator.FormatCalVer(version, config);

            // Assert
            Assert.Equal("2025.11.5-beta+feature-123", result);
        }

        [Fact]
        public void FormatCalVer_CustomSeparator_FormatsCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH", Separator = "-" };
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5 };

            // Act
            var result = _calculator.FormatCalVer(version, config);

            // Assert
            Assert.Equal("2025-11-5", result);
        }

        [Fact]
        public void FormatCalVer_NullVersion_ThrowsArgumentNullException()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _calculator.FormatCalVer(null, config));
        }

        [Fact]
        public void FormatCalVer_NullConfig_ThrowsArgumentNullException()
        {
            // Arrange
            var version = new SemVer { Major = 2025, Minor = 11, Patch = 5 };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _calculator.FormatCalVer(version, null));
        }

        #endregion

        #region ShouldIncrementVersion Tests

        [Fact]
        public void ShouldIncrementVersion_NullCurrentVersion_ReturnsTrue()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.ShouldIncrementVersion(null, config, date);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldIncrementVersion_NullConfig_ReturnsTrue()
        {
            // Arrange
            var currentVersion = new SemVer { Major = 2025, Minor = 11, Patch = 0 };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.ShouldIncrementVersion(currentVersion, null, date);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldIncrementVersion_SameMonth_ReturnsFalse()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var currentVersion = new SemVer { Major = 2025, Minor = 11, Patch = 5 };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.ShouldIncrementVersion(currentVersion, config, date);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ShouldIncrementVersion_DifferentMonth_ReturnsTrue()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var currentVersion = new SemVer { Major = 2025, Minor = 10, Patch = 5 };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.ShouldIncrementVersion(currentVersion, config, date);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldIncrementVersion_DifferentYear_ReturnsTrue()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var currentVersion = new SemVer { Major = 2024, Minor = 12, Patch = 5 };
            var date = new DateTime(2025, 1, 5);

            // Act
            var result = _calculator.ShouldIncrementVersion(currentVersion, config, date);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void CalculateVersion_NullConfig_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _calculator.CalculateVersion(null, DateTime.UtcNow));
        }

        [Fact]
        public void CalculateVersion_InvalidFormat_ThrowsArgumentException()
        {
            // Arrange
            var config = new CalVerConfig { Format = "INVALID" };
            var date = new DateTime(2025, 11, 20);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _calculator.CalculateVersion(config, date));
        }

        #endregion

        #region Start Date Tests

        [Fact]
        public void CalVerConfig_GetStartDate_ValidDate_ParsesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { StartDate = "2025-01-01" };

            // Act
            var result = config.GetStartDate();

            // Assert
            Assert.Equal(new DateTime(2025, 1, 1), result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid-date")]
        public void CalVerConfig_GetStartDate_InvalidDate_ReturnsMinValue(string startDate)
        {
            // Arrange
            var config = new CalVerConfig { StartDate = startDate };

            // Act
            var result = config.GetStartDate();

            // Assert
            Assert.Equal(DateTime.MinValue, result);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void CalculateVersion_NoExistingVersion_StartsAtZero()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 11, 20);

            // Act
            var result = _calculator.CalculateVersion(config, date, null);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_December_HandlesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 12, 31);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(12, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_January_HandlesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2025, 1, 1);

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2025, result.Major);
            Assert.Equal(1, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        [Fact]
        public void CalculateVersion_LeapYear_February_HandlesCorrectly()
        {
            // Arrange
            var config = new CalVerConfig { Format = "YYYY.MM.PATCH" };
            var date = new DateTime(2024, 2, 29); // Leap year

            // Act
            var result = _calculator.CalculateVersion(config, date);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2024, result.Major);
            Assert.Equal(2, result.Minor);
            Assert.Equal(0, result.Patch);
        }

        #endregion

        #region Helper Methods

        private int GetIsoWeekNumber(DateTime date)
        {
            // ISO 8601 week date system
            var calendar = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            var dayOfWeek = calendar.GetDayOfWeek(date);

            // ISO 8601: Week starts on Monday
            if (dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Wednesday)
            {
                date = date.AddDays(3);
            }

            // Get week number
            return calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        #endregion
    }
}
