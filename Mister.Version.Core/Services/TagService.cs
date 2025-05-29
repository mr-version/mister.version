using System;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public static class TagService
    {
        /// <summary>
        /// Creates a git tag for the specified version
        /// </summary>
        /// <param name="gitService">Git service instance</param>
        /// <param name="version">Version string to tag</param>
        /// <param name="projectName">Name of the project</param>
        /// <param name="tagPrefix">Tag prefix (usually "v")</param>
        /// <param name="customTagMessage">Custom tag message, or null for default</param>
        /// <param name="dryRun">Whether to run in dry-run mode</param>
        /// <param name="logger">Logger for output messages</param>
        /// <returns>True if tag was created or would be created (dry-run), false otherwise</returns>
        public static bool CreateVersionTag(
            IGitService gitService,
            string version,
            string projectName,
            string tagPrefix,
            string customTagMessage,
            bool dryRun,
            Action<string, string> logger)
        {
            if (string.IsNullOrEmpty(version))
            {
                logger?.Invoke("Warning", "Cannot create tag: version is null or empty");
                return false;
            }

            // Parse the version to check if it's a major release
            var semVer = gitService.ParseSemVer(version);
            var isMajorRelease = IsMajorRelease(semVer);
            
            // Create project-specific tag by default, global tag for major releases
            var tagName = isMajorRelease 
                ? $"{tagPrefix}{version}"
                : $"{projectName}/{tagPrefix}{version}";
            
            var tagMessage = string.IsNullOrEmpty(customTagMessage) 
                ? $"Version {version} for {projectName}"
                : customTagMessage;
            
            var tagType = isMajorRelease ? "global" : "project-specific";
            var actionPrefix = dryRun ? "[DRY RUN] Would create" : "Creating";
            
            logger?.Invoke("Info", $"{actionPrefix} {tagType} tag: {tagName}");
            
            var success = gitService.CreateTag(tagName, tagMessage, isMajorRelease, projectName, dryRun);
            
            if (success)
            {
                var successMessage = dryRun 
                    ? $"[DRY RUN] Would create tag: {tagName}"
                    : $"Tag created: {tagName}";
                logger?.Invoke("Info", successMessage);
            }
            else if (!dryRun)
            {
                logger?.Invoke("Warning", $"Failed to create tag: {tagName} (tag may already exist)");
            }
            
            return success;
        }

        /// <summary>
        /// Determines if a semantic version represents a major release (x.0.0 with no prerelease)
        /// </summary>
        /// <param name="semVer">Semantic version to check</param>
        /// <returns>True if this is a major release</returns>
        public static bool IsMajorRelease(SemVer semVer)
        {
            return semVer != null && 
                   semVer.Minor == 0 && 
                   semVer.Patch == 0 && 
                   string.IsNullOrEmpty(semVer.PreRelease);
        }

        /// <summary>
        /// Generates the appropriate tag name for a version
        /// </summary>
        /// <param name="version">Version string</param>
        /// <param name="projectName">Name of the project</param>
        /// <param name="tagPrefix">Tag prefix (usually "v")</param>
        /// <param name="isMajorRelease">Whether this is a major release</param>
        /// <returns>Generated tag name</returns>
        public static string GenerateTagName(string version, string projectName, string tagPrefix, bool isMajorRelease)
        {
            return isMajorRelease 
                ? $"{tagPrefix}{version}"
                : $"{projectName}/{tagPrefix}{version}";
        }

        /// <summary>
        /// Generates the default tag message for a version
        /// </summary>
        /// <param name="version">Version string</param>
        /// <param name="projectName">Name of the project</param>
        /// <returns>Generated tag message</returns>
        public static string GenerateTagMessage(string version, string projectName)
        {
            return $"Version {version} for {projectName}";
        }
    }
}