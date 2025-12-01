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
/// MSBuild task that detects whether a project has changes since a specific tag or commit.
/// This is a lightweight alternative to MonoRepoVersionTask when you only need to check for changes
/// without calculating a full version.
/// </summary>
public class HasChangesTask : Task
{
    /// <summary>
    /// Path to the project file to check for changes
    /// </summary>
    [Required]
    public string ProjectPath { get; set; }

    /// <summary>
    /// Path to start searching for the Git repository root.
    /// If not specified, will start from the project directory.
    /// </summary>
    public string RepoRoot { get; set; }

    /// <summary>
    /// List of project dependencies to check for changes
    /// </summary>
    public ITaskItem[] Dependencies { get; set; }

    /// <summary>
    /// Custom tag prefix for version tags (default: v)
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    /// <summary>
    /// Specific tag to compare against. If not specified, will find the latest version tag.
    /// </summary>
    public string SinceTag { get; set; }

    /// <summary>
    /// Specific commit SHA to compare against. Takes precedence over SinceTag.
    /// </summary>
    public string SinceCommit { get; set; }

    /// <summary>
    /// Enable file pattern-based change detection for classification
    /// </summary>
    public bool ChangeDetectionEnabled { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of file patterns to ignore (won't count as changes)
    /// </summary>
    public string IgnoreFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that indicate major changes
    /// </summary>
    public string MajorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that indicate minor changes
    /// </summary>
    public string MinorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that indicate patch changes
    /// </summary>
    public string PatchFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of additional directories to monitor for changes
    /// </summary>
    public string AdditionalMonitorPaths { get; set; }

    /// <summary>
    /// Debug mode for verbose logging
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Output parameter indicating whether the project has changes
    /// </summary>
    [Output]
    public bool HasChanges { get; set; }

    /// <summary>
    /// Output parameter containing the list of changed files
    /// </summary>
    [Output]
    public ITaskItem[] ChangedFiles { get; set; }

    /// <summary>
    /// Output parameter indicating the type of change (None, Patch, Minor, Major)
    /// </summary>
    [Output]
    public string ChangeType { get; set; }

    /// <summary>
    /// Output parameter with a human-readable reason for the change detection result
    /// </summary>
    [Output]
    public string ChangeReason { get; set; }

    /// <summary>
    /// Output parameter for the discovered Git repository root path
    /// </summary>
    [Output]
    public string DiscoveredRepoRoot { get; set; }

    /// <summary>
    /// Output parameter for the tag or commit that was compared against
    /// </summary>
    [Output]
    public string ComparedAgainst { get; set; }

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

            Log.LogMessage(MessageImportance.High, $"[HasChanges] Checking for changes in {ProjectPath}");

            // Create logger function for core services
            var logger = MSBuildLoggerFactory.CreateMSBuildLogger(Log, Debug, false);

            // Discover Git repository root
            var gitRepoRoot = RepositoryService.DiscoverRepository(searchStartPath, logger, "search start path");
            if (gitRepoRoot == null)
            {
                Log.LogError(string.Format(RepositoryService.NoRepositoryFoundError, searchStartPath));
                Log.LogError(RepositoryService.EnsureGitRepositoryMessage);
                return false;
            }

            DiscoveredRepoRoot = gitRepoRoot;

            // Create change detection service
            using var changeDetectionService = new HasChangesService(gitRepoRoot, logger);

            // Prepare request
            var dependencies = Dependencies?.Select(d => d.ItemSpec).ToList() ?? new List<string>();
            var request = new HasChangesRequest
            {
                ProjectPath = ProjectPath,
                RepoRoot = gitRepoRoot,
                Dependencies = dependencies,
                TagPrefix = TagPrefix,
                SinceTag = SinceTag,
                SinceCommit = SinceCommit,
                ChangeDetectionEnabled = ChangeDetectionEnabled,
                IgnorePatterns = ParsePatternString(IgnoreFilePatterns),
                MajorPatterns = ParsePatternString(MajorFilePatterns),
                MinorPatterns = ParsePatternString(MinorFilePatterns),
                PatchPatterns = ParsePatternString(PatchFilePatterns),
                AdditionalMonitorPaths = ParsePatternString(AdditionalMonitorPaths),
                Debug = Debug
            };

            // Execute change detection
            var result = changeDetectionService.DetectChanges(request);

            // Set output parameters
            HasChanges = result.HasChanges;
            ChangeType = result.ChangeType.ToString();
            ChangeReason = result.Reason;
            ComparedAgainst = result.ComparedAgainst;

            // Convert changed files to task items
            if (result.ChangedFiles != null && result.ChangedFiles.Count > 0)
            {
                ChangedFiles = result.ChangedFiles
                    .Select(f => new TaskItem(f) as ITaskItem)
                    .ToArray();
            }
            else
            {
                ChangedFiles = Array.Empty<ITaskItem>();
            }

            // Log results
            if (HasChanges)
            {
                Log.LogMessage(MessageImportance.High,
                    $"[HasChanges] Changes detected: {ChangeType} - {ChangeReason}");
                if (Debug && ChangedFiles.Length > 0)
                {
                    Log.LogMessage(MessageImportance.High,
                        $"[HasChanges] Changed files: {string.Join(", ", ChangedFiles.Take(10).Select(f => f.ItemSpec))}");
                    if (ChangedFiles.Length > 10)
                    {
                        Log.LogMessage(MessageImportance.High,
                            $"[HasChanges] ... and {ChangedFiles.Length - 10} more files");
                    }
                }
            }
            else
            {
                Log.LogMessage(MessageImportance.High,
                    $"[HasChanges] No changes detected since {ComparedAgainst}");
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
    /// Parses a semicolon-separated string into a list of patterns
    /// </summary>
    private static List<string> ParsePatternString(string patternsString)
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
}
