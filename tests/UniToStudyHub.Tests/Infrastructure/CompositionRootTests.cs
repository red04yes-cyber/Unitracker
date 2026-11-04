using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class CompositionRootTests
{
    [Fact]
    public async Task CoreServicesAndViewModelResolveWithoutWpf()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "unitracker.db");
        try
        {
            var services = new ServiceCollection();
            services.AddUniToStudyHubCore(databasePath);
            services.AddUniToStudyHubViewModels();

            using var provider = services.BuildServiceProvider(validateScopes: true);

            var factory = provider.GetRequiredService<IDbContextFactory<StudyHubDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.MigrateAsync();

            var store = provider.GetRequiredService<IStudyDataStore>();
            var calculator = provider.GetRequiredService<CareerCalculatorService>();
            var migrator = provider.GetRequiredService<LegacyJsonMigrator>();
            var moodle = provider.GetRequiredService<MoodleApplicationService>();
            var viewModel = provider.GetRequiredService<MainViewModel>();

            Assert.Same(store, provider.GetRequiredService<IStudyDataStore>());
            Assert.Same(calculator, provider.GetRequiredService<CareerCalculatorService>());
            Assert.Same(migrator, provider.GetRequiredService<LegacyJsonMigrator>());
            Assert.Same(moodle, provider.GetRequiredService<MoodleApplicationService>());
            Assert.Same(viewModel, provider.GetRequiredService<MainViewModel>());

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
