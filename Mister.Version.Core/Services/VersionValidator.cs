using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for validating version constraints and rules
    /// </summary>
    public class VersionValidator : IVersionValidator
    {
        private readonly ILogger<VersionValidator> _logger;

        public VersionValidator(ILogger<VersionValidator> logger = null)
        {
            _logger = logger ?? LoggerFactory.CreateLogger<VersionValidator>();
        }

        /// <inheritdoc/>
        public ValidationResult ValidateVersion(
            string version,
            string previousVersion,
            VersionConstraints constraints,
            VersionBumpType bumpType,
            bool majorApproved = false)
        {
            if (constraints == null || !constraints.Enabled)
            {
                return ValidationResult.Success();
            }

            var result = new ValidationResult { IsValid = true };

            // Parse versions
            SemVer currentSemVer = version;
            SemVer previousSemVer = previousVersion;

            if (currentSemVer == null)
            {
                return ValidationResult.Failure(
                    $"Invalid version format: {version}",
                    "Version must be a valid semantic version (e.g., 1.2.3, 2.0.0-alpha.1)");
            }

            // Check if version is blocked
            if (IsVersionBlocked(version, constraints.BlockedVersions))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Message = $"Version {version} is blocked and cannot be used",
                    ConstraintName = "BlockedVersions",
                    Actual = version,
                    Expected = "Any version not in blocked list"
                });
            }

            // Check minimum version constraint
            if (!string.IsNullOrEmpty(constraints.MinimumVersion))
            {
                SemVer minVersion = constraints.MinimumVersion;
                if (minVersion != null && currentSemVer < minVersion)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Message = $"Version {version} is below minimum allowed version",
                        ConstraintName = "MinimumVersion",
                        Expected = constraints.MinimumVersion,
                        Actual = version
                    });
                }
            }

            // Check maximum version constraint
            if (!string.IsNullOrEmpty(constraints.MaximumVersion))
            {
                SemVer maxVersion = constraints.MaximumVersion;
                if (maxVersion != null && currentSemVer > maxVersion)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Message = $"Version {version} exceeds maximum allowed version",
                        ConstraintName = "MaximumVersion",
                        Expected = constraints.MaximumVersion,
                        Actual = version
                    });
                }
            }

            // Check allowed range
            if (!string.IsNullOrEmpty(constraints.AllowedRange))
            {
                if (!IsVersionInRange(version, constraints.AllowedRange))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Message = $"Version {version} is outside allowed range",
                        ConstraintName = "AllowedRange",
                        Expected = constraints.AllowedRange,
                        Actual = version
                    });
                }
            }

            // Check monotonic increase
            if (constraints.RequireMonotonicIncrease && previousSemVer != null)
            {
                if (currentSemVer <= previousSemVer)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Message = $"Version {version} is not greater than previous version {previousVersion}",
                        ConstraintName = "RequireMonotonicIncrease",
                        Expected = $"Version > {previousVersion}",
                        Actual = version
                    });
                }
            }

            // Check major version approval requirement
            if (constraints.RequireMajorApproval && bumpType == VersionBumpType.Major && !majorApproved)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Message = "Major version bump requires explicit approval",
                    ConstraintName = "RequireMajorApproval",
                    Details = "Set major approval flag to true or disable RequireMajorApproval constraint"
                });
            }

            // Validate custom rules
            if (constraints.CustomRules != null && constraints.CustomRules.Any())
            {
                var customResult = ValidateCustomRules(version, constraints.CustomRules);
                result.Errors.AddRange(customResult.Errors);
                result.Warnings.AddRange(customResult.Warnings);
                if (!customResult.IsValid)
                {
                    result.IsValid = false;
                }
            }

            // Set summary
            if (result.IsValid)
            {
                result.Summary = "All validation checks passed";
            }
            else
            {
                result.Summary = $"Validation failed with {result.Errors.Count} error(s)";
            }

            if (result.Warnings.Any())
            {
                result.Summary += $" and {result.Warnings.Count} warning(s)";
            }

            return result;
        }

        /// <inheritdoc/>
        public ValidationResult ValidateVersionResult(
            VersionResult versionResult,
            VersionConstraints constraints,
            bool majorApproved = false)
        {
            if (versionResult == null)
            {
                return ValidationResult.Failure("VersionResult cannot be null");
            }

            var version = versionResult.CalculatedVersion;
            var previousVersion = versionResult.PreviousVersion;
            var bumpType = versionResult.BumpType;

            return ValidateVersion(version, previousVersion, constraints, bumpType, majorApproved);
        }

        /// <inheritdoc/>
        public ValidationResult ValidateDependencyVersions(
            Dictionary<string, string> projectVersions,
            VersionConstraints constraints)
        {
            if (!constraints.ValidateDependencyVersions)
            {
                return ValidationResult.Success();
            }

            var result = new ValidationResult { IsValid = true };

            // Check for version consistency across dependencies
            // This is a simplified implementation - could be extended to check
            // for actual dependency compatibility based on project references
            var versionGroups = projectVersions
                .GroupBy(kvp => kvp.Value)
                .Select(g => new { Version = g.Key, Projects = g.Select(p => p.Key).ToList() })
                .ToList();

            // Log info about version distribution
            _logger.LogDebug($"Found {versionGroups.Count} distinct versions across {projectVersions.Count} projects");

            // Check for major version conflicts
            var majorVersionGroups = projectVersions
                .Select(kvp => new { Project = kvp.Key, Version = kvp.Value, SemVer = (SemVer)kvp.Value })
                .Where(p => p.SemVer != null)
                .GroupBy(p => p.SemVer.Major)
                .ToList();

            if (majorVersionGroups.Count > 1)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Message = "Multiple major versions detected across projects",
                    Details = $"Found {majorVersionGroups.Count} different major versions: " +
                             string.Join(", ", majorVersionGroups.Select(g => $"v{g.Key}.x.x ({g.Count()} projects)"))
                });
            }

            result.Summary = result.Warnings.Any()
                ? $"Dependency validation completed with {result.Warnings.Count} warning(s)"
                : "All dependency versions are valid";

            return result;
        }

        /// <inheritdoc/>
        public bool IsVersionInRange(string version, string rangePattern)
        {
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(rangePattern))
            {
                return false;
            }

            SemVer semVer = version;
            if (semVer == null)
            {
                return false;
            }

            // Parse range pattern (e.g., "3.x.x", "2.1.x", "1.2.3")
            var parts = rangePattern.Split('.');
            if (parts.Length != 3)
            {
                _logger.LogWarning($"Invalid range pattern: {rangePattern}. Expected format: major.minor.patch or wildcards (x)");
                return false;
            }

            // Check major version
            if (parts[0] != "x" && parts[0] != "*")
            {
                if (!int.TryParse(parts[0], out int major) || semVer.Major != major)
                {
                    return false;
                }
            }

            // Check minor version
            if (parts[1] != "x" && parts[1] != "*")
            {
                if (!int.TryParse(parts[1], out int minor) || semVer.Minor != minor)
                {
                    return false;
                }
            }

            // Check patch version
            if (parts[2] != "x" && parts[2] != "*")
            {
                if (!int.TryParse(parts[2], out int patch) || semVer.Patch != patch)
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public bool IsVersionBlocked(string version, List<string> blockedVersions)
        {
            if (string.IsNullOrEmpty(version) || blockedVersions == null || !blockedVersions.Any())
            {
                return false;
            }

            return blockedVersions.Contains(version, StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ValidationResult ValidateCustomRules(
            string version,
            Dictionary<string, ValidationRule> customRules)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var ruleEntry in customRules)
            {
                var ruleName = ruleEntry.Key;
                var rule = ruleEntry.Value;

                try
                {
                    switch (rule.Type)
                    {
                        case ValidationRuleType.Pattern:
                            ValidatePatternRule(version, rule, result);
                            break;

                        case ValidationRuleType.Range:
                            ValidateRangeRule(version, rule, result);
                            break;

                        case ValidationRuleType.Custom:
                            _logger.LogWarning($"Custom rule type '{ruleName}' requires custom implementation");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error validating custom rule '{ruleName}'");
                    result.Warnings.Add(new ValidationWarning
                    {
                        Message = $"Failed to validate custom rule '{ruleName}'",
                        Details = ex.Message,
                        RuleName = ruleName
                    });
                }
            }

            return result;
        }

        private void ValidatePatternRule(string version, ValidationRule rule, ValidationResult result)
        {
            if (!rule.Config.TryGetValue("pattern", out string pattern))
            {
                _logger.LogWarning($"Pattern rule '{rule.Name}' missing 'pattern' config");
                return;
            }

            try
            {
                var regex = new Regex(pattern);
                if (!regex.IsMatch(version))
                {
                    var error = new ValidationError
                    {
                        Message = rule.Description ?? $"Version does not match required pattern",
                        ConstraintName = rule.Name,
                        Expected = pattern,
                        Actual = version
                    };

                    if (rule.Severity == ValidationSeverity.Error)
                    {
                        result.IsValid = false;
                        result.Errors.Add(error);
                    }
                    else if (rule.Severity == ValidationSeverity.Warning)
                    {
                        result.Warnings.Add(new ValidationWarning
                        {
                            Message = error.Message,
                            Details = $"Expected pattern: {pattern}, Actual: {version}",
                            RuleName = rule.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Invalid regex pattern in rule '{rule.Name}': {pattern}");
            }
        }

        private void ValidateRangeRule(string version, ValidationRule rule, ValidationResult result)
        {
            if (!rule.Config.TryGetValue("min", out string min) &&
                !rule.Config.TryGetValue("max", out string max))
            {
                _logger.LogWarning($"Range rule '{rule.Name}' missing 'min' or 'max' config");
                return;
            }

            SemVer semVer = version;
            if (semVer == null)
            {
                return;
            }

            bool valid = true;
            string expectedRange = "";

            if (!string.IsNullOrEmpty(min))
            {
                SemVer minVersion = min;
                if (minVersion != null && semVer < minVersion)
                {
                    valid = false;
                    expectedRange = $">= {min}";
                }
            }

            if (!string.IsNullOrEmpty(max))
            {
                SemVer maxVersion = max;
                if (maxVersion != null && semVer > maxVersion)
                {
                    valid = false;
                    expectedRange = string.IsNullOrEmpty(expectedRange) ? $"<= {max}" : $"{expectedRange} and <= {max}";
                }
            }

            if (!valid)
            {
                var error = new ValidationError
                {
                    Message = rule.Description ?? $"Version is outside allowed range",
                    ConstraintName = rule.Name,
                    Expected = expectedRange,
                    Actual = version
                };

                if (rule.Severity == ValidationSeverity.Error)
                {
                    result.IsValid = false;
                    result.Errors.Add(error);
                }
                else if (rule.Severity == ValidationSeverity.Warning)
                {
                    result.Warnings.Add(new ValidationWarning
                    {
                        Message = error.Message,
                        Details = $"Expected: {expectedRange}, Actual: {version}",
                        RuleName = rule.Name
                    });
                }
            }
        }
    }
}
