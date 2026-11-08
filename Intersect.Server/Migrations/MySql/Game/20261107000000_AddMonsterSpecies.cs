using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterSpecies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterSpecies",
            columns: table => new
            {
                Name = table.Column<string>(type: "longtext", nullable: true),
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                TimeCreated = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "longtext", nullable: true),
                Sprite = table.Column<string>(type: "longtext", nullable: true),
                Folder = table.Column<string>(type: "longtext", nullable: true),
                BaseStats = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MonsterSpecies", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterSpecies");
    }
}
