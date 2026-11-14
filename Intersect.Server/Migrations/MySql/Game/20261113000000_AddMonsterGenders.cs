using System;
using Intersect.Framework.Core.GameObjects.Monsters;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterGenders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MonsterGenders", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
            Name = table.Column<string>(type: "longtext", nullable: true),
            Description = table.Column<string>(type: "longtext", nullable: true),
            Symbol = table.Column<string>(type: "longtext", nullable: true),
            Folder = table.Column<string>(type: "longtext", nullable: true),
            TimeCreated = table.Column<long>(type: "bigint", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_MonsterGenders", row => row.Id));
        migrationBuilder.AddColumn<string>("GenderWeights", "MonsterSpecies", type: "longtext", nullable: true);
        migrationBuilder.InsertData("MonsterGenders", new[] { "Id", "Name", "Description", "Symbol", "Folder", "TimeCreated" },
            new object[,]
            {
                { MonsterGenderDescriptor.DefaultMale, "Male", "", "\u2642", "", 0L },
                { MonsterGenderDescriptor.DefaultFemale, "Female", "", "\u2640", "", 0L },
                { MonsterGenderDescriptor.DefaultGenderless, "Genderless", "", "", "", 0L },
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("GenderWeights", "MonsterSpecies");
        migrationBuilder.DropTable("MonsterGenders");
    }
}
