using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Player;

public partial class AddOwnedMonsterStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("StatusId", "OwnedMonsters", type: "char(36)", nullable: true, collation: "ascii_general_ci");
        migrationBuilder.AddColumn<int>("StatusTurns", "OwnedMonsters", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("StatusId", "OwnedMonsters");
        migrationBuilder.DropColumn("StatusTurns", "OwnedMonsters");
    }
}
