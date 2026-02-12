using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;

namespace StarterNG.Views;

public partial class Settings : UserControl
{
    public Settings()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            TextureResolutionSlider_OnValueChanged(null, null);
            CabTextureResolutionSlider_OnValueChanged(null, null);
            shaderResolutionSlider_OnValueChanged(null, null);
        };
        
        ChangeLanguageCb.SelectedIndex = App.Loc.CurrentLanguage switch
        {
            "Polski" => 0,
            _ => 1
        };
    }

    private void TextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (texResolution is null || textureResolutionSlider is null)
            return;

        int resolution = 1 << (int)textureResolutionSlider.Value;
        texResolution.Text = $"{resolution} px";
    }

    private void CabTextureResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (cabTexResolution is null || cabTextureResolutionSlider is null)
            return;

        int resolution = 1 << (int)cabTextureResolutionSlider.Value;
        cabTexResolution.Text = $"{resolution} px";
    }

    private void shaderResolutionSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (shaderResolution is null || shaderResolutionSlider is null)
            return;

        int resolution = 1 << (int)shaderResolutionSlider.Value;
        shaderResolution.Text = $"{resolution} px";
    }

    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        
        if (sender is not ComboBox { SelectedItem: ComboBoxItem item }) return;
        if (!item.IsKeyboardFocusWithin) return;
        var lang = item.Content?.ToString();
        if (string.IsNullOrEmpty(lang)) return;
        
        var langDict = new ResourceInclude(new Uri("avares://StarterNG/"))
        {
            Source = new Uri($"Assets/Langs/{lang}.axaml", UriKind.Relative)
        };

        var currentLangDict = App.Current.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Langs/"));

        if (currentLangDict != null)
        {
            App.Current.Resources.MergedDictionaries.Remove(currentLangDict);
        }
        App.Current.Resources.MergedDictionaries.Add(langDict);

        App.Loc.SetLanguage(langDict, lang);
    }
}