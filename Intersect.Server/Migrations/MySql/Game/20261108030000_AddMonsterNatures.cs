using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterNatures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterNatures",
            columns: table => new
            {
                Name = table.Column<string>(type: "longtext", nullable: true),
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                TimeCreated = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "longtext", nullable: true),
                Folder = table.Column<string>(type: "longtext", nullable: true),
                Modifiers = table.Column<string>(type: "longtext", nullable: true),
                SelectionWeight = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MonsterNatures", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterNatures");
    }
}
