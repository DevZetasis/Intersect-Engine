using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterExperienceCurves : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("BaseExperienceYield", "MonsterSpecies", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("GrowthPreset", "MonsterSpecies", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<Guid>("ExperienceCurveId", "MonsterSpecies", type: "TEXT", nullable: true);
        migrationBuilder.CreateTable("MonsterExperienceCurves", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", nullable: true),
            Folder = table.Column<string>(type: "TEXT", nullable: true),
            Thresholds = table.Column<string>(type: "TEXT", nullable: true),
            TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_MonsterExperienceCurves", row => row.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MonsterExperienceCurves");
        migrationBuilder.DropColumn("ExperienceCurveId", "MonsterSpecies");
        migrationBuilder.DropColumn("GrowthPreset", "MonsterSpecies");
        migrationBuilder.DropColumn("BaseExperienceYield", "MonsterSpecies");
    }
}
