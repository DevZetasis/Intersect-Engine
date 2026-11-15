using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterEncounters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MonsterEncounterTables", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
            Name = table.Column<string>(type: "longtext", nullable: true),
            Description = table.Column<string>(type: "longtext", nullable: true),
            Folder = table.Column<string>(type: "longtext", nullable: true),
            Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
            Rate = table.Column<int>(type: "int", nullable: false),
            Cooldown = table.Column<int>(type: "int", nullable: false),
            Entries = table.Column<string>(type: "longtext", nullable: true),
            TimeCreated = table.Column<long>(type: "bigint", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_MonsterEncounterTables", row => row.Id));
        migrationBuilder.AddColumn<string>("Encounters", "Maps", type: "longtext", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Encounters", "Maps");
        migrationBuilder.DropTable("MonsterEncounterTables");
    }
}
