using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLedgerAndMissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityRevisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false),
                    RevisedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyActivities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    SyllabusTopicId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    ExamId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    CalendarEventId = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EffectiveSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FocusRating = table.Column<int>(type: "INTEGER", nullable: true),
                    DifficultyRating = table.Column<int>(type: "INTEGER", nullable: true),
                    QualityRating = table.Column<int>(type: "INTEGER", nullable: true),
                    EnergyRating = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsPrivate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyMissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    SyllabusTopicId = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Reward = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyMissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MissionSteps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissionSteps_StudyMissions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "StudyMissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRevisions_ActivityId_RevisedAt",
                table: "ActivityRevisions",
                columns: new[] { "ActivityId", "RevisedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MissionSteps_MissionId_Position",
                table: "MissionSteps",
                columns: new[] { "MissionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyActivities_CourseId_StartedAt",
                table: "StudyActivities",
                columns: new[] { "CourseId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyActivities_DeletedAt",
                table: "StudyActivities",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StudyActivities_SessionId",
                table: "StudyActivities",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyActivities_StartedAt",
                table: "StudyActivities",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StudyMissions_Period_StartsAt",
                table: "StudyMissions",
                columns: new[] { "Period", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyMissions_Status_DueAt",
                table: "StudyMissions",
                columns: new[] { "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityRevisions");

            migrationBuilder.DropTable(
                name: "MissionSteps");

            migrationBuilder.DropTable(
                name: "StudyActivities");

            migrationBuilder.DropTable(
                name: "StudyMissions");
        }
    }
}
