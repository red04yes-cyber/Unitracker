using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcademicKnowledgeModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentAcademicYear",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DegreeProgramCode",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DegreeProgramName",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DegreeTotalCfu",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnrollmentYear",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDailyStudyMinutes",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<string>(
                name: "StudyPreferences",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WeeklyStudyHours",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "AcademicYear",
                table: "Courses",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AppealDate",
                table: "Courses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedStudyHours",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Courses",
                type: "TEXT",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Session",
                table: "Courses",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SyllabusSummary",
                table: "Courses",
                type: "TEXT",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SyllabusUrl",
                table: "Courses",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Teacher",
                table: "Courses",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Courses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CoursePrerequisites",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    PrerequisiteCourseId = table.Column<string>(type: "TEXT", nullable: false),
                    IsMandatory = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoursePrerequisites", x => new { x.CourseId, x.PrerequisiteCourseId });
                    table.ForeignKey(
                        name: "FK_CoursePrerequisites_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoursePrerequisites_Courses_PrerequisiteCourseId",
                        column: x => x.PrerequisiteCourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    SyllabusTopicId = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Difficulty = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CorrectAnswer = table.Column<string>(type: "TEXT", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsOfficial = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankItems_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyllabusTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    EstimatedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    LastStudiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyllabusTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyllabusTopics_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Code",
                table: "Courses",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_CoursePrerequisites_PrerequisiteCourseId",
                table: "CoursePrerequisites",
                column: "PrerequisiteCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankItems_CourseId_SyllabusTopicId",
                table: "QuestionBankItems",
                columns: new[] { "CourseId", "SyllabusTopicId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyllabusTopics_CourseId_Status",
                table: "SyllabusTopics",
                columns: new[] { "CourseId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoursePrerequisites");

            migrationBuilder.DropTable(
                name: "QuestionBankItems");

            migrationBuilder.DropTable(
                name: "SyllabusTopics");

            migrationBuilder.DropIndex(
                name: "IX_Courses_Code",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "CurrentAcademicYear",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "DegreeProgramCode",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "DegreeProgramName",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "DegreeTotalCfu",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "EnrollmentYear",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "MaxDailyStudyMinutes",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "StudyPreferences",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "WeeklyStudyHours",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "AcademicYear",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "AppealDate",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Archived",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "EstimatedStudyHours",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Session",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "SyllabusSummary",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "SyllabusUrl",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Teacher",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Courses");
        }
    }
}
