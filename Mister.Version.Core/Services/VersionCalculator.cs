using System;
using System.IO;
using System.Linq;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Core.Services
{
    public interface IVersionCalculator
    {
        VersionResult CalculateVersion(VersionOptions options);
    }

    public class VersionCalculator : IVersionCalculator
    {
        // Constants for version formatting
        private const int MAX_FEATURE_NAME_LENGTH = 50;

        private readonly IGitService _gitService;
        private readonly ICommitAnalyzer _commitAnalyzer;
        private readonly ICalVerCalculator _calVerCalculator;
        private readonly IVersionValidator _versionValidator;
        private readonly Action<string, string> _logger;

        public VersionCalculator(IGitService gitService, ICommitAnalyzer commitAnalyzer = null, ICalVerCalculator calVerCalculator = null, IVersionValidator versionValidator = null, Action<string, string> logger = null)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _commitAnalyzer = commitAnalyzer ?? new ConventionalCommitAnalyzer(logger);
            _calVerCalculator = calVerCalculator ?? new CalVerCalculator();
            _versionValidator = versionValidator ?? new VersionValidator();
            _logger = logger ?? ((level, message) => { }); // Default no-op logger
        }

        public VersionResult CalculateVersion(VersionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _logger("Info", $"Starting version calculation for {options.ProjectName}");

            // Check if we should skip versioning but still provide base global version
            if (options.SkipTestProjects && options.IsTestProject)
            {
                return CreateSkipVersionResult(
                    options.BaseVersion,
                    options.ProjectName,
                    "Test project - using base global version");
            }

            if (options.SkipNonPackableProjects && !options.IsPackable)
            {
                return CreateSkipVersionResult(
                    options.BaseVersion,
                    options.ProjectName,
                    "Non-packable project - using base global version");
            }

            // If a version is forced, use it directly
            if (!string.IsNullOrEmpty(options.ForceVersion))
            {
                _logger("Info", $"Using forced version: {options.ForceVersion}");
                var forcedSemVer = _gitService.ParseSemVer(options.ForceVersion);
                return new VersionResult
                {
                    Version = options.ForceVersion,
                    SemVer = forcedSemVer ?? new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    VersionChanged = true,
                    ChangeReason = "Forced version"
                };
            }

            // Check if CalVer is enabled
            if (options.Scheme == VersionScheme.CalVer)
            {
                _logger("Info", "Using CalVer versioning scheme");
                return CalculateCalVerVersion(options);
            }

            // Get current branch and determine type
            var currentBranch = _gitService.CurrentBranch;
            var branchType = _gitService.GetBranchType(currentBranch);
            
            _logger("Debug", $"Current branch: {currentBranch}");
            _logger("Debug", $"Branch type: {branchType}");

            // Get project directory relative to repo root
            string relativeProjectPath = "";
            if (!string.IsNullOrWhiteSpace(options.ProjectPath))
            {
                try
                {
                    var projectDir = Path.GetDirectoryName(options.ProjectPath);
                    if (string.IsNullOrEmpty(projectDir))
                    {
                        _logger("Warning", "Cannot determine project directory from path");
                        relativeProjectPath = "";
                    }
                    else
                    {
#if NET472
                        relativeProjectPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(options.RepoRoot, projectDir));
#else
                        relativeProjectPath = NormalizePath(Path.GetRelativePath(options.RepoRoot, projectDir));
#endif
                    }
                }
                catch (ArgumentException)
                {
                    // Handle invalid paths gracefully
                    relativeProjectPath = "";
                }
            }

            _logger("Debug", $"Project path: {relativeProjectPath}");

            // Get version tags
            var globalVersionTag = _gitService.GetGlobalVersionTag(branchType, options);
            var projectVersionTag = _gitService.GetProjectVersionTag(options.ProjectName, branchType, options.TagPrefix, options);

            // Determine base version
            var baseVersionTag = DetermineBaseVersion(globalVersionTag, projectVersionTag, branchType);
            
            // Ensure we always have a base version tag
            if (baseVersionTag == null)
            {
                // Use BaseVersion from config if available, otherwise use default fallback
                var fallbackVersion = !string.IsNullOrEmpty(options.BaseVersion) 
                    ? _gitService.ParseSemVer(options.BaseVersion)
                    : new SemVer { Major = 0, Minor = 1, Patch = 0 };
                
                baseVersionTag = new VersionTag
                {
                    SemVer = fallbackVersion,
                    IsGlobal = true,
                    Commit = null
                };
                
                if (!string.IsNullOrEmpty(options.BaseVersion))
                {
                    _logger("Info", $"Using base global version from config: {options.BaseVersion}");
                }
            }

            if (options.Debug)
            {
                LogVersionTagInfo(globalVersionTag, projectVersionTag, baseVersionTag);
            }

            // Calculate the new version based on changes
            var result = CalculateNewVersion(baseVersionTag, projectVersionTag, globalVersionTag, 
                relativeProjectPath, options.ProjectName, branchType, currentBranch, options);

            // Store the previous version from the base tag
            result.PreviousVersion = baseVersionTag.SemVer.ToVersionString();
            
            // Store the previous commit SHA if available
            if (baseVersionTag.Commit != null)
            {
                result.PreviousCommitSha = _gitService.GetCommitShortHash(baseVersionTag.Commit);
            }

            _logger("Info", $"Calculated version: {result.Version} for {options.ProjectName}");

            if (result.VersionChanged)
            {
                _logger("Info", $"Version changed: {result.ChangeReason}");
            }

            // Validate the calculated version if constraints are configured
            if (options.Constraints != null && options.Constraints.Enabled)
            {
                var bumpType = result.BumpType ?? VersionBumpType.Patch;
                var validationResult = _versionValidator.ValidateVersion(
                    result.Version,
                    result.PreviousVersion,
                    options.Constraints,
                    bumpType,
                    majorApproved: false); // TODO: Add majorApproved parameter to options if needed

                result.ValidationResult = validationResult;

                if (!validationResult.IsValid)
                {
                    _logger("Error", $"Version validation failed: {validationResult.Summary}");
                    foreach (var error in validationResult.Errors)
                    {
                        _logger("Error", $"  - {error.Message}");
                    }
                }
                else if (validationResult.Warnings.Any())
                {
                    _logger("Warning", $"Version validation warnings:");
                    foreach (var warning in validationResult.Warnings)
                    {
                        _logger("Warning", $"  - {warning.Message}");
                    }
                }
            }

            return result;
        }

        private VersionTag DetermineBaseVersion(VersionTag globalTag, VersionTag projectTag, BranchType branchType)
        {
            // If we have both tags, compare them and use the higher version
            if (projectTag != null && globalTag != null)
            {
                // Compare major and minor versions to determine if global is a newer release cycle
                var projectVersion = projectTag.SemVer;
                var globalVersion = globalTag.SemVer;
                
                // If global version is higher in major or minor version, use it (new release cycle)
                if (globalVersion.Major > projectVersion.Major || 
                    (globalVersion.Major == projectVersion.Major && globalVersion.Minor > projectVersion.Minor))
                {
                    _logger("Debug", $"Using global version {globalVersion.ToVersionString()} over project version {projectVersion.ToVersionString()} (new release cycle)");
                    return globalTag;
                }
                
                // Otherwise, use project tag (normal incremental versioning)
                return projectTag;
            }
            
            // If we only have a project-specific tag, use it
            if (projectTag != null)
            {
                return projectTag;
            }

            return globalTag;
        }

        /// <summary>
        /// Calculates the new version for a project based on changes and branch type.
        /// This is the main orchestration method that coordinates version calculation.
        /// </summary>
        private VersionResult CalculateNewVersion(VersionTag baseVersionTag, VersionTag projectTag,
            VersionTag globalTag, string projectPath, string projectName, BranchType branchType,
            string branchName, VersionOptions options)
        {
            var newVersion = baseVersionTag.SemVer.Clone();
            var result = new VersionResult
            {
                SemVer = newVersion,
                BranchType = branchType,
                BranchName = branchName,
                VersionChanged = false
            };

            // Handle forced version - overrides all other calculations
            var forcedResult = TryHandleForcedVersion(options);
            if (forcedResult != null)
            {
                return forcedResult;
            }

            // Determine if project has changes and handle initial repository scenarios
            var changeDetection = DetermineChangeStatus(baseVersionTag, projectPath, projectName, options, result);
            if (changeDetection.EarlyReturn != null)
            {
                return changeDetection.EarlyReturn;
            }

            bool hasChanges = changeDetection.HasChanges;
            bool isInitialRepository = changeDetection.IsInitialRepository;
            baseVersionTag = changeDetection.UpdatedBaseVersionTag ?? baseVersionTag;

            // Populate commit information for current HEAD
            PopulateCommitInfo(result);

            if (hasChanges)
            {
                var earlyReturn = HandleChangesDetected(baseVersionTag, newVersion, branchType, branchName,
                    options, result, isInitialRepository);
                if (earlyReturn != null)
                {
                    return earlyReturn;
                }
            }
            else
            {
                HandleNoChangesDetected(projectTag, globalTag, baseVersionTag, newVersion,
                    branchType, result);
            }

            // Apply branch metadata if configured
            if (options.GitIntegration?.IncludeBranchInMetadata == true)
            {
                result.SemVer = _gitService.AddBranchMetadata(result.SemVer, branchName, options.GitIntegration);
                result.Version = result.SemVer.ToVersionString();
            }

            return result;
        }

        /// <summary>
        /// Result of change detection including any early return values
        /// </summary>
        private class ChangeDetectionResult
        {
            public bool HasChanges { get; set; }
            public bool IsInitialRepository { get; set; }
            public VersionTag UpdatedBaseVersionTag { get; set; }
            public VersionResult EarlyReturn { get; set; }
        }

        /// <summary>
        /// Attempts to handle forced version if specified in options.
        /// Returns a VersionResult if forced version is valid, null otherwise.
        /// </summary>
        private VersionResult TryHandleForcedVersion(VersionOptions options)
        {
            if (string.IsNullOrEmpty(options.ForceVersion))
                return null;

            var forcedSemVer = _gitService.ParseSemVer(options.ForceVersion);
            if (forcedSemVer != null)
            {
                _logger("Debug", $"Using forced version: {options.ForceVersion}");
                return new VersionResult
                {
                    SemVer = forcedSemVer,
                    VersionChanged = true,
                    ChangeReason = $"Forced version: {options.ForceVersion}"
                };
            }

            _logger("Warning", $"Invalid forced version format: {options.ForceVersion}. Using calculated version instead.");
            return null;
        }

        /// <summary>
        /// Determines whether the project has changes since the base version.
        /// Handles complex scenarios including initial repositories, config-based versions,
        /// and existing tags. May return an early VersionResult in some cases.
        /// </summary>
        private ChangeDetectionResult DetermineChangeStatus(VersionTag baseVersionTag, string projectPath,
            string projectName, VersionOptions options, VersionResult result)
        {
            bool hasChanges = false;
            bool isInitialRepository = baseVersionTag.Commit == null;
            VersionTag updatedBaseVersionTag = null;

            // If using config baseVersion but repository has commits, it's not truly an initial repository
            // but we should NOT auto-increment - the first change should use the base version directly
            if (isInitialRepository && !string.IsNullOrEmpty(options.BaseVersion))
            {
                isInitialRepository = CheckIfTrulyInitialRepository(isInitialRepository);
            }

            if (isInitialRepository)
            {
                // For initial repository with no tags, always consider it as having changes
                hasChanges = true;
                _logger("Debug", "Initial repository detected (no tags), treating as having changes");
            }
            else if (baseVersionTag.Commit == null && !string.IsNullOrEmpty(options.BaseVersion))
            {
                // Using config baseVersion - complex change detection logic
                var configResult = HandleConfigBasedVersionChanges(baseVersionTag, projectPath, projectName,
                    options, result);
                if (configResult.EarlyReturn != null)
                {
                    return new ChangeDetectionResult
                    {
                        EarlyReturn = configResult.EarlyReturn
                    };
                }
                hasChanges = configResult.HasChanges;
                updatedBaseVersionTag = configResult.UpdatedBaseVersionTag;
            }
            else
            {
                hasChanges = _gitService.ProjectHasChangedSinceTag(baseVersionTag.Commit, projectPath,
                    options.Dependencies, options.RepoRoot, options.Debug);
            }

            return new ChangeDetectionResult
            {
                HasChanges = hasChanges,
                IsInitialRepository = isInitialRepository,
                UpdatedBaseVersionTag = updatedBaseVersionTag
            };
        }

        /// <summary>
        /// Checks if the repository is truly initial (no commits) even with a config baseVersion
        /// </summary>
        private bool CheckIfTrulyInitialRepository(bool currentValue)
        {
            try
            {
                var head = _gitService.Repository.Head;
                var hasCommits = head?.Tip != null;
                if (hasCommits)
                {
                    // Don't set hasChanges = true here!
                    // Let the normal change detection determine if there are changes
                    _logger("Debug", "Config baseVersion with existing commits: treating as new release cycle");
                    return false;
                }
            }
            catch
            {
                // If we can't check commits, fall back to original logic
            }
            return currentValue;
        }

        /// <summary>
        /// Handles change detection for config-based versions (versions without commits).
        /// This includes checking if the config version has been tagged, finding last tagged commit,
        /// and determining if the project has changes.
        /// </summary>
        private ChangeDetectionResult HandleConfigBasedVersionChanges(VersionTag baseVersionTag,
            string projectPath, string projectName, VersionOptions options, VersionResult result)
        {
            var baseVersionString = baseVersionTag.SemVer.ToVersionString();
            var tagName = $"{options.TagPrefix}{baseVersionString}";

            if (_gitService.TagExists(tagName))
            {
                // The base version has already been used (tagged)
                return HandleExistingConfigVersionTag(baseVersionTag, baseVersionString, tagName,
                    projectPath, options);
            }
            else
            {
                // First use of this base version
                return HandleNewConfigVersion(baseVersionTag, projectPath, projectName, options, result);
            }
        }

        /// <summary>
        /// Handles scenario where config baseVersion already exists as a tag
        /// </summary>
        private ChangeDetectionResult HandleExistingConfigVersionTag(VersionTag baseVersionTag,
            string baseVersionString, string tagName, string projectPath, VersionOptions options)
        {
            var existingTag = _gitService.Repository.Tags[tagName];
            if (existingTag != null)
            {
                var updatedTag = new VersionTag
                {
                    Tag = existingTag,
                    SemVer = baseVersionTag.SemVer,
                    Commit = existingTag.Target as LibGit2Sharp.Commit,
                    IsGlobal = true
                };
                _logger("Debug", $"Found existing tag for base version {baseVersionString}, using it for change detection");

                // Now let normal change detection work with the actual tag commit
                bool hasChanges = _gitService.ProjectHasChangedSinceTag(updatedTag.Commit, projectPath,
                    options.Dependencies, options.RepoRoot, options.Debug);

                return new ChangeDetectionResult
                {
                    HasChanges = hasChanges,
                    UpdatedBaseVersionTag = updatedTag
                };
            }
            else
            {
                _logger("Debug", $"Base version {baseVersionString} tag name exists but couldn't find tag object");
                return new ChangeDetectionResult
                {
                    HasChanges = true
                };
            }
        }

        /// <summary>
        /// Handles scenario where config baseVersion is being used for the first time
        /// </summary>
        private ChangeDetectionResult HandleNewConfigVersion(VersionTag baseVersionTag, string projectPath,
            string projectName, VersionOptions options, VersionResult result)
        {
            // Find the last commit that had a tag (any tag) to use as a baseline
            LibGit2Sharp.Commit lastTaggedCommit = FindLastTaggedCommit();

            if (lastTaggedCommit != null)
            {
                // Check if the project has changes since the last tagged commit
                bool hasChanges = _gitService.ProjectHasChangedSinceTag(lastTaggedCommit, projectPath,
                    options.Dependencies, options.RepoRoot, options.Debug);
                _logger("Debug", $"Checking changes since last tag for {projectName}: {hasChanges}");

                return new ChangeDetectionResult
                {
                    HasChanges = hasChanges
                };
            }
            else
            {
                // No tags at all - check if repository has any commits
                return HandleRepositoryWithNoTags(baseVersionTag, result);
            }
        }

        /// <summary>
        /// Finds the most recent commit that has a tag associated with it
        /// </summary>
        private LibGit2Sharp.Commit FindLastTaggedCommit()
        {
            foreach (var tag in _gitService.Repository.Tags.OrderByDescending(t => (t.Target as LibGit2Sharp.Commit)?.Author.When))
            {
                var tagCommit = tag.Target as LibGit2Sharp.Commit;
                if (tagCommit != null)
                {
                    return tagCommit;
                }
            }
            return null;
        }

        /// <summary>
        /// Handles repository with no tags - checks if commits exist
        /// </summary>
        private ChangeDetectionResult HandleRepositoryWithNoTags(VersionTag baseVersionTag, VersionResult result)
        {
            bool projectHasCommits = CheckIfRepositoryHasCommits();

            if (!projectHasCommits)
            {
                // Truly initial repository, use base version as-is
                var earlyReturn = new VersionResult
                {
                    VersionChanged = true,
                    ChangeReason = "New base version from configuration (initial repository)",
                    SemVer = baseVersionTag.SemVer.Clone(),
                    Version = baseVersionTag.SemVer.ToVersionString()
                };
                _logger("Debug", $"Using base version from config (initial): {earlyReturn.Version}");

                return new ChangeDetectionResult
                {
                    EarlyReturn = earlyReturn
                };
            }
            else
            {
                // Has commits but no tags - consider it as having changes for initial version
                _logger("Debug", $"Repository has commits but no tags, treating as having changes");
                return new ChangeDetectionResult
                {
                    HasChanges = true
                };
            }
        }

        /// <summary>
        /// Checks if the repository has any commits
        /// </summary>
        private bool CheckIfRepositoryHasCommits()
        {
            try
            {
                var filter = new LibGit2Sharp.CommitFilter
                {
                    IncludeReachableFrom = _gitService.Repository.Head
                };
                var commits = _gitService.Repository.Commits.QueryBy(filter);
                return commits.Any();
            }
            catch
            {
                return true; // Assume there are commits if we can't check
            }
        }

        /// <summary>
        /// Populates commit information (SHA, date, message) into the result
        /// </summary>
        private void PopulateCommitInfo(VersionResult result)
        {
            if (_gitService.Repository?.Head?.Tip != null)
            {
                result.CommitSha = _gitService.GetCommitShortHash(_gitService.Repository.Head.Tip);
                result.CommitDate = _gitService.Repository.Head.Tip.Author.When.DateTime;
                result.CommitMessage = _gitService.Repository.Head.Tip.MessageShort;
            }
        }

        /// <summary>
        /// Handles version calculation when changes are detected.
        /// Checks for first use of config baseVersion and applies branch-specific versioning.
        /// May return an early VersionResult if using baseVersion for the first time.
        /// </summary>
        private VersionResult HandleChangesDetected(VersionTag baseVersionTag, SemVer newVersion,
            BranchType branchType, string branchName, VersionOptions options, VersionResult result,
            bool isInitialRepository)
        {
            result.VersionChanged = true;

            // If baseVersion from config is higher than existing project version,
            // and this is the first change after setting the new baseVersion,
            // use the baseVersion directly without incrementing
            if (baseVersionTag.Commit == null && !string.IsNullOrEmpty(options.BaseVersion))
            {
                var baseVersionString = baseVersionTag.SemVer.ToVersionString();
                var tagName = $"{options.TagPrefix}{baseVersionString}";

                if (!_gitService.TagExists(tagName))
                {
                    // First use of this baseVersion - use it directly for projects with changes
                    _logger("Debug", $"Using base version from config for first change: {baseVersionTag.SemVer.ToVersionString()}");
                    return new VersionResult
                    {
                        SemVer = baseVersionTag.SemVer.Clone(),
                        Version = baseVersionTag.SemVer.ToVersionString(),
                        VersionChanged = true,
                        ChangeReason = "First change with new base version from configuration"
                    };
                }
                // If tag exists, continue to normal increment logic
            }

            // Apply branch-specific versioning strategies
            switch (branchType)
            {
                case BranchType.Main:
                    ApplyMainBranchVersioning(newVersion, baseVersionTag, options, result, isInitialRepository);
                    break;

                case BranchType.Dev:
                    ApplyDevBranchVersioning(newVersion, baseVersionTag, options, result);
                    break;

                case BranchType.Release:
                    ApplyReleaseBranchVersioning(newVersion, baseVersionTag, branchName, options, result);
                    break;

                case BranchType.Feature:
                    ApplyFeatureBranchVersioning(newVersion, baseVersionTag, branchName, options, result);
                    break;
            }

            // Set the final version string and SemVer after all modifications
            result.Version = newVersion.ToVersionString();
            result.SemVer = newVersion;

            return null; // No early return
        }

        /// <summary>
        /// Handles version selection when no changes are detected.
        /// Prefers existing project version, falls back to global version, then base version.
        /// </summary>
        private void HandleNoChangesDetected(VersionTag projectTag, VersionTag globalTag,
            VersionTag baseVersionTag, SemVer newVersion, BranchType branchType, VersionResult result)
        {
            // No changes detected - use the existing project version if available
            // Only use baseVersion for projects that actually have changes
            if (projectTag != null && projectTag.Commit != null)
            {
                // Project has an existing version and no changes - keep the existing version
                result.SemVer = projectTag.SemVer.Clone();
                result.Version = result.SemVer.ToVersionString();
                result.ChangeReason = "No changes detected, using existing project version";
                _logger("Debug", $"No changes, keeping existing version: {result.Version}");
            }
            else if (globalTag != null && globalTag.Commit != null &&
                     (baseVersionTag == null || baseVersionTag.Commit != null))
            {
                // No project tag, but there's a global tag with actual commits
                // Use the global tag version (not the baseVersion from config)
                result.SemVer = globalTag.SemVer.Clone();
                result.Version = result.SemVer.ToVersionString();
                result.ChangeReason = "No changes detected, using existing global version";
                _logger("Debug", $"No changes, using global version: {result.Version}");
            }
            else
            {
                // No existing tags or only config baseVersion - use the base version
                result.Version = newVersion.ToVersionString();
                result.SemVer = newVersion;
                result.ChangeReason = branchType == BranchType.Feature
                    ? "Feature branch but no changes detected, using base version"
                    : "No changes detected, using base version";
                _logger("Debug", result.ChangeReason);
            }
        }

        private void LogVersionTagInfo(VersionTag globalTag, VersionTag projectTag, VersionTag baseTag)
        {
            _logger("Debug", "--- Version Tag Information ---");
            
            if (globalTag != null)
            {
                _logger("Debug", $"Global version tag: {globalTag.Tag?.FriendlyName ?? "Default"} -> {globalTag.SemVer.ToVersionString()}");
            }
            
            if (projectTag != null)
            {
                _logger("Debug", $"Project version tag: {projectTag.Tag?.FriendlyName ?? "Default"} -> {projectTag.SemVer.ToVersionString()}");
            }
            
            _logger("Debug", $"Base version: {baseTag.SemVer.ToVersionString()} (from {(baseTag.IsGlobal ? "global" : "project")} tag)");
            _logger("Debug", "--- End Version Tag Information ---");
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.Replace('\\', '/');
        }
        
        private string NormalizePrereleaseType(string prereleaseType)
        {
            if (string.IsNullOrEmpty(prereleaseType))
                return "none";
                
            var normalized = prereleaseType.ToLowerInvariant().Trim();
            
            // Only accept valid prerelease types
            return normalized switch
            {
                "alpha" => "alpha",
                "beta" => "beta",
                "rc" => "rc",
                "none" => "none",
                _ => "none" // Default to none for invalid types
            };
        }

        private void ApplyVersionIncrement(SemVer version, string incrementType, bool resetLowerComponents = true)
        {
            var normalizedIncrement = incrementType?.ToLowerInvariant()?.Trim() ?? "patch";
            
            switch (normalizedIncrement)
            {
                case "major":
                    version.Major++;
                    if (resetLowerComponents)
                    {
                        version.Minor = 0;
                        version.Patch = 0;
                    }
                    break;
                case "minor":
                    version.Minor++;
                    if (resetLowerComponents)
                    {
                        version.Patch = 0;
                    }
                    break;
                case "patch":
                default:
                    version.Patch++;
                    break;
            }
        }

        /// <summary>
        /// Determines the version increment type by analyzing commits using conventional commit patterns
        /// </summary>
        /// <param name="baseVersionTag">The base version tag to analyze commits from</param>
        /// <param name="options">Version calculation options</param>
        /// <param name="bumpType">Output parameter for the determined bump type</param>
        /// <param name="commitClassifications">Output parameter for commit classifications</param>
        /// <returns>Increment type string (major, minor, or patch)</returns>
        private string DetermineIncrementFromCommits(VersionTag baseVersionTag, VersionOptions options,
            out VersionBumpType? bumpType, out System.Collections.Generic.List<CommitClassification> commitClassifications)
        {
            bumpType = null;
            commitClassifications = null;

            VersionBumpType commitBumpType = VersionBumpType.None;
            VersionBumpType fileBumpType = VersionBumpType.None;

            // Analyze conventional commits (if enabled)
            if (options.CommitConventions != null && options.CommitConventions.Enabled)
            {
                commitBumpType = AnalyzeConventionalCommits(baseVersionTag, options, out commitClassifications);
            }

            // Analyze file patterns (if enabled)
            if (options.ChangeDetection != null && options.ChangeDetection.Enabled)
            {
                fileBumpType = AnalyzeFilePatterns(baseVersionTag, options);
            }

            // Combine both analyses - use the higher bump type
            var finalBumpType = (VersionBumpType)Math.Max((int)commitBumpType, (int)fileBumpType);
            bumpType = finalBumpType;

            // If neither is enabled, use default
            if (!((options.CommitConventions?.Enabled ?? false) || (options.ChangeDetection?.Enabled ?? false)))
            {
                return options.DefaultIncrement ?? "patch";
            }

            // Convert VersionBumpType to string increment type
            var incrementType = finalBumpType switch
            {
                VersionBumpType.Major => "major",
                VersionBumpType.Minor => "minor",
                VersionBumpType.Patch => "patch",
                VersionBumpType.None => options.DefaultIncrement ?? "patch",
                _ => options.DefaultIncrement ?? "patch"
            };

            _logger("Info", $"Version increment determined: {incrementType} (commits: {commitBumpType}, files: {fileBumpType}, final: {finalBumpType})");
            return incrementType;
        }

        /// <summary>
        /// Analyze conventional commits to determine bump type
        /// </summary>
        private VersionBumpType AnalyzeConventionalCommits(VersionTag baseVersionTag, VersionOptions options,
            out System.Collections.Generic.List<CommitClassification> commitClassifications)
        {
            commitClassifications = null;

            try
            {
                // Get commits between base tag and HEAD
                System.Collections.Generic.IEnumerable<LibGit2Sharp.Commit> commits = null;

                if (baseVersionTag?.Commit != null && _gitService.Repository?.Head?.Tip != null)
                {
                    var filter = new LibGit2Sharp.CommitFilter
                    {
                        IncludeReachableFrom = _gitService.Repository.Head.Tip,
                        ExcludeReachableFrom = baseVersionTag.Commit,
                        SortBy = LibGit2Sharp.CommitSortStrategies.Topological | LibGit2Sharp.CommitSortStrategies.Time
                    };

                    commits = _gitService.Repository.Commits.QueryBy(filter);
                }
                else if (_gitService.Repository?.Head?.Tip != null)
                {
                    // No base tag commit, get all commits
                    var filter = new LibGit2Sharp.CommitFilter
                    {
                        IncludeReachableFrom = _gitService.Repository.Head.Tip
                    };

                    commits = _gitService.Repository.Commits.QueryBy(filter);
                }

                if (commits == null || !commits.Any())
                {
                    _logger("Debug", "No commits to analyze");
                    return VersionBumpType.None;
                }

                // Classify all commits for detailed output
                commitClassifications = commits
                    .Select(c => _commitAnalyzer.ClassifyCommit(c, options.CommitConventions))
                    .Where(c => c != null)
                    .ToList();

                // Analyze commits to determine bump type
                var determinedBumpType = _commitAnalyzer.AnalyzeBumpType(commits, options.CommitConventions);

                _logger("Info", $"Conventional commits analysis: {determinedBumpType}");
                return determinedBumpType;
            }
            catch (Exception ex)
            {
                _logger("Warning", $"Error analyzing commits: {ex.Message}");
                return VersionBumpType.None;
            }
        }

        /// <summary>
        /// Analyze file patterns to determine bump type
        /// </summary>
        private VersionBumpType AnalyzeFilePatterns(VersionTag baseVersionTag, VersionOptions options)
        {
            try
            {
                // Classify changed files based on patterns
                var classification = _gitService.ClassifyProjectChanges(
                    baseVersionTag?.Commit,
                    options.ProjectPath,
                    options.Dependencies,
                    options.RepoRoot,
                    options.ChangeDetection);

                if (classification.ShouldIgnore)
                {
                    _logger("Info", $"File pattern analysis: All changes ignored - {classification.Reason}");
                    return VersionBumpType.None;
                }

                _logger("Info", $"File pattern analysis: {classification.RequiredBumpType} - {classification.Reason}");

                // Log details about classified files
                if (classification.MajorFiles.Count > 0)
                {
                    _logger("Debug", $"  Major files ({classification.MajorFiles.Count}): {string.Join(", ", classification.MajorFiles.Take(3))}");
                }
                if (classification.MinorFiles.Count > 0)
                {
                    _logger("Debug", $"  Minor files ({classification.MinorFiles.Count}): {string.Join(", ", classification.MinorFiles.Take(3))}");
                }
                if (classification.PatchFiles.Count > 0)
                {
                    _logger("Debug", $"  Patch files ({classification.PatchFiles.Count}): {string.Join(", ", classification.PatchFiles.Take(3))}");
                }
                if (classification.IgnoredFiles.Count > 0)
                {
                    _logger("Debug", $"  Ignored files ({classification.IgnoredFiles.Count}): {string.Join(", ", classification.IgnoredFiles.Take(3))}");
                }

                return classification.RequiredBumpType;
            }
            catch (Exception ex)
            {
                _logger("Warning", $"Error analyzing file patterns: {ex.Message}");
                return VersionBumpType.None;
            }
        }

        /// <summary>
        /// Calculates version for main branch
        /// </summary>
        private void ApplyMainBranchVersioning(SemVer newVersion, VersionTag baseVersionTag, VersionOptions options, VersionResult result, bool isInitialRepository)
        {
            // Determine increment type from commits (or use default if conventional commits disabled)
            var incrementType = DetermineIncrementFromCommits(baseVersionTag, options, out var bumpType, out var commitClassifications);

            // Store conventional commits analysis information in result
            if (options.CommitConventions != null && options.CommitConventions.Enabled)
            {
                result.ConventionalCommitsEnabled = true;
                result.BumpType = bumpType;
                result.CommitClassifications = commitClassifications;
            }

            // Check if the base version already has a prerelease
            if (!string.IsNullOrEmpty(baseVersionTag.SemVer.PreRelease))
            {
                // Handle prerelease progression (alpha.1 -> alpha.2, etc.)
                var prereleaseMatch = System.Text.RegularExpressions.Regex.Match(
                    baseVersionTag.SemVer.PreRelease,
                    @"^(alpha|beta|rc)\.(\d+)$");

                if (prereleaseMatch.Success)
                {
                    var prereleaseType = prereleaseMatch.Groups[1].Value;
                    var prereleaseNumber = int.Parse(prereleaseMatch.Groups[2].Value);
                    newVersion.PreRelease = $"{prereleaseType}.{prereleaseNumber + 1}";
                    result.ChangeReason = $"Main branch: Incrementing {prereleaseType} version";
                }
                else
                {
                    // Unknown/malformed prerelease format, increment and remove prerelease
                    ApplyVersionIncrement(newVersion, incrementType);
                    newVersion.PreRelease = null; // Remove malformed prerelease
                    var normalizedPrereleaseType = NormalizePrereleaseType(options.PrereleaseType);

                    if (normalizedPrereleaseType != "none")
                    {
                        newVersion.PreRelease = $"{normalizedPrereleaseType}.1";
                        result.ChangeReason = $"Main branch: Incrementing {incrementType} version with {normalizedPrereleaseType} prerelease";
                    }
                    else
                    {
                        result.ChangeReason = $"Main branch: Incrementing {incrementType} version";
                    }
                }
            }
            else
            {
                // No prerelease, increment and add configured prerelease if not "none"
                if (!isInitialRepository)
                {
                    ApplyVersionIncrement(newVersion, incrementType);
                }

                var normalizedPrereleaseType = NormalizePrereleaseType(options.PrereleaseType);

                if (normalizedPrereleaseType != "none")
                {
                    newVersion.PreRelease = $"{normalizedPrereleaseType}.1";
                    result.ChangeReason = isInitialRepository
                        ? $"Initial repository: Adding {normalizedPrereleaseType} prerelease"
                        : $"Main branch: Incrementing {incrementType} version with {normalizedPrereleaseType} prerelease";
                }
                else
                {
                    result.ChangeReason = isInitialRepository
                        ? "Initial repository: Base version"
                        : $"Main branch: Incrementing {incrementType} version";
                }
            }
            _logger("Debug", $"Main branch: Version {newVersion.ToVersionString()}");
        }

        /// <summary>
        /// Calculates version for dev branch
        /// </summary>
        private void ApplyDevBranchVersioning(SemVer newVersion, VersionTag baseVersionTag, VersionOptions options, VersionResult result)
        {
            // Determine increment type from commits (or use default if conventional commits disabled)
            var incrementType = DetermineIncrementFromCommits(baseVersionTag, options, out var bumpType, out var commitClassifications);

            // Store conventional commits analysis information in result
            if (options.CommitConventions != null && options.CommitConventions.Enabled)
            {
                result.ConventionalCommitsEnabled = true;
                result.BumpType = bumpType;
                result.CommitClassifications = commitClassifications;
            }

            // Dev branches increment version and add dev prerelease
            ApplyVersionIncrement(newVersion, incrementType);

            // Calculate commit height from base tag
            var devCommitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
            // Ensure commit height is non-negative
            if (devCommitHeight < 0) devCommitHeight = 0;
            result.CommitHeight = devCommitHeight;

            // Format: 1.1.0-dev.1 where 1 is commit height
            newVersion.PreRelease = $"dev.{devCommitHeight}";
            result.ChangeReason = $"Dev branch: Incrementing {incrementType} version with dev.{devCommitHeight}";
            _logger("Debug", $"Dev branch: Using version {newVersion.ToVersionString()}");
        }

        /// <summary>
        /// Calculates version for release branch
        /// </summary>
        private void ApplyReleaseBranchVersioning(SemVer newVersion, VersionTag baseVersionTag, string branchName, VersionOptions options, VersionResult result)
        {
            var releaseVersion = _gitService.ExtractReleaseVersion(branchName, options.TagPrefix);
            if (releaseVersion != null)
            {
                newVersion.Major = releaseVersion.Major;
                newVersion.Minor = releaseVersion.Minor;

                // Check if baseVersionTag is already in the same release series (same major.minor)
                // If so, increment patch from the existing tag; otherwise use patch from branch name
                if (baseVersionTag != null &&
                    baseVersionTag.SemVer.Major == releaseVersion.Major &&
                    baseVersionTag.SemVer.Minor == releaseVersion.Minor)
                {
                    // We're building on an existing release in this series - increment patch
                    newVersion.Patch = baseVersionTag.SemVer.Patch + 1;
                    _logger("Debug", $"Release branch: Found existing tag in release series, incrementing patch to {newVersion.Patch}");
                }
                else
                {
                    // No existing tag in this release series, use patch from branch name
                    newVersion.Patch = releaseVersion.Patch;
                    _logger("Debug", $"Release branch: No existing tag in release series, using patch {newVersion.Patch} from branch name");
                }

                // Release branches produce final versions (no prerelease suffix)
                // These are for support patches, not release candidates
                newVersion.PreRelease = null;
                result.ChangeReason = $"Release branch: Using version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}";
                _logger("Debug", $"Release branch: Using version {newVersion.ToVersionString()}");
            }
        }

        /// <summary>
        /// Calculates version for feature branch
        /// </summary>
        private void ApplyFeatureBranchVersioning(SemVer newVersion, VersionTag baseVersionTag, string branchName, VersionOptions options, VersionResult result)
        {
            // Feature branches use defaultIncrement
            ApplyVersionIncrement(newVersion, options.DefaultIncrement);

            // Extract feature name from branch (remove common prefixes)
            var featureName = branchName;
            var prefixes = new[] { "feature/", "bugfix/", "hotfix/" };
            foreach (var prefix in prefixes)
            {
                if (featureName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    featureName = featureName.Substring(prefix.Length);
                    break;
                }
            }

            // Normalize the feature name - remove special characters and ensure valid prerelease format
            featureName = System.Text.RegularExpressions.Regex.Replace(featureName, @"[^a-zA-Z0-9\-]", "-")
                .Replace("_", "-")
                .Trim('-')
                .ToLowerInvariant();

            // Ensure it doesn't exceed reasonable length
            if (featureName.Length > MAX_FEATURE_NAME_LENGTH)
            {
                featureName = featureName.Substring(0, MAX_FEATURE_NAME_LENGTH).Trim('-');
            }

            // If the feature name becomes empty after normalization, use a default
            if (string.IsNullOrEmpty(featureName))
            {
                featureName = "feature";
            }

            // Calculate commit height from base tag
            var commitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
            // Ensure commit height is non-negative
            if (commitHeight < 0) commitHeight = 0;
            result.CommitHeight = commitHeight;

            var featureIncrementType = options.DefaultIncrement?.ToLowerInvariant() ?? "patch";

            // Format: 1.1.0-new-feature.1 where 1 is commit height
            newVersion.PreRelease = $"{featureName}.{commitHeight}";
            result.ChangeReason = $"Feature branch: Incrementing {featureIncrementType} version with pre-release {featureName}.{commitHeight}";
            _logger("Debug", $"Feature branch: Using version {newVersion.ToVersionString()}");
        }

        /// <summary>
        /// Calculates a CalVer version based on the current date and existing tags
        /// </summary>
        private VersionResult CalculateCalVerVersion(VersionOptions options)
        {
            // Ensure CalVer config exists
            if (options.CalVer == null)
            {
                options.CalVer = new CalVerConfig();
                _logger("Warning", "CalVer config not provided, using defaults");
            }

            // Validate CalVer format
            if (!options.CalVer.IsValidFormat())
            {
                _logger("Error", $"Invalid CalVer format: {options.CalVer.Format}. Using default YYYY.MM.PATCH");
                options.CalVer.Format = "YYYY.MM.PATCH";
            }

            // Get the current branch type to determine if changes matter
            var currentBranch = _gitService.CurrentBranch;
            var branchType = _gitService.GetBranchType(currentBranch);

            _logger("Debug", $"CalVer - Current branch: {currentBranch}");
            _logger("Debug", $"CalVer - Branch type: {branchType}");

            // Get the latest version tag for this project
            var projectVersionTag = _gitService.GetProjectVersionTag(
                options.ProjectName,
                branchType,
                options.TagPrefix,
                options);

            // Use the latest tag's version as the base
            SemVer existingVersion = projectVersionTag?.SemVer;

            if (existingVersion != null)
            {
                _logger("Debug", $"CalVer - Found existing version: {existingVersion.ToVersionString()}");
            }
            else
            {
                _logger("Debug", "CalVer - No existing version found, starting fresh");
            }

            // Calculate new CalVer version
            var calculationDate = DateTime.UtcNow;
            var newVersion = _calVerCalculator.CalculateVersion(options.CalVer, calculationDate, existingVersion);

            // Check if there are changes in the project
            bool hasChanges = false;
            string changeReason = "CalVer - No changes detected";

            if (existingVersion == null)
            {
                hasChanges = true;
                changeReason = "CalVer - Initial version";
            }
            else
            {
                // Check if we're in a new period (month/week)
                if (newVersion.Major != existingVersion.Major || newVersion.Minor != existingVersion.Minor)
                {
                    hasChanges = true;
                    changeReason = $"CalVer - New period: {newVersion.Major}.{newVersion.Minor}";
                }
                else
                {
                    // Same period, check for actual code changes
                    var relativeProjectPath = "";
                    if (!string.IsNullOrWhiteSpace(options.ProjectPath))
                    {
                        try
                        {
                            var projectDir = Path.GetDirectoryName(options.ProjectPath);
                            if (!string.IsNullOrEmpty(projectDir))
                            {
#if NET472
                                relativeProjectPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(options.RepoRoot, projectDir));
#else
                                relativeProjectPath = NormalizePath(Path.GetRelativePath(options.RepoRoot, projectDir));
#endif
                            }
                        }
                        catch (ArgumentException)
                        {
                            relativeProjectPath = "";
                        }
                    }

                    if (!string.IsNullOrEmpty(relativeProjectPath))
                    {
                        var projectChanges = _gitService.ClassifyProjectChanges(
                            projectVersionTag.Commit,
                            relativeProjectPath,
                            options.Dependencies ?? new System.Collections.Generic.List<string>(),
                            options.RepoRoot,
                            options.ChangeDetection);

                        if (projectChanges.HasChanges)
                        {
                            hasChanges = true;
                            changeReason = $"CalVer - Changes detected in project (patch increment)";

                            // Increment patch version for changes in the same period
                            newVersion.Patch++;
                        }
                    }
                }
            }

            // Apply prerelease suffix if specified
            if (!string.IsNullOrEmpty(options.PrereleaseType) &&
                options.PrereleaseType.ToLowerInvariant() != "none")
            {
                newVersion.PreRelease = options.PrereleaseType.ToLowerInvariant();
            }

            // Apply branch metadata if configured
            if (options.GitIntegration?.IncludeBranchInMetadata == true &&
                branchType != BranchType.Main &&
                branchType != BranchType.Release)
            {
                var branchName = SanitizeBranchName(currentBranch);
                newVersion.BuildMetadata = branchName;
            }

            var versionString = newVersion.ToString();

            _logger("Info", $"CalVer calculated version: {versionString}");
            _logger("Debug", $"CalVer change reason: {changeReason}");

            return new VersionResult
            {
                Version = versionString,
                SemVer = newVersion,
                VersionChanged = hasChanges,
                ChangeReason = changeReason,
                Scheme = VersionScheme.CalVer,
                CalVerConfig = options.CalVer
            };
        }

        /// <summary>
        /// Sanitizes branch name for use in version metadata
        /// </summary>
        private string SanitizeBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return "unknown";

            // Remove common prefixes
            var sanitized = branchName
                .Replace("feature/", "")
                .Replace("hotfix/", "")
                .Replace("bugfix/", "")
                .Replace("dev/", "");

            // Replace invalid characters with hyphens
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-zA-Z0-9\-]", "-");

            // Remove consecutive hyphens
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"-+", "-");

            // Trim hyphens from start and end
            sanitized = sanitized.Trim('-');

            return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
        }

        /// <summary>
        /// Creates a version result for skipped projects (test projects or non-packable projects)
        /// </summary>
        private VersionResult CreateSkipVersionResult(string baseVersion, string projectName, string reason)
        {
            var skipVersion = !string.IsNullOrEmpty(baseVersion) ? baseVersion : "1.0.0";
            var skipSemVer = !string.IsNullOrEmpty(baseVersion)
                ? _gitService.ParseSemVer(baseVersion) ?? new SemVer { Major = 1, Minor = 0, Patch = 0 }
                : new SemVer { Major = 1, Minor = 0, Patch = 0 };

            _logger("Info", $"Using base global version for {projectName}: {skipVersion} ({reason})");

            return new VersionResult
            {
                Version = skipVersion,
                SemVer = skipSemVer,
                VersionChanged = false,
                ChangeReason = reason
            };
        }
    }
}