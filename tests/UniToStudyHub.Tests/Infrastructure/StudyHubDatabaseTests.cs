using UniToStudyHub.Infrastructure.Data;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class StudyHubDatabaseTests
{
    [Fact]
    public void DataDirectoryOverrideIsUsedForIsolatedStartup()
    {
        var previous = Environment.GetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable);
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable, folder);

            var expected = Path.GetFullPath(folder);
            Assert.Equal(expected, StudyHubDatabase.GetDataDirectory());
            Assert.Equal(Path.Combine(expected, "unitracker.db"), StudyHubDatabase.GetDatabasePath());
            Assert.True(Directory.Exists(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable, previous);
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
