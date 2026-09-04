namespace StarterNG.Application.Abstractions;

public interface ILocalizedStrings
{
    string this[string key] { get; }

    string Opt(string key);

    string CurrentLanguage { get; }

    string CurrentLangCode { get; }
}
