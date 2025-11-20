using System.Collections.Generic;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for validating version constraints and rules
    /// </summary>
    public interface IVersionValidator
    {
        /// <summary>
        /// Validate a calculated version against configured constraints
        /// </summary>
        /// <param name="version">Version to validate</param>
        /// <param name="previousVersion">Previous version (for monotonic increase checks)</param>
        /// <param name="constraints">Validation constraints</param>
        /// <param name="bumpType">Type of version bump being performed</param>
        /// <param name="majorApproved">Whether major version bump has been explicitly approved</param>
        /// <returns>Validation result</returns>
        ValidationResult ValidateVersion(
            string version,
            string previousVersion,
            VersionConstraints constraints,
            VersionBumpType bumpType,
            bool majorApproved = false);

        /// <summary>
        /// Validate a version result against configured constraints
        /// </summary>
        /// <param name="versionResult">Version result to validate</param>
        /// <param name="constraints">Validation constraints</param>
        /// <param name="majorApproved">Whether major version bump has been explicitly approved</param>
        /// <returns>Validation result</returns>
        ValidationResult ValidateVersionResult(
            VersionResult versionResult,
            VersionConstraints constraints,
            bool majorApproved = false);

        /// <summary>
        /// Validate dependency versions for compatibility
        /// </summary>
        /// <param name="projectVersions">Dictionary of project names to their versions</param>
        /// <param name="constraints">Validation constraints</param>
        /// <returns>Validation result</returns>
        ValidationResult ValidateDependencyVersions(
            Dictionary<string, string> projectVersions,
            VersionConstraints constraints);

        /// <summary>
        /// Check if a version is within the allowed range
        /// </summary>
        /// <param name="version">Version to check</param>
        /// <param name="rangePattern">Range pattern (e.g., "3.x.x", "2.1.x")</param>
        /// <returns>True if version is in range</returns>
        bool IsVersionInRange(string version, string rangePattern);

        /// <summary>
        /// Check if a version is blocked/forbidden
        /// </summary>
        /// <param name="version">Version to check</param>
        /// <param name="blockedVersions">List of blocked versions</param>
        /// <returns>True if version is blocked</returns>
        bool IsVersionBlocked(string version, List<string> blockedVersions);

        /// <summary>
        /// Validate custom rules
        /// </summary>
        /// <param name="version">Version to validate</param>
        /// <param name="customRules">Custom validation rules</param>
        /// <returns>Validation result</returns>
        ValidationResult ValidateCustomRules(
            string version,
            Dictionary<string, ValidationRule> customRules);
    }
}
