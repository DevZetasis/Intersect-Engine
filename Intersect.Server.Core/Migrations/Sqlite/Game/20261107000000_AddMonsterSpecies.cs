using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterSpecies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterSpecies",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Sprite = table.Column<string>(type: "TEXT", nullable: true),
                Folder = table.Column<string>(type: "TEXT", nullable: true),
                BaseStats = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MonsterSpecies", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterSpecies");
    }
}
