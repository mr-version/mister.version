using LibGit2Sharp;

namespace Mister.Version.Core.Models;

/// <summary>
/// Version tag information combining git tag with parsed semantic version
/// </summary>
public class VersionTag
{
    public Tag Tag { get; set; }
    public SemVer SemVer { get; set; }
    public Commit Commit { get; set; }
    public bool IsGlobal { get; set; }
    public string ProjectName { get; set; }
}