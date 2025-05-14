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
    /// <summary>
    /// Path to the project file being built
    /// </summary>
    [Required]
    public string ProjectPath { get; set; }

    /// <summary>
    /// Path to the root of the monorepo
    /// </summary>
    [Required]
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

    public override bool Execute()
    {
        try
        {
            Log.LogMessage(MessageImportance.High, $"Starting Mister.Version versioning for {ProjectPath}");

            // Create logger function for core services
            Action<string, string> logger = (level, message) =>
            {
                var importance = level switch
                {
                    "Error" => MessageImportance.High,
                    "Warning" => MessageImportance.Normal,
                    "Info" => MessageImportance.High,
                    "Debug" when Debug || ExtraDebug => MessageImportance.High,
                    _ => MessageImportance.Low
                };

                if (importance != MessageImportance.Low)
                {
                    Log.LogMessage(importance, $"[{level}] {message}");
                }
            };

            // Initialize services
            using var gitService = new GitService(RepoRoot);
            var versionCalculator = new VersionCalculator(gitService, logger);

            // Prepare version options
            var dependencies = Dependencies?.Select(d => d.ItemSpec).ToList() ?? new List<string>();
            var projectName = Path.GetFileNameWithoutExtension(ProjectPath);

            var versionOptions = new VersionOptions
            {
                RepoRoot = RepoRoot,
                ProjectPath = ProjectPath,
                ProjectName = projectName,
                TagPrefix = TagPrefix,
                ForceVersion = ForceVersion,
                Dependencies = dependencies,
                Debug = Debug,
                ExtraDebug = ExtraDebug,
                SkipTestProjects = SkipTestProjects,
                SkipNonPackableProjects = SkipNonPackableProjects,
                IsTestProject = IsTestProject,
                IsPackable = IsPackable
            };

            // Calculate version
            var versionResult = versionCalculator.CalculateVersion(versionOptions);

            // Set output parameters
            Version = versionResult.Version;
            VersionChanged = versionResult.VersionChanged;

            Log.LogMessage(MessageImportance.High, $"Calculated version: {Version} for {projectName}");

            if (VersionChanged)
            {
                Log.LogMessage(MessageImportance.High, $"Version changed: {versionResult.ChangeReason}");
            }

            // Update the project file if enabled (disabled by default)
            if (UpdateProjectFile && !string.IsNullOrEmpty(Version))
            {
                UpdateProjectFileVersion();
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
        using var gitService = new GitService(RepoRoot);
        return gitService.GetBranchType(branchName);
    }

    /// <summary>
    /// Extracts version from release branch name
    /// </summary>
    internal SemVer ExtractReleaseVersion(string branchName)
    {
        using var gitService = new GitService(RepoRoot);
        return gitService.ExtractReleaseVersion(branchName, TagPrefix);
    }

    /// <summary>
    /// Parses a string into a semantic version
    /// </summary>
    internal SemVer ParseSemVer(string version)
    {
        using var gitService = new GitService(RepoRoot);
        return gitService.ParseSemVer(version);
    }
}