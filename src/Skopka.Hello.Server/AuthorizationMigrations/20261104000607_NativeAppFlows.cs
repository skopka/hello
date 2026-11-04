using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skopka.Hello.Server.AuthorizationMigrations
{
    /// <inheritdoc />
    public partial class NativeAppFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "skopka_hello");

            migrationBuilder.CreateTable(
                name: "native_app_flows",
                schema: "skopka_hello",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientRevision = table.Column<long>(type: "bigint", nullable: false),
                    Callback = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    State = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Challenge = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    WebViewBinderHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LaunchSecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrowserBinderHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CompletionReceiptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProtectedSource = table.Column<string>(type: "text", nullable: true),
                    ReturnUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SessionMetadata = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    LaunchExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ApprovalExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_native_app_flows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_native_app_flows_ExpiresAt",
                schema: "skopka_hello",
                table: "native_app_flows",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "native_app_flows",
                schema: "skopka_hello");
        }
    }
}
