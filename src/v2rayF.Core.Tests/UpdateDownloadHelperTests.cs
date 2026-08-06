using System.IO.Compression;
using System.Text;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class UpdateDownloadHelperTests
{
    [Fact]
    public void ExtractZipSafe_RejectsParentTraversal()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"v2rayf-zipslip-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"v2rayf-extract-{Guid.NewGuid():N}");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("nope");
            }

            Assert.Throws<InvalidOperationException>(() =>
                UpdateDownloadHelper.ExtractZipSafe(zipPath, extractDir));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        }
    }

    [Fact]
    public void ExtractZipSafe_ExtractsNormalEntry()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"v2rayf-zipok-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"v2rayf-extractok-{Guid.NewGuid():N}");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("hello.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hi");
            }

            UpdateDownloadHelper.ExtractZipSafe(zipPath, extractDir);
            Assert.Equal("hi", File.ReadAllText(Path.Combine(extractDir, "hello.txt")));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        }
    }

    [Fact]
    public void EnsureAllowedDownloadUrl_RejectsNonGithub()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UpdateDownloadHelper.EnsureAllowedDownloadUrl("https://evil.example/update.zip"));
    }

    [Fact]
    public void EnsureAllowedDownloadUrl_AllowsGithub()
    {
        UpdateDownloadHelper.EnsureAllowedDownloadUrl(
            "https://github.com/drmikecrypto/v2rayF/releases/download/v1.0.0/v2rayF-win-x64.zip");
    }

    [Fact]
    public void VerifySha256_RejectsMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"v2rayf-hash-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("abc"));
            Assert.Throws<InvalidOperationException>(() =>
                UpdateDownloadHelper.VerifySha256(path, new string('0', 64)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
