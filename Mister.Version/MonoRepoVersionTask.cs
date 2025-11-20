using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version;

/// <summary>
/// MSBuild task that automatically versions projects in a monorepo based on git history
/// and change detection rules without requiring version changes to be committed
/// </summary>
public class MonoRepoVersionTask : Task
{
    // Static cache shared across all task instances in the same build
    // This significantly improves performance in multi-project builds
    private static readonly object _cacheLock = new object();
    private static VersionCache _staticCache;
    private static string _cacheRepoRoot;

    /// <summary>
    /// Path to the project file being built
    /// </summary>
    [Required]
    public string ProjectPath { get; set; }

    /// <summary>
    /// Path to start searching for the Git repository root.
    /// If not specified, will start from the project directory.
    /// </summary>
    public string RepoRoot { get; set; }

    /// <summary>
    /// Output parameter for the calculated version
    /// </summary>
    [Output]
    public string Version { get; set; }

    /// <summary>
    /// Output parameter that indicates if changes were detected for this project
    /// </summary>
    [Output]
    public bool VersionChanged { get; set; }

    /// <summary>
    /// Output parameter for the discovered Git repository root path
    /// </summary>
    [Output]
    public string DiscoveredRepoRoot { get; set; }

    /// <summary>
    /// Whether to automatically update the project file with the new version
    /// This is set to FALSE by default to avoid requiring commits for version changes
    /// </summary>
    public bool UpdateProjectFile { get; set; } = false;

    /// <summary>
    /// Optional parameter to force a specific version
    /// </summary>
    public string ForceVersion { get; set; }

    /// <summary>
    /// List of project dependencies to check for changes
    /// </summary>
    public ITaskItem[] Dependencies { get; set; }

    /// <summary>
    /// Custom tag prefix for version tags (default: v)
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    /// <summary>
    /// Debug mode for verbose logging
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Extra debug info to display dependency graph and change details
    /// </summary>
    public bool ExtraDebug { get; set; } = false;

    /// <summary>
    /// Whether to skip versioning for test projects
    /// </summary>
    public bool SkipTestProjects { get; set; } = true;

    /// <summary>
    /// Whether to skip versioning for non-packable projects
    /// </summary>
    public bool SkipNonPackableProjects { get; set; } = true;

    /// <summary>
    /// Whether this project is a test project
    /// </summary>
    public bool IsTestProject { get; set; } = false;

    /// <summary>
    /// Whether this project is packable
    /// </summary>
    public bool IsPackable { get; set; } = true;

    /// <summary>
    /// Prerelease type to use for main/dev branches when incrementing versions
    /// Options: none, alpha, beta, rc
    /// Default: none (no prerelease suffix)
    /// </summary>
    public string PrereleaseType { get; set; } = "none";
    
    /// <summary>
    /// Whether to create a git tag for the calculated version
    /// </summary>
    public bool CreateTag { get; set; } = false;
    
    /// <summary>
    /// Custom message for the git tag (used with CreateTag)
    /// </summary>
    public string TagMessage { get; set; }

    /// <summary>
    /// Path to version configuration YAML file
    /// </summary>
    public string ConfigFile { get; set; }
    
    /// <summary>
    /// Whether to run in dry-run mode (show what would be done without making changes)
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Whether to enable caching of version calculations across builds (default: true)
    /// Set to false to disable caching for testing or debugging
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// Enable conventional commits analysis for semantic version bump detection
    /// </summary>
    public bool ConventionalCommitsEnabled { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of commit patterns for major version bumps
    /// </summary>
    public string MajorPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns for minor version bumps
    /// </summary>
    public string MinorPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns for patch version bumps
    /// </summary>
    public string PatchPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns to ignore for versioning
    /// </summary>
    public string IgnorePatterns { get; set; }

    /// <summary>
    /// Enable file pattern-based change detection for smart versioning
    /// </summary>
    public bool ChangeDetectionEnabled { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of file patterns to ignore (won't trigger version bumps)
    /// </summary>
    public string IgnoreFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require major version bumps
    /// </summary>
    public string MajorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require minor version bumps
    /// </summary>
    public string MinorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require patch version bumps
    /// </summary>
    public string PatchFilePatterns { get; set; }

    /// <summary>
    /// Source-only mode: only version when source code changes (ignore test/doc changes)
    /// </summary>
    public bool SourceOnlyMode { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of additional directories to monitor for changes
    /// </summary>
    public string AdditionalMonitorPaths { get; set; }

    /// <summary>
    /// Enable automatic changelog generation during build
    /// </summary>
    public bool GenerateChangelog { get; set; } = false;

    /// <summary>
    /// Changelog output format (markdown, text, json)
    /// </summary>
    public string ChangelogFormat { get; set; } = "markdown";

    /// <summary>
    /// Changelog output file path
    /// </summary>
    public string ChangelogOutputPath { get; set; } = "CHANGELOG.md";

    /// <summary>
    /// Repository URL for generating links in changelog
    /// </summary>
    public string ChangelogRepositoryUrl { get; set; }

    /// <summary>
    /// Include commit authors in changelog
    /// </summary>
    public bool ChangelogIncludeAuthors { get; set; } = false;

    /// <summary>
    /// Enable shallow clone support for repos cloned with --depth
    /// </summary>
    public bool ShallowCloneSupport { get; set; } = true;

    /// <summary>
    /// Fallback version for shallow clones with limited history
    /// </summary>
    public string ShallowCloneFallbackVersion { get; set; }

    /// <summary>
    /// Enable submodule change detection
    /// </summary>
    public bool SubmoduleSupport { get; set; } = false;

    /// <summary>
    /// Custom tag patterns (semicolon-separated)
    /// </summary>
    public string CustomTagPatterns { get; set; }

    /// <summary>
    /// Enable branch-based versioning strategies
    /// </summary>
    public bool BranchBasedVersioning { get; set; } = false;

    /// <summary>
    /// Branch versioning rules (semicolon-separated)
    /// </summary>
    public string BranchVersioningRules { get; set; }

    /// <summary>
    /// Include branch name in version build metadata
    /// </summary>
    public bool IncludeBranchInMetadata { get; set; } = false;

    /// <summary>
    /// Validate that version tags are reachable from current branch
    /// </summary>
    public bool ValidateTagAncestry { get; set; } = true;

    /// <summary>
    /// Fetch depth for shallow clone operations
    /// </summary>
    public int FetchDepth { get; set; } = 50;

    /// <summary>
    /// Gets or creates the static cache for the current repository
    /// </summary>
    private VersionCache GetOrCreateCache(string repoRoot, string currentHeadSha)
    {
        if (!EnableCache)
            return null;

        lock (_cacheLock)
        {
            // If cache doesn't exist or is for a different repo, create a new one
            if (_staticCache == null || _cacheRepoRoot != repoRoot)
            {
                _staticCache = new VersionCache(repoRoot, currentHeadSha);
                _cacheRepoRoot = repoRoot;
                Log.LogMessage(MessageImportance.Low, $"[Mister.Version] Created new cache for repository: {repoRoot}");
            }
            else
            {
                // Validate cache and invalidate if HEAD changed
                if (_staticCache.ValidateAndInvalidate(currentHeadSha))
                {
                    Log.LogMessage(MessageImportance.Low, "[Mister.Version] Cache invalidated due to HEAD change");
                }
            }

            return _staticCache;
        }
    }

    /// <summary>
    /// Clears the static cache (useful for testing)
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _staticCache = null;
            _cacheRepoRoot = null;
        }
    }

    public override bool Execute()
    {
        try
        {
            // Validate required properties
            if (string.IsNullOrEmpty(ProjectPath))
                throw new InvalidOperationException("ProjectPath is required but was not provided.");

            // Use project directory as starting point if RepoRoot is not specified
            var searchStartPath = string.IsNullOrEmpty(RepoRoot) 
                ? Path.GetDirectoryName(ProjectPath) 
                : RepoRoot;
                
            Log.LogMessage(MessageImportance.High, $"Starting Mister.Version versioning for {ProjectPath}");

            // Create logger function for core services
            var logger = MSBuildLoggerFactory.CreateMSBuildLogger(Log, Debug, ExtraDebug);

            // Discover Git repository root
            var gitRepoRoot = RepositoryService.DiscoverRepository(searchStartPath, logger, "search start path");
            if (gitRepoRoot == null)
            {
                Log.LogError(string.Format(RepositoryService.NoRepositoryFoundError, searchStartPath));
                Log.LogError(RepositoryService.EnsureGitRepositoryMessage);
                return false;
            }

            // Get current HEAD SHA for cache validation
            var currentHeadSha = GetRepositoryHeadSha(gitRepoRoot);

            // Get or create cache
            var cache = GetOrCreateCache(gitRepoRoot, currentHeadSha);

            // Log cache statistics if in debug mode
            if (Debug && cache != null)
            {
                var stats = cache.GetStatistics();
                Log.LogMessage(MessageImportance.Low, $"[Mister.Version] Cache Stats: {stats}");
            }

            // Initialize versioning service with cache
            using var versioningService = new VersioningService(gitRepoRoot, logger, cache);

            // Prepare version request
            var dependencies = Dependencies?.Select(d => d.ItemSpec).ToList() ?? new List<string>();
            var versionRequest = new VersioningRequest
            {
                RepoRoot = gitRepoRoot,
                ProjectPath = ProjectPath,
                ConfigFile = ConfigFile,
                PrereleaseType = PrereleaseType,
                TagPrefix = TagPrefix,
                ForceVersion = ForceVersion,
                Dependencies = dependencies,
                Debug = Debug,
                ExtraDebug = ExtraDebug,
                SkipTestProjects = SkipTestProjects,
                SkipNonPackableProjects = SkipNonPackableProjects,
                IsTestProject = IsTestProject,
                IsPackable = IsPackable,
                ConventionalCommitsEnabled = ConventionalCommitsEnabled,
                MajorPatterns = MajorPatterns,
                MinorPatterns = MinorPatterns,
                PatchPatterns = PatchPatterns,
                IgnorePatterns = IgnorePatterns,
                ChangeDetectionEnabled = ChangeDetectionEnabled,
                IgnoreFilePatterns = IgnoreFilePatterns,
                MajorFilePatterns = MajorFilePatterns,
                MinorFilePatterns = MinorFilePatterns,
                PatchFilePatterns = PatchFilePatterns,
                SourceOnlyMode = SourceOnlyMode,
                AdditionalMonitorPaths = AdditionalMonitorPaths,
                ShallowCloneSupport = ShallowCloneSupport,
                ShallowCloneFallbackVersion = ShallowCloneFallbackVersion,
                SubmoduleSupport = SubmoduleSupport,
                CustomTagPatterns = CustomTagPatterns,
                BranchBasedVersioning = BranchBasedVersioning,
                BranchVersioningRules = BranchVersioningRules,
                IncludeBranchInMetadata = IncludeBranchInMetadata,
                ValidateTagAncestry = ValidateTagAncestry,
                FetchDepth = FetchDepth
            };

            // Calculate version
            var versionResult = versioningService.CalculateProjectVersion(versionRequest);
            
            if (!versionResult.Success)
            {
                Log.LogError($"Version calculation failed: {versionResult.ErrorMessage}");
                return false;
            }

            // Set output parameters
            Version = versionResult.Version;
            VersionChanged = versionResult.VersionChanged;
            DiscoveredRepoRoot = gitRepoRoot;

            Log.LogMessage(MessageImportance.High, $"Calculated version: {Version} for {versionResult.ProjectName}");

            if (VersionChanged)
            {
                Log.LogMessage(MessageImportance.High, $"Version changed: {versionResult.ChangeReason}");
            }

            // Update the project file if enabled (disabled by default)
            if (UpdateProjectFile && !string.IsNullOrEmpty(Version))
            {
                if (DryRun)
                {
                    Log.LogMessage(MessageImportance.High, $"[DRY RUN] Would update project file {ProjectPath} with version {Version}");
                }
                else
                {
                    UpdateProjectFileVersion();
                }
            }
            
            // Create git tag if requested
            if (CreateTag && !string.IsNullOrEmpty(Version))
            {
                var tagSuccess = versioningService.CreateTag(versionResult, TagPrefix, TagMessage, DryRun);
                
                if (!tagSuccess && !DryRun)
                {
                    Log.LogWarning("Tag creation failed");
                }
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    /// <summary>
    /// Updates the project file with the calculated version
    /// Note: This is disabled by default to avoid requiring version changes to be committed
    /// </summary>
    private void UpdateProjectFileVersion()
    {
        try
        {
            var projectFile = File.ReadAllText(ProjectPath);

            // Update or add the Version element
            var versionRegex = new System.Text.RegularExpressions.Regex(@"<Version>.*?</Version>");
            if (versionRegex.IsMatch(projectFile))
            {
                // Update existing Version element
                projectFile = versionRegex.Replace(projectFile, $"<Version>{Version}</Version>");
            }
            else
            {
                // Add Version element after PropertyGroup opening tag
                var propertyGroupRegex = new System.Text.RegularExpressions.Regex(@"<PropertyGroup>");
                projectFile = propertyGroupRegex.Replace(projectFile, $"<PropertyGroup>\r\n    <Version>{Version}</Version>", 1);
            }

            File.WriteAllText(ProjectPath, projectFile);
            Log.LogMessage(MessageImportance.Normal, $"Updated project file with version {Version}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to update project file: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines the type of branch based on its name
    /// </summary>
    internal BranchType DetermineBranchType(string branchName)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot, 
            gitService => gitService.GetBranchType(branchName), 
            BranchType.Feature);
    }

    /// <summary>
    /// Extracts version from release branch name
    /// </summary>
    internal SemVer ExtractReleaseVersion(string branchName)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot, 
            gitService => gitService.ExtractReleaseVersion(branchName, TagPrefix), 
            null);
    }

    /// <summary>
    /// Parses a string into a semantic version
    /// </summary>
    internal SemVer ParseSemVer(string version)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot,
            gitService => gitService.ParseSemVer(version),
            null);
    }

    /// <summary>
    /// Gets the current HEAD SHA from a repository efficiently
    /// </summary>
    /// <param name="repoRoot">Repository root path</param>
    /// <returns>HEAD SHA or null if unavailable</returns>
    private static string GetRepositoryHeadSha(string repoRoot)
    {
        try
        {
            using (var repo = new LibGit2Sharp.Repository(repoRoot))
            {
                return repo.Head?.Tip?.Sha;
            }
        }
        catch
        {
            // If we can't get HEAD SHA, caching will be disabled
            return null;
        }
    }

}