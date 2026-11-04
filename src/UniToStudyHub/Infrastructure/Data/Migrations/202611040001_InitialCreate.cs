using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UniToStudyHub.Infrastructure.Data.Migrations;

[DbContext(typeof(StudyHubDbContext))]
[Migration("202611040001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProfileSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                GraduationTarget = table.Column<DateTime>(type: "TEXT", nullable: false),
                MoodleBaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProfileSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Courses",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                MoodleCourseId = table.Column<int>(type: "INTEGER", nullable: true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                Completed = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Courses", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Grades",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                CourseId = table.Column<string>(type: "TEXT", nullable: false),
                CourseName = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<int>(type: "INTEGER", nullable: false),
                Honors = table.Column<bool>(type: "INTEGER", nullable: false),
                Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                CountsTowardAverage = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Grades", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PlannedExams",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                CourseId = table.Column<string>(type: "TEXT", nullable: false),
                CourseName = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                EstimatedStudyHours = table.Column<int>(type: "INTEGER", nullable: false),
                PlannedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                PlannedSession = table.Column<string>(type: "TEXT", nullable: false),
                Completed = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlannedExams", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Announcements",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                SourceKey = table.Column<string>(type: "TEXT", nullable: false),
                CourseName = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Summary = table.Column<string>(type: "TEXT", nullable: false),
                Url = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                IsNew = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Announcements", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CareerExams",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                CourseId = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                CountsTowardAverage = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CareerExams", x => x.Id));

        migrationBuilder.CreateTable(
            name: "LegacyImports",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                SourceHash = table.Column<string>(type: "TEXT", nullable: false),
                ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_LegacyImports", x => x.Key));

        migrationBuilder.CreateTable(
            name: "ExamAttempts",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ExamId = table.Column<string>(type: "TEXT", nullable: false),
                AttemptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                GradeDecision = table.Column<int>(type: "INTEGER", nullable: false),
                NumericGrade = table.Column<int>(type: "INTEGER", nullable: true),
                IsHonors = table.Column<bool>(type: "INTEGER", nullable: false),
                IsPassFail = table.Column<bool>(type: "INTEGER", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                table.ForeignKey("FK_ExamAttempts_CareerExams_ExamId", x => x.ExamId, "CareerExams", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_Courses_MoodleCourseId", table: "Courses", column: "MoodleCourseId");
        migrationBuilder.CreateIndex(name: "IX_Grades_CourseId", table: "Grades", column: "CourseId");
        migrationBuilder.CreateIndex(name: "IX_PlannedExams_CourseId", table: "PlannedExams", column: "CourseId");
        migrationBuilder.CreateIndex(name: "IX_Announcements_SourceKey", table: "Announcements", column: "SourceKey");
        migrationBuilder.CreateIndex(name: "IX_ExamAttempts_ExamId_AttemptedAt", table: "ExamAttempts", columns: new[] { "ExamId", "AttemptedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ExamAttempts");
        migrationBuilder.DropTable("LegacyImports");
        migrationBuilder.DropTable("Announcements");
        migrationBuilder.DropTable("PlannedExams");
        migrationBuilder.DropTable("Grades");
        migrationBuilder.DropTable("Courses");
        migrationBuilder.DropTable("ProfileSettings");
        migrationBuilder.DropTable("CareerExams");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) { }
}