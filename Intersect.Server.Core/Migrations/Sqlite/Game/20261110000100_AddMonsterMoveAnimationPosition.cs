using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterMoveAnimationPosition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>("AnimationPosition", "MonsterMoves", type: "INTEGER", nullable: false, defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("AnimationPosition", "MonsterMoves");
}
