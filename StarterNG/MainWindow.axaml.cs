using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SukiUI.Controls; 
namespace StarterNG;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeComponent();
    }
    private void linkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) 
            return;

        if (btn.Tag is not string url || string.IsNullOrWhiteSpace(url))
            return;

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void openLastLogClick(object? sender, RoutedEventArgs e)
    {
        string logPath = "log.txt";
        if (File.Exists(logPath))
        {
            Process.Start(new ProcessStartInfo(logPath)
            {
                UseShellExecute = true
            });
        }
    }
}