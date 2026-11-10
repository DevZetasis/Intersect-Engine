using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterMovePP : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "MaximumPP", table: "MonsterMoves", type: "int", nullable: false, defaultValue: 20);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MaximumPP", table: "MonsterMoves");
    }
}
