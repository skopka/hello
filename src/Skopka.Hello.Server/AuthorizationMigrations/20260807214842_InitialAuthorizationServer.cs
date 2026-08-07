using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skopka.Hello.Server.AuthorizationMigrations
{
    /// <inheritdoc />
    public partial class InitialAuthorizationServer : Migration
    {
        private static readonly string[] AuthorizationIndexColumns =
            ["ApplicationId", "Status", "Subject", "Type"];

        private static readonly string[] TokenIndexColumns =
            ["ApplicationId", "Status", "Subject", "Type"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "skopka_hello_oauth");

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "skopka_hello_oauth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scopes",
                schema: "skopka_hello_oauth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "authorizations",
                schema: "skopka_hello_oauth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authorizations_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "skopka_hello_oauth",
                        principalTable: "applications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "tokens",
                schema: "skopka_hello_oauth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tokens_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "skopka_hello_oauth",
                        principalTable: "applications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_tokens_authorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "skopka_hello_oauth",
                        principalTable: "authorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_applications_ClientId",
                schema: "skopka_hello_oauth",
                table: "applications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authorizations_ApplicationId_Status_Subject_Type",
                schema: "skopka_hello_oauth",
                table: "authorizations",
                columns: AuthorizationIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_scopes_Name",
                schema: "skopka_hello_oauth",
                table: "scopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tokens_ApplicationId_Status_Subject_Type",
                schema: "skopka_hello_oauth",
                table: "tokens",
                columns: TokenIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_tokens_AuthorizationId",
                schema: "skopka_hello_oauth",
                table: "tokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_tokens_ReferenceId",
                schema: "skopka_hello_oauth",
                table: "tokens",
                column: "ReferenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scopes",
                schema: "skopka_hello_oauth");

            migrationBuilder.DropTable(
                name: "tokens",
                schema: "skopka_hello_oauth");

            migrationBuilder.DropTable(
                name: "authorizations",
                schema: "skopka_hello_oauth");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "skopka_hello_oauth");
        }
    }
}
