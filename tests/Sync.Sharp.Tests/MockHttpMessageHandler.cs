using System.Net;

namespace Sync.Sharp.Tests;

public sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncHandler)
        : this((req, _) => Task.FromResult(syncHandler(req)))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await handler(request, cancellationToken);
    }

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
