using System.Collections.Generic;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for managing version policies across projects in a monorepo
    /// </summary>
    public interface IVersionPolicyEngine
    {
        /// <summary>
        /// Get the version group that a project belongs to, if any
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="config">Version policy configuration</param>
        /// <returns>Version group if found, null otherwise</returns>
        VersionGroup GetProjectGroup(string projectName, VersionPolicyConfig config);

        /// <summary>
        /// Determine if a project should use its own independent versioning
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="config">Version policy configuration</param>
        /// <returns>True if project versions independently</returns>
        bool IsIndependent(string projectName, VersionPolicyConfig config);

        /// <summary>
        /// Get all projects that share a version with the specified project
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="allProjects">List of all project names in the repository</param>
        /// <param name="config">Version policy configuration</param>
        /// <returns>List of project names that share a version (includes the specified project)</returns>
        List<string> GetLinkedProjects(string projectName, List<string> allProjects, VersionPolicyConfig config);

        /// <summary>
        /// Validate version policy configuration for conflicts and errors
        /// </summary>
        /// <param name="config">Version policy configuration to validate</param>
        /// <param name="allProjects">List of all project names in the repository</param>
        /// <returns>List of validation error messages, empty if valid</returns>
        List<string> ValidateConfiguration(VersionPolicyConfig config, List<string> allProjects);

        /// <summary>
        /// Determine the coordinated version for a group of projects
        /// Uses the highest version found among all projects in the group
        /// </summary>
        /// <param name="projectVersions">Dictionary of project names to their calculated versions</param>
        /// <param name="group">Version group</param>
        /// <returns>Coordinated version to use for all projects in the group</returns>
        string CoordinateGroupVersion(Dictionary<string, VersionResult> projectVersions, VersionGroup group);
    }
}
