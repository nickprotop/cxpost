using CXPost.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CXPost.Services;

public class ConfigService : IConfigService
{
    private readonly string _configPath;
    private CXPostConfig? _cached;

    public ConfigService(string configDir)
    {
        _configPath = Path.Combine(configDir, "config.yaml");
    }

    public CXPostConfig Load()
    {
        if (_cached != null) return _cached;

        if (!File.Exists(_configPath))
        {
            _cached = new CXPostConfig();
            return _cached;
        }

        var yaml = File.ReadAllText(_configPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _cached = deserializer.Deserialize<CXPostConfig>(yaml) ?? new CXPostConfig();
        return _cached;
    }

    public void Save(CXPostConfig config)
    {
        _cached = config;

        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, serializer.Serialize(config));
        File.Move(tempPath, _configPath, overwrite: true);
    }
}
