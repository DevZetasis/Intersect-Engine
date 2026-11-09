using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intersect.Server.Migrations.MySql.Player;

public partial class AddMonsterAttributes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "IvHP", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "IvAttack", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "IvDefense", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "IvSpecialAttack", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "IvSpecialDefense", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "IvSpeed", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvHP", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvAttack", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvDefense", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvSpecialAttack", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvSpecialDefense", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "EvSpeed", table: "OwnedMonsters", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<Guid>(name: "NatureId", table: "OwnedMonsters", type: "char(36)", nullable: true, collation: "ascii_general_ci");
        migrationBuilder.CreateIndex(name: "IX_OwnedMonsters_NatureId", table: "OwnedMonsters", column: "NatureId");
        migrationBuilder.AddCheckConstraint(name: "CK_OwnedMonsters_IVs", table: "OwnedMonsters", sql: "IvHP BETWEEN 0 AND 31 AND IvAttack BETWEEN 0 AND 31 AND IvDefense BETWEEN 0 AND 31 AND IvSpecialAttack BETWEEN 0 AND 31 AND IvSpecialDefense BETWEEN 0 AND 31 AND IvSpeed BETWEEN 0 AND 31");
        migrationBuilder.AddCheckConstraint(name: "CK_OwnedMonsters_EVs", table: "OwnedMonsters", sql: "EvHP BETWEEN 0 AND 255 AND EvAttack BETWEEN 0 AND 255 AND EvDefense BETWEEN 0 AND 255 AND EvSpecialAttack BETWEEN 0 AND 255 AND EvSpecialDefense BETWEEN 0 AND 255 AND EvSpeed BETWEEN 0 AND 255");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_OwnedMonsters_IVs", table: "OwnedMonsters");
        migrationBuilder.DropCheckConstraint(name: "CK_OwnedMonsters_EVs", table: "OwnedMonsters");
        migrationBuilder.DropIndex(name: "IX_OwnedMonsters_NatureId", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "NatureId", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvHP", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvAttack", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvDefense", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvSpecialAttack", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvSpecialDefense", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "IvSpeed", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvHP", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvAttack", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvDefense", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvSpecialAttack", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvSpecialDefense", table: "OwnedMonsters");
        migrationBuilder.DropColumn(name: "EvSpeed", table: "OwnedMonsters");
    }
}
