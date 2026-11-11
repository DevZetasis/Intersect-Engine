using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterMoveAnimationPosition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>("AnimationPosition", "MonsterMoves", type: "int", nullable: false, defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("AnimationPosition", "MonsterMoves");
}
