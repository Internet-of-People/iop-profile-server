using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeNet.Migrations
{
    public partial class expirationdateindex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Identities_IdentityId_HomeNodeId_Name_Type_ExtraData",
                table: "Identities");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_IdentityId_HomeNodeId_Name_Type_ExtraData_ExpirationDate",
                table: "Identities",
                columns: new[] { "IdentityId", "HomeNodeId", "Name", "Type", "ExtraData", "ExpirationDate" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Identities_IdentityId_HomeNodeId_Name_Type_ExtraData_ExpirationDate",
                table: "Identities");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_IdentityId_HomeNodeId_Name_Type_ExtraData",
                table: "Identities",
                columns: new[] { "IdentityId", "HomeNodeId", "Name", "Type", "ExtraData" });
        }
    }
}
