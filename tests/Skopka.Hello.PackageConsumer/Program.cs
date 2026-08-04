using Skopka.Hello;
using Skopka.Hello.Admin;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Oidc;
using Skopka.Hello.UI;

Type[] packageSurfaces =
[
    typeof(SkopkaHelloOptions),
    typeof(SkopkaHelloAdminOptions),
    typeof(OperationResultProblemMapper),
    typeof(HelloOidcOptions),
    typeof(SkopkaHelloUiOptions),
];

if (typeof(IHelloAdminRoleApplication).Assembly
        != typeof(SkopkaHelloAdminOptions).Assembly
    || string.IsNullOrWhiteSpace(
        HelloAdminSecurityEventTypes.RoleCreated))
{
    throw new InvalidOperationException(
        "The role-administration package surface could not be loaded.");
}

var assemblies = packageSurfaces
    .Select(type => type.Assembly.GetName())
    .Select(name => $"{name.Name} {name.Version}")
    .ToArray();

if (assemblies.Length != 5
    || assemblies.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException(
        "The complete Skopka.Hello package surface could not be loaded.");
}

Console.WriteLine(string.Join(Environment.NewLine, assemblies));
