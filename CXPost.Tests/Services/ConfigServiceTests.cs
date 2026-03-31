using CXPost.Models;
using CXPost.Services;

namespace CXPost.Tests.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cxpost-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new ConfigService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_returns_empty_config_when_no_file_exists()
    {
        var config = _service.Load();

        Assert.NotNull(config);
        Assert.Empty(config.Accounts);
        Assert.Equal("classic", config.Layout);
    }

    [Fact]
    public void Save_and_Load_roundtrips_config()
    {
        var config = new CXPostConfig
        {
            Layout = "vertical",
            SyncIntervalSeconds = 120,
            Notifications = false,
            Accounts =
            [
                new Account
                {
                    Name = "Test",
                    Email = "test@example.com",
                    ImapHost = "imap.example.com",
                    ImapPort = 993,
                    SmtpHost = "smtp.example.com",
                    SmtpPort = 587,
                    Signature = "-- \nSent from CXPost"
                }
            ]
        };

        _service.Save(config);
        var loaded = _service.Load();

        Assert.Equal("vertical", loaded.Layout);
        Assert.Equal(120, loaded.SyncIntervalSeconds);
        Assert.False(loaded.Notifications);
        Assert.Single(loaded.Accounts);
        Assert.Equal("test@example.com", loaded.Accounts[0].Email);
        Assert.Equal("imap.example.com", loaded.Accounts[0].ImapHost);
    }
}
