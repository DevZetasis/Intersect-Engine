using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Intersect.Server.Migrations.Sqlite.Game;
public partial class AddMonsterBattleAppearance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FrontBattleSprite", table: "MonsterSpecies", type: "TEXT", maxLength: 255, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "BackBattleSprite", table: "MonsterSpecies", type: "TEXT", maxLength: 255, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "BattleBackground", table: "MonsterSpecies", type: "TEXT", maxLength: 255, nullable: false, defaultValue: "");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FrontBattleSprite", table: "MonsterSpecies");
        migrationBuilder.DropColumn(name: "BackBattleSprite", table: "MonsterSpecies");
        migrationBuilder.DropColumn(name: "BattleBackground", table: "MonsterSpecies");
    }
}
