using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPomodoroAndExportPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PomodoroCycles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    CycleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FocusStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FocusEndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FocusSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BreakType = table.Column<int>(type: "INTEGER", nullable: false),
                    BreakStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    BreakEndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    BreakSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    WasSkipped = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PomodoroCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PomodoroCycles_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PomodoroSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    FocusMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 25),
                    ShortBreakMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    LongBreakMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 15),
                    SessionsBeforeLongBreak = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 4)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PomodoroSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PomodoroTimer",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Phase = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseWindowStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PhaseStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PhaseEndsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RemainingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FocusesCompletedInSet = table.Column<int>(type: "INTEGER", nullable: false),
                    CycleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentCycleId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PomodoroTimer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PomodoroTimer_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PomodoroCycles_CompletedAt",
                table: "PomodoroCycles",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PomodoroCycles_SessionId_CycleNumber",
                table: "PomodoroCycles",
                columns: new[] { "SessionId", "CycleNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PomodoroTimer_SessionId",
                table: "PomodoroTimer",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PomodoroCycles");

            migrationBuilder.DropTable(
                name: "PomodoroSettings");

            migrationBuilder.DropTable(
                name: "PomodoroTimer");
        }
    }
}
