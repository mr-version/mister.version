using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.CLI
{
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<ReportOptions, VersionOptions>(args)
                .MapResult(
                    (ReportOptions opts) => RunReportCommand(opts),
                    (VersionOptions opts) => RunVersionCommand(opts),
                    errs => 1);
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

        [Option('o', "output", Required = false, HelpText = "Output format: text, json, or csv", Default = "text")]
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

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }
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
            Action<string, string> logger = (level, message) =>
            {
                if (_options.Debug || level == "Error" || level == "Warning")
                {
                    Console.WriteLine($"[{level}] {message}");
                }
            };

            logger("Info", $"Analyzing repository: {_options.RepoPath}");

            // Initialize services
            using var gitService = new GitService(_options.RepoPath);
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
            var globalVersion = gitService.GetGlobalVersionTag(branchType, _options.TagPrefix);

            logger("Info", $"Current branch: {currentBranch} ({branchType})");
            logger("Info", $"Global version: {globalVersion?.SemVer?.ToVersionString() ?? "None"}");

            // Analyze projects
            var projects = projectAnalyzer.AnalyzeProjects(_options.RepoPath, _options.ProjectDir);
            logger("Info", $"Found {projects.Count} projects");

            // Create report
            var report = new VersionReport
            {
                Repository = _options.RepoPath,
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
                OutputFormat = _options.OutputFormat
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
            // Create logger
            Action<string, string> logger = (level, message) =>
            {
                if (_options.Debug || level == "Error" || level == "Warning")
                {
                    Console.WriteLine($"[{level}] {message}");
                }
            };

            if (!File.Exists(_options.ProjectPath))
            {
                Console.Error.WriteLine($"Project file not found: {_options.ProjectPath}");
                return 1;
            }

            logger("Info", $"Calculating version for project: {_options.ProjectPath}");

            // Initialize services
            using var gitService = new GitService(_options.RepoPath);
            var versionCalculator = new VersionCalculator(gitService, logger);
            var projectAnalyzer = new ProjectAnalyzer(versionCalculator, gitService, logger);

            // Analyze the specific project
            var projectInfo = projectAnalyzer.AnalyzeProject(_options.ProjectPath, _options.RepoPath);

            if (projectInfo == null)
            {
                Console.Error.WriteLine("Failed to analyze project");
                return 1;
            }

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

            return 0;
        }
    }
}