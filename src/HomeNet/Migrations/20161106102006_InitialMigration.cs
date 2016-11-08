using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeNet.Migrations
{
    public partial class InitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Identities",
                columns: table => new
                {
                    IdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    ExpirationDate = table.Column<DateTime>(nullable: true),
                    ExtraData = table.Column<string>(maxLength: 200, nullable: true),
                    HomeNodeId = table.Column<byte[]>(maxLength: 32, nullable: true),
                    InitialLocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    InitialLocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Name = table.Column<string>(maxLength: 64, nullable: false),
                    ProfileImage = table.Column<Guid>(nullable: true),
                    PublicKey = table.Column<byte[]>(maxLength: 256, nullable: false),
                    ThumbnailImage = table.Column<Guid>(nullable: true),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identities", x => x.IdentityId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Name = table.Column<string>(nullable: false),
                    Value = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Identities_IdentityId_HomeNodeId_Name_Type_InitialLocationLatitude_InitialLocationLongitude_ExtraData_ExpirationDate",
                table: "Identities",
                columns: new[] { "IdentityId", "HomeNodeId", "Name", "Type", "InitialLocationLatitude", "InitialLocationLongitude", "ExtraData", "ExpirationDate" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Identities");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
