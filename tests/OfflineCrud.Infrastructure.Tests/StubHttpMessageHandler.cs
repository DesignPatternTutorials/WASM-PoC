namespace OfflineCrud.Infrastructure.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records each request and answers with a canned response,
/// so the API client can be tested without a network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public List<RecordedRequest> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? json = null)
    {
        return new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = json is null ? null : new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        }));
    }

    public static StubHttpMessageHandler Throwing(Exception exception)
    {
        return new StubHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        string? idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values) ? values.Single() : null;
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, idempotencyKey));
        return await _respond(request, cancellationToken);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? IdempotencyKey);
}
