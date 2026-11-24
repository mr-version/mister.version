using System.Linq;
using System;
using System.Text.RegularExpressions;

namespace Mister.Version.Core.Models;

/// <summary>
/// Semantic version representation
/// </summary>
public class SemVer : IComparable<SemVer>, IEquatable<SemVer>
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

    public int CompareTo(SemVer other)
    {
        if (other == null) return 1;

        // Compare major, minor, patch
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) return majorComparison;

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0) return minorComparison;

        var patchComparison = Patch.CompareTo(other.Patch);
        if (patchComparison != 0) return patchComparison;

        // Handle prerelease comparison
        if (string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
            return 0;

        if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
            return 1; // Release version is higher than prerelease

        if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
            return -1; // Prerelease is lower than release

        // Both have prerelease, compare them
        return ComparePrerelease(PreRelease, other.PreRelease);
    }

    private int ComparePrerelease(string prerelease1, string prerelease2)
    {
        var parts1 = prerelease1.Split('.');
        var parts2 = prerelease2.Split('.');

        int maxLength = Math.Max(parts1.Length, parts2.Length);
        for (int i = 0; i < maxLength; i++)
        {
            var part1 = i < parts1.Length ? parts1[i] : "";
            var part2 = i < parts2.Length ? parts2[i] : "";

            // Try to parse as numbers
            bool isNum1 = int.TryParse(part1, out int num1);
            bool isNum2 = int.TryParse(part2, out int num2);

            if (isNum1 && isNum2)
            {
                var numComparison = num1.CompareTo(num2);
                if (numComparison != 0) return numComparison;
            }
            else
            {
                var stringComparison = string.CompareOrdinal(part1, part2);
                if (stringComparison != 0) return stringComparison;
            }
        }

        return 0;
    }

    public bool Equals(SemVer other)
    {
        if (other == null) return false;
        return Major == other.Major &&
               Minor == other.Minor &&
               Patch == other.Patch &&
               PreRelease == other.PreRelease &&
               BuildMetadata == other.BuildMetadata;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SemVer);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + Major.GetHashCode();
            hash = hash * 23 + Minor.GetHashCode();
            hash = hash * 23 + Patch.GetHashCode();
            hash = hash * 23 + (PreRelease?.GetHashCode() ?? 0);
            hash = hash * 23 + (BuildMetadata?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(SemVer left, SemVer right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(SemVer left, SemVer right)
    {
        return !(left == right);
    }

    public static bool operator <(SemVer left, SemVer right)
    {
        if (left is null) return right is not null;
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemVer left, SemVer right)
    {
        if (left is null) return false;
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SemVer left, SemVer right)
    {
        if (left is null) return true;
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(SemVer left, SemVer right)
    {
        if (left is null) return right is null;
        return left.CompareTo(right) >= 0;
    }

    public static bool TryParse(string version, out SemVer result)
    {
        result = null;

        if (string.IsNullOrEmpty(version))
            return false;

        // Handle semver with prerelease and build metadata
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?(?:-([0-9A-Za-z\-\.]+))?(?:\+([0-9A-Za-z\-\.]+))?$");
        if (!match.Success)
            return false;

        // Numbers too large for an int (v99999999999.0.0) are not versions
        if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor))
            return false;
        var patch = 0;
        if (match.Groups[3].Success &&
            !int.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out patch))
            return false;

        var preRelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (preRelease != null && !IsValidPreRelease(preRelease))
            return false;

        var buildMetadata = match.Groups[5].Success ? match.Groups[5].Value : null;
        if (buildMetadata != null && buildMetadata.Split('.').Any(identifier => identifier.Length == 0))
            return false;

        result = new SemVer
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            PreRelease = preRelease,
            BuildMetadata = buildMetadata
        };
        return true;
    }

    /// <summary>
    /// SemVer 2.0 prerelease: dot-separated non-empty identifiers; numeric identifiers without leading zeros (NuGet
    /// rejects "1.0.0-01").
    /// </summary>
    public static bool IsValidPreRelease(string preRelease)
    {
        if (string.IsNullOrEmpty(preRelease))
            return false;
        foreach (var identifier in preRelease.Split('.'))
        {
            if (identifier.Length == 0)
                return false;
            if (identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsDigit))
                return false;
        }
        return true;
    }

    public static implicit operator SemVer(string version)
    {
        return TryParse(version, out var result) ? result : null;
    }
}