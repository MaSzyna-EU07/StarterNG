using System.ComponentModel;
using Avalonia.Controls;

namespace StarterNG.Services;

public class LocalizationService : INotifyPropertyChanged
{
   private IResourceProvider _currentProvider = new ResourceDictionary();

    public event PropertyChangedEventHandler? PropertyChanged;


    public string CurrentLanguage;

    public string this[string key]
    {
        get
        {
            if (_currentProvider.TryGetResource(key, null, out var value))
            {
                return value?.ToString() ?? key;
            }
            return key;
        }
    }

    public void SetLanguage(IResourceProvider provider, string langName)
    {
        _currentProvider = provider;
        CurrentLanguage = langName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
    }

 
}