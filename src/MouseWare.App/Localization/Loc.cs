using System;
using System.ComponentModel;
using System.Globalization;

namespace MouseWare.App.Localization;

/// <summary>
/// Current UI language. XAML binds to the indexer:
/// <c>{Binding [Key], Source={x:Static l:Loc.Instance}}</c>, and refreshes when the language changes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _language = "tr";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (for code-built texts).</summary>
    public event Action? LanguageChanged;

    public string Language
    {
        get => _language;
        set
        {
            var lang = value == "en" ? "en" : "tr";
            if (lang == _language)
                return;
            _language = lang;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    public string this[string key] => Strings.Get(_language, key);

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}
