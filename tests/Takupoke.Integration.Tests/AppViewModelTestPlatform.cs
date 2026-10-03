// Compile the real AppViewModel scheduler on both CI hosts. Only WinUI dispatch,
// Windows identity/protection/notifications/authentication and model runtimes are
// substituted; persistence, material refresh and file watchers remain production.
// This does not verify native DPAPI, WinUI dispatch or inference behavior.
global using WindowsDpapiProtector = Takupoke.Integration.Tests.ViewModelTestProtector;

using System.Collections.Concurrent;
using System.ComponentModel;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Notifications;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

namespace CommunityToolkit.Mvvm.ComponentModel
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; PropertyChanged?.Invoke(this, new(name)); return true;
        }
    }
}
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly ConcurrentQueue<Action> _callbacks = new();
        public bool HasThreadAccess => true;
        public int Pending => _callbacks.Count;
        public bool TryEnqueue(Action action) { _callbacks.Enqueue(action); return true; }
        public DispatcherQueueTimer CreateTimer() => new();
        public void Drain() { while (_callbacks.TryDequeue(out var action)) action(); }
    }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public event Action<DispatcherQueueTimer, object>? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
    }
}
namespace Takupoke.Integration.Tests
{
    public sealed class ViewModelTestProtector : IKeyProtector
    {
        public byte[] Protect(byte[] key) => key.ToArray();
        public byte[] Unprotect(byte[] wrapped) => wrapped.ToArray();
    }
}
namespace Takupoke.Win
{
    public static class Program
    {
        public static Action<Uri>? ProtocolCallback { get; set; }
        public static void DrainCallbacks() { }
    }
}
namespace Takupoke.Win.Platform
{
    public sealed class WindowsFileIdentity : IFileIdentityProvider
    {
        public string Identity(FileStream stream) => "fictional-viewmodel-source";
    }
    public sealed class BrowserAuthenticator
    {
        public BrowserAuthenticator(OidcClient client, Action<Uri>? openBrowser) { }
        public event Action<string>? ProgressChanged { add { } remove { } }
        public static void RegisterProtocol() => throw new NotSupportedException();
        public void HandleCallback(Uri uri) => throw new NotSupportedException();
        public Task<string> AuthenticateAsync(CancellationToken token) => throw new NotSupportedException();
        public void Cancel() { }
        public void Dispose() { }
    }
    public sealed class WindowsNotifications : INotificationSink
    {
        public event Action? Activated { add { } remove { } }
        public string Status => "test-only notifications disabled";
        public void Initialize() => throw new NotSupportedException();
        public Task<bool> IsAllowedAsync(CancellationToken token) => Task.FromResult(false);
        public Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token) => Task.FromResult(false);
        public Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken token) => Task.CompletedTask;
        public Task ClearKindAsync(string kind, CancellationToken token) => Task.CompletedTask;
        public void Dispose() { }
    }
    public sealed record FoundryPinnedManifest;
    public sealed class FoundryPinnedModelStore
    {
        public static IReadOnlyList<FoundryPinnedManifest> Candidates => [];
        public Task<FoundryPinnedManifest?> InstalledAsync(CancellationToken token) => Task.FromResult<FoundryPinnedManifest?>(null);
        public Task InstallAsync(FoundryPinnedManifest manifest, Action<float> progress, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(CancellationToken token) => throw new NotSupportedException();
    }
    public sealed class WindowsRecoveryModels
    {
        public WindowsRecoveryModels(string root) { }
        public FoundryPinnedModelStore Foundry { get; } = new();
        public Task CleanupBeforeProvidersAsync(CancellationToken token) => Task.CompletedTask;
        public Task<(RecoveryModelBundle Bundle, string Path)?> OcrInstalledAsync(CancellationToken token) => Task.FromResult<(RecoveryModelBundle, string)?>(null);
        public Task<(RecoveryModelBundle Bundle, string Path)?> OcrStateAsync(CancellationToken token) => Task.FromResult<(RecoveryModelBundle, string)?>(null);
        public Task InstallOcrAsync(Action<long, long> progress, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteOcrAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ILocalRecoveryProvider>> ProvidersAsync(CancellationToken token) => throw new NotSupportedException();
    }
    public sealed class WindowsPdfRecovery
    {
        public WindowsPdfRecovery(WindowsRecoveryModels models) { }
        public Task<RecoveryDocument> BuildAsync(byte[] bytes, MaterialKind kind, string hash, RecoveryReadCapture capture, CancellationToken token) => throw new NotSupportedException();
    }
}
