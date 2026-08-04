namespace Skopka.Hello.Admin;

public sealed class HelloAdminRoutePaths
{
    public HelloAdminRoutePaths(
        HelloUiRoutePaths helloRoutes,
        SkopkaHelloAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(helloRoutes);
        ArgumentNullException.ThrowIfNull(options);

        if (Overlaps(helloRoutes.RootPath, options.ApiPathPrefix))
        {
            throw new InvalidOperationException(
                "The admin API prefix overlaps the Hello UI route namespace.");
        }

        RootPath = helloRoutes.RootPath.TrimEnd('/')
            + options.ApiPathPrefix;
        UsersPath = RootPath + "/users";
        RolesPath = RootPath + "/roles";
    }

    public string RootPath { get; }

    public string UsersPath { get; }

    public string RolesPath { get; }

    private static bool Overlaps(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || left.StartsWith(
                right.TrimEnd('/') + "/",
                StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(
                left.TrimEnd('/') + "/",
                StringComparison.OrdinalIgnoreCase);
}
