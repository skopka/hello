namespace Skopka.Hello.Admin;

public static class HelloAdminDefaults
{
    public const string ModuleName = "Skopka.Hello.Admin";

    public const string ReadPolicy = "Skopka.Hello.Admin.Read";

    public const string ManagePolicy = "Skopka.Hello.Admin.Manage";

    public const string DeletePolicy = "Skopka.Hello.Admin.Delete";

    public const string RoleAssignmentPolicy =
        "Skopka.Hello.Admin.RoleAssignment";

    public const string AdministratorRole = "Skopka.Hello.Admin";

    public const string DefaultApiPathPrefix = "/admin";

    public const string BuiltInStylesheetPath =
        "/_content/Skopka.Hello.Admin/css/admin.css";

    public const string BootstrapStylesheetPath =
        "/_content/Skopka.Hello.Admin/lib/bootstrap/css/bootstrap.min.css";

    public const string BootstrapScriptPath =
        "/_content/Skopka.Hello.Admin/lib/bootstrap/js/bootstrap.bundle.min.js";

    public const string DefaultLayoutPath =
        "/Pages/Shared/_AdminLayout.cshtml";
}
