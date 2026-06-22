using System;
using System.Windows;

namespace App.Desktop;

/// <summary>The Blazor Hybrid shell window: a single <c>BlazorWebView</c> hosting the App.UI root component.</summary>
public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services, string hostPagePath)
    {
        InitializeComponent();
        webView.HostPage = hostPagePath; // extracted from the embedded resource at startup
        webView.Services = services;
    }
}
