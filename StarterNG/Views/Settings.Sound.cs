using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using StarterNG.Application;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Views;

// The OpenAL Soft options of the sound category. The lists of devices, HRTF data sets and
// resamplers come from the simulator's own OpenAL library, read in the background; until then,
// and where the library cannot say, a list holds the library's choice and the value of the file.
public partial class Settings
{
    // eu07.ini is written in code page 1250: a name it cannot hold (a device named in Cyrillic, say)
    // would reach the simulator as question marks, so such a name is not offered.
    private static readonly Encoding StrictCodePage1250 =
        Encoding.GetEncoding(LegacyText.CodePage1250.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private async void ReadAudioChoices()
    {
        string root = AppServices.Current.Paths.Root;
        AudioChoices choices = await Task.Run(() => OpenAlProbe.Read(root));
        // Adding items leaves the selection alone, so this marks nothing as changed.
        AddNames(AudioDeviceCb, choices.Devices, FitsTheFile);
        // The simulator finds a device named in code page 1250 too, but an HRTF data set or a
        // resampler only by the library's own spelling, so of those only plain ASCII is offered.
        AddNames(AudioHrtfProfileCb, choices.HrtfProfiles, PlainAscii);
        AddNames(AudioResamplerCb, choices.Resamplers, PlainAscii);
    }

    private static void AddNames(ComboBox cb, IReadOnlyList<string> names, Func<string, bool> offered)
    {
        foreach (string name in names)
            if (name.Length > 0 && offered(name) && IndexOfTag(cb, name) < 0)
                cb.Items.Add(new ComboBoxItem { Tag = name, Content = name });
    }

    private static bool PlainAscii(string name) => Ascii.IsValid(name);

    private static bool FitsTheFile(string name)
    {
        try
        {
            StrictCodePage1250.GetByteCount(name);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private void ShowSound(SimulatorSettings s)
    {
        SelectOrAddTag(AudioDeviceCb, s.AudioDevice, s.AudioDevice);
        SelectOrAddTag(AudioOutputModeCb, s.AudioOutputMode, s.AudioOutputMode);
        SelectOrAddTag(AudioHrtfCb, s.AudioHrtf, s.AudioHrtf);
        SelectOrAddTag(AudioHrtfProfileCb, s.AudioHrtfProfile, s.AudioHrtfProfile);
        SelectOrAddTag(AudioResamplerCb, s.AudioResampler, s.AudioResampler);
        SelectOrAddTag(AudioLimiterCb, s.AudioLimiter, s.AudioLimiter);
        string frequency = s.AudioFrequency.ToString(Invariant);
        SelectOrAddTag(AudioFrequencyCb, frequency, frequency + " Hz");
        AudioDopplerSlider.Value = s.AudioDoppler;
        AudioLowpassCb.IsChecked = s.AudioLowpass;
        AudioAirAbsorptionSlider.Value = s.AudioAirAbsorption;
    }

    private void CaptureSound(SimulatorSettings s)
    {
        s.AudioDevice = SelectedTag(AudioDeviceCb) ?? "";
        s.AudioOutputMode = SelectedTag(AudioOutputModeCb) ?? "auto";
        s.AudioHrtf = SelectedTag(AudioHrtfCb) ?? "auto";
        s.AudioHrtfProfile = SelectedTag(AudioHrtfProfileCb) ?? "";
        s.AudioResampler = SelectedTag(AudioResamplerCb) ?? "";
        s.AudioLimiter = SelectedTag(AudioLimiterCb) ?? "auto";
        s.AudioFrequency = TagInt(AudioFrequencyCb, 0);
        // A value the slider can't show (past its end, between its ticks) is kept by the
        // shown-unlike bookkeeping of the page until the slider is moved.
        s.AudioDoppler = AudioDopplerSlider.Value;
        s.AudioLowpass = IsChecked(AudioLowpassCb);
        s.AudioAirAbsorption = AudioAirAbsorptionSlider.Value;
    }
}
