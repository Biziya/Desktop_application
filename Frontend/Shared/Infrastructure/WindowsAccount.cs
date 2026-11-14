namespace Frontend.Shared.Infrastructure;

public static class WindowsAccount
{
    // DOMAIN\User for a domain account; a local account's "domain" is just the computer name, so it is left out.
    public static string Current()
    {
        var domain = Environment.UserDomainName;

        return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? Environment.UserName
            : $"{domain}\\{Environment.UserName}";
    }
}
