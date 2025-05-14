using System;

namespace Mister.Version.Core.Models;

/// <summary>
/// Version calculation result
/// </summary>
public class VersionResult
{
    public string Version { get; set; }
    public SemVer SemVer { get; set; }
    public bool VersionChanged { get; set; }
    public string ChangeReason { get; set; }
    public string CommitSha { get; set; }
    public DateTime? CommitDate { get; set; }
    public string CommitMessage { get; set; }
    public BranchType BranchType { get; set; }
    public string BranchName { get; set; }
    public int CommitHeight { get; set; }
}