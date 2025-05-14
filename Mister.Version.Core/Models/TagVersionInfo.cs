namespace Mister.Version.Models;
/// <summary>
/// Tag version information for reporting
/// </summary>
public class TagVersionInfo
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }
    public string CommitSha { get; set; }
    public string CommitDate { get; set; }
    public string CommitMessage { get; set; }
}
