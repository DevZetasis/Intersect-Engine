using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterMoves : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Learnset", table: "MonsterSpecies", type: "longtext", nullable: true);
        migrationBuilder.CreateTable(
            name: "MonsterMoves",
            columns: table => new
            {
                Name = table.Column<string>(type: "longtext", nullable: true),
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                TimeCreated = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "longtext", nullable: true),
                Folder = table.Column<string>(type: "longtext", nullable: true),
                TypeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                Category = table.Column<int>(type: "int", nullable: false),
                BasePower = table.Column<int>(type: "int", nullable: false),
                Accuracy = table.Column<int>(type: "int", nullable: false),
                AlwaysHits = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Priority = table.Column<int>(type: "int", nullable: false),
                Target = table.Column<int>(type: "int", nullable: false),
                AnimationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
            },
            constraints: table => table.PrimaryKey("PK_MonsterMoves", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterMoves");
        migrationBuilder.DropColumn(name: "Learnset", table: "MonsterSpecies");
    }
}
