using CXPost.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CXPost.Services;

public class ConfigService : IConfigService
{
    private readonly string _configPath;

    public ConfigService(string configDir)
    {
        _configPath = Path.Combine(configDir, "config.yaml");
    }

    public CXPostConfig Load()
    {
        if (!File.Exists(_configPath))
            return new CXPostConfig();

        var yaml = File.ReadAllText(_configPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<CXPostConfig>(yaml) ?? new CXPostConfig();
    }

    public void Save(CXPostConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        File.WriteAllText(_configPath, serializer.Serialize(config));
    }
}
