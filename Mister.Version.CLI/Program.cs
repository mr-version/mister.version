using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommandLine;
using CommandLine.Text;
using LibGit2Sharp;
using Mister.Version.Core;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.CLI
{
    class Program
    {
        static int Main(string[] args)
        {
            try 
            {
                // Early debug output for CI debugging
                if (args.Contains("--debug"))
                {
                    Console.Error.WriteLine($"[DEBUG] CLI started with args: {string.Join(" ", args)}");
                    Console.Error.WriteLine($"[DEBUG] Working directory: {Directory.GetCurrentDirectory()}");
                    Console.Error.WriteLine($"[DEBUG] .NET version: {Environment.Version}");
                }
                
                var parser = new Parser(with => {
                    with.EnableDashDash = true;
                    with.AutoVersion = false;
                    with.AutoHelp = true;
                    with.HelpWriter = Console.Out;
                });
                
                if (args.Contains("--debug"))
                {
                    Console.Error.WriteLine($"[DEBUG] About to parse arguments: {string.Join(" ", args)}");
                }
                
                var result = parser.ParseArguments<ReportOptions, VersionOptions>(args);
                
                if (args.Contains("--debug"))
                {
                    Console.Error.WriteLine($"[DEBUG] Parse result type: {result.GetType().Name}");
                }
                
                return result.MapResult(
                        (ReportOptions opts) => {
                            if (args.Contains("--debug")) Console.Error.WriteLine($"[DEBUG] Running report command");
                            return RunReportCommand(opts);
                        },
                        (VersionOptions opts) => {
                            if (args.Contains("--debug")) Console.Error.WriteLine($"[DEBUG] Running version command");
                            return RunVersionCommand(opts);
                        },
                        errs => {
                            var errList = errs.ToList();
                            if (args.Contains("--debug")) 
                            {
                                Console.Error.WriteLine($"[DEBUG] Parser errors: {errList.Count}");
                                foreach (var err in errList)
                                {
                                    Console.Error.WriteLine($"[DEBUG] Error: {err.Tag} - {err}");
                                }
                            }
                            
                            // Handle help/version requests
                            if (errList.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError))
                            {
                                // Help or version was requested - return success
                                return 0;
                            }
                            
                            // Check if help was requested (any form)
                            if (args.Any(arg => arg == "--help" || arg == "-h" || arg == "-?" || arg == "help"))
                            {
                                // Check if it's a BadVerbSelectedError (no verb provided)
                                if (errList.Any(e => e.Tag == ErrorType.BadVerbSelectedError))
                                {
                                    // Show general help manually since AutoHelp won't trigger for BadVerb
                                    Console.WriteLine("Mister.Version CLI - Semantic versioning for monorepos");
                                    Console.WriteLine("Copyright (c) 2025 Mister.Version Team");
                                    Console.WriteLine();
                                    Console.WriteLine("Usage: mr-version <command> [options]");
                                    Console.WriteLine();
                                    Console.WriteLine("Commands:");
                                    Console.WriteLine("  report    Generate a version report for projects in the repository");
                                    Console.WriteLine("  version   Calculate the version for a specific project");
                                    Console.WriteLine();
                                    Console.WriteLine("Use 'mr-version <command> --help' for more information about a command.");
                                    Console.WriteLine();
                                    Console.WriteLine("Options:");
                                    Console.WriteLine("  --help       Display this help screen.");
                                    Console.WriteLine("  --version    Display version information.");
                                    return 0;
                                }
                                // For other cases, the library should have shown help
                                return 0;
                            }
                            
                            // Other errors - return failure
                            return 1;
                        });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Unhandled exception in Main: {ex.Message}");
                Console.Error.WriteLine($"[ERROR] Exception type: {ex.GetType().Name}");
                Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"[ERROR] Inner exception: {ex.InnerException.Message}");
                    Console.Error.WriteLine($"[ERROR] Inner stack trace: {ex.InnerException.StackTrace}");
                }
                return 1;
            }
        }

        static int RunReportCommand(ReportOptions options)
        {
            try
            {
                var reporter = new VersionReporter(options);
                return reporter.GenerateReport();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (options.Debug)
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return 1;
            }
        }

        static int RunVersionCommand(VersionOptions options)
        {
            try
            {
                if (options.Debug)
                {
                    Console.Error.WriteLine($"[DEBUG] RunVersionCommand called");
                    Console.Error.WriteLine($"[DEBUG] Project path: {options.ProjectPath}");
                    Console.Error.WriteLine($"[DEBUG] Repo path: {options.RepoPath}");
                }
                
                var calculator = new SingleProjectVersionCalculator(options);
                return calculator.CalculateVersion();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (options.Debug)
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return 1;
            }
        }
    }

    [Verb("report", HelpText = "Generate a version report for projects in the repository")]
    public class ReportOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project-dir", Required = false, HelpText = "Path to the directory containing projects")]
        public string ProjectDir { get; set; }

        [Option('o', "output", Required = false, HelpText = "Output format: text, json, csv, or graph", Default = "text")]
        public string OutputFormat { get; set; }

        [Option('f', "file", Required = false, HelpText = "Output file path (if not specified, outputs to console)")]
        public string OutputFile { get; set; }

        [Option('b', "branch", Required = false, HelpText = "Branch to report versions for (defaults to current branch)")]
        public string Branch { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags", Default = "v")]
        public string TagPrefix { get; set; }

        [Option("include-commits", Required = false, HelpText = "Include commit information in the report", Default = true)]
        public bool IncludeCommits { get; set; }

        [Option("include-dependencies", Required = false, HelpText = "Include dependency information in the report", Default = true)]
        public bool IncludeDependencies { get; set; }

        [Option("include-test-projects", Required = false, HelpText = "Include test projects in the report", Default = false)]
        public bool IncludeTestProjects { get; set; }

        [Option("include-non-packable", Required = false, HelpText = "Include non-packable projects in the report", Default = false)]
        public bool IncludeNonPackableProjects { get; set; }

        [Option("graph-format", Required = false, HelpText = "Graph format when output is 'graph': mermaid, dot, or ascii", Default = "mermaid")]
        public string GraphFormat { get; set; }

        [Option("show-versions", Required = false, HelpText = "Show version numbers in graph nodes", Default = true)]
        public bool ShowVersions { get; set; }

        [Option("changed-only", Required = false, HelpText = "Show only projects with version changes", Default = false)]
        public bool ShowChangedOnly { get; set; }

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }

        [Option("config-file", Required = false, HelpText = "Path to mr-version.yml configuration file (auto-detected if not specified)")]
        public string ConfigFile { get; set; }

        [Option("dry-run", Required = false, HelpText = "Show what would be done without making changes", Default = false)]
        public bool DryRun { get; set; }
    }

    [Verb("version", HelpText = "Calculate the version for a specific project")]
    public class VersionOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project", Required = true, HelpText = "Path to the project file")]
        public string ProjectPath { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags", Default = "v")]
        public string TagPrefix { get; set; }

        [Option('d', "detailed", Required = false, HelpText = "Show detailed information about version calculation", Default = false)]
        public bool Detailed { get; set; }

        [Option('j', "json", Required = false, HelpText = "Output in JSON format", Default = false)]
        public bool JsonOutput { get; set; }

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }

        [Option("prerelease-type", Required = false, HelpText = "Prerelease type for main/dev branches (none, alpha, beta, rc)", Default = "none")]
        public string PrereleaseType { get; set; }

        [Option("force-version", Required = false, HelpText = "Force a specific version (overrides calculated version)")]
        public string ForceVersion { get; set; }

        [Option("dependencies", Required = false, HelpText = "Comma-separated list of dependency paths to track")]
        public string Dependencies { get; set; }
        
        [Option("create-tag", Required = false, HelpText = "Create a git tag for the calculated version", Default = false)]
        public bool CreateTag { get; set; }
        
        [Option("tag-message", Required = false, HelpText = "Message for the git tag (used with --create-tag)")]
        public string TagMessage { get; set; }
        
        [Option("config-file", Required = false, HelpText = "Path to mr-version.yml configuration file (auto-detected if not specified)")]
        public string ConfigFile { get; set; }
        
        [Option("dry-run", Required = false, HelpText = "Show what would be done without making changes", Default = false)]
        public bool DryRun { get; set; }
    }

    public class VersionReporter
    {
        private readonly ReportOptions _options;

        public VersionReporter(ReportOptions options)
        {
            _options = options;
        }

        public int GenerateReport()
        {
            // Create logger
            var logger = LoggerFactory.CreateReportLogger(_options.Debug);

            logger("Info", $"Analyzing repository: {_options.RepoPath}");

            // Discover Git repository root
            var gitRepoRoot = RepositoryService.DiscoverRepositoryWithConsoleErrors(_options.RepoPath, logger);
            if (gitRepoRoot == null)
            {
                return 1;
            }

            // detect if there is a mr-version.yml file
            // Load configuration
            var config = ConfigurationService.LoadConfiguration(
                _options.ConfigFile,
                gitRepoRoot,
                logger);
            
            if (config != null)
            {
                logger("Info", "Configuration loaded successfully");
                if (!string.IsNullOrEmpty(config.BaseVersion))
                    logger("Info", $"  Base version: {config.BaseVersion}");
                if (!string.IsNullOrEmpty(config.DefaultIncrement))
                    logger("Info", $"  Default increment: {config.DefaultIncrement}");
                if (!string.IsNullOrEmpty(config.PrereleaseType))
                    logger("Info", $"  Prerelease type: {config.PrereleaseType}");
                if (!string.IsNullOrEmpty(config.TagPrefix))
                    logger("Info", $"  Tag prefix: {config.TagPrefix}");
                if (config.Projects?.Count > 0)
                    logger("Info", $"  Project-specific configs: {config.Projects.Count}");
            }
            else if (!string.IsNullOrEmpty(_options.ConfigFile))
            {
                logger("Warning", $"Configuration file not found or invalid: {_options.ConfigFile}");
            }

            // Create base configuration values from CLI options
            var baseValues = new ConfigurationOverrides
            {
                BaseVersion = null,  // No CLI option for base version
                DefaultIncrement = "patch",  // No CLI option for default increment in report
                PrereleaseType = "none",  // No CLI option for prerelease type in report
                TagPrefix = _options.TagPrefix,
                SkipTestProjects = !_options.IncludeTestProjects,
                SkipNonPackableProjects = !_options.IncludeNonPackableProjects,
                ForceVersion = null  // No CLI option for force version in report
            };

            // Apply configuration overrides (CLI options take precedence)
            var mergedConfig = new ConfigurationOverrides
            {
                BaseVersion = config?.BaseVersion ?? baseValues.BaseVersion,
                DefaultIncrement = config?.DefaultIncrement ?? baseValues.DefaultIncrement,
                PrereleaseType = config?.PrereleaseType ?? baseValues.PrereleaseType,
                TagPrefix = baseValues.TagPrefix ?? config?.TagPrefix,  // CLI takes precedence
                SkipTestProjects = baseValues.SkipTestProjects,  // CLI takes precedence
                SkipNonPackableProjects = baseValues.SkipNonPackableProjects,  // CLI takes precedence
                ForceVersion = baseValues.ForceVersion
            };

            // Create VersionOptions template with merged settings
            var versionOptionsTemplate = new Core.Models.VersionOptions
            {
                TagPrefix = mergedConfig.TagPrefix ?? "v",
                DefaultIncrement = mergedConfig.DefaultIncrement ?? "patch",
                PrereleaseType = mergedConfig.PrereleaseType ?? "none",
                BaseVersion = mergedConfig.BaseVersion,
                SkipTestProjects = mergedConfig.SkipTestProjects ?? true,
                SkipNonPackableProjects = mergedConfig.SkipNonPackableProjects ?? true,
                Debug = _options.Debug
            };

            // Initialize services
            using var gitService = new GitService(gitRepoRoot);
            var versionCalculator = new VersionCalculator(gitService, logger);
            var projectAnalyzer = new ProjectAnalyzer(versionCalculator, gitService, logger);
            var reportGenerator = new ReportGenerator();

            // Switch to specified branch if provided
            if (!string.IsNullOrEmpty(_options.Branch))
            {
                logger("Info", $"Analyzing branch: {_options.Branch}");
                // Note: In a real implementation, you'd need to checkout the branch or use the branch reference
                // For now, we'll just work with the current branch
            }

            // Get current branch info
            var currentBranch = gitService.CurrentBranch;
            var branchType = gitService.GetBranchType(currentBranch);
            var globalVersion = gitService.GetGlobalVersionTag(branchType, versionOptionsTemplate);

            logger("Info", $"Current branch: {currentBranch} ({branchType})");
            logger("Info", $"Global version: {globalVersion?.SemVer?.ToVersionString() ?? "None"}");

            // Analyze projects with configuration
            var projects = AnalyzeProjectsWithConfiguration(
                projectAnalyzer, 
                gitRepoRoot, 
                _options.ProjectDir, 
                config, 
                versionOptionsTemplate);
            logger("Info", $"Found {projects.Count} projects");

            // Create report
            var report = new VersionReport
            {
                Repository = gitRepoRoot,
                Branch = currentBranch,
                BranchType = branchType,
                GlobalVersion = globalVersion?.SemVer,
                Projects = projects
            };

            // Generate report
            var reportOptions = new Core.Services.ReportOptions
            {
                IncludeCommits = _options.IncludeCommits,
                IncludeDependencies = _options.IncludeDependencies,
                IncludeTestProjects = _options.IncludeTestProjects,
                IncludeNonPackableProjects = _options.IncludeNonPackableProjects,
                OutputFormat = _options.OutputFormat,
                GraphFormat = _options.GraphFormat,
                ShowVersions = _options.ShowVersions,
                ShowChangedOnly = _options.ShowChangedOnly
            };

            var reportContent = reportGenerator.GenerateReport(report, reportOptions);

            // Output report
            if (string.IsNullOrEmpty(_options.OutputFile))
            {
                Console.WriteLine(reportContent);
            }
            else
            {
                File.WriteAllText(_options.OutputFile, reportContent);
                logger("Info", $"Report written to {_options.OutputFile}");
            }

            return 0;
        }

        private List<ProjectInfo> AnalyzeProjectsWithConfiguration(
            ProjectAnalyzer projectAnalyzer,
            string repositoryPath,
            string projectDirectory,
            VersionConfig config,
            Core.Models.VersionOptions baseOptions)
        {
            var projects = new List<ProjectInfo>();
            
            // Default to scanning the entire repository if no project directory specified
            var searchPath = string.IsNullOrEmpty(projectDirectory) 
                ? repositoryPath 
                : Path.Combine(repositoryPath, projectDirectory);

            if (!Directory.Exists(searchPath))
            {
                _logger("Warning", $"Project directory does not exist: {searchPath}");
                return projects;
            }

            // Find all project files
            var projectFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(searchPath, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(searchPath, "*.fsproj", SearchOption.AllDirectories))
                .ToList();

            _logger("Info", $"Found {projectFiles.Count} project files");

            foreach (var projectFile in projectFiles)
            {
                try
                {
                    var projectName = Path.GetFileNameWithoutExtension(projectFile);
                    
                    // Create project-specific options
                    var projectOptions = new Core.Models.VersionOptions
                    {
                        RepoRoot = repositoryPath,
                        ProjectPath = projectFile,
                        ProjectName = projectName,
                        TagPrefix = baseOptions.TagPrefix,
                        DefaultIncrement = baseOptions.DefaultIncrement,
                        PrereleaseType = baseOptions.PrereleaseType,
                        BaseVersion = baseOptions.BaseVersion,
                        SkipTestProjects = baseOptions.SkipTestProjects,
                        SkipNonPackableProjects = baseOptions.SkipNonPackableProjects,
                        Debug = baseOptions.Debug
                    };

                    // Apply project-specific configuration if exists
                    if (config?.Projects != null && config.Projects.ContainsKey(projectName))
                    {
                        var projectConfig = config.Projects[projectName];
                        projectOptions.PrereleaseType = projectConfig.PrereleaseType ?? projectOptions.PrereleaseType;
                        projectOptions.ForceVersion = projectConfig.ForceVersion;
                        _logger("Debug", $"Applied project-specific configuration for {projectName}");
                    }

                    var projectInfo = projectAnalyzer.AnalyzeProject(projectFile, repositoryPath, projectOptions);
                    if (projectInfo != null)
                    {
                        projects.Add(projectInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger("Error", $"Failed to analyze project {projectFile}: {ex.Message}");
                }
            }

            // Build dependency graph
            projectAnalyzer.BuildDependencyGraph(projects);

            return projects;
        }

        private void _logger(string level, string message)
        {
            var logger = LoggerFactory.CreateReportLogger(_options.Debug);
            logger(level, message);
        }
    }

    public class SingleProjectVersionCalculator
    {
        private readonly VersionOptions _options;

        public SingleProjectVersionCalculator(VersionOptions options)
        {
            _options = options;
        }

        public int CalculateVersion()
        {
            try
            {
                // Create logger
                var logger = LoggerFactory.CreateCliLogger(_options.Debug, _options.JsonOutput);

            // Resolve project path relative to repository path
            var resolvedProjectPath = _options.ProjectPath;
            if (!Path.IsPathRooted(_options.ProjectPath))
            {
                resolvedProjectPath = Path.GetFullPath(Path.Combine(_options.RepoPath, _options.ProjectPath));
            }
            
            logger("Debug", $"Original project path: {_options.ProjectPath}");
            logger("Debug", $"Repository path: {_options.RepoPath}");
            logger("Debug", $"Resolved project path: {resolvedProjectPath}");
            
            if (!File.Exists(resolvedProjectPath))
            {
                Console.Error.WriteLine($"Project file not found: {_options.ProjectPath}");
                Console.Error.WriteLine($"Resolved path: {resolvedProjectPath}");
                Console.Error.WriteLine($"Repository path: {_options.RepoPath}");
                
                // List files in the repository to help debug
                if (Directory.Exists(_options.RepoPath))
                {
                    Console.Error.WriteLine($"Files in repository root:");
                    foreach (var file in Directory.GetFiles(_options.RepoPath, "*", SearchOption.AllDirectories).Take(20))
                    {
                        Console.Error.WriteLine($"  {file}");
                    }
                }
                
                return 1;
            }
            
            // Update options with resolved path
            _options.ProjectPath = resolvedProjectPath;

            logger("Info", $"Calculating version for project: {_options.ProjectPath}");

            // Discover Git repository root
            var gitRepoRoot = RepositoryService.DiscoverRepositoryWithConsoleErrors(_options.RepoPath, logger);
            if (gitRepoRoot == null)
            {
                return 1;
            }

            // Initialize versioning service with timeout
            VersioningService versioningService;
            try
            {
                versioningService = new VersioningService(gitRepoRoot, logger);
                logger("Debug", "VersioningService initialized successfully");
            }
            catch (Exception ex)
            {
                logger("Error", $"Failed to initialize VersioningService: {ex.Message}");
                Console.Error.WriteLine($"Git repository error: {ex.Message}");
                Console.Error.WriteLine(RepositoryService.EnsureGitRepositoryCliMessage);
                return 1;
            }
            
            using (versioningService)
            {
                // Create version request with automatic dependency detection
                var dependencies = !string.IsNullOrEmpty(_options.Dependencies) 
                    ? _options.Dependencies.Split(',').Select(d => d.Trim()).ToList() 
                    : null; // Will be auto-detected if null

                // Auto-detect dependencies and project properties from project file
                var projectAnalyzer = new ProjectAnalyzer(null, null, logger);
                bool isTestProject = false;
                bool isPackable = true;
                
                // Detect if this is a test project
                try
                {
                    var projectContent = File.ReadAllText(_options.ProjectPath);
                    isTestProject = IsTestProject(projectContent);
                    isPackable = IsPackable(projectContent);
                    
                    logger("Debug", $"Project type detection: IsTestProject={isTestProject}, IsPackable={isPackable}");
                }
                catch (Exception ex)
                {
                    logger("Warning", $"Failed to detect project type: {ex.Message}");
                }
                
                if (dependencies == null || dependencies.Count == 0)
                {
                    try
                    {
                        dependencies = projectAnalyzer.GetProjectDependencies(_options.ProjectPath, gitRepoRoot);
                        
                        if (dependencies.Count > 0)
                        {
                            logger("Debug", $"Auto-detected {dependencies.Count} project dependencies:");
                            foreach (var dep in dependencies)
                            {
                                logger("Debug", $"  - {dep}");
                            }
                        }
                        else
                        {
                            logger("Debug", "No project dependencies detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger("Warning", $"Failed to auto-detect dependencies: {ex.Message}");
                        dependencies = new List<string>();
                    }
                }

                var versionRequest = new VersioningRequest
                {
                    RepoRoot = gitRepoRoot,
                    ProjectPath = _options.ProjectPath,
                    ConfigFile = _options.ConfigFile,
                    PrereleaseType = _options.PrereleaseType,
                    TagPrefix = _options.TagPrefix,
                    ForceVersion = _options.ForceVersion,
                    Dependencies = dependencies,
                    Debug = _options.Debug,
                    ExtraDebug = false,
                    SkipTestProjects = true,
                    SkipNonPackableProjects = true,
                    IsTestProject = isTestProject,
                    IsPackable = isPackable
                };

                // Calculate version
                var versionResult = versioningService.CalculateProjectVersion(versionRequest);

                if (!versionResult.Success)
                {
                    Console.Error.WriteLine($"Failed to calculate version: {versionResult.ErrorMessage}");
                    return 1;
                }

                // For CLI, we need to create a ProjectInfo-like object for output
                var projectInfo = new ProjectInfoForCli
                {
                    Name = versionResult.ProjectName,
                    Path = _options.ProjectPath,
                    Version = new Core.Models.VersionResult
                    {
                        Version = versionResult.Version,
                        VersionChanged = versionResult.VersionChanged,
                        ChangeReason = versionResult.ChangeReason
                    },
                    DirectDependencies = dependencies,
                    IsTestProject = isTestProject,
                    IsPackable = isPackable
                };

            // Output results
            if (_options.JsonOutput)
            {
                var jsonResult = new
                {
                    project = projectInfo.Name,
                    path = projectInfo.Path,
                    version = projectInfo.Version?.Version,
                    versionChanged = projectInfo.Version?.VersionChanged == true,
                    changeReason = projectInfo.Version?.ChangeReason,
                    commitSha = projectInfo.Version?.CommitSha,
                    commitDate = projectInfo.Version?.CommitDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    commitMessage = projectInfo.Version?.CommitMessage,
                    branchType = projectInfo.Version?.BranchType.ToString(),
                    branchName = projectInfo.Version?.BranchName,
                    commitHeight = projectInfo.Version?.CommitHeight,
                    isTestProject = projectInfo.IsTestProject,
                    isPackable = projectInfo.IsPackable,
                    dependencies = projectInfo.DirectDependencies
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };

                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonResult, jsonOptions));
            }
            else
            {
                Console.WriteLine($"Project: {projectInfo.Name}");
                Console.WriteLine($"Version: {projectInfo.Version?.Version ?? "Unknown"}");
                Console.WriteLine($"Changed: {(projectInfo.Version?.VersionChanged == true ? "Yes" : "No")}");

                if (_options.Detailed && projectInfo.Version != null)
                {
                    Console.WriteLine($"Reason: {projectInfo.Version.ChangeReason}");
                    Console.WriteLine($"Branch: {projectInfo.Version.BranchName} ({projectInfo.Version.BranchType})");
                    
                    if (!string.IsNullOrEmpty(projectInfo.Version.CommitSha))
                    {
                        Console.WriteLine($"Commit: {projectInfo.Version.CommitSha}");
                        Console.WriteLine($"Date: {projectInfo.Version.CommitDate:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"Message: {projectInfo.Version.CommitMessage}");
                    }

                    if (projectInfo.Version.BranchType == BranchType.Feature && projectInfo.Version.CommitHeight > 0)
                    {
                        Console.WriteLine($"Commit Height: {projectInfo.Version.CommitHeight}");
                    }

                    Console.WriteLine($"Test Project: {projectInfo.IsTestProject}");
                    Console.WriteLine($"Packable: {projectInfo.IsPackable}");

                    if (projectInfo.DirectDependencies.Count > 0)
                    {
                        Console.WriteLine($"Dependencies: {string.Join(", ", projectInfo.DirectDependencies)}");
                    }
                }
            }
            
            // Handle tag creation if requested
            if (_options.CreateTag && projectInfo.Version != null && !string.IsNullOrEmpty(projectInfo.Version.Version))
            {
                var tagSuccess = versioningService.CreateTag(versionResult, _options.TagPrefix, _options.TagMessage, _options.DryRun);
                
                if (!tagSuccess && !_options.DryRun)
                {
                    Console.Error.WriteLine("Failed to create tag");
                    return 1;
                }
            }

                return 0;
            } // end using gitService
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                return 1;
            }
        }

        private bool IsTestProject(string projectContent)
        {
            // Check for explicit test project marker
            if (Regex.IsMatch(projectContent, @"<IsTestProject\s*>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Check for common test frameworks
            var testFrameworks = new[]
            {
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "NUnit",
                "MSTest",
                "nunit",
                "MSTest.TestFramework"
            };

            return testFrameworks.Any(framework => 
                Regex.IsMatch(projectContent, $@"<PackageReference[^>]+Include\s*=\s*""{Regex.Escape(framework)}""", RegexOptions.IgnoreCase));
        }

        private bool IsPackable(string projectContent)
        {
            // Check for explicit IsPackable setting
            var isPackableMatch = Regex.Match(projectContent, @"<IsPackable\s*>\s*(true|false)\s*</IsPackable>", RegexOptions.IgnoreCase);
            if (isPackableMatch.Success)
            {
                return string.Equals(isPackableMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }

            // Check project type - some types are not packable by default
            if (Regex.IsMatch(projectContent, @"<OutputType\s*>\s*Exe\s*</OutputType>", RegexOptions.IgnoreCase))
            {
                // Console applications are typically not packable unless explicitly set
                return false;
            }

            // Default to packable for library projects
            return true;
        }
    }

    /// <summary>
    /// Simple project info class for CLI output
    /// </summary>
    public class ProjectInfoForCli
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public Core.Models.VersionResult Version { get; set; }
        public List<string> DirectDependencies { get; set; } = new List<string>();
        public bool IsTestProject { get; set; }
        public bool IsPackable { get; set; }
    }
}