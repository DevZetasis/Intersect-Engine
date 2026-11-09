using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Types", table: "MonsterSpecies", type: "longtext", nullable: true);
        migrationBuilder.CreateTable(
            name: "MonsterTypes",
            columns: table => new
            {
                Name = table.Column<string>(type: "longtext", nullable: true),
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                TimeCreated = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "longtext", nullable: true),
                Folder = table.Column<string>(type: "longtext", nullable: true),
                DisplayColor = table.Column<string>(type: "longtext", nullable: true),
                Effectiveness = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MonsterTypes", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterTypes");
        migrationBuilder.DropColumn(name: "Types", table: "MonsterSpecies");
    }
}
