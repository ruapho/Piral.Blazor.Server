﻿using NuGet.Frameworks;
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

            if (zip is not null)
            {
                using var zipStream = zip.Open();
                var msStream = new MemoryStream();
                zipStream.CopyTo(msStream);
                msStream.Position = 0;
                return msStream;
            }
        }
        catch (FileNotFoundException)
        {
            // This is expected - nothing wrong here
        }

        return null;
    }

    protected override Assembly? LoadMissingAssembly(AssemblyLoadContext _, AssemblyName assemblyName)
    {
        var dll = $"{assemblyName.Name}.dll";

        foreach (var package in _packages.Values)
        {
            var libItems = package.GetLibItems().Where(m => IsCompatible(m.TargetFramework)).SelectMany(m => m.Items);

            if (libItems is not null)
            {
                foreach (var lib in libItems)
                {
                    if (lib.EndsWith(dll))
                    {
                        return LoadAssembly(package, lib);
                    }
                }
            }
        }

        return null;
    }

    private static bool IsCompatible(NuGetFramework framework)
    {
        var current = NuGetFramework.Parse(target);
        return DefaultCompatibilityProvider.Instance.IsCompatible(current, framework);
    }

    protected override string GetCssName() => $"{Name}.bundle.scp.css";

    protected override Assembly? GetAssembly() => LoadAssembly(_packages[Name], $"lib/{target}/{Name}.dll");

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
