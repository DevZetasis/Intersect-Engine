using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterCaptureSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("CaptureMultiplier", "Items", type: "TEXT", nullable: false, defaultValue: 1m, precision: 4, scale: 2);
        migrationBuilder.AddColumn<bool>("GuaranteedCapture", "Items", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("BaseCaptureChance", "MonsterSpecies", type: "INTEGER", nullable: false, defaultValue: 40);
        migrationBuilder.AddColumn<decimal>("CaptureMultiplier", "MonsterStatuses", type: "TEXT", nullable: false, defaultValue: 1m, precision: 3, scale: 2);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("CaptureMultiplier", "MonsterStatuses");
        migrationBuilder.DropColumn("BaseCaptureChance", "MonsterSpecies");
        migrationBuilder.DropColumn("GuaranteedCapture", "Items");
        migrationBuilder.DropColumn("CaptureMultiplier", "Items");
    }
}
