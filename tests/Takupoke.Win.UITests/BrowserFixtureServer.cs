using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Takupoke.Win.UITests;

// Real loopback HTTP/TLS pages, never an internet or school service.
internal sealed class BrowserFixtureServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly X509Certificate2? _certificate;
    private readonly Task _accept;
    public Uri Address { get; }
    public int Requests;
    public BrowserFixtureServer(X509Certificate2? certificate = null)
    {
        _certificate = certificate; _listener.Start();
        Address = new($"{(certificate is null ? "http" : "https")}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try { while (!_stop.IsCancellationRequested) _connections.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token))); }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using var network = client.GetStream(); Stream stream = network;
                using var tls = _certificate is null ? null : new SslStream(network, leaveInnerStreamOpen: true);
                if (tls is not null)
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                        ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, deadline.Token);
                    stream = tls;
                }
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(deadline.Token); if (request is null) return;
                var path = request.Split(' ').ElementAtOrDefault(1) ?? "/";
                for (var count = 0; count < 100; count++) { if (string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) break; }
                Interlocked.Increment(ref Requests);
                var heading = path.StartsWith("/target", StringComparison.Ordinal) ? "架空新規ページ到達" :
                    path.StartsWith("/afterblank", StringComparison.Ordinal) ? "架空空白からの遷移到達" : "架空ローカルページ到達";
                var html = "<!doctype html><html lang='ja'><meta charset='utf-8'><title>Fictional local browser fixture</title><h1>" + heading + "</h1>" +
                    "<a href='/target' target='_blank'>架空新規リンク</a>" +
                    "<button onclick=\"const w=window.open('about:blank'); if(w) w.location='/afterblank';\">架空空白から開く</button>" +
                    "<a href='http://example.invalid/'>架空外部HTTP拒否</a></html>";
                var body = Encoding.UTF8.GetBytes(html);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), deadline.Token);
                await stream.WriteAsync(body, deadline.Token); await stream.FlushAsync(deadline.Token);
            }
            catch (Exception error) when (error is IOException or AuthenticationException or OperationCanceledException)
            {
                if (_certificate is not null && error is not OperationCanceledException)
                {
                    var inner = error.InnerException;
                    var native = inner is System.ComponentModel.Win32Exception windows ? windows.NativeErrorCode.ToString("X8") : "none";
                    Console.Error.WriteLine($"Synthetic TLS server failure: type={error.GetType().Name};hresult={error.HResult:X8};inner={inner?.GetType().Name ?? "none"};native={native}");
                }
            }
        }
    }
    public void Dispose()
    {
        _stop.Cancel(); _listener.Stop(); _accept.GetAwaiter().GetResult();
        Task.WhenAll(_connections).GetAwaiter().GetResult(); _stop.Dispose();
    }
}

internal sealed class BrowserFixtureCertificates : IDisposable
{
    private readonly RSA _rootKey = RSA.Create(2048);
    private readonly X509Certificate2 _root;
    public X509Certificate2 Trusted { get; }
    public X509Certificate2 Mismatched { get; }
    public X509Certificate2 Untrusted { get; }
    public BrowserFixtureCertificates()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
            throw new InvalidOperationException("Machine certificate fixtures are restricted to isolated offline CI.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("The isolated CI browser fixture requires an elevated disposable runner.");
        var request = new CertificateRequest("CN=Takupoke isolated browser test " + Guid.NewGuid().ToString("N"), _rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        _root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // User Root import can wait for system consent, including certutil -f.
        // The disposable elevated CI runner uses its machine Root for this one
        // synthetic CA; normal browser TLS verification remains enabled.
        var file = Path.Combine(Path.GetTempPath(), "takupoke-browser-root-" + Guid.NewGuid().ToString("N") + ".cer");
        try
        {
            Trusted = Leaf("127.0.0.1", true); Mismatched = Leaf("fictional.invalid", true); Untrusted = Leaf("127.0.0.1", false);
            File.WriteAllBytes(file, _root.Export(X509ContentType.Cert));
            Console.WriteLine("Browser fixture stage: import synthetic root certificate");
            CertUtil("-addstore", "Root", file);
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, _root.Thumbprint, false).Count != 1)
                throw new InvalidOperationException("The synthetic root was not installed exactly once.");
            Console.WriteLine("Browser fixture stage: synthetic root certificate installed");
        }
        catch { Dispose(); throw; }
        finally { File.Delete(file); }
    }
    private X509Certificate2 Leaf(string name, bool trusted)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); if (IPAddress.TryParse(name, out var ip)) san.AddIpAddress(ip); else san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        X509Certificate2 leaf;
        if (!trusted) leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(1));
        else
        {
            using var signed = request.Create(_root, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
            leaf = signed.CopyWithPrivateKey(key);
        }
        using (leaf)
        {
            // Windows SChannel cannot use the generated ephemeral private key.
            // UserKeySet without PersistKeySet gives it an OS key container that
            // X509Certificate2 deletes on Dispose. The PKCS12 never goes to a file.
            var pkcs12 = leaf.Export(X509ContentType.Pkcs12);
            try { return X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pkcs12); }
        }
    }
    public void Dispose()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, _root.Thumbprint, false).Count != 0)
                CertUtil("-delstore", "Root", _root.Thumbprint);
            using var verified = new X509Store(StoreName.Root, StoreLocation.LocalMachine); verified.Open(OpenFlags.ReadOnly);
            if (verified.Certificates.Find(X509FindType.FindByThumbprint, _root.Thumbprint, false).Count != 0)
                throw new InvalidOperationException("The owned synthetic root was not removed.");
        }
        finally { Trusted?.Dispose(); Mismatched?.Dispose(); Untrusted?.Dispose(); _root.Dispose(); _rootKey.Dispose(); }
    }
    private static void CertUtil(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "certutil.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-f" }.Concat(arguments)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Certificate fixture tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true); process.WaitForExit(5000);
            throw new TimeoutException("Certificate fixture OS import/removal did not complete in 15 seconds.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("Certificate fixture OS command failed with exit code " + process.ExitCode);
    }
}

// Elevated WebView2 hosts ignore environment overrides. This per-app machine
// policy exists only on the disposable CI runner and is removed on every exit.
internal sealed class BrowserFixtureDebugPolicy : IDisposable
{
    private const string Prefix = @"SOFTWARE\Policies\Microsoft";
    private const string PolicyPath = Prefix + @"\Edge\WebView2\AdditionalBrowserArguments";
    private readonly List<string> _ownedKeys = [];
    private readonly string _application;
    private readonly string _arguments;
    private bool _installed;
    public BrowserFixtureDebugPolicy(string executable, int port)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
            throw new InvalidOperationException("Machine browser policies are restricted to isolated offline CI.");
        _application = Path.GetFileName(executable);
        if (string.IsNullOrEmpty(_application) || port is < 1 or > 65535)
            throw new ArgumentException("An owned executable and allocated loopback port are required.");
        _arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}";
        try
        {
            using var prefix = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Prefix)
                ?? throw new InvalidOperationException("The isolated runner's machine policy root is unavailable.");
            foreach (var path in new[] { Prefix + @"\Edge", Prefix + @"\Edge\WebView2", PolicyPath })
            {
                using var existing = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
                if (existing is not null) continue;
                using var created = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(path, writable: true)
                    ?? throw new InvalidOperationException("The owned test policy key could not be created.");
                _ownedKeys.Add(path);
            }
            using var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PolicyPath, writable: true)
                ?? throw new InvalidOperationException("The owned test policy key is unavailable.");
            if (policy.GetValueNames().Contains(_application, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("An existing application policy must not be overwritten by the browser fixture.");
            policy.SetValue(_application, _arguments, Microsoft.Win32.RegistryValueKind.String);
            _installed = true;
            if (policy.GetValue(_application) as string != _arguments)
                throw new InvalidOperationException("The isolated browser policy was not installed.");
            Console.WriteLine("Browser fixture stage: owned per-app machine debug policy installed");
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_installed)
        {
            using var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PolicyPath, writable: true)
                ?? throw new InvalidOperationException("The owned browser policy disappeared before cleanup.");
            if (policy.GetValue(_application) as string != _arguments)
                throw new InvalidOperationException("The browser policy changed; refusing to delete another owner's value.");
            policy.DeleteValue(_application);
            if (policy.GetValueNames().Contains(_application, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The owned browser policy was not removed.");
            _installed = false;
        }
        foreach (var path in _ownedKeys.AsEnumerable().Reverse())
        {
            bool empty;
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                empty = key is not null && key.ValueCount == 0 && key.SubKeyCount == 0;
            if (empty) Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
        _ownedKeys.Clear();
    }
}
