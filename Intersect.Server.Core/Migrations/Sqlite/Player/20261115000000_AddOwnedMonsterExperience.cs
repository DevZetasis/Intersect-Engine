using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Player;

public partial class AddOwnedMonsterExperience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("ExperienceInitialized", "OwnedMonsters", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("MoveLearning", "OwnedMonsters", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("MoveLearning", "OwnedMonsters");
        migrationBuilder.DropColumn("ExperienceInitialized", "OwnedMonsters");
    }
}
