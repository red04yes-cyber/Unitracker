using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyLabAndCalendarPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsAllDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: true),
                    CourseName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Verification = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ExternalSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecurrenceRule = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Distractions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Distractions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnergyCheckIns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EnergyLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    FocusLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnergyCheckIns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErrorJournalEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    SyllabusTopicId = table.Column<string>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    IncorrectAnswer = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    CorrectAnswer = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    ErrorType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RootCause = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FlashcardId = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousIntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    NewIntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    EaseFactorAfter = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flashcards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    SyllabusTopicId = table.Column<string>(type: "TEXT", nullable: false),
                    Front = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Back = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EaseFactor = table.Column<double>(type: "REAL", nullable: false, defaultValue: 2.5),
                    IntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    Lapses = table.Column<int>(type: "INTEGER", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsSuspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flashcards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PdfDocuments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PagesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudySessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PlannedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastResumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PausedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AccumulatedFocusSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    AccumulatedPauseSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_Source_ExternalId",
                table: "CalendarEvents",
                columns: new[] { "Source", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_StartsAt",
                table: "CalendarEvents",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Distractions_CreatedAt",
                table: "Distractions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EnergyCheckIns_RecordedAt",
                table: "EnergyCheckIns",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorJournalEntries_Status_NextReviewAt",
                table: "ErrorJournalEntries",
                columns: new[] { "Status", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_FlashcardId_ReviewedAt",
                table: "FlashcardReviews",
                columns: new[] { "FlashcardId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Flashcards_IsSuspended_DueAt",
                table: "Flashcards",
                columns: new[] { "IsSuspended", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PdfDocuments_Sha256",
                table: "PdfDocuments",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_StartedAt",
                table: "StudySessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_State_StartedAt",
                table: "StudySessions",
                columns: new[] { "State", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyTasks_Status_DueAt",
                table: "StudyTasks",
                columns: new[] { "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEvents");

            migrationBuilder.DropTable(
                name: "Distractions");

            migrationBuilder.DropTable(
                name: "EnergyCheckIns");

            migrationBuilder.DropTable(
                name: "ErrorJournalEntries");

            migrationBuilder.DropTable(
                name: "FlashcardReviews");

            migrationBuilder.DropTable(
                name: "Flashcards");

            migrationBuilder.DropTable(
                name: "PdfDocuments");

            migrationBuilder.DropTable(
                name: "StudySessions");

            migrationBuilder.DropTable(
                name: "StudyTasks");
        }
    }
}
