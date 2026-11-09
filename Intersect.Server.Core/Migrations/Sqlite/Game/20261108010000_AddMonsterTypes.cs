using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Types", table: "MonsterSpecies", type: "TEXT", nullable: true);
        migrationBuilder.CreateTable(
            name: "MonsterTypes",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Folder = table.Column<string>(type: "TEXT", nullable: true),
                DisplayColor = table.Column<string>(type: "TEXT", nullable: true),
                Effectiveness = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MonsterTypes", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterTypes");
        migrationBuilder.DropColumn(name: "Types", table: "MonsterSpecies");
    }
}
