using System;
using MouseWare.Core;

namespace MouseWare.App.Services;

/// <summary>Owns the config the UI edits. Every save hands a fresh copy to the input engine.</summary>
internal sealed class ConfigService
{
    public AppConfig Current { get; private set; } = AppConfig.CreateDefault();

    /// <summary>Set when the config file could not be read at start-up.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Raised after every successful save (and after load/replace).</summary>
    public event Action? Changed;

    public void Load()
    {
        var result = ConfigStore.Load(AppPaths.ConfigFile);
        Current = result.Config;
        LoadError = result.Error;
        if (result.Error != null)
            Log.Error("Config could not be read, defaults used: " + result.Error);
        Changed?.Invoke();
    }

    public bool Save()
    {
        try
        {
            ConfigStore.Save(AppPaths.ConfigFile, Current);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Saving config failed", ex);
            Changed?.Invoke(); // still apply in memory
            return false;
        }
    }

    public void Replace(AppConfig config)
    {
        config.Normalize();
        Current = config;
        Save();
    }

    /// <summary>A copy the hook thread can use without locking against UI edits.</summary>
    public AppConfig Snapshot() => ConfigStore.Clone(Current);
}
