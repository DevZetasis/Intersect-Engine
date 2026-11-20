using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterExperienceCurves : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("BaseExperienceYield", "MonsterSpecies", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("GrowthPreset", "MonsterSpecies", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<Guid>("ExperienceCurveId", "MonsterSpecies", type: "char(36)", nullable: true, collation: "ascii_general_ci");
        migrationBuilder.CreateTable("MonsterExperienceCurves", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
            Name = table.Column<string>(type: "longtext", nullable: true),
            Folder = table.Column<string>(type: "longtext", nullable: true),
            Thresholds = table.Column<string>(type: "longtext", nullable: true),
            TimeCreated = table.Column<long>(type: "bigint", nullable: false),
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
