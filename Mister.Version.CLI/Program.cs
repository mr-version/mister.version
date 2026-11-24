using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                // Emoji in reports and changelogs must survive on consoles whose default code page is not UTF-8
                // (Windows runners); no BOM, so redirected output stays clean.
                TrySetUtf8Output();

                // Early debug output for CI debugging
                if (args.Contains("--debug"))
                {
                    Console.Error.WriteLine($"[DEBUG] CLI started with args: {string.Join(" ", args)}");
                    Console.Error.WriteLine($"[DEBUG] Working directory: {Directory.GetCurrentDirectory()}");
                    Console.Error.WriteLine($"[DEBUG] .NET version: {Environment.Version}");
                }

                // Help and parse errors are written by HandleParseErrors: help to stdout, errors to stderr
                var parser = new Parser(with => {
                    with.EnableDashDash = true;
                    with.AutoVersion = false;
                    with.AutoHelp = true;
                    with.HelpWriter = null;
                });

                var normalizedArgs = CliArguments.NormalizeBooleanOptions(args);

                if (args.Contains("--debug"))
                {
                    Console.Error.WriteLine($"[DEBUG] About to parse arguments: {string.Join(" ", normalizedArgs)}");
                }

                var result = parser.ParseArguments<ReportOptions, VersionOptions, ChangelogOptions>(normalizedArgs);

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
                        (ChangelogOptions opts) => {
                            if (args.Contains("--debug")) Console.Error.WriteLine($"[DEBUG] Running changelog command");
                            return RunChangelogCommand(opts);
                        },
                        errs => HandleParseErrors(result, errs.ToList(), args));
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

        private static void TrySetUtf8Output()
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (IOException)
            {
                // No console attached
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        private static int HandleParseErrors(ParserResult<object> result, List<Error> errList, string[] args)
        {
            if (args.Contains("--debug"))
            {
                Console.Error.WriteLine($"[DEBUG] Parser errors: {errList.Count}");
                foreach (var err in errList)
                {
                    Console.Error.WriteLine($"[DEBUG] Error: {err.Tag} - {err}");
                }
            }

            var helpRequested = errList.Any(e => e.Tag == ErrorType.HelpRequestedError ||
                                                 e.Tag == ErrorType.HelpVerbRequestedError ||
                                                 e.Tag == ErrorType.VersionRequestedError) ||
                                args.Any(arg => arg == "--help" || arg == "-h" || arg == "-?" || arg == "help");

            if (helpRequested && errList.Any(e => e.Tag == ErrorType.BadVerbSelectedError))
            {
                // "-h" / "-?" without a verb: show general help manually since AutoHelp won't trigger for BadVerb
                Console.WriteLine("Mister.Version CLI - Semantic versioning for monorepos");
                Console.WriteLine("Copyright (c) 2025 Mister.Version Team");
                Console.WriteLine();
                Console.WriteLine("Usage: mr-version <command> [options]");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  report      Generate a version report for projects in the repository");
                Console.WriteLine("  version     Calculate the version for a specific project");
                Console.WriteLine("  changelog   Generate a changelog from commit history");
                Console.WriteLine();
                Console.WriteLine("Use 'mr-version <command> --help' for more information about a command.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --help       Display this help screen.");
                Console.WriteLine("  --version    Display version information.");
                return 0;
            }

            var helpText = HelpText.AutoBuild(result);
            if (helpRequested)
            {
                // Help was requested - print it to stdout and return success
                Console.WriteLine(helpText);
                return 0;
            }

            // Parse errors go to stderr so callers capturing stderr see why the command failed
            Console.Error.WriteLine(helpText);
            return 1;
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

        static int RunChangelogCommand(ChangelogOptions options)
        {
            try
            {
                if (options.Debug)
                {
                    Console.Error.WriteLine($"[DEBUG] RunChangelogCommand called");
                    Console.Error.WriteLine($"[DEBUG] Repo path: {options.RepoPath}");
                }

                var logger = LoggerFactory.CreateReportLogger(options.Debug);

                // Resolve repository root (works for worktrees and submodules, where .git is a file)
                var repoRoot = Path.GetFullPath(options.RepoPath);
                if (!Repository.IsValid(repoRoot))
                {
                    Console.Error.WriteLine($"Error: Not a git repository: {repoRoot}");
                    return 1;
                }

                // Open repository
                using var repo = new Repository(repoRoot);

                // Load configuration
                var config = ConfigurationService.LoadConfiguration(options.ConfigFile, repoRoot, options.Debug ? logger : null);
                var tagPrefix = CliVersioning.ResolveTagPrefix(options.TagPrefix, config);
                if (!string.IsNullOrWhiteSpace(options.ProjectName) && string.IsNullOrEmpty(options.ToVersion))
                {
                    CliVersioning.WarnIfTagPrefixOverridden(options.TagPrefix, config, logger);
                }

                // Resolve the project (a name or a project file path) so commits can be filtered by its directory
                string projectName = options.ProjectName;
                string projectFile = null;
                string projectDirectory = null;
                if (!string.IsNullOrWhiteSpace(options.ProjectName))
                {
                    var analyzer = new ProjectAnalyzer(null, null, logger);
                    projectFile = CliVersioning.ResolveProjectFile(options.ProjectName, options.RepoPath, repoRoot, analyzer, logger, out var resolveError);
                    if (resolveError != null)
                    {
                        Console.Error.WriteLine($"Error: {resolveError}");
                        return 1;
                    }

                    if (projectFile != null)
                    {
                        projectName = Path.GetFileNameWithoutExtension(projectFile);
                        projectDirectory = CliVersioning.GetRepoRelativeDirectory(repoRoot, projectFile);
                        logger("Debug", $"Changelog for project {projectName} ({projectFile}), commits filtered to '{projectDirectory ?? "."}'");
                    }
                }

                // Get commit range
                Commit fromCommit = null;
                Commit toCommit = repo.Head.Tip;
                string fromVersion = null;

                // Tag lookups peel annotated tags (Target is a TagAnnotation for those, not a Commit).
                // Project tags ("{project}/{prefix}1.2.3", "{project}-{prefix}1.2.3") are tried before global tags.
                Commit FindTagCommit(string version)
                {
                    var candidates = new List<string>();
                    if (!string.IsNullOrEmpty(projectName))
                    {
                        candidates.Add($"{projectName}/{tagPrefix}{version}");
                        candidates.Add($"{projectName}-{tagPrefix}{version}");
                    }
                    candidates.Add(tagPrefix + version);

                    foreach (var name in candidates)
                    {
                        if (repo.Tags.FirstOrDefault(t => t.FriendlyName == name)?.PeeledTarget is Commit commit)
                        {
                            return commit;
                        }
                    }
                    return null;
                }

                if (!string.IsNullOrEmpty(options.ToVersion))
                {
                    toCommit = FindTagCommit(options.ToVersion);
                    if (toCommit == null)
                    {
                        Console.Error.WriteLine($"Error: Could not find tag {tagPrefix}{options.ToVersion}");
                        return 1;
                    }
                }

                List<Commit> commits;
                if (!string.IsNullOrEmpty(options.FromVersion))
                {
                    fromCommit = FindTagCommit(options.FromVersion);
                    if (fromCommit == null)
                    {
                        Console.Error.WriteLine($"Error: Could not find tag {tagPrefix}{options.FromVersion}");
                        return 1;
                    }
                    fromVersion = options.FromVersion;
                    commits = repo.Commits.QueryBy(new CommitFilter
                    {
                        IncludeReachableFrom = toCommit,
                        ExcludeReachableFrom = fromCommit
                    }).ToList();

                    if (projectDirectory != null)
                    {
                        commits = commits.Where(c => TouchesDirectory(repo, c, projectDirectory)).ToList();
                    }
                }
                else
                {
                    // Previous release = nearest version tag in toCommit's history (not the newest tag by date,
                    // which may be on another branch); a tag on toCommit itself is the release being described.
                    var range = ChangelogRange.Resolve(repo, toCommit, tagPrefix, projectName, projectDirectory, excludeTagsOnTarget: true);
                    fromCommit = range.FromCommit;
                    fromVersion = range.FromVersion;
                    commits = range.Commits;
                }

                if (options.Debug)
                {
                    Console.Error.WriteLine($"[DEBUG] Found {commits.Count} commits");
                }

                // Determine the version label for the end of the range: the requested tag, a version tag on the
                // target commit, the project's calculated (next) version, or "Unreleased".
                string toVersion;
                if (!string.IsNullOrEmpty(options.ToVersion))
                {
                    toVersion = options.ToVersion;
                }
                else if (toCommit != null && ChangelogRange.GetVersionTagsByCommit(repo, tagPrefix, projectName)
                             .TryGetValue(toCommit.Sha, out var tagsOnTarget))
                {
                    toVersion = tagsOnTarget.OrderByDescending(v => v, StringComparer.Ordinal).First();
                }
                else
                {
                    toVersion = projectFile != null
                        ? CliVersioning.CalculateVersionForChangelog(repoRoot, projectFile, options, tagPrefix, logger)
                        : null;
                    toVersion = toVersion ?? CliVersioning.UnreleasedLabel;
                }

                // Changelog settings: mr-version.yml, then only the flags given explicitly on the command line
                var changelogConfig = config?.Changelog ?? new ChangelogConfig { Enabled = true };
                changelogConfig.OutputFormat = options.OutputFormat;
                changelogConfig.RepositoryUrl = options.RepositoryUrl ?? changelogConfig.RepositoryUrl;
                changelogConfig.IncludeAuthors = options.IncludeAuthors ?? changelogConfig.IncludeAuthors;
                changelogConfig.IncludeCommitLinks = options.IncludeCommitLinks ?? changelogConfig.IncludeCommitLinks;
                changelogConfig.IncludeIssueReferences = options.IncludeIssueReferences ?? changelogConfig.IncludeIssueReferences;
                changelogConfig.IncludePullRequestReferences = options.IncludePullRequestReferences ?? changelogConfig.IncludePullRequestReferences;
                changelogConfig.GroupBreakingChanges = options.GroupBreakingChanges ?? changelogConfig.GroupBreakingChanges;

                // Get conventional commit config
                var conventionalCommitConfig = config?.CommitConventions ?? new ConventionalCommitConfig { Enabled = true };

                // Generate changelog
                var generator = new ChangelogGenerator(new ConventionalCommitAnalyzer());
                var changelog = generator.GenerateChangelog(
                    commits,
                    toVersion,
                    fromVersion,
                    changelogConfig,
                    conventionalCommitConfig,
                    projectName);

                // Format output
                string output;
                switch (options.OutputFormat.ToLowerInvariant())
                {
                    case "markdown":
                    case "md":
                        output = CliVersioning.FixUnreleasedHeader(generator.FormatAsMarkdown(changelog, changelogConfig), toVersion);
                        break;
                    case "text":
                    case "txt":
                        output = CliVersioning.FixUnreleasedHeader(generator.FormatAsText(changelog, changelogConfig), toVersion);
                        break;
                    case "json":
                        output = generator.FormatAsJson(changelog);
                        break;
                    default:
                        Console.Error.WriteLine($"Error: Unknown output format: {options.OutputFormat}");
                        return 1;
                }

                // Write output
                if (!string.IsNullOrEmpty(options.OutputFile))
                {
                    File.WriteAllText(options.OutputFile, output);
                    Console.WriteLine($"Changelog written to {options.OutputFile}");
                }
                else
                {
                    Console.WriteLine(output);
                }

                return 0;
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

        private static bool TouchesDirectory(Repository repo, Commit commit, string directory)
        {
            var parent = commit.Parents.FirstOrDefault();
            return repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree, new[] { directory.TrimEnd('/') + "/" }).Count > 0;
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

        [Option('b', "branch", Required = false, HelpText = "Branch name to version as (default: the checked-out branch; for a detached HEAD the CI branch or a branch whose tip is HEAD). Versions are still calculated for the checked-out commit.")]
        public string Branch { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags (default: tagPrefix from mr-version.yml, else \"v\")")]
        public string TagPrefix { get; set; }

        [Option("include-commits", Required = false, HelpText = "Include commit information in the report (true/false; a bare flag means true)", Default = true)]
        public bool? IncludeCommits { get; set; }

        [Option("include-dependencies", Required = false, HelpText = "Include dependency information in the report (true/false; a bare flag means true)", Default = true)]
        public bool? IncludeDependencies { get; set; }

        [Option("include-test-projects", Required = false, HelpText = "Include test projects in the report (true/false; a bare flag means true)", Default = false)]
        public bool? IncludeTestProjects { get; set; }

        [Option("include-non-packable", Required = false, HelpText = "Include non-packable projects in the report (true/false; a bare flag means true)", Default = false)]
        public bool? IncludeNonPackableProjects { get; set; }

        [Option("graph-format", Required = false, HelpText = "Graph format when output is 'graph': mermaid, dot, or ascii", Default = "mermaid")]
        public string GraphFormat { get; set; }

        [Option("show-versions", Required = false, HelpText = "Show version numbers in graph nodes (true/false; a bare flag means true)", Default = true)]
        public bool? ShowVersions { get; set; }

        [Option("changed-only", Required = false, HelpText = "Show only projects with version changes (true/false; a bare flag means true)", Default = false)]
        public bool? ShowChangedOnly { get; set; }

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }

        [Option("config-file", Required = false, HelpText = "Path to mr-version.yml configuration file (auto-detected if not specified)")]
        public string ConfigFile { get; set; }

        [Option("enable-file-cache", Required = false, HelpText = "Enable file-based caching of version results in .mrversion folder", Default = false)]
        public bool EnableFileCache { get; set; }

        [Option("file-cache-path", Required = false, HelpText = "Custom path for the file cache directory (defaults to .mrversion in repo root)")]
        public string FileCachePath { get; set; }

    }

    [Verb("version", HelpText = "Calculate the version for a specific project")]
    public class VersionOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project", Required = true, HelpText = "Path to the project file")]
        public string ProjectPath { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags (default: tagPrefix from mr-version.yml, else \"v\")")]
        public string TagPrefix { get; set; }

        [Option('b', "branch", Required = false, HelpText = "Branch name to version as (default: the checked-out branch; for a detached HEAD the CI branch or a branch whose tip is HEAD). The checked-out commit is still what gets versioned.")]
        public string Branch { get; set; }

        [Option("dependencies", Required = false, HelpText = "Additional dependency paths (projects or directories, relative to --repo) whose changes count for this project, separated by ';', ',' or spaces. Added to the project's ProjectReferences (direct and transitive), which are always tracked.")]
        public string Dependencies { get; set; }

        [Option('d', "detailed", Required = false, HelpText = "Show detailed information about version calculation", Default = false)]
        public bool Detailed { get; set; }

        [Option('j', "json", Required = false, HelpText = "Output in JSON format", Default = false)]
        public bool JsonOutput { get; set; }

        [Option("dry-run", Required = false, HelpText = "Accepted for compatibility; 'version' never modifies the repository or project files", Default = false)]
        public bool DryRun { get; set; }

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }

        [Option("prerelease-type", Required = false, HelpText = "Prerelease type for main/dev branches (none, alpha, beta, rc)", Default = "none")]
        public string PrereleaseType { get; set; }

        [Option("force-version", Required = false, HelpText = "Force a specific version (overrides calculated version)")]
        public string ForceVersion { get; set; }

        [Option("config-file", Required = false, HelpText = "Path to mr-version.yml configuration file (auto-detected if not specified)")]
        public string ConfigFile { get; set; }

        [Option("enable-file-cache", Required = false, HelpText = "Enable file-based caching of version results in .mrversion folder", Default = false)]
        public bool EnableFileCache { get; set; }

        [Option("file-cache-path", Required = false, HelpText = "Custom path for the file cache directory (defaults to .mrversion in repo root)")]
        public string FileCachePath { get; set; }
    }

    [Verb("changelog", HelpText = "Generate a changelog from commit history")]
    public class ChangelogOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project", Required = false, HelpText = "Project name or project file path (monorepos): only commits that touch the project's directory are included")]
        public string ProjectName { get; set; }

        [Option('f', "from", Required = false, HelpText = "Start version tag (defaults to previous tag)")]
        public string FromVersion { get; set; }

        [Option('t', "to", Required = false, HelpText = "End version tag (defaults to HEAD)")]
        public string ToVersion { get; set; }

        [Option('o', "output", Required = false, HelpText = "Output format: markdown, text, json", Default = "markdown")]
        public string OutputFormat { get; set; }

        [Option("output-file", Required = false, HelpText = "Output file path (if not specified, outputs to console)")]
        public string OutputFile { get; set; }

        [Option("tag-prefix", Required = false, HelpText = "Prefix for version tags (default: tagPrefix from mr-version.yml, else \"v\")")]
        public string TagPrefix { get; set; }

        [Option("repo-url", Required = false, HelpText = "Repository URL for generating links (e.g., https://github.com/owner/repo)")]
        public string RepositoryUrl { get; set; }

        [Option("include-authors", Required = false, HelpText = "Include commit authors (true/false; a bare flag means true; default: mr-version.yml changelog.includeAuthors, else false)")]
        public bool? IncludeAuthors { get; set; }

        [Option("include-commits", Required = false, HelpText = "Include commit links (true/false; a bare flag means true; default: mr-version.yml changelog.includeCommitLinks, else true)")]
        public bool? IncludeCommitLinks { get; set; }

        [Option("include-issues", Required = false, HelpText = "Include issue references (true/false; a bare flag means true; default: mr-version.yml changelog.includeIssueReferences, else true)")]
        public bool? IncludeIssueReferences { get; set; }

        [Option("include-prs", Required = false, HelpText = "Include PR references (true/false; a bare flag means true; default: mr-version.yml changelog.includePullRequestReferences, else true)")]
        public bool? IncludePullRequestReferences { get; set; }

        [Option("group-breaking", Required = false, HelpText = "Group breaking changes separately (true/false; a bare flag means true; default: mr-version.yml changelog.groupBreakingChanges, else true)")]
        public bool? GroupBreakingChanges { get; set; }

        [Option("config-file", Required = false, HelpText = "Path to mr-version.yml configuration file (auto-detected if not specified)")]
        public string ConfigFile { get; set; }

        [Option("debug", Required = false, HelpText = "Enable debug output", Default = false)]
        public bool Debug { get; set; }
    }

    /// <summary>
    /// Command-line pre-processing.
    /// </summary>
    internal static class CliArguments
    {
        // Boolean options that take an optional value: "--x", "--x true" and "--x false" all work. CommandLineParser
        // only offers switches (presence = true, a following "false" is silently dropped) or nullable bools that
        // require a value, so a bare flag gets an explicit "true" before parsing.
        private static readonly Dictionary<string, HashSet<string>> BooleanOptionsByVerb = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["report"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "--include-commits", "--include-dependencies", "--include-test-projects", "--include-non-packable",
                "--show-versions", "--changed-only"
            },
            ["changelog"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "--include-authors", "--include-commits", "--include-issues", "--include-prs", "--group-breaking"
            }
        };

        public static string[] NormalizeBooleanOptions(string[] args)
        {
            if (args == null || args.Length == 0 || !BooleanOptionsByVerb.TryGetValue(args[0], out var booleanOptions))
            {
                return args;
            }

            var result = new List<string>(args.Length + 4);
            for (var i = 0; i < args.Length; i++)
            {
                result.Add(args[i]);
                if (args[i] == "--")
                {
                    result.AddRange(args.Skip(i + 1));
                    break;
                }

                if (booleanOptions.Contains(args[i]) && (i + 1 >= args.Length || !bool.TryParse(args[i + 1], out _)))
                {
                    result.Add("true");
                }
            }
            return result.ToArray();
        }
    }

    /// <summary>
    /// Version calculation shared by the version, report and changelog commands, built the same way the MSBuild
    /// task builds it: <see cref="VersioningService.CalculateProjectVersion"/> with the project's direct and
    /// transitive ProjectReferences and its IsTestProject/IsPackable.
    /// </summary>
    internal static class CliVersioning
    {
        public const string UnreleasedLabel = "Unreleased";

        private static readonly string[] ProjectFileExtensions = { ".csproj", ".fsproj", ".vbproj" };

        /// <summary>
        /// Tag prefix precedence: the command-line option, then tagPrefix from mr-version.yml, then "v".
        /// </summary>
        public static string ResolveTagPrefix(string optionValue, VersionConfig config) =>
            optionValue ?? config?.TagPrefix ?? "v";

        /// <summary>
        /// VersioningService applies mr-version.yml over request values (as it does for MSBuild properties), so a
        /// conflicting --tag-prefix does not reach the version calculation; say so instead of failing silently.
        /// </summary>
        public static void WarnIfTagPrefixOverridden(string optionValue, VersionConfig config, Action<string, string> logger)
        {
            if (optionValue != null && config?.TagPrefix != null && !string.Equals(optionValue, config.TagPrefix, StringComparison.Ordinal))
            {
                logger("Warning", $"--tag-prefix '{optionValue}' differs from tagPrefix '{config.TagPrefix}' in mr-version.yml; " +
                                  "version calculation uses the mr-version.yml value");
            }
        }

        public sealed class Settings
        {
            public string RepoRoot { get; set; }
            public string ConfigFile { get; set; }
            public string PrereleaseType { get; set; } = "none";
            public string TagPrefix { get; set; } = "v";
            public string ForceVersion { get; set; }
            public string BranchName { get; set; }
            public bool Debug { get; set; }
            public bool EnableFileCache { get; set; }
            public string FileCachePath { get; set; }
        }

        public static VersioningRequest CreateRequest(Settings settings, string projectPath, ProjectTypeInfo projectType, List<string> dependencies)
        {
            return new VersioningRequest
            {
                RepoRoot = settings.RepoRoot,
                ProjectPath = projectPath,
                ConfigFile = settings.ConfigFile,
                PrereleaseType = settings.PrereleaseType ?? "none",
                TagPrefix = settings.TagPrefix ?? "v",
                ForceVersion = settings.ForceVersion,
                BranchName = settings.BranchName,
                Dependencies = dependencies ?? new List<string>(),
                Debug = settings.Debug,
                ExtraDebug = false,
                // Same defaults as the MSBuild task; mr-version.yml skipTestProjects/skipNonPackableProjects override
                SkipTestProjects = true,
                SkipNonPackableProjects = true,
                IsTestProject = projectType?.IsTestProject ?? false,
                IsPackable = projectType?.IsPackable ?? true,
                EnableFileCache = settings.EnableFileCache,
                FileCachePath = settings.FileCachePath
            };
        }

        /// <summary>
        /// The full calculation result. Results served from the file cache carry only the version, so the
        /// branch and commit information is filled in from the repository.
        /// </summary>
        public static Core.Models.VersionResult GetDetails(VersioningResult result, string repoRoot, string branchOverride)
        {
            if (result.Details != null)
            {
                return result.Details;
            }

            var details = new Core.Models.VersionResult
            {
                Version = result.Version,
                SemVer = result.SemVer,
                VersionChanged = result.VersionChanged,
                ChangeReason = result.ChangeReason,
                Skipped = result.Skipped,
                SkippedWithConfiguredVersion = result.SkippedWithConfiguredVersion
            };

            try
            {
                using var gitService = new GitService(repoRoot);
                var branch = string.IsNullOrWhiteSpace(branchOverride) ? gitService.CurrentBranch : branchOverride.Trim();
                details.BranchName = branch;
                details.BranchType = gitService.GetBranchType(branch);

                var tip = gitService.Repository?.Head?.Tip;
                if (tip != null)
                {
                    details.CommitSha = gitService.GetCommitShortHash(tip);
                    details.CommitDate = tip.Author.When.DateTime;
                    details.CommitMessage = tip.MessageShort;
                }
            }
            catch (Exception)
            {
                // Branch/commit information is best effort for cached results
            }

            return details;
        }

        /// <summary>
        /// Parses --dependencies: paths separated by ';', ',', newlines or spaces (a piece that exists as a path
        /// is kept whole, so paths containing spaces work when separated by ';' or ','). Relative paths are
        /// resolved against <paramref name="basePath"/>.
        /// </summary>
        public static List<string> ParseDependencies(string value, string basePath)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var piece in value.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(p => p.Trim())
                         .Where(p => p.Length > 0))
            {
                var tokens = PathExists(ResolvePath(piece, basePath))
                    ? new[] { piece }
                    : piece.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                {
                    var resolved = ResolvePath(token, basePath);
                    if (!result.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(resolved);
                    }
                }
            }

            return result;
        }

        private static string ResolvePath(string path, string basePath)
        {
            var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(basePath ?? ".", normalized));
        }

        private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        /// <summary>
        /// Resolves a project given by name (looked up among the repository's projects) or by project file path.
        /// Returns null (and no error) for a name that matches no project file.
        /// </summary>
        public static string ResolveProjectFile(string projectArgument, string basePath, string repoRoot, ProjectAnalyzer analyzer,
            Action<string, string> logger, out string error)
        {
            error = null;
            var value = projectArgument.Trim();

            var looksLikePath = ProjectFileExtensions.Any(ext => value.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
                                value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0;
            if (looksLikePath)
            {
                var full = ResolvePath(value, basePath);
                if (File.Exists(full))
                {
                    return full;
                }

                error = $"Project file not found: {projectArgument} (resolved to {full})";
                return null;
            }

            var matches = analyzer.FindAllProjects(repoRoot)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), value, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                error = $"Project name '{projectArgument}' is ambiguous; pass the project file path instead: {string.Join(", ", matches)}";
                return null;
            }

            logger("Warning", $"No project file named '{projectArgument}' found in {repoRoot}; commits are not filtered by project directory");
            return null;
        }

        /// <summary>Repository-relative directory of a project file ('/' separators), or null for the root.</summary>
        public static string GetRepoRelativeDirectory(string repoRoot, string projectFile)
        {
            var relative = PathUtils.GetRelativePath(repoRoot, Path.GetDirectoryName(Path.GetFullPath(projectFile)))
                .Replace('\\', '/')
                .Trim('/');
            return relative.Length == 0 || relative == "." ? null : relative;
        }

        /// <summary>
        /// The project's calculated version for a changelog whose end is not tagged, or null when it cannot be
        /// calculated or the project is not versioned (test / non-packable).
        /// </summary>
        public static string CalculateVersionForChangelog(string repoRoot, string projectFile, ChangelogOptions options, string tagPrefix, Action<string, string> logger)
        {
            try
            {
                var gitRepoRoot = RepositoryService.DiscoverRepository(repoRoot, logger) ?? repoRoot;
                var analyzer = new ProjectAnalyzer(null, null, logger);
                var settings = new Settings
                {
                    RepoRoot = gitRepoRoot,
                    ConfigFile = options.ConfigFile,
                    TagPrefix = tagPrefix,
                    Debug = options.Debug
                };

                using var versioningService = new VersioningService(gitRepoRoot, logger);
                var result = versioningService.CalculateProjectVersion(CreateRequest(
                    settings,
                    projectFile,
                    ProjectAnalyzer.DetectProjectType(projectFile, gitRepoRoot),
                    analyzer.GetTransitiveDependencies(projectFile, gitRepoRoot)));

                if (!result.Success)
                {
                    logger("Warning", $"Could not calculate the version of {Path.GetFileNameWithoutExtension(projectFile)}: {result.ErrorMessage}");
                    return null;
                }

                return result.Skipped || string.IsNullOrEmpty(result.Version) ? null : result.Version;
            }
            catch (Exception ex)
            {
                logger("Warning", $"Could not calculate the version of {Path.GetFileNameWithoutExtension(projectFile)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The changelog formatter always writes "v{version}" in the header; "vUnreleased" reads as a version.
        /// </summary>
        public static string FixUnreleasedHeader(string output, string toVersion)
        {
            const string marker = "v" + UnreleasedLabel;
            if (toVersion != UnreleasedLabel || output == null)
            {
                return output;
            }

            var index = output.IndexOf(marker, StringComparison.Ordinal);
            return index < 0 ? output : output.Substring(0, index) + UnreleasedLabel + output.Substring(index + marker.Length);
        }

        /// <summary>
        /// Wraps a logger so a warning repeated for every project (e.g. about the configuration file) is shown once.
        /// </summary>
        public static Action<string, string> DeduplicateWarnings(Action<string, string> logger)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return (level, message) =>
            {
                if (level == "Warning" && !seen.Add(message))
                {
                    return;
                }
                logger(level, message);
            };
        }
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

            var tagPrefix = CliVersioning.ResolveTagPrefix(_options.TagPrefix, config);
            CliVersioning.WarnIfTagPrefixOverridden(_options.TagPrefix, config, logger);
            var branchOverride = string.IsNullOrWhiteSpace(_options.Branch) ? null : _options.Branch.Trim();

            // Every project is versioned exactly like `mr-version version` (and the MSBuild task) would version it
            var settings = new CliVersioning.Settings
            {
                RepoRoot = gitRepoRoot,
                ConfigFile = _options.ConfigFile,
                PrereleaseType = "none", // mr-version.yml prereleaseType (global or per project) applies
                TagPrefix = tagPrefix,
                BranchName = branchOverride,
                Debug = _options.Debug,
                EnableFileCache = _options.EnableFileCache,
                FileCachePath = _options.FileCachePath
            };

            // Initialize services
            using var gitService = new GitService(gitRepoRoot);
            using var versioningService = new VersioningService(gitRepoRoot, CliVersioning.DeduplicateWarnings(logger));
            var projectAnalyzer = new ProjectAnalyzer(null, gitService, logger)
            {
                // Project discovery and ProjectReference parsing once per report, not once per project
                Cache = new VersionCache(gitRepoRoot, gitService.Repository?.Head?.Tip?.Sha ?? string.Empty)
            };
            var reportGenerator = new ReportGenerator();

            // Get current branch info
            var currentBranch = branchOverride ?? gitService.CurrentBranch;
            var branchType = gitService.GetBranchType(currentBranch);
            var globalVersion = gitService.GetGlobalVersionTag(branchType, new Core.Models.VersionOptions
            {
                RepoRoot = gitRepoRoot,
                TagPrefix = tagPrefix,
                DefaultIncrement = config?.DefaultIncrement ?? "patch",
                PrereleaseType = config?.PrereleaseType ?? "none",
                BaseVersion = config?.BaseVersion,
                BranchName = branchOverride,
                GitIntegration = config?.GitIntegration ?? new GitIntegrationConfig(),
                Debug = _options.Debug
            });

            logger("Info", $"Current branch: {currentBranch} ({branchType})");
            logger("Info", $"Global version: {globalVersion?.SemVer?.ToVersionString() ?? "None"}");

            var projects = AnalyzeProjects(projectAnalyzer, versioningService, settings, gitRepoRoot, _options.ProjectDir, branchOverride, logger);
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
                IncludeCommits = _options.IncludeCommits ?? true,
                IncludeDependencies = _options.IncludeDependencies ?? true,
                IncludeTestProjects = _options.IncludeTestProjects ?? false,
                IncludeNonPackableProjects = _options.IncludeNonPackableProjects ?? false,
                OutputFormat = _options.OutputFormat,
                GraphFormat = _options.GraphFormat,
                ShowVersions = _options.ShowVersions ?? true,
                ShowChangedOnly = _options.ShowChangedOnly ?? false
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

        private static List<ProjectInfo> AnalyzeProjects(
            ProjectAnalyzer projectAnalyzer,
            VersioningService versioningService,
            CliVersioning.Settings settings,
            string repositoryPath,
            string projectDirectory,
            string branchOverride,
            Action<string, string> logger)
        {
            var projects = new List<ProjectInfo>();

            // Default to scanning the entire repository if no project directory specified
            var searchPath = string.IsNullOrEmpty(projectDirectory)
                ? repositoryPath
                : Path.Combine(repositoryPath, projectDirectory);

            if (!Directory.Exists(searchPath))
            {
                logger("Warning", $"Project directory does not exist: {searchPath}");
                return projects;
            }

            // Find all project files
            var projectFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(searchPath, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(searchPath, "*.fsproj", SearchOption.AllDirectories))
                .ToList();

            logger("Info", $"Found {projectFiles.Count} project files");

            foreach (var projectFile in projectFiles)
            {
                try
                {
                    var projectInfo = projectAnalyzer.AnalyzeProjectWith(projectFile, repositoryPath, (path, projectType, dependencies) =>
                    {
                        var result = versioningService.CalculateProjectVersion(
                            CliVersioning.CreateRequest(settings, path, projectType, dependencies));

                        if (!result.Success)
                        {
                            logger("Error", $"{Path.GetFileNameWithoutExtension(path)}: {result.ErrorMessage}");
                            if (result.Details == null)
                            {
                                return new Core.Models.VersionResult { ChangeReason = $"Error: {result.ErrorMessage}" };
                            }
                        }

                        return CliVersioning.GetDetails(result, repositoryPath, branchOverride);
                    });

                    if (projectInfo != null)
                    {
                        projects.Add(projectInfo);
                    }
                }
                catch (Exception ex)
                {
                    logger("Error", $"Failed to analyze project {projectFile}: {ex.Message}");
                }
            }

            // Build dependency graph
            projectAnalyzer.BuildDependencyGraph(projects);

            return projects;
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
                var projectAnalyzer = new ProjectAnalyzer(null, null, logger);

                // Project type as MSBuild evaluates it (project file + Directory.Build.props)
                var projectType = new ProjectTypeInfo();
                try
                {
                    projectType = ProjectAnalyzer.DetectProjectType(_options.ProjectPath, gitRepoRoot);
                    logger("Debug", $"Project type detection: IsTestProject={projectType.IsTestProject}, IsPackable={projectType.IsPackable}");
                }
                catch (Exception ex)
                {
                    logger("Warning", $"Failed to detect project type: {ex.Message}");
                }

                // Direct and transitive ProjectReferences, plus any --dependencies
                List<string> dependencies;
                try
                {
                    dependencies = projectAnalyzer.GetTransitiveDependencies(_options.ProjectPath, gitRepoRoot);
                }
                catch (Exception ex)
                {
                    logger("Warning", $"Failed to auto-detect dependencies: {ex.Message}");
                    dependencies = new List<string>();
                }

                foreach (var extra in CliVersioning.ParseDependencies(_options.Dependencies, _options.RepoPath))
                {
                    if (!dependencies.Contains(extra, StringComparer.OrdinalIgnoreCase))
                    {
                        dependencies.Add(extra);
                    }
                }

                if (dependencies.Count > 0)
                {
                    logger("Debug", $"Tracking {dependencies.Count} dependencies:");
                    foreach (var dep in dependencies)
                    {
                        logger("Debug", $"  - {dep}");
                    }
                }
                else
                {
                    logger("Debug", "No project dependencies detected");
                }

                if (_options.DryRun)
                {
                    logger("Debug", "--dry-run: 'version' has no side effects");
                }

                // Load configuration for the tag prefix precedence (option > mr-version.yml > "v")
                var config = ConfigurationService.LoadConfiguration(_options.ConfigFile, gitRepoRoot, null);
                CliVersioning.WarnIfTagPrefixOverridden(_options.TagPrefix, config, logger);
                var branchOverride = string.IsNullOrWhiteSpace(_options.Branch) ? null : _options.Branch.Trim();

                var versionRequest = CliVersioning.CreateRequest(new CliVersioning.Settings
                {
                    RepoRoot = gitRepoRoot,
                    ConfigFile = _options.ConfigFile,
                    PrereleaseType = _options.PrereleaseType,
                    TagPrefix = CliVersioning.ResolveTagPrefix(_options.TagPrefix, config),
                    ForceVersion = _options.ForceVersion,
                    BranchName = branchOverride,
                    Debug = _options.Debug,
                    EnableFileCache = _options.EnableFileCache,
                    FileCachePath = _options.FileCachePath
                }, _options.ProjectPath, projectType, dependencies);

                // Calculate version
                var versionResult = versioningService.CalculateProjectVersion(versionRequest);

                if (!versionResult.Success)
                {
                    Console.Error.WriteLine($"Failed to calculate version: {versionResult.ErrorMessage}");
                    if (versionResult.ValidationFailed && versionResult.Details != null && _options.JsonOutput)
                    {
                        // The validation details are useful to automation even though the calculation failed
                        WriteJson(versionResult.ProjectName, versionResult.Details, projectType, dependencies);
                    }
                    return 1;
                }

                var details = CliVersioning.GetDetails(versionResult, gitRepoRoot, branchOverride);
                var projectName = versionResult.ProjectName;

            // Output results
            if (_options.JsonOutput)
            {
                WriteJson(projectName, details, projectType, dependencies);
            }
            else
            {
                Console.WriteLine($"Project: {projectName}");
                Console.WriteLine($"Version: {details.Version ?? "Unknown"}");
                Console.WriteLine($"Changed: {(details.VersionChanged ? "Yes" : "No")}");

                if (_options.Detailed)
                {
                    WriteDetails(details, projectType, dependencies);
                }
            }

                return 0;
            } // end using versioningService
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                return 1;
            }
        }

        private void WriteJson(string projectName, Core.Models.VersionResult details, ProjectTypeInfo projectType, List<string> dependencies)
        {
            var jsonResult = new
            {
                project = projectName,
                path = _options.ProjectPath,
                version = details.Version,
                versionChanged = details.VersionChanged,
                changeReason = details.ChangeReason,
                previousVersion = details.PreviousVersion,
                previousCommitSha = details.PreviousCommitSha,
                commitSha = details.CommitSha,
                commitDate = details.CommitDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                commitMessage = details.CommitMessage,
                branchType = details.BranchType.ToString(),
                branchName = details.BranchName,
                commitHeight = details.CommitHeight,
                isTestProject = projectType.IsTestProject,
                isPackable = projectType.IsPackable,
                dependencies,
                scheme = details.Scheme.ToString(),
                calver = details.Scheme == Core.Models.VersionScheme.CalVer && details.CalVerConfig != null ? new
                {
                    format = details.CalVerConfig.Format,
                    startDate = details.CalVerConfig.StartDate,
                    resetPatchPeriodically = details.CalVerConfig.ResetPatchPeriodically,
                    separator = details.CalVerConfig.Separator
                } : null,
                conventionalCommits = details.ConventionalCommitsEnabled ? new
                {
                    enabled = true,
                    bumpType = details.BumpType?.ToString(),
                    commitsAnalyzed = details.CommitClassifications?.Count ?? 0,
                    majorChanges = details.CommitClassifications?.Count(c => c.BumpType == VersionBumpType.Major) ?? 0,
                    minorChanges = details.CommitClassifications?.Count(c => c.BumpType == VersionBumpType.Minor) ?? 0,
                    patchChanges = details.CommitClassifications?.Count(c => c.BumpType == VersionBumpType.Patch) ?? 0,
                    ignoredCommits = details.CommitClassifications?.Count(c => c.ShouldIgnore) ?? 0,
                    classifications = details.CommitClassifications?.Select(c => new
                    {
                        sha = c.CommitSha,
                        type = c.CommitType,
                        scope = c.Scope,
                        description = c.Description,
                        bumpType = c.BumpType.ToString(),
                        isBreakingChange = c.IsBreakingChange,
                        breakingChangeDescription = c.BreakingChangeDescription,
                        shouldIgnore = c.ShouldIgnore,
                        reason = c.Reason
                    }).ToList()
                } : null,
                validation = details.ValidationResult != null ? new
                {
                    isValid = details.ValidationResult.IsValid,
                    summary = details.ValidationResult.Summary,
                    errors = details.ValidationResult.Errors?.Select(e => new
                    {
                        message = e.Message,
                        constraintName = e.ConstraintName,
                        expected = e.Expected,
                        actual = e.Actual,
                        details = e.Details
                    }).ToList(),
                    warnings = details.ValidationResult.Warnings?.Select(w => new
                    {
                        message = w.Message,
                        ruleName = w.RuleName,
                        details = w.Details
                    }).ToList()
                } : null
            };

            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonResult, jsonOptions));
        }

        private static void WriteDetails(Core.Models.VersionResult details, ProjectTypeInfo projectType, List<string> dependencies)
        {
            Console.WriteLine($"Reason: {details.ChangeReason}");
            Console.WriteLine($"Branch: {details.BranchName} ({details.BranchType})");
            if (!string.IsNullOrEmpty(details.PreviousVersion))
            {
                Console.WriteLine($"Previous Version: {details.PreviousVersion}" +
                                  (string.IsNullOrEmpty(details.PreviousCommitSha) ? "" : $" ({details.PreviousCommitSha})"));
            }

            // Show version scheme information
            Console.WriteLine($"Scheme: {details.Scheme}");
            if (details.Scheme == Core.Models.VersionScheme.CalVer && details.CalVerConfig != null)
            {
                Console.WriteLine($"CalVer Format: {details.CalVerConfig.Format}");
                if (!string.IsNullOrEmpty(details.CalVerConfig.StartDate))
                {
                    Console.WriteLine($"CalVer Start Date: {details.CalVerConfig.StartDate}");
                }
                Console.WriteLine($"CalVer Reset Patch: {details.CalVerConfig.ResetPatchPeriodically}");
            }

            // Show conventional commits analysis if enabled
            if (details.ConventionalCommitsEnabled && details.BumpType.HasValue)
            {
                Console.WriteLine();
                Console.WriteLine("Conventional Commits Analysis:");
                Console.WriteLine($"  Bump Type: {details.BumpType.Value}");

                if (details.CommitClassifications != null && details.CommitClassifications.Count > 0)
                {
                    Console.WriteLine($"  Commits Analyzed: {details.CommitClassifications.Count}");

                    // Group commits by bump type
                    var majorCommits = details.CommitClassifications.Where(c => c.BumpType == VersionBumpType.Major).ToList();
                    var minorCommits = details.CommitClassifications.Where(c => c.BumpType == VersionBumpType.Minor).ToList();
                    var patchCommits = details.CommitClassifications.Where(c => c.BumpType == VersionBumpType.Patch).ToList();
                    var ignoredCommits = details.CommitClassifications.Where(c => c.ShouldIgnore).ToList();

                    if (majorCommits.Any())
                    {
                        Console.WriteLine($"  Major changes: {majorCommits.Count}");
                        foreach (var commit in majorCommits.Take(3))
                        {
                            Console.WriteLine($"    - {commit.CommitSha}: {commit.Description}");
                            if (commit.IsBreakingChange)
                            {
                                Console.WriteLine($"      BREAKING: {commit.BreakingChangeDescription}");
                            }
                        }
                        if (majorCommits.Count > 3)
                            Console.WriteLine($"    ... and {majorCommits.Count - 3} more");
                    }

                    if (minorCommits.Any())
                    {
                        Console.WriteLine($"  Minor changes: {minorCommits.Count}");
                        foreach (var commit in minorCommits.Take(3))
                        {
                            Console.WriteLine($"    - {commit.CommitSha}: {commit.CommitType}: {commit.Description}");
                        }
                        if (minorCommits.Count > 3)
                            Console.WriteLine($"    ... and {minorCommits.Count - 3} more");
                    }

                    if (patchCommits.Any())
                    {
                        Console.WriteLine($"  Patch changes: {patchCommits.Count}");
                        foreach (var commit in patchCommits.Take(3))
                        {
                            Console.WriteLine($"    - {commit.CommitSha}: {commit.CommitType}: {commit.Description}");
                        }
                        if (patchCommits.Count > 3)
                            Console.WriteLine($"    ... and {patchCommits.Count - 3} more");
                    }

                    if (ignoredCommits.Any())
                    {
                        Console.WriteLine($"  Ignored commits: {ignoredCommits.Count} ({string.Join(", ", ignoredCommits.GroupBy(c => c.CommitType).Select(g => $"{g.Key}: {g.Count()}"))})");
                    }
                }
            }

            // Show validation results if validation was performed
            if (details.ValidationResult != null)
            {
                Console.WriteLine();
                Console.WriteLine("Version Validation:");
                Console.WriteLine($"  Status: {(details.ValidationResult.IsValid ? "✓ PASSED" : "✗ FAILED")}");

                if (details.ValidationResult.Errors != null && details.ValidationResult.Errors.Count > 0)
                {
                    Console.WriteLine($"  Errors: {details.ValidationResult.Errors.Count}");
                    foreach (var error in details.ValidationResult.Errors)
                    {
                        Console.WriteLine($"    ✗ {error.Message}");
                        if (!string.IsNullOrEmpty(error.ConstraintName))
                        {
                            Console.WriteLine($"      Constraint: {error.ConstraintName}");
                        }
                        if (!string.IsNullOrEmpty(error.Expected) && !string.IsNullOrEmpty(error.Actual))
                        {
                            Console.WriteLine($"      Expected: {error.Expected}");
                            Console.WriteLine($"      Actual: {error.Actual}");
                        }
                        if (!string.IsNullOrEmpty(error.Details))
                        {
                            Console.WriteLine($"      Details: {error.Details}");
                        }
                    }
                }

                if (details.ValidationResult.Warnings != null && details.ValidationResult.Warnings.Count > 0)
                {
                    Console.WriteLine($"  Warnings: {details.ValidationResult.Warnings.Count}");
                    foreach (var warning in details.ValidationResult.Warnings)
                    {
                        Console.WriteLine($"    ⚠ {warning.Message}");
                        if (!string.IsNullOrEmpty(warning.Details))
                        {
                            Console.WriteLine($"      Details: {warning.Details}");
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(details.CommitSha))
            {
                Console.WriteLine();
                Console.WriteLine($"Commit: {details.CommitSha}");
                Console.WriteLine($"Date: {details.CommitDate:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Message: {details.CommitMessage}");
            }

            if (details.BranchType == BranchType.Feature && details.CommitHeight > 0)
            {
                Console.WriteLine($"Commit Height: {details.CommitHeight}");
            }

            Console.WriteLine($"Test Project: {projectType.IsTestProject}");
            Console.WriteLine($"Packable: {projectType.IsPackable}");

            if (dependencies.Count > 0)
            {
                Console.WriteLine($"Dependencies: {string.Join(", ", dependencies)}");
            }
        }
    }
}
