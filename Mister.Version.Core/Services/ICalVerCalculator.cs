using System;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services;

/// <summary>
/// Interface for calculating Calendar Versions (CalVer)
/// </summary>
public interface ICalVerCalculator
{
    /// <summary>
    /// Calculates a CalVer version based on the current date and configuration
    /// </summary>
    /// <param name="config">CalVer configuration</param>
    /// <param name="date">Date to use for calculation (defaults to current date)</param>
    /// <param name="existingVersion">Existing version to increment from (optional)</param>
    /// <returns>A SemVer object representing the CalVer version</returns>
    SemVer CalculateVersion(CalVerConfig config, DateTime? date = null, SemVer existingVersion = null);

    /// <summary>
    /// Parses a CalVer version string into a SemVer object
    /// </summary>
    /// <param name="versionString">Version string to parse</param>
    /// <param name="config">CalVer configuration for format information</param>
    /// <returns>Parsed SemVer object</returns>
    SemVer ParseCalVer(string versionString, CalVerConfig config);

    /// <summary>
    /// Formats a SemVer object as a CalVer string according to configuration
    /// </summary>
    /// <param name="version">SemVer object to format</param>
    /// <param name="config">CalVer configuration</param>
    /// <returns>Formatted CalVer string</returns>
    string FormatCalVer(SemVer version, CalVerConfig config);

    /// <summary>
    /// Determines if a version should be incremented based on date change
    /// </summary>
    /// <param name="currentVersion">Current version</param>
    /// <param name="config">CalVer configuration</param>
    /// <param name="date">Date to check against</param>
    /// <returns>True if version should be incremented</returns>
    bool ShouldIncrementVersion(SemVer currentVersion, CalVerConfig config, DateTime date);
}
