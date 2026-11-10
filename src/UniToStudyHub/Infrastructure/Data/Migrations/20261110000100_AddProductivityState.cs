using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UniToStudyHub.Infrastructure.Data;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations;

[DbContext(typeof(StudyHubDbContext))]
[Migration("20261110000100_AddProductivityState")]
public sealed partial class AddProductivityState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProductivityStateJson",
            table: "ProfileSettings",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProductivityStateJson",
            table: "ProfileSettings");
    }
}
