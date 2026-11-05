using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionNotePrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotesArePrivate",
                table: "StudySessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotesArePrivate",
                table: "StudySessions");
        }
    }
}
