using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterMoveEffects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("Effects", "MonsterMoves", type: "longtext", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("Effects", "MonsterMoves");
}
