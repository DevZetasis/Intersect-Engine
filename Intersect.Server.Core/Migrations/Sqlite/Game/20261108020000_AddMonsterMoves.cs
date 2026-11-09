using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterMoves : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Learnset", table: "MonsterSpecies", type: "TEXT", nullable: true);
        migrationBuilder.CreateTable(
            name: "MonsterMoves",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Folder = table.Column<string>(type: "TEXT", nullable: true),
                TypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Category = table.Column<int>(type: "INTEGER", nullable: false),
                BasePower = table.Column<int>(type: "INTEGER", nullable: false),
                Accuracy = table.Column<int>(type: "INTEGER", nullable: false),
                AlwaysHits = table.Column<bool>(type: "INTEGER", nullable: false),
                Priority = table.Column<int>(type: "INTEGER", nullable: false),
                Target = table.Column<int>(type: "INTEGER", nullable: false),
                AnimationId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MonsterMoves", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MonsterMoves");
        migrationBuilder.DropColumn(name: "Learnset", table: "MonsterSpecies");
    }
}
