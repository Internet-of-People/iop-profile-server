using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeNet.Migrations
{
    public partial class initial : Migration
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
                name: "NeighborhoodIdentities",
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
                    table.PrimaryKey("PK_NeighborhoodIdentities", x => x.IdentityId);
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
                name: "IX_Identities_ExpirationDate",
                table: "Identities",
                column: "ExpirationDate");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_ExtraData",
                table: "Identities",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_IdentityId",
                table: "Identities",
                column: "IdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identities_Name",
                table: "Identities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_Type",
                table: "Identities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_InitialLocationLatitude_InitialLocationLongitude",
                table: "Identities",
                columns: new[] { "InitialLocationLatitude", "InitialLocationLongitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Identities_ExpirationDate_InitialLocationLatitude_InitialLocationLongitude_Type_Name",
                table: "Identities",
                columns: new[] { "ExpirationDate", "InitialLocationLatitude", "InitialLocationLongitude", "Type", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_ExpirationDate",
                table: "NeighborhoodIdentities",
                column: "ExpirationDate");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_ExtraData",
                table: "NeighborhoodIdentities",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_HomeNodeId",
                table: "NeighborhoodIdentities",
                column: "HomeNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_IdentityId",
                table: "NeighborhoodIdentities",
                column: "IdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_Name",
                table: "NeighborhoodIdentities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_Type",
                table: "NeighborhoodIdentities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_InitialLocationLatitude_InitialLocationLongitude",
                table: "NeighborhoodIdentities",
                columns: new[] { "InitialLocationLatitude", "InitialLocationLongitude" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_InitialLocationLatitude_InitialLocationLongitude_Type_Name",
                table: "NeighborhoodIdentities",
                columns: new[] { "InitialLocationLatitude", "InitialLocationLongitude", "Type", "Name" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Identities");

            migrationBuilder.DropTable(
                name: "NeighborhoodIdentities");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
