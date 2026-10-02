using System.Diagnostics;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Win32;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;

namespace Takupoke.Win.Platform;

public sealed class BrowserAuthenticator(OidcClient oidc, Action<Uri>? openBrowser = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _callbackGate = new();
    private AuthorizationAttempt? _attempt;
    private TaskCompletionSource<Uri>? _callback;
    public event Action<string>? ProgressChanged;
    public static void RegisterProtocol()
    {
        ActivationRegistrationManager.RegisterForProtocolActivation("jp.n624.takupoke.win", "", "たくポケ Win", Environment.ProcessPath!);
        // Repair the direct command left by older installers as well as the SDK association.
        using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jp.n624.takupoke.win\shell\open\command");
        command.SetValue("", "\"" + Environment.ProcessPath! + "\" \"----ms-protocol:%1\"");
    }
    public void HandleCallback(Uri uri)
    {
        lock (_callbackGate)
        {
            if (_attempt is null || _callback is null) return;
            if (OidcClient.IsAuthenticatedErrorCallback(uri, _attempt))
            { _callback.TrySetException(new ApiException(ApiFailure.Authentication)); return; }
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
            ProgressChanged?.Invoke("ブラウザでの認証を待っています。");
            if (openBrowser is not null) openBrowser(attempt.AuthorizationUri);
            else Process.Start(new ProcessStartInfo(attempt.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
            var callback = await completion.Task;
            ProgressChanged?.Invoke("認証の戻り先を受け取りました。認証情報を検証しています。");
            var result = await oidc.ExchangeAsync(callback, attempt, deadline.Token);
            ProgressChanged?.Invoke("認証情報の検証が完了しました。学校データを取得しています。");
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ApiException(ApiFailure.AuthenticationTimeout); }
        finally { lock (_callbackGate) { _attempt = null; _callback = null; } _gate.Release(); }
    }
    public void Dispose() => Cancel();
}
