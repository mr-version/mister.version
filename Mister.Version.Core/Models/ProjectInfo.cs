using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Project information for reporting
/// </summary>
public class ProjectInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string FullPath { get; set; }
    public VersionResult Version { get; set; }
    public List<string> DirectDependencies { get; set; } = new List<string>();
    public List<string> AllDependencies { get; set; } = new List<string>();
    public bool IsTestProject { get; set; }
    public bool IsPackable { get; set; }
}