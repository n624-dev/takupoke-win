using System.Diagnostics;
using Xunit;

namespace Takupoke.Integration.Tests;

// Compile actual UI method bodies against narrowly controlled native boundaries.
// The probe has no NuGet packages, network feeds, or application data directory.
internal static class CSharpMethodProbe
{
    internal static async Task AssertRunAsync(string program)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-ui-method-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NoWarn>CS0067;CS0169;CS0649</NoWarn></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), program);
            var build = await Run(root, "build", "Probe.csproj", "--configuration", "Release", "--disable-build-servers", "--nologo", "--verbosity", "quiet");
            Assert.True(build.Code == 0, build.Output);
            var run = await Run(root, Path.Combine(root, "bin", "Release", "net10.0", "Probe.dll"));
            Assert.True(run.Code == 0, run.Output);
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task<(int Code, string Output)> Run(string root, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in args) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        return (process.ExitCode, await output + await error);
    }
}
