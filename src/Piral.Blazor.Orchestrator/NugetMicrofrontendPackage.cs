using NuGet.Frameworks;
using NuGet.Packaging;
using System.Reflection;
using System.Runtime.Loader;

namespace Piral.Blazor.Orchestrator;

internal class NugetMicrofrontendPackage(string name, string version, List<PackageArchiveReader> packages, IModuleContainerService container, IEvents events, IData data) : MicrofrontendPackage(name, version, container, events, data)
{
    private const string target = "net8.0";
    private readonly Dictionary<string, PackageArchiveReader> _packages = packages.ToDictionary(m => m.NuspecReader.GetId());

    private Assembly? LoadAssembly(PackageArchiveReader package, string path)
    {
        using var msStream = GetFile(package, path);

        if (msStream is not null)
        {
            return Context.LoadFromStream(msStream);
        }

        return null;
    }

    private static Stream? GetFile(PackageArchiveReader package, string path)
    {
        try
        {
            var zip = package.GetEntry(path);
            //Console.WriteLine("Loading nuget package file: '{0}'", path);

            if (zip is not null)
            {
                using var zipStream = zip.Open();
                var msStream = new MemoryStream();
                zipStream.CopyTo(msStream);
                msStream.Position = 0;
                return msStream;
            }
        } catch (FileNotFoundException)
        {
            //Console.WriteLine("Nuget package not found file: '{0}'", path);
            // This is expected - nothing wrong here
        } catch (InvalidDataException ex)
        {
            //Console.WriteLine("Error reading nuget package: {0} {1}", path, ex.Message);
            // Also expected - nothing wrong here
        }
        return null;
    }

    protected override Assembly? LoadMissingAssembly(AssemblyLoadContext _, AssemblyName assemblyName)
    {
        var dll = $"{assemblyName.Name}.dll";
        //Console.WriteLine("Searching for: '{0}'", dll);

        foreach (var package in _packages.Values)
        {
            var libItem = GetBestFit(package.GetLibItems(), dll);
            if(libItem is not null)
            {
				//Console.WriteLine("Taking {0} as the most compatible", libItem);
				return LoadAssembly(package, libItem);
			}
    }

        return null;
    }

    private static string? GetBestFit(IEnumerable<FrameworkSpecificGroup> frameworks, string dll)
	{
		var current = NuGetFramework.Parse(target);
        var compatible = frameworks.Where(f => DefaultCompatibilityProvider.Instance.IsCompatible(current, f.TargetFramework));

        if (!compatible.Any())
		{
            return null;
		}

        var candidates = compatible
            .OrderByDescending(f => f.TargetFramework.Version)
            .ThenBy(f => f.TargetFramework.Framework == ".NETStandard" ? 1 : 0)// Prefer non - .NETStandard
            .Where(f => f.Items.Any(i => i.EndsWith(dll)))
            .SelectMany(f => f.Items)
            .Where(i => i.EndsWith(dll))
            .ToList();

        //if(candidates.Any())
			//Console.WriteLine("Found {0} matching dlls: \n-{1}", candidates.Count(), string.Join("\n-", candidates));

		return candidates.FirstOrDefault();
	}


    protected override string GetCssName() => $"{Name}.bundle.scp.css";

    protected override Assembly? GetAssembly() {
        var dll = $"lib/{target}/{Name}.dll";

		Console.WriteLine("LoadAssembly {0} from path: {1}", Name, dll);
		return LoadAssembly(_packages[Name], dll);
    }

    public override Stream? GetFile(string path)
    {
        if (path.StartsWith("_content"))
        {
            var segments = path.Split('/');
            var packageName = segments[1];
            var localPath = string.Join('/', segments.Skip(2));
            var package = _packages[packageName];

            if (package is not null)
            {
                return GetFile(package, $"staticwebassets/{localPath}");
            }

            return null;
        }
        else
        {
            var package = _packages[Name];
            return GetFile(package, $"staticwebassets/{path}");
        }
    }
}
