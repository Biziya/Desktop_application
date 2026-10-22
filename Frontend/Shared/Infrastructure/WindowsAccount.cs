namespace Frontend.Shared.Infrastructure;

public static class WindowsAccount
{
    /// <summary>
    /// The Windows account running the app: DOMAIN\Username for a domain account, otherwise the user name
    /// (a local account's "domain" is just the computer name, so it is left out).
    /// </summary>
    public static string Current()
    {
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;

        return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? user
            : $"{domain}\\{user}";
    }
}
