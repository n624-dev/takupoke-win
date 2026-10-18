using Takupoke.Infrastructure.Materials;

namespace Takupoke.Win.Platform;

// Compiled only on non-Windows hosts; Windows tests use the actual provider.
public sealed class WindowsFileIdentity : IFileIdentityProvider
{
    public string Identity(FileStream stream) => "fictional-viewmodel-source";
}
