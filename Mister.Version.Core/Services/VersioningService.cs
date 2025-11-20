using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// High-level service that coordinates versioning workflow
    /// </summary>
    public class VersioningService : IDisposable
    {
        private readonly IGitService _gitService;
        private readonly VersionCalculator _versionCalculator;
        private readonly Action<string, string> _logger;
        private readonly VersionCache _cache;
        private bool _disposed = false;

        public VersioningService(string repoRoot, Action<string, string> logger)
            : this(repoRoot, logger, null)
        {
        }

        public VersioningService(string repoRoot, Action<string, string> logger, VersionCache cache)
        {
            _logger = logger ?? ((level, message) => { });
            _cache = cache;

            // Initialize GitService with cache
            if (_cache != null)
            {
                _gitService = new GitService(repoRoot, _cache);
            }
            else
            {
                _gitService = new GitService(repoRoot);
            }

            _versionCalculator = new VersionCalculator(_gitService, _logger);
        }

        /// <summary>
        /// Calculates version for a single project with configuration loading
        /// </summary>
        /// <param name="request">Version calculation request</param>
        /// <returns>Version calculation result</returns>
        public VersioningResult CalculateProjectVersion(VersioningRequest request)
        {
            try
            {
                _logger("Debug", $"Starting version calculation for: {request.ProjectPath}");

                // Load configuration
                var config = ConfigurationService.LoadConfiguration(
                    request.ConfigFile, 
                    request.RepoRoot, 
                    _logger);

                // Create base configuration values
                var baseValues = new ConfigurationOverrides
                {
                    BaseVersion = null,
                    DefaultIncrement = "patch",
                    PrereleaseType = request.PrereleaseType,
                    TagPrefix = request.TagPrefix,
                    SkipTestProjects = request.SkipTestProjects,
                    SkipNonPackableProjects = request.SkipNonPackableProjects,
                    ForceVersion = request.ForceVersion
                };

                // Apply configuration overrides
                var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
                var configOverrides = ConfigurationService.ApplyConfiguration(
                    config, 
                    projectName, 
                    baseValues, 
                    _logger);

                // Create conventional commits configuration
                ConventionalCommitConfig conventionalCommitsConfig = null;

                // First priority: YAML configuration
                if (config?.CommitConventions != null)
                {
                    conventionalCommitsConfig = config.CommitConventions;
                }
                // Second priority: MSBuild properties from request
                else if (request.ConventionalCommitsEnabled)
                {
                    conventionalCommitsConfig = new ConventionalCommitConfig
                    {
                        Enabled = request.ConventionalCommitsEnabled,
                        MajorPatterns = ParsePatternString(request.MajorPatterns),
                        MinorPatterns = ParsePatternString(request.MinorPatterns),
                        PatchPatterns = ParsePatternString(request.PatchPatterns),
                        IgnorePatterns = ParsePatternString(request.IgnorePatterns)
                    };
                }

                // Create change detection configuration
                ChangeDetectionConfig changeDetectionConfig = null;

                // First priority: YAML configuration
                if (config?.ChangeDetection != null)
                {
                    changeDetectionConfig = config.ChangeDetection;
                }
                // Second priority: MSBuild properties from request
                else if (request.ChangeDetectionEnabled)
                {
                    changeDetectionConfig = new ChangeDetectionConfig
                    {
                        Enabled = request.ChangeDetectionEnabled,
                        IgnorePatterns = ParsePatternString(request.IgnoreFilePatterns),
                        MajorPatterns = ParsePatternString(request.MajorFilePatterns),
                        MinorPatterns = ParsePatternString(request.MinorFilePatterns),
                        PatchPatterns = ParsePatternString(request.PatchFilePatterns),
                        SourceOnlyMode = request.SourceOnlyMode,
                        AdditionalMonitorPaths = ParsePatternString(request.AdditionalMonitorPaths)
                    };
                }

                // Merge project-specific AdditionalMonitorPaths from YAML if available
                if (config?.Projects != null &&
                    !string.IsNullOrEmpty(projectName) &&
                    config.Projects.TryGetValue(projectName, out var projectConfig) &&
                    projectConfig.AdditionalMonitorPaths != null &&
                    projectConfig.AdditionalMonitorPaths.Count > 0)
                {
                    // If we already have a change detection config, merge the paths
                    if (changeDetectionConfig != null)
                    {
                        var mergedPaths = new List<string>(changeDetectionConfig.AdditionalMonitorPaths ?? new List<string>());
                        mergedPaths.AddRange(projectConfig.AdditionalMonitorPaths);
                        changeDetectionConfig.AdditionalMonitorPaths = mergedPaths.Distinct().ToList();
                        _logger("Info", $"Merged project-specific additional monitor paths for {projectName}");
                    }
                    else
                    {
                        // Create a minimal config just for the additional paths
                        changeDetectionConfig = new ChangeDetectionConfig
                        {
                            Enabled = true,
                            AdditionalMonitorPaths = projectConfig.AdditionalMonitorPaths
                        };
                        _logger("Info", $"Applied project-specific additional monitor paths for {projectName}");
                    }
                }

                // Create git integration configuration
                GitIntegrationConfig gitIntegrationConfig = null;

                // First priority: YAML configuration
                if (config?.GitIntegration != null)
                {
                    gitIntegrationConfig = config.GitIntegration;
                }
                // Second priority: MSBuild properties from request (always create config with defaults)
                else
                {
                    gitIntegrationConfig = new GitIntegrationConfig
                    {
                        ShallowCloneSupport = request.ShallowCloneSupport,
                        ShallowCloneFallbackVersion = request.ShallowCloneFallbackVersion,
                        SubmoduleSupport = request.SubmoduleSupport,
                        CustomTagPatterns = ParsePatternString(request.CustomTagPatterns),
                        BranchBasedVersioning = request.BranchBasedVersioning,
                        BranchVersioningRules = ParsePatternString(request.BranchVersioningRules),
                        IncludeBranchInMetadata = request.IncludeBranchInMetadata,
                        ValidateTagAncestry = request.ValidateTagAncestry,
                        FetchDepth = request.FetchDepth
                    };
                }

                // Load version policy configuration (YAML only for now)
                VersionPolicyConfig versionPolicyConfig = null;
                if (config?.VersionPolicy != null)
                {
                    versionPolicyConfig = config.VersionPolicy;
                    _logger("Info", $"Loaded version policy configuration: {versionPolicyConfig.Policy}");
                }

                // Create version options
                var versionOptions = new VersionOptions
                {
                    RepoRoot = request.RepoRoot,
                    ProjectPath = request.ProjectPath,
                    ProjectName = projectName,
                    TagPrefix = configOverrides.TagPrefix ?? request.TagPrefix,
                    DefaultIncrement = configOverrides.DefaultIncrement ?? "patch",
                    ForceVersion = configOverrides.ForceVersion,
                    Dependencies = request.Dependencies ?? new List<string>(),
                    Debug = request.Debug,
                    ExtraDebug = request.ExtraDebug,
                    SkipTestProjects = configOverrides.SkipTestProjects ?? request.SkipTestProjects,
                    SkipNonPackableProjects = configOverrides.SkipNonPackableProjects ?? request.SkipNonPackableProjects,
                    IsTestProject = request.IsTestProject,
                    IsPackable = request.IsPackable,
                    PrereleaseType = configOverrides.PrereleaseType ?? request.PrereleaseType,
                    BaseVersion = configOverrides.BaseVersion,
                    CommitConventions = conventionalCommitsConfig,
                    ChangeDetection = changeDetectionConfig,
                    GitIntegration = gitIntegrationConfig,
                    VersionPolicy = versionPolicyConfig,
                    Scheme = ParseVersionScheme(request.VersionScheme),
                    CalVer = new CalVerConfig
                    {
                        Format = request.CalVerFormat ?? "YYYY.MM.PATCH",
                        StartDate = request.CalVerStartDate,
                        ResetPatchPeriodically = request.CalVerResetPatch,
                        Separator = request.CalVerSeparator ?? "."
                    },
                    Constraints = request.ValidationEnabled ? new VersionConstraints
                    {
                        Enabled = request.ValidationEnabled,
                        MinimumVersion = request.MinimumVersion,
                        MaximumVersion = request.MaximumVersion,
                        AllowedRange = request.AllowedVersionRange,
                        ValidateDependencyVersions = request.ValidateDependencyVersions,
                        RequireMajorApproval = request.RequireMajorApproval,
                        BlockedVersions = ParsePatternString(request.BlockedVersions),
                        RequireMonotonicIncrease = request.RequireMonotonicIncrease
                    } : null
                };

                // Calculate version
                var versionResult = _versionCalculator.CalculateVersion(versionOptions);

                _logger("Info", $"Calculated version: {versionResult.Version} for {projectName}");

                return new VersioningResult
                {
                    Success = true,
                    Version = versionResult.Version,
                    VersionChanged = versionResult.VersionChanged,
                    ChangeReason = versionResult.ChangeReason,
                    ProjectName = projectName,
                    SemVer = _gitService.ParseSemVer(versionResult.Version),
                    ConfigurationApplied = config != null
                };
            }
            catch (Exception ex)
            {
                _logger("Error", $"Failed to calculate version: {ex.Message}");
                return new VersioningResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Creates a tag for the calculated version
        /// </summary>
        /// <param name="result">Version calculation result</param>
        /// <param name="tagPrefix">Tag prefix</param>
        /// <param name="customTagMessage">Custom tag message</param>
        /// <param name="dryRun">Whether to run in dry-run mode</param>
        /// <returns>True if tag was created successfully</returns>
        public bool CreateTag(VersioningResult result, string tagPrefix, string customTagMessage, bool dryRun)
        {
            if (!result.Success || string.IsNullOrEmpty(result.Version))
            {
                _logger("Warning", "Cannot create tag: version calculation failed or no version available");
                return false;
            }

            return TagService.CreateVersionTag(
                _gitService,
                result.Version,
                result.ProjectName,
                tagPrefix,
                customTagMessage,
                dryRun,
                _logger);
        }

        /// <summary>
        /// Parses a semicolon-separated string into a list of patterns
        /// </summary>
        /// <param name="patternsString">Semicolon-separated pattern string</param>
        /// <returns>List of patterns, or default list if string is null/empty</returns>
        private List<string> ParsePatternString(string patternsString)
        {
            if (string.IsNullOrWhiteSpace(patternsString))
            {
                return new List<string>();
            }

            return patternsString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        /// <summary>
        /// Parses version scheme string to enum
        /// </summary>
        /// <param name="schemeString">Version scheme string (semver or calver)</param>
        /// <returns>VersionScheme enum value</returns>
        private VersionScheme ParseVersionScheme(string schemeString)
        {
            if (string.IsNullOrWhiteSpace(schemeString))
                return VersionScheme.SemVer;

            return schemeString.ToLowerInvariant() == "calver"
                ? VersionScheme.CalVer
                : VersionScheme.SemVer;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _gitService?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Request object for version calculation
    /// </summary>
    public class VersioningRequest
    {
        public string RepoRoot { get; set; }
        public string ProjectPath { get; set; }
        public string ConfigFile { get; set; }
        public string PrereleaseType { get; set; } = "none";
        public string TagPrefix { get; set; } = "v";
        public string ForceVersion { get; set; }
        public List<string> Dependencies { get; set; }
        public bool Debug { get; set; }
        public bool ExtraDebug { get; set; }
        public bool SkipTestProjects { get; set; } = true;
        public bool SkipNonPackableProjects { get; set; } = true;
        public bool IsTestProject { get; set; }
        public bool IsPackable { get; set; } = true;

        // Conventional commits configuration
        public bool ConventionalCommitsEnabled { get; set; } = false;
        public string MajorPatterns { get; set; }
        public string MinorPatterns { get; set; }
        public string PatchPatterns { get; set; }
        public string IgnorePatterns { get; set; }

        // Change detection configuration
        public bool ChangeDetectionEnabled { get; set; } = false;
        public string IgnoreFilePatterns { get; set; }
        public string MajorFilePatterns { get; set; }
        public string MinorFilePatterns { get; set; }
        public string PatchFilePatterns { get; set; }
        public bool SourceOnlyMode { get; set; } = false;
        public string AdditionalMonitorPaths { get; set; }

        // Git integration configuration
        public bool ShallowCloneSupport { get; set; } = true;
        public string ShallowCloneFallbackVersion { get; set; }
        public bool SubmoduleSupport { get; set; } = false;
        public string CustomTagPatterns { get; set; }
        public bool BranchBasedVersioning { get; set; } = false;
        public string BranchVersioningRules { get; set; }
        public bool IncludeBranchInMetadata { get; set; } = false;
        public bool ValidateTagAncestry { get; set; } = true;
        public int FetchDepth { get; set; } = 50;

        // CalVer configuration
        public string VersionScheme { get; set; } = "semver";
        public string CalVerFormat { get; set; } = "YYYY.MM.PATCH";
        public string CalVerStartDate { get; set; }
        public bool CalVerResetPatch { get; set; } = true;
        public string CalVerSeparator { get; set; } = ".";

        // Validation constraints configuration
        public bool ValidationEnabled { get; set; } = false;
        public string MinimumVersion { get; set; }
        public string MaximumVersion { get; set; }
        public string AllowedVersionRange { get; set; }
        public bool ValidateDependencyVersions { get; set; } = false;
        public bool RequireMajorApproval { get; set; } = false;
        public string BlockedVersions { get; set; }
        public bool RequireMonotonicIncrease { get; set; } = true;
        public bool MajorVersionApproved { get; set; } = false;
    }

    /// <summary>
    /// Result object for version calculation
    /// </summary>
    public class VersioningResult
    {
        public bool Success { get; set; }
        public string Version { get; set; }
        public bool VersionChanged { get; set; }
        public string ChangeReason { get; set; }
        public string ProjectName { get; set; }
        public SemVer SemVer { get; set; }
        public bool ConfigurationApplied { get; set; }
        public string ErrorMessage { get; set; }
    }
}