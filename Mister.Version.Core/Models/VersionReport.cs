using System;
using System.Collections.Generic;
using System.Linq;

namespace Mister.Version.Core.Models;

/// <summary>
/// Repository version report
/// </summary>
public class VersionReport
{
    public string Repository { get; set; }
    public string Branch { get; set; }
    public BranchType BranchType { get; set; }
    public SemVer GlobalVersion { get; set; }
    public List<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int TotalProjects => Projects.Count;
    public int ProjectsWithChanges => Projects.Count(p => p.Version?.VersionChanged == true);
}