using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProfileServer.Migrations
{
    public partial class offline_queue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MissedCalls",
                columns: table => new {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CallerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    CalleeId = table.Column<int>(nullable: false),
                    StoredAt = table.Column<DateTime>(nullable: false),
                    Payload = table.Column<byte[]>(maxLength: 1*1024*1024, nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_MissedCalls", x => x.DbId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissedCalls_CallerId_StoredAt",
                table: "MissedCalls",
                columns: new[] { "CallerId", "StoredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MissedCalls_CalleeId_StoredAt",
                table: "MissedCalls",
                columns: new[] { "CalleeId", "StoredAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissedCalls");
        }
    }
}
