using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Intersect.Server.Migrations.Sqlite.Player;
public partial class AddMonsterCondition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "CurrentHP", table: "OwnedMonsters", type: "INTEGER", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>(name: "MaximumHPAtLastUpdate", table: "OwnedMonsters", type: "INTEGER", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<bool>(name: "ConditionInitialized", table: "OwnedMonsters", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "MovePP", table: "OwnedMonsters", type: "TEXT", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CurrentHP", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "MaximumHPAtLastUpdate", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "ConditionInitialized", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "MovePP", table: "OwnedMonsters");
    }
}
