namespace Mister.Version.Core.Models;

/// <summary>
/// Change information
/// </summary>
public class ChangeInfo
{
    public string FilePath { get; set; }
    public string ChangeType { get; set; }
    public string ProjectName { get; set; }
    public bool IsPackageLockFile { get; set; }
    public bool IsDependencyChange { get; set; }
    public string DependencyName { get; set; }
}