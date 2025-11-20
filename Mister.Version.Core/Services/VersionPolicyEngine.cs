using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for managing version policies across projects in a monorepo
    /// </summary>
    public class VersionPolicyEngine : IVersionPolicyEngine
    {
        /// <summary>
        /// Get the version group that a project belongs to, if any
        /// </summary>
        public VersionGroup GetProjectGroup(string projectName, VersionPolicyConfig config)
        {
            if (config == null || config.Groups == null || string.IsNullOrEmpty(projectName))
                return null;

            foreach (var kvp in config.Groups)
            {
                var group = kvp.Value;
                if (group.Projects != null && group.Projects.Any(pattern => MatchesPattern(projectName, pattern)))
                {
                    return group;
                }
            }

            return null;
        }

        /// <summary>
        /// Determine if a project should use its own independent versioning
        /// </summary>
        public bool IsIndependent(string projectName, VersionPolicyConfig config)
        {
            if (config == null)
                return true; // Default to independent if no config

            // If policy is LockStep, no project is independent
            if (config.Policy == VersionPolicy.LockStep)
                return false;

            // If policy is Independent, all projects are independent unless in a group
            if (config.Policy == VersionPolicy.Independent)
                return GetProjectGroup(projectName, config) == null;

            // If policy is Grouped, check if project is in a group
            if (config.Policy == VersionPolicy.Grouped)
            {
                var group = GetProjectGroup(projectName, config);
                // Projects not in any group are independent
                if (group == null)
                    return true;

                // Projects in a group with Independent strategy are independent
                return group.Strategy == VersionPolicy.Independent;
            }

            return true;
        }

        /// <summary>
        /// Get all projects that share a version with the specified project
        /// </summary>
        public List<string> GetLinkedProjects(string projectName, List<string> allProjects, VersionPolicyConfig config)
        {
            var linkedProjects = new List<string>();

            if (config == null || allProjects == null)
            {
                linkedProjects.Add(projectName);
                return linkedProjects;
            }

            // If LockStep policy, all projects are linked
            if (config.Policy == VersionPolicy.LockStep)
            {
                return new List<string>(allProjects);
            }

            // Find the group this project belongs to
            var group = GetProjectGroup(projectName, config);

            // If not in a group, or group strategy is Independent, project versions alone
            if (group == null || group.Strategy == VersionPolicy.Independent)
            {
                linkedProjects.Add(projectName);
                return linkedProjects;
            }

            // Find all projects in the same group
            foreach (var project in allProjects)
            {
                if (group.Projects.Any(pattern => MatchesPattern(project, pattern)))
                {
                    linkedProjects.Add(project);
                }
            }

            return linkedProjects;
        }

        /// <summary>
        /// Validate version policy configuration for conflicts and errors
        /// </summary>
        public List<string> ValidateConfiguration(VersionPolicyConfig config, List<string> allProjects)
        {
            var errors = new List<string>();

            if (config == null)
                return errors; // No config is valid (defaults apply)

            // Check for projects in multiple groups
            var projectGroupMapping = new Dictionary<string, List<string>>();

            if (config.Groups != null)
            {
                foreach (var kvp in config.Groups)
                {
                    var groupName = kvp.Key;
                    var group = kvp.Value;

                    if (group.Projects == null || group.Projects.Count == 0)
                    {
                        errors.Add($"Version group '{groupName}' has no projects defined");
                        continue;
                    }

                    // Check each project in allProjects against this group's patterns
                    foreach (var project in allProjects)
                    {
                        foreach (var pattern in group.Projects)
                        {
                            if (MatchesPattern(project, pattern))
                            {
                                if (!projectGroupMapping.ContainsKey(project))
                                {
                                    projectGroupMapping[project] = new List<string>();
                                }
                                projectGroupMapping[project].Add(groupName);
                            }
                        }
                    }
                }

                // Check for projects in multiple groups
                foreach (var kvp in projectGroupMapping)
                {
                    if (kvp.Value.Count > 1)
                    {
                        errors.Add($"Project '{kvp.Key}' belongs to multiple groups: {string.Join(", ", kvp.Value)}");
                    }
                }

                // Validate base versions if specified
                foreach (var kvp in config.Groups)
                {
                    var groupName = kvp.Key;
                    var group = kvp.Value;

                    if (!string.IsNullOrEmpty(group.BaseVersion))
                    {
                        if (!SemVer.TryParse(group.BaseVersion, out _))
                        {
                            errors.Add($"Version group '{groupName}' has invalid base version: {group.BaseVersion}");
                        }
                    }
                }
            }

            // Validate policy-specific constraints
            if (config.Policy == VersionPolicy.Grouped && (config.Groups == null || config.Groups.Count == 0))
            {
                errors.Add("Version policy is set to 'Grouped' but no groups are defined");
            }

            return errors;
        }

        /// <summary>
        /// Determine the coordinated version for a group of projects
        /// Uses the highest version found among all projects in the group
        /// </summary>
        public string CoordinateGroupVersion(Dictionary<string, VersionResult> projectVersions, VersionGroup group)
        {
            if (projectVersions == null || projectVersions.Count == 0)
                return group.BaseVersion ?? "0.1.0";

            // If group has a base version, use it
            if (!string.IsNullOrEmpty(group.BaseVersion))
                return group.BaseVersion;

            // Find the highest version among projects in the group
            SemVer highestVersion = null;

            foreach (var kvp in projectVersions)
            {
                var projectName = kvp.Key;
                var versionResult = kvp.Value;

                // Check if this project is in the group
                if (group.Projects.Any(pattern => MatchesPattern(projectName, pattern)))
                {
                    if (versionResult?.SemVer != null)
                    {
                        if (highestVersion == null || versionResult.SemVer.CompareTo(highestVersion) > 0)
                        {
                            highestVersion = versionResult.SemVer;
                        }
                    }
                }
            }

            return highestVersion?.ToString() ?? group.BaseVersion ?? "0.1.0";
        }

        /// <summary>
        /// Check if a project name matches a pattern (supports wildcards)
        /// </summary>
        private bool MatchesPattern(string projectName, string pattern)
        {
            if (string.IsNullOrEmpty(projectName) || string.IsNullOrEmpty(pattern))
                return false;

            // Exact match
            if (projectName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Wildcard match
            if (pattern.Contains("*"))
            {
                var regex = WildcardToRegex(pattern);
                return regex.IsMatch(projectName);
            }

            return false;
        }

        /// <summary>
        /// Convert a wildcard pattern to a regular expression
        /// </summary>
        private Regex WildcardToRegex(string pattern)
        {
            // Escape special regex characters except *
            var regexPattern = Regex.Escape(pattern).Replace("\\*", ".*");

            // Anchor the pattern
            regexPattern = "^" + regexPattern + "$";

            return new Regex(regexPattern, RegexOptions.IgnoreCase);
        }
    }
}
