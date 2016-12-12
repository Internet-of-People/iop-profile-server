using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProfileServer.Migrations
{
    public partial class initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Followers",
                columns: table => new
                {
                    Id = table.Column<byte[]>(maxLength: 32, nullable: false),
                    IpAddress = table.Column<string>(nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: true),
                    PrimaryPort = table.Column<int>(nullable: false),
                    SrNeighborPort = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Followers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Identities",
                columns: table => new
                {
                    IdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    ExpirationDate = table.Column<DateTime>(nullable: true),
                    ExtraData = table.Column<string>(maxLength: 200, nullable: false),
                    HostingServerId = table.Column<byte[]>(maxLength: 32, nullable: true),
                    InitialLocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    InitialLocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Name = table.Column<string>(maxLength: 64, nullable: false),
                    ProfileImage = table.Column<Guid>(nullable: true),
                    PublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    ThumbnailImage = table.Column<Guid>(nullable: true),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identities", x => x.IdentityId);
                });

            migrationBuilder.CreateTable(
                name: "Neighbors",
                columns: table => new
                {
                    Id = table.Column<byte[]>(maxLength: 32, nullable: false),
                    IpAddress = table.Column<string>(nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: true),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    PrimaryPort = table.Column<int>(nullable: false),
                    SrNeighborPort = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Neighbors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeighborhoodActions",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdditionalData = table.Column<string>(nullable: true),
                    ExecuteAfter = table.Column<DateTime>(nullable: true),
                    ServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    TargetIdentityId = table.Column<byte[]>(maxLength: 32, nullable: true),
                    Timestamp = table.Column<DateTime>(nullable: false),
                    Type = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborhoodActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeighborhoodIdentities",
                columns: table => new
                {
                    IdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    HostingServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    ExpirationDate = table.Column<DateTime>(nullable: true),
                    ExtraData = table.Column<string>(maxLength: 200, nullable: false),
                    InitialLocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    InitialLocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Name = table.Column<string>(maxLength: 64, nullable: false),
                    ProfileImage = table.Column<Guid>(nullable: true),
                    PublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    ThumbnailImage = table.Column<Guid>(nullable: true),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborhoodIdentities", x => new { x.IdentityId, x.HostingServerId });
                });

            migrationBuilder.CreateTable(
                name: "RelatedIdentities",
                columns: table => new
                {
                    IdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    ApplicationId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    CardId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    CardVersion = table.Column<byte[]>(maxLength: 3, nullable: false),
                    IssuerPublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    IssuerSignature = table.Column<byte[]>(maxLength: 100, nullable: false),
                    RecipientPublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    RecipientSignature = table.Column<byte[]>(maxLength: 100, nullable: false),
                    RelatedToIdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateTime>(nullable: false),
                    ValidTo = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedIdentities", x => new { x.IdentityId, x.ApplicationId });
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
                name: "IX_Followers_Id",
                table: "Followers",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Followers_LastRefreshTime",
                table: "Followers",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_IpAddress_PrimaryPort",
                table: "Followers",
                columns: new[] { "IpAddress", "PrimaryPort" });

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
                name: "IX_Neighbors_Id",
                table: "Neighbors",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_LastRefreshTime",
                table: "Neighbors",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_IpAddress_PrimaryPort",
                table: "Neighbors",
                columns: new[] { "IpAddress", "PrimaryPort" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ExecuteAfter",
                table: "NeighborhoodActions",
                column: "ExecuteAfter");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Id",
                table: "NeighborhoodActions",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ServerId",
                table: "NeighborhoodActions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_TargetIdentityId",
                table: "NeighborhoodActions",
                column: "TargetIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Timestamp",
                table: "NeighborhoodActions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Type",
                table: "NeighborhoodActions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ServerId_Type_TargetIdentityId",
                table: "NeighborhoodActions",
                columns: new[] { "ServerId", "Type", "TargetIdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_ExpirationDate",
                table: "NeighborhoodIdentities",
                column: "ExpirationDate");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_ExtraData",
                table: "NeighborhoodIdentities",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_HostingServerId",
                table: "NeighborhoodIdentities",
                column: "HostingServerId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_Name",
                table: "NeighborhoodIdentities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_Type",
                table: "NeighborhoodIdentities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_IdentityId_HostingServerId",
                table: "NeighborhoodIdentities",
                columns: new[] { "IdentityId", "HostingServerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_InitialLocationLatitude_InitialLocationLongitude",
                table: "NeighborhoodIdentities",
                columns: new[] { "InitialLocationLatitude", "InitialLocationLongitude" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodIdentities_InitialLocationLatitude_InitialLocationLongitude_Type_Name",
                table: "NeighborhoodIdentities",
                columns: new[] { "InitialLocationLatitude", "InitialLocationLongitude", "Type", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RelatedIdentities_RelatedToIdentityId",
                table: "RelatedIdentities",
                column: "RelatedToIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedIdentities_Type",
                table: "RelatedIdentities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedIdentities_IdentityId_ApplicationId",
                table: "RelatedIdentities",
                columns: new[] { "IdentityId", "ApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelatedIdentities_ValidFrom_ValidTo",
                table: "RelatedIdentities",
                columns: new[] { "ValidFrom", "ValidTo" });

            migrationBuilder.CreateIndex(
                name: "IX_RelatedIdentities_IdentityId_Type_RelatedToIdentityId_ValidFrom_ValidTo",
                table: "RelatedIdentities",
                columns: new[] { "IdentityId", "Type", "RelatedToIdentityId", "ValidFrom", "ValidTo" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Followers");

            migrationBuilder.DropTable(
                name: "Identities");

            migrationBuilder.DropTable(
                name: "Neighbors");

            migrationBuilder.DropTable(
                name: "NeighborhoodActions");

            migrationBuilder.DropTable(
                name: "NeighborhoodIdentities");

            migrationBuilder.DropTable(
                name: "RelatedIdentities");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
