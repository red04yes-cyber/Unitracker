using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.Navigation;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class CompositionRootTests
{
    [Fact]
    public async Task CoreServicesAndPresentationShellResolveWithoutWpf()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "unitracker.db");
        try
        {
            var services = new ServiceCollection();
            services.AddUniToStudyHubCore(databasePath);
            services.AddUniToStudyHubPresentation();

            using var provider = services.BuildServiceProvider(validateScopes: true);

            var factory = provider.GetRequiredService<IDbContextFactory<StudyHubDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.MigrateAsync();

            var store = provider.GetRequiredService<IStudyDataStore>();
            var calculator = provider.GetRequiredService<CareerCalculatorService>();
            var migrator = provider.GetRequiredService<LegacyJsonMigrator>();
            var moodle = provider.GetRequiredService<MoodleApplicationService>();
            var profile = provider.GetRequiredService<AcademicProfileService>();
            var career = provider.GetRequiredService<CareerApplicationService>();
            var syllabus = provider.GetRequiredService<SyllabusTrackerService>();
            var questions = provider.GetRequiredService<QuestionBankService>();
            var readiness = provider.GetRequiredService<ExamReadinessService>();
            var timeBudget = provider.GetRequiredService<TimeBudgetService>();
            var backup = provider.GetRequiredService<BackupService>();
            var viewModel = provider.GetRequiredService<MainViewModel>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var shell = provider.GetRequiredService<ShellViewModel>();

            Assert.Same(store, provider.GetRequiredService<IStudyDataStore>());
            Assert.Same(calculator, provider.GetRequiredService<CareerCalculatorService>());
            Assert.Same(migrator, provider.GetRequiredService<LegacyJsonMigrator>());
            Assert.Same(moodle, provider.GetRequiredService<MoodleApplicationService>());
            Assert.Same(profile, provider.GetRequiredService<AcademicProfileService>());
            Assert.Same(career, provider.GetRequiredService<CareerApplicationService>());
            Assert.Same(syllabus, provider.GetRequiredService<SyllabusTrackerService>());
            Assert.Same(questions, provider.GetRequiredService<QuestionBankService>());
            Assert.Same(readiness, provider.GetRequiredService<ExamReadinessService>());
            Assert.Same(timeBudget, provider.GetRequiredService<TimeBudgetService>());
            Assert.Same(backup, provider.GetRequiredService<BackupService>());
            Assert.Same(viewModel, provider.GetRequiredService<MainViewModel>());
            Assert.Same(viewModel, shell.LegacyContent);
            Assert.Equal(StudyRoute.Today, navigation.CurrentRoute);
            Assert.Equal((int)StudyRoute.Today, shell.SelectedPageIndex);

            shell.SelectedPageIndex = (int)StudyRoute.Calendar;
            Assert.Equal(StudyRoute.Calendar, navigation.CurrentRoute);
            Assert.Equal((int)StudyRoute.Calendar, shell.SelectedPageIndex);

            Assert.True(shell.NavigateTo(StudyRoute.Settings));
            Assert.Equal(StudyRoute.Settings, shell.CurrentRoute);
            Assert.Equal((int)StudyRoute.Settings, shell.SelectedPageIndex);

            Assert.False(shell.NavigateTo((StudyRoute)999));
            Assert.Equal(StudyRoute.Settings, shell.CurrentRoute);

            await viewModel.InitializeAsync();
            Assert.Equal("—", viewModel.AverageDisplay);
            Assert.Equal("CFU completati: 0", viewModel.CreditsDisplay);
            Assert.Equal("Voto di partenza: —", viewModel.BaseGradeDisplay);
            Assert.Empty(viewModel.Courses);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
