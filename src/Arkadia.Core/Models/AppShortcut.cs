namespace Arkadia.Core.Models;

public sealed record AppShortcut(Guid Id, string Name, string Uri)
{
    public Uri? FaviconUri
    {
        get
        {
            if (!System.Uri.TryCreate(Uri, UriKind.Absolute, out var address)
                || address.Scheme != System.Uri.UriSchemeHttps)
            {
                return null;
            }

            return new System.Uri($"https://{address.Host}/favicon.ico");
        }
    }

    public string DisplayAddress => System.Uri.TryCreate(Uri, UriKind.Absolute, out var address)
        ? address.Host
        : Uri;
}
