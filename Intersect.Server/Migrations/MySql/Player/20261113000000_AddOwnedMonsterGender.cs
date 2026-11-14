using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Player;

public partial class AddOwnedMonsterGender : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>("GenderId", "OwnedMonsters", type: "char(36)", nullable: true, collation: "ascii_general_ci");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("GenderId", "OwnedMonsters");
}
