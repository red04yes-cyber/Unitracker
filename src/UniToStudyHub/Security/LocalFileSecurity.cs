using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace UniToStudyHub.Security;

/// <summary>
/// Local file hardening shared by the database, backups, exports, secrets and
/// diagnostics.
///
/// On Windows the inherited DACL is replaced with a single full-control ACE for
/// the current user; on Unix the mode becomes 0700 for folders and 0600 for
/// files. Hardening never throws: a filesystem that cannot store ACLs (FAT32,
/// some network shares) must not stop the application, the failure is reported
/// to the caller so it can be surfaced as a limitation instead of being hidden.
/// </summary>
public static class LocalFileSecurity
{
    public const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    public const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Bytes overwritten before deleting a secret file.</summary>
    private const int ScrubBytes = 64 * 1024;

    public static bool TryRestrictDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        try
        {
            if (OperatingSystem.IsWindows()) ApplyDirectoryAcl(path);
            else File.SetUnixFileMode(path, PrivateDirectoryMode);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException
                                       or InvalidOperationException or ArgumentException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool TryRestrictFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            if (OperatingSystem.IsWindows()) ApplyFileAcl(path);
            else File.SetUnixFileMode(path, PrivateFileMode);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException
                                       or InvalidOperationException or ArgumentException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static void EnsurePrivateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A directory path is required.", nameof(path));
        Directory.CreateDirectory(path);
        TryRestrictDirectory(path);
    }

    /// <summary>
    /// Writes through a temporary file in the same directory, flushes it to the
    /// device, restricts its ACL and only then replaces the destination. An
    /// interruption therefore leaves either the previous file or the new one,
    /// never a half-written file, and never a readable temporary copy in an
    /// unexpected location.
    /// </summary>
    public static async Task WriteAllBytesAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("The destination path has no folder.", nameof(path));
        EnsurePrivateDirectory(directory);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            TryRestrictFile(temporary);
            File.Move(temporary, fullPath, overwrite: true);
            TryRestrictFile(fullPath);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static Task WriteAllTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default) =>
        WriteAllBytesAtomicallyAsync(path, new System.Text.UTF8Encoding(false).GetBytes(content ?? string.Empty),
            cancellationToken);

    /// <summary>Synchronous variant for call sites that cannot await (secret writes).</summary>
    public static void WriteAllBytesAtomically(string path, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("The destination path has no folder.", nameof(path));
        EnsurePrivateDirectory(directory);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       81920, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            TryRestrictFile(temporary);
            File.Move(temporary, fullPath, overwrite: true);
            TryRestrictFile(fullPath);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static bool TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) 
        {
            return false;
        }
    }

    /// <summary>
    /// Best-effort deletion of a file that held secrets: the leading bytes are
    /// overwritten before the directory entry is removed. This cannot defeat
    /// SSD wear levelling, journaling or volume snapshots; it only avoids
    /// leaving a plaintext copy in the filesystem cache of the file itself.
    /// </summary>
    public static void ScrubAndDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var length = (int)Math.Min(stream.Length, ScrubBytes);
                if (length > 0)
                {
                    var zeros = new byte[length];
                    stream.Write(zeros, 0, zeros.Length);
                    stream.Flush(flushToDisk: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The delete below is still attempted; the scrub is advisory.
        }
        TryDelete(path);
    }

    /// <summary>
    /// Removes abandoned temporary files (interrupted saves, backups and
    /// restores). Only files older than <paramref name="minimumAge"/> are
    /// touched so a concurrent operation is never disturbed.
    /// </summary>
    public static int CleanupStaleTemporaryFiles(
        string directory,
        TimeSpan minimumAge,
        params string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
        if (patterns.Length == 0) patterns = new[] { "*.tmp", "*.part" };
        var deleted = 0;
        var threshold = DateTime.UtcNow - minimumAge;
        foreach (var pattern in patterns)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                try
                {
                    var info = new FileInfo(candidate);
                    if (!info.Exists || info.LastWriteTimeUtc > threshold) continue;
                    if (!IsInside(directory, candidate)) continue;
                    info.Delete();
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Another process may own the file; it is retried next start.
                }
            }
        }
        return deleted;
    }

    /// <summary>
    /// Returns true only when <paramref name="candidate"/> is a descendant of
    /// <paramref name="root"/> and no existing path component below the
    /// boundary is a symbolic link, junction or other reparse point.
    /// Failing closed prevents a lexically contained download or cleanup path
    /// from escaping through a filesystem link.
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate)) return false;

        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullCandidate = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootPrefix = Path.EndsInDirectorySeparator(fullRoot)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            // A child path must be strictly below the boundary, not the
            // boundary itself or a similarly prefixed sibling.
            if (!fullCandidate.StartsWith(rootPrefix, comparison)) return false;

            var current = fullRoot;
            if (IsReparsePoint(current)) return false;

            var relative = Path.GetRelativePath(fullRoot, fullCandidate);
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current)) return false;

                // Once a component does not exist, no later component can
                // currently redirect the path. The caller rechecks after
                // creating destination directories.
                if (!File.Exists(current) && !Directory.Exists(current)) break;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            // An unverifiable path is never considered safe.
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        // LinkTarget also detects dangling symbolic links, for which Exists()
        // returns false. Check both file and directory entries because the
        // target type is not known from an untrusted path.
        if (new FileInfo(path).LinkTarget is not null) return true;
        if (new DirectoryInfo(path).LinkTarget is not null) return true;

        // On Windows, LinkTarget can be null for a dangling file symlink even
        // though the directory entry still exists. ResolveLinkTarget inspects
        // the link entry independently of whether its target exists. Try both
        // file and directory forms because the target type is untrusted.
        try
        {
            if (File.ResolveLinkTarget(path, returnFinalTarget: false) is not null) return true;
        }
        catch (IOException)
        {
            // The path may be missing or may be a directory link; try the other form.
        }

        try
        {
            if (Directory.ResolveLinkTarget(path, returnFinalTarget: false) is not null) return true;
        }
        catch (IOException)
        {
            // The path may be missing or may be a file link; attribute checks follow.
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyDirectoryAcl(string path)
    {
        var identity = WindowsIdentity.GetCurrent().User;
        if (identity is null) return;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyFileAcl(string path)
    {
        var identity = WindowsIdentity.GetCurrent().User;
        if (identity is null) return;
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
