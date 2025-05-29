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
            _gitService = gitService;
            _logger = logger ?? ((level, message) => { }); // Default no-op logger
        }

        public VersionResult CalculateVersion(VersionOptions options)
        {
            _logger("Info", $"Starting version calculation for {options.ProjectName}");

            // Check if we should skip versioning
            if (options.SkipTestProjects && options.IsTestProject)
            {
                _logger("Info", $"Skipping versioning for test project: {options.ProjectName}");
                return new VersionResult
                {
                    Version = "1.0.0",
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    VersionChanged = false,
                    ChangeReason = "Test project - versioning skipped"
                };
            }

            if (options.SkipNonPackableProjects && !options.IsPackable)
            {
                _logger("Info", $"Skipping versioning for non-packable project: {options.ProjectName}");
                return new VersionResult
                {
                    Version = "1.0.0",
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    VersionChanged = false,
                    ChangeReason = "Non-packable project - versioning skipped"
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
            var projectDir = Path.GetDirectoryName(options.ProjectPath);
#if NET472
            var relativeProjectPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(options.RepoRoot, projectDir));
#else
            var relativeProjectPath = NormalizePath(Path.GetRelativePath(options.RepoRoot, projectDir));
#endif

            _logger("Debug", $"Project path: {relativeProjectPath}");

            // Get version tags
            var globalVersionTag = _gitService.GetGlobalVersionTag(branchType, options.TagPrefix);
            var projectVersionTag = _gitService.GetProjectVersionTag(options.ProjectName, branchType, options.TagPrefix);

            // Determine base version
            var baseVersionTag = DetermineBaseVersion(globalVersionTag, projectVersionTag, branchType);

            if (options.Debug)
            {
                LogVersionTagInfo(globalVersionTag, projectVersionTag, baseVersionTag);
            }

            // Calculate the new version based on changes
            var result = CalculateNewVersion(baseVersionTag, relativeProjectPath, options.ProjectName, 
                branchType, currentBranch, options);

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
            if (_gitService.Repository.Head.Tip != null)
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
                                // Unknown prerelease format, increment patch and reset to alpha.1
                                newVersion.Patch++;
                                newVersion.PreRelease = "alpha.1";
                                result.ChangeReason = "Main branch: Incrementing patch version with alpha prerelease";
                            }
                        }
                        else
                        {
                            // No prerelease, increment patch and add alpha.1
                            if (!isInitialRepository)
                            {
                                newVersion.Patch++;
                            }
                            newVersion.PreRelease = "alpha.1";
                            result.ChangeReason = isInitialRepository 
                                ? "Initial repository: Adding alpha prerelease" 
                                : "Main branch: Incrementing patch version with alpha prerelease";
                        }
                        _logger("Debug", $"Main branch: Version {newVersion.ToVersionString()}");
                        break;

                    case BranchType.Dev:
                        // Dev branches increment minor version and add dev prerelease
                        newVersion.Minor++;
                        newVersion.Patch = 0; // Reset patch when incrementing minor
                        
                        // Calculate commit height from base tag
                        var devCommitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
                        result.CommitHeight = devCommitHeight;
                        
                        // Format: 1.1.0-dev.1 where 1 is commit height
                        newVersion.PreRelease = $"dev.{devCommitHeight}";
                        result.ChangeReason = $"Dev branch: Incrementing minor version with dev.{devCommitHeight}";
                        _logger("Debug", $"Dev branch: Using version {newVersion.ToVersionString()}");
                        break;

                    case BranchType.Release:
                        var releaseVersion = _gitService.ExtractReleaseVersion(branchName, options.TagPrefix);
                        if (releaseVersion != null)
                        {
                            newVersion.Major = releaseVersion.Major;
                            newVersion.Minor = releaseVersion.Minor;
                            newVersion.Patch = 0; // Reset patch for release branches
                            
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
                        // Feature branches increment minor version
                        newVersion.Minor++;
                        newVersion.Patch = 0; // Reset patch when incrementing minor
                        
                        // Extract feature name from branch (remove "feature/" prefix)
                        var featureName = branchName;
                        if (featureName.StartsWith("feature/", StringComparison.OrdinalIgnoreCase))
                        {
                            featureName = featureName.Substring("feature/".Length);
                        }
                        
                        // Normalize the feature name
                        featureName = featureName
                            .Replace("/", "-")
                            .Replace("_", "-")
                            .ToLowerInvariant();

                        // Calculate commit height from base tag
                        var commitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
                        result.CommitHeight = commitHeight;

                        // Format: 1.1.0-new-feature.1 where 1 is commit height
                        newVersion.PreRelease = $"{featureName}.{commitHeight}";
                        result.ChangeReason = $"Feature branch: Incrementing minor version with pre-release {featureName}.{commitHeight}";
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
    }
}