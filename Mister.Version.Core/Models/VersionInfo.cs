namespace Mister.Version.Models;
/// <summary>
/// Version information for reporting
/// </summary>
public class VersionInfo
{
    public string Version { get; set; }
    public string CommitSha { get; set; }
    public string CommitDate { get; set; }
    public string CommitMessage { get; set; }
}
