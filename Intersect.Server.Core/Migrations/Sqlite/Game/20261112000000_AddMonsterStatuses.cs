using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intersect.Server.Migrations.Sqlite.Game;

public partial class AddMonsterStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable("MonsterStatuses", table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Folder = table.Column<string>(type: "TEXT", nullable: true),
                TimeCreated = table.Column<long>(type: "INTEGER", nullable: false),
                Group = table.Column<int>(type: "INTEGER", nullable: false),
                Duration = table.Column<int>(type: "INTEGER", nullable: false),
                MinimumTurns = table.Column<int>(type: "INTEGER", nullable: false),
                MaximumTurns = table.Column<int>(type: "INTEGER", nullable: false),
                RecoveryChance = table.Column<int>(type: "INTEGER", nullable: false),
                PreventActionChance = table.Column<int>(type: "INTEGER", nullable: false),
                Damage = table.Column<int>(type: "INTEGER", nullable: false),
                AttackPercent = table.Column<int>(type: "INTEGER", nullable: false),
                DefensePercent = table.Column<int>(type: "INTEGER", nullable: false),
                SpecialAttackPercent = table.Column<int>(type: "INTEGER", nullable: false),
                SpecialDefensePercent = table.Column<int>(type: "INTEGER", nullable: false),
                SpeedPercent = table.Column<int>(type: "INTEGER", nullable: false),
                PersistOutsideBattle = table.Column<bool>(type: "INTEGER", nullable: false),
                RemoveWhenSwitching = table.Column<bool>(type: "INTEGER", nullable: false),
                DamageAmount = table.Column<decimal>(type: "TEXT", nullable: false, precision: 14, scale: 4),
                AppliedAnimationId = table.Column<Guid>(type: "TEXT", nullable: true),
                ActivationAnimationId = table.Column<Guid>(type: "TEXT", nullable: true),
                RemovedAnimationId = table.Column<Guid>(type: "TEXT", nullable: true),
                PreventActionMessage = table.Column<string>(type: "TEXT", nullable: true)
            }, constraints: table => table.PrimaryKey("PK_MonsterStatuses", row => row.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("MonsterStatuses");
}
