using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services;

/// <summary>
/// Calculator for Calendar Versioning (CalVer)
/// </summary>
public class CalVerCalculator : ICalVerCalculator
{
    /// <summary>
    /// Calculates a CalVer version based on the current date and configuration
    /// </summary>
    public SemVer CalculateVersion(CalVerConfig config, DateTime? date = null, SemVer existingVersion = null)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (!config.IsValidFormat())
            throw new ArgumentException($"Invalid CalVer format: {config.Format}", nameof(config));

        var calculationDate = date ?? DateTime.UtcNow;
        var version = new SemVer();

        // Parse the format and set major/minor based on date
        var format = config.Format.ToUpperInvariant();

        if (format.StartsWith("YYYY.MM") || format.StartsWith("YYYY.0M"))
        {
            // Full year, month format: 2025.11.PATCH
            version.Major = calculationDate.Year;
            version.Minor = calculationDate.Month;
        }
        else if (format.StartsWith("YY.0M"))
        {
            // Short year, month format: 25.11.PATCH
            version.Major = calculationDate.Year % 100;
            version.Minor = calculationDate.Month;
        }
        else if (format.StartsWith("YYYY.WW"))
        {
            // Week-based format: 2025.47.PATCH. Major must be the ISO week-numbering year, not the calendar
            // year: 2024-12-30 is week 1 of 2025 and 2027-01-01 is week 53 of 2026; pairing the calendar year
            // with the ISO week made versions go backwards around New Year.
            GetIsoWeek(calculationDate, out var isoYear, out var isoWeek);
            version.Major = isoYear;
            version.Minor = isoWeek;
        }
        else
        {
            throw new ArgumentException($"Unsupported CalVer format: {config.Format}", nameof(config));
        }

        // Determine patch version
        if (existingVersion != null)
        {
            // Check if we're in the same period (month/week) as existing version
            if (version.Major == existingVersion.Major && version.Minor == existingVersion.Minor)
            {
                // Same period, keep the same patch (will be incremented by VersionCalculator if there are changes)
                version.Patch = existingVersion.Patch;
            }
            else if (config.ResetPatchPeriodically)
            {
                // New period, reset patch to 0
                version.Patch = 0;
            }
            else
            {
                // New period but don't reset, continue from existing
                version.Patch = existingVersion.Patch + 1;
            }
        }
        else
        {
            // No existing version, start at patch 0
            version.Patch = 0;
        }

        return version;
    }

    /// <summary>
    /// Parses a CalVer version string into a SemVer object
    /// </summary>
    public SemVer ParseCalVer(string versionString, CalVerConfig config)
    {
        if (string.IsNullOrEmpty(versionString))
            return null;

        // Remove 'v' prefix if present
        versionString = versionString.TrimStart('v', 'V');

        // Use SemVer's implicit conversion for parsing
        // CalVer versions are structurally the same as SemVer (major.minor.patch)
        return (SemVer)versionString;
    }

    /// <summary>
    /// Formats a SemVer object as a CalVer string according to configuration
    /// </summary>
    public string FormatCalVer(SemVer version, CalVerConfig config)
    {
        if (version == null)
            throw new ArgumentNullException(nameof(version));

        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var separator = config.Separator ?? ".";
        var result = $"{version.Major}{separator}{version.Minor:D2}{separator}{version.Patch}";

        // Add prerelease and build metadata if present
        if (!string.IsNullOrEmpty(version.PreRelease))
            result += $"-{version.PreRelease}";

        if (!string.IsNullOrEmpty(version.BuildMetadata))
            result += $"+{version.BuildMetadata}";

        return result;
    }

    /// <summary>
    /// Determines if a version should be incremented based on date change
    /// </summary>
    public bool ShouldIncrementVersion(SemVer currentVersion, CalVerConfig config, DateTime date)
    {
        if (currentVersion == null || config == null)
            return true;

        var newVersion = CalculateVersion(config, date, null);

        // Should increment if the period (major.minor) has changed
        return currentVersion.Major != newVersion.Major || currentVersion.Minor != newVersion.Minor;
    }

    /// <summary>
    /// ISO 8601 week-numbering year and week (weeks start on Monday; week 1 contains the year's first Thursday).
    /// </summary>
    internal static void GetIsoWeek(DateTime date, out int isoYear, out int isoWeek)
    {
        // The Thursday of this ISO week determines its year
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        var thursday = date.Date.AddDays(3 - daysFromMonday);
        isoYear = thursday.Year;
        isoWeek = (thursday.DayOfYear - 1) / 7 + 1;
    }
}
