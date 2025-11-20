using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Configuration for version validation and constraints
/// </summary>
public class VersionConstraints
{
    /// <summary>
    /// Minimum version that cannot be violated.
    /// Any calculated version below this will fail validation.
    /// Example: "2.0.0"
    /// </summary>
    public string MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version that cannot be exceeded.
    /// Any calculated version above this will fail validation.
    /// Example: "5.0.0"
    /// </summary>
    public string MaximumVersion { get; set; }

    /// <summary>
    /// Allowed version range pattern.
    /// Supports wildcards: "3.x.x", "2.1.x"
    /// Any calculated version outside this range will fail validation.
    /// </summary>
    public string AllowedRange { get; set; }

    /// <summary>
    /// Whether to validate that dependency versions are compatible with each other.
    /// When enabled, checks that project dependencies don't have conflicting version requirements.
    /// Default: false
    /// </summary>
    public bool ValidateDependencyVersions { get; set; } = false;

    /// <summary>
    /// Require explicit approval/flag for major version bumps.
    /// When enabled, major version bumps will fail validation unless explicitly approved.
    /// Useful to prevent accidental breaking changes.
    /// Default: false
    /// </summary>
    public bool RequireMajorApproval { get; set; } = false;

    /// <summary>
    /// Specific versions that are blocked/forbidden.
    /// Useful for skipping versions due to bugs or other issues.
    /// Example: ["1.2.3", "2.0.0"]
    /// </summary>
    public List<string> BlockedVersions { get; set; } = new List<string>();

    /// <summary>
    /// Require that versions are monotonically increasing.
    /// When enabled, ensures new version is always greater than the previous tag.
    /// Default: true
    /// </summary>
    public bool RequireMonotonicIncrease { get; set; } = true;

    /// <summary>
    /// Custom validation rules that can be defined per-project or globally.
    /// Key: rule name, Value: rule configuration
    /// </summary>
    public Dictionary<string, ValidationRule> CustomRules { get; set; } = new Dictionary<string, ValidationRule>();

    /// <summary>
    /// Whether validation is enabled.
    /// When disabled, all constraints are ignored.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Custom validation rule configuration
/// </summary>
public class ValidationRule
{
    /// <summary>
    /// Name of the rule
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Description of what the rule validates
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Rule type
    /// </summary>
    public ValidationRuleType Type { get; set; }

    /// <summary>
    /// Rule-specific configuration (pattern, value, etc.)
    /// </summary>
    public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Whether this rule causes a hard failure or just a warning
    /// </summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
}

/// <summary>
/// Types of validation rules
/// </summary>
public enum ValidationRuleType
{
    /// <summary>
    /// Pattern-based validation (regex)
    /// </summary>
    Pattern,

    /// <summary>
    /// Range-based validation
    /// </summary>
    Range,

    /// <summary>
    /// Custom validation logic
    /// </summary>
    Custom
}

/// <summary>
/// Severity of validation failures
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Information only, does not fail validation
    /// </summary>
    Info,

    /// <summary>
    /// Warning, does not fail validation but logs a warning
    /// </summary>
    Warning,

    /// <summary>
    /// Error, fails validation
    /// </summary>
    Error
}

/// <summary>
/// Result of version validation
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation errors
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new List<ValidationError>();

    /// <summary>
    /// List of validation warnings
    /// </summary>
    public List<ValidationWarning> Warnings { get; set; } = new List<ValidationWarning>();

    /// <summary>
    /// Summary message
    /// </summary>
    public string Summary { get; set; }

    /// <summary>
    /// Create a successful validation result
    /// </summary>
    public static ValidationResult Success()
    {
        return new ValidationResult
        {
            IsValid = true,
            Summary = "All validation checks passed"
        };
    }

    /// <summary>
    /// Create a failed validation result with an error
    /// </summary>
    public static ValidationResult Failure(string error, string details = null)
    {
        return new ValidationResult
        {
            IsValid = false,
            Summary = error,
            Errors = new List<ValidationError>
            {
                new ValidationError { Message = error, Details = details }
            }
        };
    }
}

/// <summary>
/// Validation error details
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Additional error details
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// Constraint that was violated
    /// </summary>
    public string ConstraintName { get; set; }

    /// <summary>
    /// Expected value or range
    /// </summary>
    public string Expected { get; set; }

    /// <summary>
    /// Actual value that caused the violation
    /// </summary>
    public string Actual { get; set; }
}

/// <summary>
/// Validation warning details
/// </summary>
public class ValidationWarning
{
    /// <summary>
    /// Warning message
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Additional warning details
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// Rule that generated the warning
    /// </summary>
    public string RuleName { get; set; }
}
