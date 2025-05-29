using System;
using System.IO;
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
        private readonly IGitService _gitService;
        private readonly Action<string, string> _logger;

        public VersionCalculator(IGitService gitService, Action<string, string> logger = null)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
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
                var skipVersion = !string.IsNullOrEmpty(options.BaseVersion) ? options.BaseVersion : "1.0.0";
                var skipSemVer = !string.IsNullOrEmpty(options.BaseVersion) 
                    ? _gitService.ParseSemVer(options.BaseVersion) ?? new SemVer { Major = 1, Minor = 0, Patch = 0 }
                    : new SemVer { Major = 1, Minor = 0, Patch = 0 };
                
                _logger("Info", $"Using base global version for test project: {options.ProjectName} -> {skipVersion}");
                return new VersionResult
                {
                    Version = skipVersion,
                    SemVer = skipSemVer,
                    VersionChanged = false,
                    ChangeReason = "Test project - using base global version"
                };
            }

            if (options.SkipNonPackableProjects && !options.IsPackable)
            {
                var skipVersion = !string.IsNullOrEmpty(options.BaseVersion) ? options.BaseVersion : "1.0.0";
                var skipSemVer = !string.IsNullOrEmpty(options.BaseVersion) 
                    ? _gitService.ParseSemVer(options.BaseVersion) ?? new SemVer { Major = 1, Minor = 0, Patch = 0 }
                    : new SemVer { Major = 1, Minor = 0, Patch = 0 };
                
                _logger("Info", $"Using base global version for non-packable project: {options.ProjectName} -> {skipVersion}");
                return new VersionResult
                {
                    Version = skipVersion,
                    SemVer = skipSemVer,
                    VersionChanged = false,
                    ChangeReason = "Non-packable project - using base global version"
                };
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
#if NET472
                    relativeProjectPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(options.RepoRoot, projectDir));
#else
                    relativeProjectPath = NormalizePath(Path.GetRelativePath(options.RepoRoot, projectDir));
#endif
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
            var projectVersionTag = _gitService.GetProjectVersionTag(options.ProjectName, branchType, options.TagPrefix);

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
            var result = CalculateNewVersion(baseVersionTag, relativeProjectPath, options.ProjectName, 
                branchType, currentBranch, options);

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

            return result;
        }

        private VersionTag DetermineBaseVersion(VersionTag globalTag, VersionTag projectTag, BranchType branchType)
        {
            // If we have a project-specific tag, use it as the base
            // Project-specific tags always take precedence over global tags
            if (projectTag != null)
            {
                return projectTag;
            }

            return globalTag;
        }

        private VersionResult CalculateNewVersion(VersionTag baseVersionTag, string projectPath, 
            string projectName, BranchType branchType, string branchName, VersionOptions options)
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
            if (!string.IsNullOrEmpty(options.ForceVersion))
            {
                var forcedSemVer = _gitService.ParseSemVer(options.ForceVersion);
                if (forcedSemVer != null)
                {
                    result.SemVer = forcedSemVer;
                    result.VersionChanged = true;
                    result.ChangeReason = $"Forced version: {options.ForceVersion}";
                    _logger("Debug", $"Using forced version: {options.ForceVersion}");
                    return result;
                }
                else
                {
                    _logger("Warning", $"Invalid forced version format: {options.ForceVersion}. Using calculated version instead.");
                }
            }

            // Check if the project has any changes since the base tag
            bool hasChanges = false;
            bool isInitialRepository = baseVersionTag.Commit == null;
            
            if (isInitialRepository)
            {
                // For initial repository with no tags, always consider it as having changes
                hasChanges = true;
                _logger("Debug", "Initial repository detected (no tags), treating as having changes");
            }
            else
            {
                hasChanges = _gitService.ProjectHasChangedSinceTag(baseVersionTag.Commit, projectPath, 
                    options.Dependencies, options.RepoRoot, options.Debug);
            }
            
            // Get commit information for current HEAD
            if (_gitService.Repository?.Head?.Tip != null)
            {
                result.CommitSha = _gitService.GetCommitShortHash(_gitService.Repository.Head.Tip);
                result.CommitDate = _gitService.Repository.Head.Tip.Author.When.DateTime;
                result.CommitMessage = _gitService.Repository.Head.Tip.MessageShort;
            }

            if (hasChanges)
            {
                result.VersionChanged = true;

                switch (branchType)
                {
                    case BranchType.Main:
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
                                // Unknown/malformed prerelease format, increment using defaultIncrement and remove prerelease
                                ApplyVersionIncrement(newVersion, options.DefaultIncrement);
                                newVersion.PreRelease = null; // Remove malformed prerelease
                                var normalizedPrereleaseType = NormalizePrereleaseType(options.PrereleaseType);
                                var incrementType = options.DefaultIncrement?.ToLowerInvariant() ?? "patch";
                                
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
                            // No prerelease, increment using defaultIncrement and add configured prerelease if not "none"
                            if (!isInitialRepository)
                            {
                                ApplyVersionIncrement(newVersion, options.DefaultIncrement);
                            }
                            
                            var normalizedPrereleaseType = NormalizePrereleaseType(options.PrereleaseType);
                            var incrementType = options.DefaultIncrement?.ToLowerInvariant() ?? "patch";
                            
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
                        break;

                    case BranchType.Dev:
                        // Dev branches use defaultIncrement and add dev prerelease
                        ApplyVersionIncrement(newVersion, options.DefaultIncrement);
                        
                        // Calculate commit height from base tag
                        var devCommitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
                        // Ensure commit height is non-negative
                        if (devCommitHeight < 0) devCommitHeight = 0;
                        result.CommitHeight = devCommitHeight;
                        
                        var devIncrementType = options.DefaultIncrement?.ToLowerInvariant() ?? "patch";
                        
                        // Format: 1.1.0-dev.1 where 1 is commit height
                        newVersion.PreRelease = $"dev.{devCommitHeight}";
                        result.ChangeReason = $"Dev branch: Incrementing {devIncrementType} version with dev.{devCommitHeight}";
                        _logger("Debug", $"Dev branch: Using version {newVersion.ToVersionString()}");
                        break;

                    case BranchType.Release:
                        var releaseVersion = _gitService.ExtractReleaseVersion(branchName, options.TagPrefix);
                        if (releaseVersion != null)
                        {
                            newVersion.Major = releaseVersion.Major;
                            newVersion.Minor = releaseVersion.Minor;
                            newVersion.Patch = releaseVersion.Patch; // Keep patch from release branch name
                            
                            // Calculate RC number based on commits since base tag
                            var rcNumber = 1; // Default to rc.1
                            if (!isInitialRepository)
                            {
                                var rcCommitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
                                if (rcCommitHeight > 0)
                                {
                                    rcNumber = rcCommitHeight;
                                }
                            }
                            
                            newVersion.PreRelease = $"rc.{rcNumber}";
                            result.ChangeReason = $"Release branch: Using version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}-rc.{rcNumber}";
                            _logger("Debug", $"Release branch: Using version {newVersion.ToVersionString()}");
                        }
                        break;

                    case BranchType.Feature:
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
                        if (featureName.Length > 50)
                        {
                            featureName = featureName.Substring(0, 50).Trim('-');
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
                        break;
                }
            }
            else
            {
                // No changes detected
                if (branchType == BranchType.Feature)
                {
                    result.ChangeReason = "Feature branch but no changes detected, using base version";
                }
                else
                {
                    result.ChangeReason = "No changes detected, using base version";
                }
                _logger("Debug", result.ChangeReason);
            }

            result.Version = newVersion.ToVersionString();
            result.SemVer = newVersion;
            
            return result;
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
                _logger("Debug", $"Project version tag: {projectTag.Tag.FriendlyName} -> {projectTag.SemVer.ToVersionString()}");
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
    }
}