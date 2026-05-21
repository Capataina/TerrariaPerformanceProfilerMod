#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Web.Server;

/// <summary>
/// Inbound HTTP request, parsed to the minimum we need.
///
/// <para>
/// The mod's <see cref="DashboardHttpServer"/> only handles GET requests
/// for the dashboard SPA and the JSON API. We never read a body and never
/// look at headers other than the request line itself, so this struct
/// only carries the three fields a router needs to dispatch.
/// </para>
/// </summary>
public sealed class HttpRequest
{
    /// <summary>HTTP verb, e.g. <c>GET</c>.</summary>
    public string Method { get; }

    /// <summary>Path with any query string stripped. Example: <c>/api/now</c>.</summary>
    public string Path { get; }

    /// <summary>Original request-line target including query string, untouched.</summary>
    public string RawTarget { get; }

    public HttpRequest(string method, string path, string rawTarget)
    {
        Method = method;
        Path = path;
        RawTarget = rawTarget;
    }
}
