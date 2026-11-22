using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Presentation;

public sealed class StudyWorkspaceTimeBudgetTests
{
    [Fact]
    public async Task MainViewModelInitializationAndRefreshExposeBudgetWithoutTreatingMissingEstimateAsCovered()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var services = new ServiceCollection();
            services.AddUniToStudyHubCore(Path.Combine(folder, "unitracker.db"));
            services.AddUniToStudyHubViewModels();

            using var provider = services.BuildServiceProvider(validateScopes: true);
            var factory = provider.GetRequiredService<IDbContextFactory<StudyHubDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.MigrateAsync();

            var store = provider.GetRequiredService<IStudyDataStore>();
            var data = new StudyData
            {
                WeeklyStudyHours = 1,
                MaxDailyStudyMinutes = 60
            };
            data.Courses.Add(new Course { Id = "course-1", Name = "Analisi" });
            data.Exams.Add(new Exam
            {
                Id = "exam-1",
                CourseId = "course-1",
                CourseName = "Analisi",
                Name = "Analisi",
                PlannedDate = DateTime.Today.AddDays(14)
            });
            await store.SaveAsync(data);

            var main = provider.GetRequiredService<MainViewModel>();
            var workspace = main.Workspace;
            var changedProperties = new HashSet<string>(StringComparer.Ordinal);
            workspace.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is not null)
                    changedProperties.Add(args.PropertyName);
            };

            await main.InitializeAsync();

            var initialPlan = Assert.IsType<TimeBudgetPlan>(workspace.CurrentTimeBudgetPlan);
            Assert.Equal(60, initialPlan.WeeklyMinutes);
            Assert.Equal(120, initialPlan.AvailableMinutes);
            Assert.Equal(0, initialPlan.RequiredMinutes);
            Assert.True(workspace.HasTimeBudgetAllocations);
            Assert.Equal(1, workspace.TimeBudgetUnknownEstimateCount);
            Assert.True(workspace.HasTimeBudgetUnknownEstimates);
            Assert.Contains("Budget parziale", workspace.TimeBudgetStatus);

            var unknownRow = Assert.Single(workspace.TimeBudgetAllocations);
            Assert.False(unknownRow.HasEstimate);
            Assert.Contains("non è valutabile", unknownRow.Explanation);

            main.Exams.Single().EstimatedStudyHours = 4;
            workspace.RefreshTimeBudget();

            var updatedPlan = Assert.IsType<TimeBudgetPlan>(workspace.CurrentTimeBudgetPlan);
            Assert.Equal(240, updatedPlan.RequiredMinutes);
            Assert.Equal(120, updatedPlan.AvailableMinutes);
            Assert.Equal(120, workspace.TimeBudgetAllocations.Single().AllocatedMinutes);
            Assert.True(workspace.TimeBudgetAllocations.Single().HasEstimate);
            Assert.True(workspace.TimeBudgetAllocations.Single().IsAtRisk);
            Assert.True(workspace.IsTimeBudgetOverCapacity);
            Assert.Equal(0, workspace.TimeBudgetUnknownEstimateCount);
            Assert.Contains("supera", workspace.TimeBudgetStatus, StringComparison.OrdinalIgnoreCase);

            Assert.Contains(nameof(StudyWorkspaceViewModel.CurrentTimeBudgetPlan), changedProperties);
            Assert.Contains(nameof(StudyWorkspaceViewModel.IsTimeBudgetOverCapacity), changedProperties);
            Assert.Contains(nameof(StudyWorkspaceViewModel.TimeBudgetUnknownEstimateCount), changedProperties);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
