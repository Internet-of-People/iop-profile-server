using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeNet.Migrations
{
    public partial class first : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Identities",
                columns: table => new
                {
                    IdentityId = table.Column<byte[]>(maxLength: 20, nullable: false),
                    ExtraData = table.Column<string>(maxLength: 200, nullable: true),
                    InitialLocationEncoded = table.Column<uint>(nullable: false),
                    Name = table.Column<string>(maxLength: 64, nullable: false),
                    Picture = table.Column<byte[]>(maxLength: 20480, nullable: true),
                    PublicKey = table.Column<byte[]>(maxLength: 256, nullable: false),
                    Type = table.Column<string>(maxLength: 32, nullable: false),
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
