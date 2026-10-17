namespace OfflineCrud.Application.Common;

/// <summary>Why a call to the remote API failed, in terms the application can act on.</summary>
public enum ApiFailureKind
{
    /// <summary>The request never got a response: offline, DNS failure, CORS rejection or timeout.</summary>
    Network,

    /// <summary>The server answered with a 5xx status.</summary>
    Server,

    /// <summary>The server answered 429 Too Many Requests.</summary>
    RateLimited,

    /// <summary>The server answered 404 Not Found.</summary>
    NotFound,

    /// <summary>The server rejected the request with a 4xx status other than 404 or 429.</summary>
    Rejected,

    /// <summary>The server answered successfully, but the body couldn't be understood.</summary>
    InvalidResponse,
}
