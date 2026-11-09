using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Player;

public partial class AddOwnedMonsters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OwnedMonsters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SpeciesId = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                Level = table.Column<int>(type: "INTEGER", nullable: false),
                Experience = table.Column<long>(type: "INTEGER", nullable: false),
                Nickname = table.Column<string>(type: "TEXT", nullable: true),
                Location = table.Column<byte>(type: "INTEGER", nullable: false),
                Slot = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtcMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                GrantRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Revision = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OwnedMonsters", x => x.Id);
                table.CheckConstraint("CK_OwnedMonsters_Placement", "Slot >= 0 AND Location IN (0, 1)");
                table.CheckConstraint("CK_OwnedMonsters_Progression", "Level >= 1 AND Experience >= 0");
                table.ForeignKey("FK_OwnedMonsters_Players_OwnerPlayerId", x => x.OwnerPlayerId, "Players", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_OwnedMonsters_GrantRequestId", "OwnedMonsters", "GrantRequestId", unique: true);
        migrationBuilder.CreateIndex("IX_OwnedMonsters_OwnerPlayerId_Location_Slot", "OwnedMonsters", new[] { "OwnerPlayerId", "Location", "Slot" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("OwnedMonsters");
}
