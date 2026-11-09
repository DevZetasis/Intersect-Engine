using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterNatures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterNatures",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Folder = table.Column<string>(type: "TEXT", nullable: true),
                Modifiers = table.Column<string>(type: "TEXT", nullable: true),
                SelectionWeight = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MonsterNatures", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterNatures");
    }
}
