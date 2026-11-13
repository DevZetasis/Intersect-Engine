using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Player;

public partial class AddOwnedMonsterStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("StatusId", "OwnedMonsters", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>("StatusTurns", "OwnedMonsters", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("StatusId", "OwnedMonsters");
        migrationBuilder.DropColumn("StatusTurns", "OwnedMonsters");
    }
}
