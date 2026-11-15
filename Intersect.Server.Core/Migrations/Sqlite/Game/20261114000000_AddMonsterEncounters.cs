using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterEncounters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MonsterEncounterTables", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", nullable: true),
            Description = table.Column<string>(type: "TEXT", nullable: true),
            Folder = table.Column<string>(type: "TEXT", nullable: true),
            Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
            Rate = table.Column<int>(type: "INTEGER", nullable: false),
            Cooldown = table.Column<int>(type: "INTEGER", nullable: false),
            Entries = table.Column<string>(type: "TEXT", nullable: true),
            TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_MonsterEncounterTables", row => row.Id));
        migrationBuilder.AddColumn<string>("Encounters", "Maps", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Encounters", "Maps");
        migrationBuilder.DropTable("MonsterEncounterTables");
    }
}
