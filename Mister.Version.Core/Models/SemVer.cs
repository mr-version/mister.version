namespace Mister.Version.Core.Models;

/// <summary>
/// Semantic version representation
/// </summary>
public class SemVer
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }
    public string PreRelease { get; set; }
    public string BuildMetadata { get; set; }

    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(PreRelease))
        {
            version += $"-{PreRelease}";
        }
        if (!string.IsNullOrEmpty(BuildMetadata))
        {
            version += $"+{BuildMetadata}";
        }
        return version;
    }

    public string ToVersionString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(PreRelease))
        {
            version += $"-{PreRelease}";
        }
        return version;
    }

    public SemVer Clone()
    {
        return new SemVer
        {
            Major = Major,
            Minor = Minor,
            Patch = Patch,
            PreRelease = PreRelease,
            BuildMetadata = BuildMetadata
        };
    }
}