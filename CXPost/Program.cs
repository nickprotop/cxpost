using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using CXPost.Data;
using CXPost.Services;
using CXPost.UI;

namespace CXPost;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            if (width <= 0 || height <= 0)
            {
                Console.Error.WriteLine("CXPost requires an interactive terminal.");
                return 1;
            }
        }
        catch
        {
            Console.Error.WriteLine("CXPost requires an interactive terminal.");
            return 1;
        }

        // Resolve OS-standard paths
        var configDir = GetConfigDir();
        var dataDir = GetDataDir();
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);

        // Open SQLite databases
        var mailDbPath = Path.Combine(dataDir, "mail.db");
        var contactsDbPath = Path.Combine(dataDir, "contacts.db");

        var mailConn = new SqliteConnection($"Data Source={mailDbPath}");
        mailConn.Open();
        DatabaseMigrations.Apply(mailConn);

        var contactsConn = new SqliteConnection($"Data Source={contactsDbPath}");
        contactsConn.Open();
        DatabaseMigrations.ApplyContacts(contactsConn);

        try
        {
            // DI container
            var services = new ServiceCollection();

            // Window system
            var ws = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer));
            services.AddSingleton(ws);

            // Repositories
            services.AddSingleton(new MailRepository(mailConn));
            services.AddSingleton(new ContactRepository(contactsConn));

            // Services
            services.AddSingleton<IConfigService>(new ConfigService(configDir));
            services.AddSingleton<ICredentialService, CredentialService>();
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<IContactsService, ContactsService>();
            services.AddSingleton<IImapService, ImapService>();
            services.AddSingleton<ISmtpService, SmtpService>();
            services.AddSingleton<ThreadingService>();

            // App
            services.AddSingleton<CXPostApp>();

            var provider = services.BuildServiceProvider();

            using var app = provider.GetRequiredService<CXPostApp>();
            app.Run();
        }
        finally
        {
            mailConn.Dispose();
            contactsConn.Dispose();
        }

        return 0;
    }

    private static string GetConfigDir()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CXPost");
    }

    private static string GetDataDir()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CXPost");
    }
}
