using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Version calculation options
/// </summary>
public class VersionOptions
{
    public string RepoRoot { get; set; }
    public string ProjectPath { get; set; }
    public string ProjectName { get; set; }
    public string TagPrefix { get; set; } = "v";
    public string ForceVersion { get; set; }
    public List<string> Dependencies { get; set; } = new List<string>();
    public bool Debug { get; set; } = false;
    public bool ExtraDebug { get; set; } = false;
    public bool SkipTestProjects { get; set; } = true;
    public bool SkipNonPackableProjects { get; set; } = true;
    public bool IsTestProject { get; set; } = false;
    public bool IsPackable { get; set; } = true;
}