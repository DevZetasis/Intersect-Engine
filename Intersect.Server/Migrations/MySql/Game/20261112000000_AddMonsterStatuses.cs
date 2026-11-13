using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.MySql.Game;

public partial class AddMonsterStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable("MonsterStatuses", table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                Name = table.Column<string>(type: "longtext", nullable: true),
                Description = table.Column<string>(type: "longtext", nullable: true),
                Folder = table.Column<string>(type: "longtext", nullable: true),
                TimeCreated = table.Column<long>(type: "bigint", nullable: false),
                Group = table.Column<int>(type: "int", nullable: false),
                Duration = table.Column<int>(type: "int", nullable: false),
                MinimumTurns = table.Column<int>(type: "int", nullable: false),
                MaximumTurns = table.Column<int>(type: "int", nullable: false),
                RecoveryChance = table.Column<int>(type: "int", nullable: false),
                PreventActionChance = table.Column<int>(type: "int", nullable: false),
                Damage = table.Column<int>(type: "int", nullable: false),
                AttackPercent = table.Column<int>(type: "int", nullable: false),
                DefensePercent = table.Column<int>(type: "int", nullable: false),
                SpecialAttackPercent = table.Column<int>(type: "int", nullable: false),
                SpecialDefensePercent = table.Column<int>(type: "int", nullable: false),
                SpeedPercent = table.Column<int>(type: "int", nullable: false),
                PersistOutsideBattle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                RemoveWhenSwitching = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DamageAmount = table.Column<decimal>(type: "decimal(14,4)", nullable: false, precision: 14, scale: 4),
                AppliedAnimationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                ActivationAnimationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                RemovedAnimationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                PreventActionMessage = table.Column<string>(type: "longtext", nullable: true)
            }, constraints: table => table.PrimaryKey("PK_MonsterStatuses", row => row.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("MonsterStatuses");
}
