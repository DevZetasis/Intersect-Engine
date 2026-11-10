using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.Sqlite.Player;

public partial class AddMonsterMoveSlots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "Move1Id", table: "OwnedMonsters", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "Move2Id", table: "OwnedMonsters", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "Move3Id", table: "OwnedMonsters", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "Move4Id", table: "OwnedMonsters", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "MovesInitialized", table: "OwnedMonsters", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex(name: "IX_OwnedMonsters_Move1Id", table: "OwnedMonsters", column: "Move1Id");
        migrationBuilder.CreateIndex(name: "IX_OwnedMonsters_Move2Id", table: "OwnedMonsters", column: "Move2Id");
        migrationBuilder.CreateIndex(name: "IX_OwnedMonsters_Move3Id", table: "OwnedMonsters", column: "Move3Id");
        migrationBuilder.CreateIndex(name: "IX_OwnedMonsters_Move4Id", table: "OwnedMonsters", column: "Move4Id");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_OwnedMonsters_Move1Id", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "Move1Id", table: "OwnedMonsters");
        migrationBuilder.DropIndex(name: "IX_OwnedMonsters_Move2Id", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "Move2Id", table: "OwnedMonsters");
        migrationBuilder.DropIndex(name: "IX_OwnedMonsters_Move3Id", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "Move3Id", table: "OwnedMonsters");
        migrationBuilder.DropIndex(name: "IX_OwnedMonsters_Move4Id", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "Move4Id", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "MovesInitialized", table: "OwnedMonsters");
    }
}
