using System.Diagnostics;
using Microsoft.Windows.AppLifecycle;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;

namespace Takupoke.Win.Platform;

public sealed class BrowserAuthenticator(OidcClient oidc) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _callbackGate = new();
    private AuthorizationAttempt? _attempt;
    private TaskCompletionSource<Uri>? _callback;
    public static void RegisterProtocol() => ActivationRegistrationManager.RegisterForProtocolActivation("jp.n624.takupoke.win", "", "たくポケ Win", Environment.ProcessPath!);
    public void HandleCallback(Uri uri)
    {
        lock (_callbackGate)
        {
            if (_attempt is null || _callback is null) return;
            try { OidcClient.ValidateCallback(uri, _attempt); _callback.TrySetResult(uri); }
            catch (ApiException) { /* An unrelated or forged protocol activation cannot finish the current request. */ }
        }
    }
    public void Cancel() { lock (_callbackGate) _callback?.TrySetCanceled(); }
    public async Task<string> AuthenticateAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(10));
            var attempt = OidcClient.CreateAttempt(); var completion = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_callbackGate) { _attempt = attempt; _callback = completion; }
            using var registration = deadline.Token.Register(() => completion.TrySetCanceled(deadline.Token));
            Process.Start(new ProcessStartInfo(attempt.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
            return await oidc.ExchangeAsync(await completion.Task, attempt, deadline.Token);
        }
        finally { lock (_callbackGate) { _attempt = null; _callback = null; } _gate.Release(); }
    }
    public void Dispose() => Cancel();
}
