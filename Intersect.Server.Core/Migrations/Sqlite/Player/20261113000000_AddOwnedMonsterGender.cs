using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Player;

public partial class AddOwnedMonsterGender : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>("GenderId", "OwnedMonsters", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("GenderId", "OwnedMonsters");
}
