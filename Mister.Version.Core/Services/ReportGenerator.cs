using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public interface IReportGenerator
    {
        string GenerateTextReport(VersionReport report, ReportOptions options);
        string GenerateJsonReport(VersionReport report, ReportOptions options);
        string GenerateCsvReport(VersionReport report, ReportOptions options);
        string GenerateReport(VersionReport report, ReportOptions options);
        string GenerateDependencyGraph(VersionReport report, ReportOptions options);
    }

    public class ReportOptions
    {
        public bool IncludeCommits { get; set; } = true;
        public bool IncludeDependencies { get; set; } = true;
        public bool IncludeTestProjects { get; set; } = false;
        public bool IncludeNonPackableProjects { get; set; } = false;
        public string OutputFormat { get; set; } = "text"; // text, json, csv, graph
        public string GraphFormat { get; set; } = "mermaid"; // mermaid, dot, ascii
        public bool ShowVersions { get; set; } = true;
        public bool ShowChangedOnly { get; set; } = false;
    }

    public class ReportGenerator : IReportGenerator
    {
        // Constants for limiting output
        private const int MAX_TRANSITIVE_DEPS_TO_DISPLAY = 10;
        private const int MAX_GRAPH_DEPTH = 10;

        // Constants for graph visualization - Emoji symbols
        private const string EMOJI_CHANGED = "🔄";
        private const string EMOJI_TEST = "🧪";
        private const string EMOJI_PACKABLE = "📦";
        private const string EMOJI_NORMAL = "📁";

        // Constants for graph visualization - Color codes
        private const string COLOR_CHANGED = "#ff9999";
        private const string COLOR_CHANGED_BORDER = "#ff0000";
        private const string COLOR_TEST = "#ccccff";
        private const string COLOR_TEST_BORDER = "#0000ff";
        private const string COLOR_PACKABLE = "#ccffcc";
        private const string COLOR_PACKABLE_BORDER = "#00aa00";
        private const string COLOR_NORMAL = "#f9f9f9";
        private const string COLOR_NORMAL_BORDER = "#333";

        public string GenerateReport(VersionReport report, ReportOptions options)
        {
            return options.OutputFormat.ToLower() switch
            {
                "json" => GenerateJsonReport(report, options),
                "csv" => GenerateCsvReport(report, options),
                "graph" => GenerateDependencyGraph(report, options),
                _ => GenerateTextReport(report, options)
            };
        }

        public string GenerateTextReport(VersionReport report, ReportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MonoRepo Version Report ===");
            sb.AppendLine($"Repository: {report.Repository}");
            sb.AppendLine($"Branch: {report.Branch} ({report.BranchType})");
            sb.AppendLine($"Global Version: {report.GlobalVersion?.ToVersionString() ?? "Unknown"}");
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Total Projects: {report.TotalProjects}");
            sb.AppendLine($"Projects with Changes: {report.ProjectsWithChanges}");
            sb.AppendLine();

            var filteredProjects = FilterProjects(report.Projects, options);
            var orderedProjects = filteredProjects.OrderBy(p => p.Name).ToList();

            foreach (var project in orderedProjects)
            {
                sb.AppendLine($"Project: {project.Name}");
                sb.AppendLine($"  Path: {project.Path}");
                sb.AppendLine($"  Version: {project.Version?.Version ?? "Unknown"}");
                sb.AppendLine($"  Previous: {project.Version?.PreviousVersion ?? "N/A"}");
                if (!string.IsNullOrEmpty(project.Version?.PreviousCommitSha))
                {
                    sb.AppendLine($"  Previous Commit: {project.Version.PreviousCommitSha}");
                }
                sb.AppendLine($"  Changed: {(project.Version?.VersionChanged == true ? "Yes" : "No")}");

                if (project.Version?.VersionChanged == true && !string.IsNullOrEmpty(project.Version.ChangeReason))
                {
                    sb.AppendLine($"  Reason: {project.Version.ChangeReason}");
                }

                if (options.IncludeCommits && project.Version != null)
                {
                    if (!string.IsNullOrEmpty(project.Version.CommitSha))
                    {
                        sb.AppendLine($"  Commit: {project.Version.CommitSha}");
                        sb.AppendLine($"  Date: {project.Version.CommitDate:yyyy-MM-dd HH:mm:ss}");
                        sb.AppendLine($"  Message: {project.Version.CommitMessage}");
                    }

                    if (project.Version.BranchType == BranchType.Feature && project.Version.CommitHeight > 0)
                    {
                        sb.AppendLine($"  Commit Height: {project.Version.CommitHeight}");
                    }
                }

                if (options.IncludeDependencies)
                {
                    if (project.DirectDependencies.Count > 0)
                    {
                        sb.AppendLine($"  Direct Dependencies ({project.DirectDependencies.Count}):");
                        foreach (var dep in project.DirectDependencies)
                        {
                            var depProject = report.Projects.FirstOrDefault(p => p.Name == dep);
                            var depVersion = depProject?.Version?.Version ?? "Unknown";
                            sb.AppendLine($"    - {dep} ({depVersion})");
                        }
                    }

                    if (project.AllDependencies.Count > project.DirectDependencies.Count)
                    {
                        var transitiveDeps = project.AllDependencies.Except(project.DirectDependencies).ToList();
                        if (transitiveDeps.Count > 0)
                        {
                            sb.AppendLine($"  Transitive Dependencies ({transitiveDeps.Count}):");
                            foreach (var dep in transitiveDeps.Take(MAX_TRANSITIVE_DEPS_TO_DISPLAY))
                            {
                                var depProject = report.Projects.FirstOrDefault(p => p.Name == dep);
                                var depVersion = depProject?.Version?.Version ?? "Unknown";
                                sb.AppendLine($"    - {dep} ({depVersion})");
                            }
                            if (transitiveDeps.Count > MAX_TRANSITIVE_DEPS_TO_DISPLAY)
                            {
                                sb.AppendLine($"    ... and {transitiveDeps.Count - MAX_TRANSITIVE_DEPS_TO_DISPLAY} more");
                            }
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string GenerateJsonReport(VersionReport report, ReportOptions options)
        {
            var filteredProjects = FilterProjects(report.Projects, options);

            var jsonReport = new
            {
                repository = report.Repository,
                branch = report.Branch,
                branchType = report.BranchType.ToString(),
                globalVersion = report.GlobalVersion?.ToVersionString(),
                generatedAt = report.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                totalProjects = filteredProjects.Count,
                projectsWithChanges = filteredProjects.Count(p => p.Version?.VersionChanged == true),
                projects = filteredProjects.OrderBy(p => p.Name).Select(p => new
                {
                    name = p.Name,
                    path = p.Path,
                    fullPath = p.FullPath,
                    version = new
                    {
                        version = p.Version?.Version,
                        previousVersion = p.Version?.PreviousVersion,
                        previousCommitSha = p.Version?.PreviousCommitSha,
                        semVer = p.Version?.SemVer != null ? new
                        {
                            major = p.Version.SemVer.Major,
                            minor = p.Version.SemVer.Minor,
                            patch = p.Version.SemVer.Patch,
                            preRelease = p.Version.SemVer.PreRelease,
                            buildMetadata = p.Version.SemVer.BuildMetadata
                        } : null,
                        versionChanged = p.Version?.VersionChanged == true,
                        changeReason = p.Version?.ChangeReason,
                        commitSha = options.IncludeCommits ? p.Version?.CommitSha : null,
                        commitDate = options.IncludeCommits ? p.Version?.CommitDate?.ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
                        commitMessage = options.IncludeCommits ? p.Version?.CommitMessage : null,
                        branchType = p.Version?.BranchType.ToString(),
                        branchName = p.Version?.BranchName,
                        commitHeight = (options.IncludeCommits && p.Version?.BranchType == BranchType.Feature) ? p.Version?.CommitHeight : null
                    },
                    dependencies = options.IncludeDependencies ? new
                    {
                        direct = p.DirectDependencies.Select(d => new
                        {
                            name = d,
                            version = report.Projects.FirstOrDefault(proj => proj.Name == d)?.Version?.Version
                        }),
                        all = p.AllDependencies.Select(d => new
                        {
                            name = d,
                            version = report.Projects.FirstOrDefault(proj => proj.Name == d)?.Version?.Version,
                            isTransitive = !p.DirectDependencies.Contains(d)
                        })
                    } : null,
                    isTestProject = p.IsTestProject,
                    isPackable = p.IsPackable
                })
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(jsonReport, jsonOptions);
        }

        public string GenerateCsvReport(VersionReport report, ReportOptions options)
        {
            var sb = new StringBuilder();
            var filteredProjects = FilterProjects(report.Projects, options);

            // Write header
            var headers = new List<string>
            {
                "ProjectName",
                "ProjectPath",
                "Version",
                "PreviousVersion",
                "PreviousCommitSha",
                "VersionChanged",
                "ChangeReason"
            };

            if (options.IncludeCommits)
            {
                headers.AddRange(new[] { "CommitSha", "CommitDate", "CommitMessage", "BranchType", "BranchName" });
            }

            if (options.IncludeDependencies)
            {
                headers.AddRange(new[] { "DirectDependencies", "AllDependencies" });
            }

            headers.AddRange(new[] { "IsTestProject", "IsPackable" });

            sb.AppendLine(string.Join(",", headers));

            // Write data
            foreach (var project in filteredProjects.OrderBy(p => p.Name))
            {
                var row = new List<string>
                {
                    EscapeCsv(project.Name),
                    EscapeCsv(project.Path),
                    EscapeCsv(project.Version?.Version ?? ""),
                    EscapeCsv(project.Version?.PreviousVersion ?? "N/A"),
                    EscapeCsv(project.Version?.PreviousCommitSha ?? "N/A"),
                    (project.Version?.VersionChanged == true).ToString(),
                    EscapeCsv(project.Version?.ChangeReason ?? "")
                };

                if (options.IncludeCommits)
                {
                    row.AddRange(new[]
                    {
                        EscapeCsv(project.Version?.CommitSha ?? ""),
                        EscapeCsv(project.Version?.CommitDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                        EscapeCsv(project.Version?.CommitMessage ?? ""),
                        EscapeCsv(project.Version?.BranchType.ToString() ?? ""),
                        EscapeCsv(project.Version?.BranchName ?? "")
                    });
                }

                if (options.IncludeDependencies)
                {
                    row.AddRange(new[]
                    {
                        EscapeCsv(string.Join(";", project.DirectDependencies)),
                        EscapeCsv(string.Join(";", project.AllDependencies))
                    });
                }

                row.AddRange(new[]
                {
                    project.IsTestProject.ToString(),
                    project.IsPackable.ToString()
                });

                sb.AppendLine(string.Join(",", row));
            }

            return sb.ToString();
        }

        private List<ProjectInfo> FilterProjects(List<ProjectInfo> projects, ReportOptions options)
        {
            return projects.Where(p =>
            {
                if (!options.IncludeTestProjects && p.IsTestProject)
                    return false;
                
                if (!options.IncludeNonPackableProjects && !p.IsPackable)
                    return false;

                return true;
            }).ToList();
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // If the value contains a comma, quote, or newline, enclose it in quotes and escape embedded quotes
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        public string GenerateDependencyGraph(VersionReport report, ReportOptions options)
        {
            var filteredProjects = FilterProjects(report.Projects, options);
            
            if (options.ShowChangedOnly)
            {
                filteredProjects = filteredProjects.Where(p => p.Version?.VersionChanged == true).ToList();
            }

            return options.GraphFormat.ToLower() switch
            {
                "dot" => GenerateDotGraph(filteredProjects, options),
                "ascii" => GenerateAsciiGraph(filteredProjects, options),
                _ => GenerateMermaidGraph(filteredProjects, options)
            };
        }

        private string GenerateMermaidGraph(List<ProjectInfo> projects, ReportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("```mermaid");
            sb.AppendLine("graph TD");
            sb.AppendLine("    %% MonoRepo Dependency Graph with Versions");
            sb.AppendLine();

            // Generate node definitions with versions and styling
            foreach (var project in projects.OrderBy(p => p.Name))
            {
                var nodeId = SanitizeNodeId(project.Name);
                var label = options.ShowVersions && project.Version != null 
                    ? $"{project.Name}<br/>{project.Version.Version}"
                    : project.Name;

                // Style nodes based on type and status
                var style = GetMermaidNodeStyle(project);
                sb.AppendLine($"    {nodeId}[\"{label}\"]");
                if (!string.IsNullOrEmpty(style))
                {
                    sb.AppendLine($"    class {nodeId} {style};");
                }
            }

            sb.AppendLine();

            // Generate dependencies
            foreach (var project in projects.OrderBy(p => p.Name))
            {
                var projectNodeId = SanitizeNodeId(project.Name);
                
                foreach (var dependency in project.DirectDependencies)
                {
                    var depProject = projects.FirstOrDefault(p => p.Name == dependency);
                    if (depProject != null)
                    {
                        var depNodeId = SanitizeNodeId(dependency);
                        var edgeLabel = "";
                        
                        if (options.ShowVersions && depProject.Version != null)
                        {
                            edgeLabel = $"|{depProject.Version.Version}|";
                        }

                        sb.AppendLine($"    {depNodeId} --> {projectNodeId}{edgeLabel}");
                    }
                }
            }

            sb.AppendLine();

            // Add styling classes
            sb.AppendLine($"    classDef changed fill:{COLOR_CHANGED},stroke:{COLOR_CHANGED_BORDER},stroke-width:2px,color:#000;");
            sb.AppendLine($"    classDef test fill:{COLOR_TEST},stroke:{COLOR_TEST_BORDER},stroke-width:1px,color:#000;");
            sb.AppendLine($"    classDef packable fill:{COLOR_PACKABLE},stroke:{COLOR_PACKABLE_BORDER},stroke-width:1px,color:#000;");
            sb.AppendLine($"    classDef normal fill:{COLOR_NORMAL},stroke:{COLOR_NORMAL_BORDER},stroke-width:1px,color:#000;");
            
            sb.AppendLine("```");
            return sb.ToString();
        }

        private string GenerateDotGraph(List<ProjectInfo> projects, ReportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("digraph MonoRepoDependencies {");
            sb.AppendLine("    rankdir=TD;");
            sb.AppendLine("    node [shape=box, style=filled];");
            sb.AppendLine("    edge [color=gray];");
            sb.AppendLine();

            // Generate node definitions
            foreach (var project in projects.OrderBy(p => p.Name))
            {
                var nodeId = SanitizeNodeId(project.Name);
                var label = options.ShowVersions && project.Version != null 
                    ? $"{project.Name}\\n{project.Version.Version}"
                    : project.Name;

                var style = GetDotNodeStyle(project);
                sb.AppendLine($"    {nodeId} [label=\"{label}\"{style}];");
            }

            sb.AppendLine();

            // Generate dependencies
            foreach (var project in projects.OrderBy(p => p.Name))
            {
                var projectNodeId = SanitizeNodeId(project.Name);
                
                foreach (var dependency in project.DirectDependencies)
                {
                    var depProject = projects.FirstOrDefault(p => p.Name == dependency);
                    if (depProject != null)
                    {
                        var depNodeId = SanitizeNodeId(dependency);
                        var edgeStyle = "";
                        
                        if (options.ShowVersions && depProject.Version != null)
                        {
                            edgeStyle = $", label=\"{depProject.Version.Version}\"";
                        }

                        sb.AppendLine($"    {depNodeId} -> {projectNodeId}[{edgeStyle}];");
                    }
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private string GenerateAsciiGraph(List<ProjectInfo> projects, ReportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MonoRepo Dependency Graph ===");
            sb.AppendLine();

            // Create a tree-like ASCII representation
            var rootProjects = projects.Where(p => 
                !projects.Any(other => other.DirectDependencies.Contains(p.Name))).ToList();

            if (!rootProjects.Any())
            {
                // If there are circular dependencies, just pick projects without dependencies
                rootProjects = projects.Where(p => !p.DirectDependencies.Any()).ToList();
            }

            if (!rootProjects.Any())
            {
                // Fallback: show all projects
                rootProjects = projects.Take(1).ToList();
            }

            var visited = new HashSet<string>();
            foreach (var root in rootProjects.OrderBy(p => p.Name))
            {
                if (!visited.Contains(root.Name))
                {
                    GenerateAsciiNode(sb, root, projects, options, visited, 0);
                }
            }

            // Show any remaining projects that weren't visited (isolated or circular)
            var remaining = projects.Where(p => !visited.Contains(p.Name)).ToList();
            if (remaining.Any())
            {
                sb.AppendLine();
                sb.AppendLine("=== Isolated/Circular Dependencies ===");
                foreach (var project in remaining.OrderBy(p => p.Name))
                {
                    if (!visited.Contains(project.Name))
                    {
                        GenerateAsciiNode(sb, project, projects, options, visited, 0);
                    }
                }
            }

            return sb.ToString();
        }

        private void GenerateAsciiNode(StringBuilder sb, ProjectInfo project, List<ProjectInfo> allProjects, 
            ReportOptions options, HashSet<string> visited, int depth)
        {
            if (visited.Contains(project.Name) || depth > MAX_GRAPH_DEPTH) // Prevent infinite recursion
            {
                return;
            }

            visited.Add(project.Name);

            var indent = new string(' ', depth * 2);
            var symbol = GetAsciiSymbol(project);
            var versionInfo = options.ShowVersions && project.Version != null ? $" ({project.Version.Version})" : "";
            var changeIndicator = project.Version?.VersionChanged == true ? " [CHANGED]" : "";

            sb.AppendLine($"{indent}{symbol} {project.Name}{versionInfo}{changeIndicator}");

            // Show dependencies
            var dependents = allProjects.Where(p => p.DirectDependencies.Contains(project.Name))
                .OrderBy(p => p.Name).ToList();
            
            foreach (var dependent in dependents)
            {
                GenerateAsciiNode(sb, dependent, allProjects, options, visited, depth + 1);
            }
        }

        private string SanitizeNodeId(string name)
        {
            // Replace invalid characters for node IDs
            return Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        }

        private string GetMermaidNodeStyle(ProjectInfo project)
        {
            if (project.Version?.VersionChanged == true)
                return "changed";
            if (project.IsTestProject)
                return "test";
            if (project.IsPackable)
                return "packable";
            return "normal";
        }

        private string GetDotNodeStyle(ProjectInfo project)
        {
            if (project.Version?.VersionChanged == true)
                return $", fillcolor=\"{COLOR_CHANGED}\", color=\"{COLOR_CHANGED_BORDER}\"";
            if (project.IsTestProject)
                return $", fillcolor=\"{COLOR_TEST}\", color=\"{COLOR_TEST_BORDER}\"";
            if (project.IsPackable)
                return $", fillcolor=\"{COLOR_PACKABLE}\", color=\"{COLOR_PACKABLE_BORDER}\"";
            return $", fillcolor=\"{COLOR_NORMAL}\", color=\"{COLOR_NORMAL_BORDER}\"";
        }

        private string GetAsciiSymbol(ProjectInfo project)
        {
            if (project.Version?.VersionChanged == true)
                return EMOJI_CHANGED;
            if (project.IsTestProject)
                return EMOJI_TEST;
            if (project.IsPackable)
                return EMOJI_PACKABLE;
            return EMOJI_NORMAL;
        }
    }
}