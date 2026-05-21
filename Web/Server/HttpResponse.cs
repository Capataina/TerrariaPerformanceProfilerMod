#nullable enable

using System;
using System.Text;

namespace PerformanceProfiler.Web.Server;

/// <summary>
/// Outbound HTTP response. Body is bytes so binary assets fit too.
///
/// <para>
/// The four named factories (<see cref="Html"/>, <see cref="Json"/>,
/// <see cref="PlainText"/>, <see cref="NotFound"/>) cover every response
/// shape the dashboard surface produces. The raw-bytes constructor is
/// kept public for future asset types (PNG / WOFF / etc) the dashboard
/// might want to serve from disk.
/// </para>
/// </summary>
public sealed class HttpResponse
{
    public int Status { get; }
    public string ContentType { get; }
    public byte[] Body { get; }

    public HttpResponse(int status, string contentType, byte[] body)
    {
        Status = status;
        ContentType = contentType;
        Body = body;
    }

    public static HttpResponse Html(string html) =>
        new HttpResponse(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public static HttpResponse Json(string json) =>
        new HttpResponse(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

    public static HttpResponse PlainText(int status, string text) =>
        new HttpResponse(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

    public static readonly HttpResponse NotFound =
        PlainText(404, "Not Found");
}
