using Microsoft.AspNetCore.Http;
using Piral.Blazor.Shared;
using System.IO.Pipelines;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piral.Blazor.Orchestrator.Connector;

internal class MfEmulatorConnector(IMfRepository repository, IGlobalEvents events) : IMfDebugConnector
{
    private readonly IEnumerable<string> _styles = [];
    private readonly IEnumerable<string> _scripts = ["_content/Piral.Blazor.Orchestrator/debug.js"];
    private readonly IMfRepository _repository = repository;
    private readonly IGlobalEvents _events = events;

    public IEnumerable<string> Styles => _styles;

    public IEnumerable<string> Scripts => _scripts;

    public async Task<bool> InterceptAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/_debug"))
        {
            if (context.Request.Method == "GET")
            {
                // just read the current state
            }
            else if (context.Request.Method == "POST")
            {
                var segments = context.Request.Path.Value?.Split('/') ?? [];

                if (segments.Length < 3)
                {
                    return false;
                }

                // perform action; we read the current state later
                var area = segments[2];

                switch (area)
                {
                    case "event":
                    {
                        var payload = await context.Request.ReadFromJsonAsync<EventRequest>();

                        if (payload is not null && payload.Name is not null)
                        {
                            _events.DispatchEvent(payload.Name, payload.Args);
                        }

                        break;
                    }
                    case "pilet":
                    {
                        var payload = await context.Request.ReadFromJsonAsync<PiletRequest>();

                        if (payload is not null && payload.Name is not null)
                        {
                            switch (payload.Mode)
                            {
                                case "add":
                                    //TODO
                                    break;
                                case "update":
                                    var package = _repository.GetPackage(payload.Name);

                                    if (package is not null)
                                    {
                                        package.IsDisabled = payload.IsDisabled;
                                    }

                                    break;
                                case "remove":
                                    await _repository.DeletePackage(payload.Name);
                                    break;
                                default:
                                    break;
                            }
                        }

                        break;
                    }
                    default:
                        break;
                }
            }
            else
            {
                return false;
            }

            await SendCurrentState(context).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private ValueTask<FlushResult> SendCurrentState(HttpContext context)
    {
        var state = CollectCurrentState();
        var content = JsonSerializer.Serialize(state);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        return context.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes(content));
    }

    private MfDebugState CollectCurrentState()
    {
        var state = new MfDebugState();
        var root = AssemblyLoadContext.All.First(m => m.Name == "root");
        var assembly = root.Assemblies.FirstOrDefault(m => m.DefinedTypes.Any(n => n.Name == "Program"));
        var assemblyDetails = assembly?.GetName();
        state.App.Name = assemblyDetails?.Name ?? "Piral.Blazor.Server";
        state.App.Version = assemblyDetails?.Version?.ToString() ?? "0.0.0";

        foreach (var package in _repository.Packages)
        {
            var dependencies = new List<string>();

            foreach (var componentName in package.ComponentNames)
            {
                var routePrefix = "route:";

                if (componentName.StartsWith(routePrefix))
                {
                    state.Routes.Add(componentName[routePrefix.Length..]);
                }
                else
                {
                    state.Extensions.Add(componentName);
                }
            }

            foreach (var dependency in package.Dependencies)
            {
                dependencies.Add(dependency);

                if (!state.Dependencies.Contains(dependency))
                {
                    state.Dependencies.Add(dependency);
                }
            }

            state.Pilets.Add(new MfPiletInfo
            {
                Name = package.Name,
                Version = package.Version,
                IsDisabled = package.IsDisabled,
                Dependencies = dependencies,
            });
        }

        return state;
    }

    class EventRequest
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("args")]
        public JsonElement Args { get; set; }
    }

    class PiletRequest
    {
        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
        
        [JsonPropertyName("disabled")]
        public bool IsDisabled { get; set; }
    }

    class MfDebugState
    {
        [JsonPropertyName("app")]
        public MfAppInfo App { get; } = new();

        [JsonPropertyName("extensions")]
        public List<string> Extensions { get; } = [];

        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; } = [];

        [JsonPropertyName("pilets")]
        public List<MfPiletInfo> Pilets { get; } = [];

        [JsonPropertyName("routes")]
        public List<string> Routes { get; } = [];
    }

    class MfAppInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Piral.Blazor.Server";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "0.0.0";
    }

    class MfPiletInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("disabled")]
        public bool IsDisabled { get; set; } = false;

        [JsonPropertyName("dependencies")]
        public List<string>? Dependencies { get; set; }
    }
}
