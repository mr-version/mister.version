using System;
using System.IO;
using YamlDotNet.Serialization;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public static class ConfigurationService
    {
        private static readonly string[] DefaultConfigFiles = new[] 
        { 
            "mr-version.yml", 
            "mr-version.yaml", 
            "mister-version.yaml" 
        };

        /// <summary>
        /// Loads configuration from a YAML file or discovers default configuration files
        /// </summary>
        /// <param name="configPath">Explicit config file path, or null to auto-discover</param>
        /// <param name="repoRoot">Repository root directory to search for default configs</param>
        /// <param name="logger">Logger for debug/error messages</param>
        /// <returns>Configuration object or null if not found/failed to load</returns>
        public static VersionConfig LoadConfiguration(string configPath, string repoRoot, Action<string, string> logger)
        {
            var configToLoad = configPath;
            
            // Auto-discover default config files if no explicit path provided
            if (string.IsNullOrEmpty(configToLoad))
            {
                configToLoad = DiscoverDefaultConfigFile(repoRoot, logger);
            }
            
            if (string.IsNullOrEmpty(configToLoad) || !File.Exists(configToLoad))
            {
                return null;
            }

            return LoadConfigFromYaml(configToLoad, logger);
        }

        /// <summary>
        /// Discovers default configuration files in the repository root
        /// </summary>
        /// <param name="repoRoot">Repository root directory</param>
        /// <param name="logger">Logger for debug messages</param>
        /// <returns>Path to discovered config file or null if none found</returns>
        public static string DiscoverDefaultConfigFile(string repoRoot, Action<string, string> logger)
        {
            foreach (var defaultConfig in DefaultConfigFiles)
            {
                var configPath = Path.Combine(repoRoot, defaultConfig);
                if (File.Exists(configPath))
                {
                    logger?.Invoke("Info", $"Found configuration file: {defaultConfig}");
                    return configPath;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Loads and deserializes a YAML configuration file
        /// </summary>
        /// <param name="configPath">Path to the YAML config file</param>
        /// <param name="logger">Logger for error messages</param>
        /// <returns>Configuration object or null if failed to load</returns>
        public static VersionConfig LoadConfigFromYaml(string configPath, Action<string, string> logger)
        {
            try
            {
                var yaml = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                    .Build();
                
                var config = deserializer.Deserialize<VersionConfig>(yaml);
                logger?.Invoke("Info", $"Loaded configuration from {configPath}");

                // Validate version policy configuration if present
                if (config?.VersionPolicy != null)
                {
                    ValidateVersionPolicy(config.VersionPolicy, logger);
                }

                return config;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Warning", $"Failed to load configuration from {configPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates version policy configuration and logs any errors
        /// </summary>
        /// <param name="policyConfig">Version policy configuration to validate</param>
        /// <param name="logger">Logger for error messages</param>
        public static void ValidateVersionPolicy(VersionPolicyConfig policyConfig, Action<string, string> logger)
        {
            if (policyConfig == null)
                return;

            var policyEngine = new VersionPolicyEngine();

            // For validation, we need a list of projects. Since we don't have it at config load time,
            // we'll do basic structural validation here. Full validation will happen during version calculation
            // when all projects are known.

            logger?.Invoke("Info", $"Version policy: {policyConfig.Policy}");

            if (policyConfig.Policy == VersionPolicy.Grouped && (policyConfig.Groups == null || policyConfig.Groups.Count == 0))
            {
                logger?.Invoke("Warning", "Version policy is set to 'Grouped' but no groups are defined");
            }

            if (policyConfig.Groups != null)
            {
                foreach (var kvp in policyConfig.Groups)
                {
                    var groupName = kvp.Key;
                    var group = kvp.Value;

                    if (group.Projects == null || group.Projects.Count == 0)
                    {
                        logger?.Invoke("Warning", $"Version group '{groupName}' has no projects defined");
                    }
                    else
                    {
                        logger?.Invoke("Info", $"  Group '{groupName}': {group.Projects.Count} project pattern(s), strategy: {group.Strategy}");
                    }

                    if (!string.IsNullOrEmpty(group.BaseVersion))
                    {
                        if (!SemVer.TryParse(group.BaseVersion, out _))
                        {
                            logger?.Invoke("Warning", $"Version group '{groupName}' has invalid base version: {group.BaseVersion}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Applies configuration settings to override default values
        /// </summary>
        /// <param name="config">Configuration object</param>
        /// <param name="projectName">Name of the project for project-specific overrides</param>
        /// <param name="baseValues">Base configuration values to override</param>
        /// <param name="logger">Logger for info messages</param>
        /// <returns>Updated configuration values</returns>
        public static ConfigurationOverrides ApplyConfiguration(
            VersionConfig config, 
            string projectName, 
            ConfigurationOverrides baseValues,
            Action<string, string> logger)
        {
            if (config == null)
                return baseValues;

            var result = new ConfigurationOverrides
            {
                BaseVersion = config.BaseVersion ?? baseValues.BaseVersion,
                DefaultIncrement = config.DefaultIncrement ?? baseValues.DefaultIncrement,
                PrereleaseType = config.PrereleaseType ?? baseValues.PrereleaseType,
                TagPrefix = config.TagPrefix ?? baseValues.TagPrefix,
                SkipTestProjects = config.SkipTestProjects ?? baseValues.SkipTestProjects,
                SkipNonPackableProjects = config.SkipNonPackableProjects ?? baseValues.SkipNonPackableProjects,
                ForceVersion = baseValues.ForceVersion
            };

            // Check for project-specific configuration
            if (config.Projects != null && 
                !string.IsNullOrEmpty(projectName) && 
                config.Projects.TryGetValue(projectName, out var projectConfig))
            {
                result.PrereleaseType = projectConfig.PrereleaseType ?? result.PrereleaseType;
                result.ForceVersion = projectConfig.ForceVersion ?? result.ForceVersion;
                logger?.Invoke("Info", $"Applied project-specific configuration for {projectName}");
            }

            return result;
        }
    }

    /// <summary>
    /// Container for configuration values that can be overridden
    /// </summary>
    public class ConfigurationOverrides
    {
        public string BaseVersion { get; set; }
        public string DefaultIncrement { get; set; }
        public string PrereleaseType { get; set; }
        public string TagPrefix { get; set; }
        public bool? SkipTestProjects { get; set; }
        public bool? SkipNonPackableProjects { get; set; }
        public string ForceVersion { get; set; }
    }
}