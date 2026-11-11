using System.ComponentModel;
using System.Diagnostics;
using Arkadia.Core.Models;
using Arkadia.Core.Services;

namespace Arkadia.App.Services;

public sealed class ExternalLauncher : IExternalLauncher
{
    public Task<LaunchResult> OpenUriAsync(string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Core.Services.LaunchUriPolicy.IsAllowed(uri))
        {
            return Task.FromResult(new LaunchResult(false, "The launch URI is not allowed."));
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return Task.FromResult(new LaunchResult(true));
        }
        catch (Win32Exception exception)
        {
            return Task.FromResult(new LaunchResult(false, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(new LaunchResult(false, exception.Message));
        }
    }
}
