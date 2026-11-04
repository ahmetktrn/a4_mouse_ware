using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseWare.Core;

public sealed record ConfigLoadResult(AppConfig Config, string? Error);

/// <summary>Reads and writes the JSON config file.</summary>
public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public static AppConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
            ?? throw new JsonException("Config file is empty.");
        config.Normalize();
        return config;
    }

    /// <summary>A deep, independent copy (the hook thread gets its own snapshot).</summary>
    public static AppConfig Clone(AppConfig config) => Deserialize(Serialize(config));

    /// <summary>
    /// Loads the config. A missing file gives the defaults. A broken file is kept as
    /// "*.bad.json" so the user's work is not lost, and the defaults are used.
    /// </summary>
    public static ConfigLoadResult Load(string path)
    {
        if (!File.Exists(path))
            return new ConfigLoadResult(AppConfig.CreateDefault(), null);

        try
        {
            return new ConfigLoadResult(Deserialize(File.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException)
        {
            try
            {
                File.Copy(path, Path.ChangeExtension(path, ".bad.json"), overwrite: true);
            }
            catch (IOException)
            {
                // Best effort only.
            }
            return new ConfigLoadResult(AppConfig.CreateDefault(), ex.Message);
        }
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written config.</summary>
    public static void Save(string path, AppConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(config));
        File.Move(temp, path, overwrite: true);
    }
}
