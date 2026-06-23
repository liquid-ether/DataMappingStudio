using System;
using System.Windows;

namespace App.Desktop;

/// <summary>The Blazor Hybrid shell window: a single <c>BlazorWebView</c> hosting the App.UI root component.</summary>
public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        webView.Services = services; // HostPage (wwwroot/index.html) is set in XAML, relative to the app dir
    }
}
