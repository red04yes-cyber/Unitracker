using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed class MoodleApplicationService
{
    public async Task<string> VerifyAndStoreTokenAsync(string baseUrl, string token, CancellationToken cancellationToken = default)
    {
        using var client = new MoodleClient(baseUrl, token);
        var site = await client.GetSiteInfoAsync(cancellationToken);
        WindowsTokenVault.Save(token);
        return site.fullname;
    }

    public async Task<IReadOnlyList<MoodleCourse>> GetEnrolledCoursesAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        var token = WindowsTokenVault.Load();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No Moodle token is stored. Verify and save a token first.");
        using var client = new MoodleClient(baseUrl, token);
        var site = await client.GetSiteInfoAsync(cancellationToken);
        return await client.GetEnrolledCoursesAsync(site.userid, cancellationToken);
    }
}