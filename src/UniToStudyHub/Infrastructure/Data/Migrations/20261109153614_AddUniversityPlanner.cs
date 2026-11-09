using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversityPlanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Difficulty",
                table: "SyllabusTopics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "SyllabusTopics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParentTopicId",
                table: "SyllabusTopics",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "SyllabusTopics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreparationLevel",
                table: "SyllabusTopics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "SyllabusTopics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "CalendarEventId",
                table: "StudySessions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SyllabusTopicId",
                table: "StudySessions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ActualMinutes",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanId",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlannerStatus",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PriorityScore",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RescheduleCount",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SyllabusTopicId",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PlannerReschedules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CalendarEventId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PreviousStartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreviousEndsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NewStartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NewEndsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    MovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerReschedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredSessionMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 50),
                    MinimumSessionMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 25),
                    BreakBetweenSessionsMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    MaxSessionsPerDay = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    ReviewReservePercent = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 15),
                    ReviewReserveDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    IncludeWeekends = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    AutoRescheduleMissed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    AvailabilityJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CourseName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ExamId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExamTitle = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    ExamDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequiredMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewReserveMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPlans_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyllabusTopics_CourseId_ParentTopicId_Position",
                table: "SyllabusTopics",
                columns: new[] { "CourseId", "ParentTopicId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_CalendarEventId",
                table: "StudySessions",
                column: "CalendarEventId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_PlanId_StartsAt",
                table: "CalendarEvents",
                columns: new[] { "PlanId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerReschedules_CalendarEventId_MovedAt",
                table: "PlannerReschedules",
                columns: new[] { "CalendarEventId", "MovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerReschedules_PlanId",
                table: "PlannerReschedules",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_CourseId",
                table: "StudyPlans",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_Status_ExamDate",
                table: "StudyPlans",
                columns: new[] { "Status", "ExamDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerReschedules");

            migrationBuilder.DropTable(
                name: "PlannerSettings");

            migrationBuilder.DropTable(
                name: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_SyllabusTopics_CourseId_ParentTopicId_Position",
                table: "SyllabusTopics");

            migrationBuilder.DropIndex(
                name: "IX_StudySessions_CalendarEventId",
                table: "StudySessions");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_PlanId_StartsAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "ParentTopicId",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "PreparationLevel",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SyllabusTopics");

            migrationBuilder.DropColumn(
                name: "CalendarEventId",
                table: "StudySessions");

            migrationBuilder.DropColumn(
                name: "SyllabusTopicId",
                table: "StudySessions");

            migrationBuilder.DropColumn(
                name: "ActualMinutes",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "PlannerStatus",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "PriorityScore",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RescheduleCount",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "SyllabusTopicId",
                table: "CalendarEvents");
        }
    }
}
