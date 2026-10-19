namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>Tunables of the PrusaLink web proxy (T-028); tests shorten or observe them.</summary>
internal sealed record PrusaLinkWebProxyOptions
{
    public static PrusaLinkWebProxyOptions Default { get; } = new();

    /// <summary>
    /// Where the proxy listens: always loopback in the app (the browser address uses 127.0.0.1). Tests use an address
    /// that cannot be bound to simulate a start failure.
    /// </summary>
    public System.Net.IPAddress ListenAddress { get; init; } = System.Net.IPAddress.Loopback;

    /// <summary>A proxy without any request for this long stops (decided 2026-10-18: 10 minutes).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Request bodies up to this size are buffered, so that a request answered with a stale-nonce 401 can be sent again.
    /// Larger (or chunked) bodies are streamed and cannot be repeated.
    /// </summary>
    public int MaxBufferedBodySize { get; init; } = 64 * 1024;

    /// <summary>
    /// Before a streamed body, a nonce older than this (the firmware accepts it for 300 s) is renewed with a cheap
    /// authenticated request, so that the upload itself is not rejected.
    /// </summary>
    public TimeSpan NonceRefreshAge { get; init; } = TimeSpan.FromSeconds(240);

    /// <summary>Limit of the login check made before the browser is opened.</summary>
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long stopping waits for requests in progress (they are cancelled first) before connections are dropped.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
