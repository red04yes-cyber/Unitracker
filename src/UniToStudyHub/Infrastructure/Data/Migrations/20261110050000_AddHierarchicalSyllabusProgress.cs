using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    public partial class AddHierarchicalSyllabusProgress : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentTopicId", table: "SyllabusTopics", type: "TEXT",
                maxLength: 64, nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Kind", table: "SyllabusTopics", type: "INTEGER",
                nullable: false, defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "Position", table: "SyllabusTopics", type: "INTEGER",
                nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompletionPercent", table: "SyllabusTopics", type: "INTEGER",
                nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Preparedness", table: "SyllabusTopics", type: "INTEGER",
                nullable: false, defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SyllabusTopics_CourseId_ParentTopicId_Position",
                table: "SyllabusTopics",
                columns: new[] { "CourseId", "ParentTopicId", "Position" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyllabusTopics_CourseId_ParentTopicId_Position",
                table: "SyllabusTopics");
            migrationBuilder.DropColumn(name: "ParentTopicId", table: "SyllabusTopics");
            migrationBuilder.DropColumn(name: "Kind", table: "SyllabusTopics");
            migrationBuilder.DropColumn(name: "Position", table: "SyllabusTopics");
            migrationBuilder.DropColumn(name: "CompletionPercent", table: "SyllabusTopics");
            migrationBuilder.DropColumn(name: "Preparedness", table: "SyllabusTopics");
        }
    }
}