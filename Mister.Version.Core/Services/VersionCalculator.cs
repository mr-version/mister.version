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
            if (projectTag != null)
            {
                // Check if the project tag is based on the current global tag
                if (projectTag.SemVer.Major == globalTag.SemVer.Major &&
                    projectTag.SemVer.Minor == globalTag.SemVer.Minor)
                {
                    return projectTag;
                }
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
            if (baseVersionTag.Commit != null)
            {
                hasChanges = _gitService.ProjectHasChangedSinceTag(baseVersionTag.Commit, projectPath, 
                    options.Dependencies, options.RepoRoot, options.Debug);
                
                // Get commit information
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
                        newVersion.Patch++;
                        result.ChangeReason = "Main branch: Incrementing patch version due to changes";
                        _logger("Debug", $"Main branch: Incrementing patch version to {newVersion.Patch}");
                        break;

                    case BranchType.Dev:
                        // Dev branches now also increment patch like main branches
                        newVersion.Patch++;
                        result.ChangeReason = "Dev branch: Incrementing patch version due to changes";
                        _logger("Debug", $"Dev branch: Incrementing patch version to {newVersion.Patch}");
                        break;

                    case BranchType.Release:
                        var releaseVersion = _gitService.ExtractReleaseVersion(branchName, options.TagPrefix);
                        if (releaseVersion != null)
                        {
                            newVersion.Major = releaseVersion.Major;
                            newVersion.Minor = releaseVersion.Minor;
                            newVersion.Patch++;
                            result.ChangeReason = $"Release branch: Using version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}";
                            _logger("Debug", $"Release branch: Using version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}");
                        }
                        break;

                    case BranchType.Feature:
                        // Feature branches include commit height and branch name
                        var branchNameNormalized = branchName
                            .Replace("/", "-")
                            .Replace("_", "-")
                            .ToLowerInvariant();

                        // Calculate commit height from base tag
                        var commitHeight = _gitService.GetCommitHeight(baseVersionTag.Commit);
                        result.CommitHeight = commitHeight;

                        // Generate hash for uniqueness
                        var hashPart = _gitService.GetCommitShortHash(_gitService.Repository.Head.Tip);

                        // Format: v3.0.4-feature.1-{git-hash} where 1 is commit height
                        newVersion.PreRelease = $"{branchNameNormalized}.{commitHeight}-{hashPart}";
                        result.ChangeReason = $"Feature branch: Using pre-release version with commit height {commitHeight}";
                        _logger("Debug", $"Feature branch: Using pre-release version {newVersion.ToVersionString()}");
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