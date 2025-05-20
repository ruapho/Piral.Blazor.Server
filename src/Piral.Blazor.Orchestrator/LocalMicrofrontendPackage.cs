﻿using NuGet.Packaging;
using Piral.Blazor.Shared;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Piral.Blazor.Orchestrator;

internal class LocalMicrofrontendPackage(string path, IPiralConfig config, IModuleContainerService container, IGlobalEvents events, IData data) :
    MicrofrontendPackage(MakePackage(path), config, container, events, data)
{
    private readonly string _path = path;
    private readonly List<string> _contentRoots = [];
    private readonly List<PackageArchiveReader> _packages = [];

    private Assembly? LoadAssembly(PackageArchiveReader package, string path)
    {
        using var msStream = GetFile(package, path).Result;

        if (msStream is not null)
        {
            return Context.LoadFromStream(msStream);
        }

        return null;
    }

    private static MfPackageMetadata MakePackage(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var config = GetMicrofrontendConfig(path);
        return new MfPackageMetadata { Name = name, Version = "0.0.0", Config = config };
    }

    private static JsonObject? GetMicrofrontendConfig(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var cfgPath = Path.Combine(dir, "config.json");

        if (File.Exists(cfgPath))
        {
            var text = File.ReadAllText(cfgPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<JsonObject?>(text);
        }

        return null;
    }

    protected override Assembly? ResolveAssembly(string dll)
    {
        foreach (var package in _packages)
        {
            var libItems = package.GetMatchingLibItems();

            foreach (var lib in libItems)
            {
                if (lib.EndsWith(dll))
                {
                    return LoadAssembly(package, lib);
                }
            }
        }

        return null;
    }

    protected override async Task OnInitializing()
    {
        await SetContentRoots();
        await SetDependencies();
        await base.OnInitializing();
    }

    private async Task SetContentRoots()
    {
        var infos = Path.ChangeExtension(_path, ".staticwebassets.runtime.json");
        using var fs = File.OpenRead(infos);
        var assets = await JsonSerializer.DeserializeAsync<StaticWebAssets>(fs);

        if (assets?.ContentRoots is not null)
        {
            _contentRoots.AddRange(assets.ContentRoots);
        }
    }

    private async Task SetDependencies()
    {
        var infos = Path.ChangeExtension(_path, ".deps.json");
        using var fs = File.OpenRead(infos);
        var deps = await JsonSerializer.DeserializeAsync<DependenciesList>(fs);

        if (deps?.Libraries is not null)
        {
            foreach (var lib in deps.Libraries)
            {
                if (lib.Value.Type == "package" && lib.Value.Path is not null)
                {
                    var packageName = lib.Key.ToLowerInvariant().Replace('/', '.');
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var packagePath = Path.Combine(userProfile, ".nuget", "packages", lib.Value.Path, $"{packageName}.nupkg");
                    var stream = File.OpenRead(packagePath);
                    _packages.Add(new PackageArchiveReader(stream));
                }
            }
        }
    }

    protected override Assembly? GetAssembly() => Context.LoadFromAssemblyPath(_path);

    public override Task<Stream?> GetFile(string path)
    {
        if (path.StartsWith("_content"))
        {
            var segments = path.Split('/');
            var packageName = segments[1];
            var localPath = string.Join('/', segments.Skip(2));

            foreach (var contentRoot in _contentRoots)
            {
                if (contentRoot.Contains(packageName, StringComparison.OrdinalIgnoreCase))
                {
                    var fullPath = Path.Combine(contentRoot, localPath);

                    if (File.Exists(fullPath))
                    {
                        var fs = File.OpenRead(fullPath);
                        return Task.FromResult<Stream?>(fs);
                    }
                }
            }
        }
        else
        {
            foreach (var contentRoot in _contentRoots)
            {
                var fullPath = Path.Combine(contentRoot, path);

                if (File.Exists(fullPath))
                {
                    var fs = File.OpenRead(fullPath);
                    return Task.FromResult<Stream?>(fs);
                }
            }
        }

        return Task.FromResult<Stream?>(null);
    }

    protected override string GetCssName() => $"{Name}.styles.css";

    private static async Task<MemoryStream?> GetFile(PackageArchiveReader package, string path)
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
        catch (InvalidDataException)
        {
            // This is not expected, but should be handled gracefully
        }

        return null;
    }

    class StaticWebAssets
    {
        public List<string>? ContentRoots { get; set; }
    }

    class DependenciesList
    {
        [JsonPropertyName("libraries")]
        public Dictionary<string, DependencyDescription>? Libraries { get; set; }
    }

    class DependencyDescription
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("serviceable")]
        public bool? IsServiceable { get; set; }

        [JsonPropertyName("sha512")]
        public string? SHA512 { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("hashPath")]
        public string? HashPath { get; set; }
    }
}
