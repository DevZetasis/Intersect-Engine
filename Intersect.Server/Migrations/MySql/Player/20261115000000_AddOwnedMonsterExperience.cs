using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Player;

public partial class AddOwnedMonsterExperience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("ExperienceInitialized", "OwnedMonsters", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("MoveLearning", "OwnedMonsters", type: "longtext", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("MoveLearning", "OwnedMonsters");
        migrationBuilder.DropColumn("ExperienceInitialized", "OwnedMonsters");
    }
}
