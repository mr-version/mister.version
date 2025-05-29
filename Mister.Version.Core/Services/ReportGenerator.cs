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
    }

    public class ReportOptions
    {
        public bool IncludeCommits { get; set; } = true;
        public bool IncludeDependencies { get; set; } = true;
        public bool IncludeTestProjects { get; set; } = false;
        public bool IncludeNonPackableProjects { get; set; } = false;
        public string OutputFormat { get; set; } = "text"; // text, json, csv
    }

    public class ReportGenerator : IReportGenerator
    {
        public string GenerateReport(VersionReport report, ReportOptions options)
        {
            return options.OutputFormat.ToLower() switch
            {
                "json" => GenerateJsonReport(report, options),
                "csv" => GenerateCsvReport(report, options),
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
                            foreach (var dep in transitiveDeps.Take(10)) // Limit to avoid too much output
                            {
                                var depProject = report.Projects.FirstOrDefault(p => p.Name == dep);
                                var depVersion = depProject?.Version?.Version ?? "Unknown";
                                sb.AppendLine($"    - {dep} ({depVersion})");
                            }
                            if (transitiveDeps.Count > 10)
                            {
                                sb.AppendLine($"    ... and {transitiveDeps.Count - 10} more");
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
    }
}