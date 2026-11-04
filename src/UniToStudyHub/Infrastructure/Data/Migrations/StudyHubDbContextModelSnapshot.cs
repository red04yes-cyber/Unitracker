using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace UniToStudyHub.Infrastructure.Data.Migrations;

[DbContext(typeof(StudyHubDbContext))]
public sealed class StudyHubDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 64);
        StudyHubDbContext.ConfigureModel(modelBuilder);
    }
}
