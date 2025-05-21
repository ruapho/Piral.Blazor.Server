using NuGet.Frameworks;
using NuGet.Packaging;

namespace Piral.Blazor.Orchestrator;

internal static class NuGetExtensions
{
    private static bool CheckCompatibility(FrameworkSpecificGroup group) => !group.TargetFramework.IsAny && IsCompatible(group.TargetFramework);

    private static bool IsCompatible(NuGetFramework framework) => DefaultCompatibilityProvider.Instance.IsCompatible(Constants.CurrentFramework, framework);

    public static IEnumerable<string> GetMatchingLibItems(this PackageArchiveReader package)
    {
        var compatibleItems = package.GetLibItems().Where(CheckCompatibility);

        if (compatibleItems == null || !compatibleItems.Any())
        {
            return package.GetLibItems().FirstOrDefault(m => m.TargetFramework.IsAny)?.Items ?? Enumerable.Empty<string>();
        }

        return compatibleItems
            .OrderByDescending(f => f.TargetFramework.Version)
            .ThenBy(f => f.TargetFramework.Framework == ".NETStandard" ? 1 : 0) // Prefer non - .NETStandard
            .SelectMany(f => f.Items);
    }
}
