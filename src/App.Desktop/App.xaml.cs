using System.Configuration;
using System.Data;
using System.Windows;

namespace App.Desktop;

/// <summary>
/// Interaction logic for App.xaml (the Blazor Hybrid desktop shell).
/// Named <c>DesktopApp</c> rather than <c>App</c>: a type named <c>App</c> would collide with the
/// <c>App.*</c> root namespace in WPF-generated code. The base type is fully qualified because the
/// <c>App.Application</c> project makes the unqualified name <c>Application</c> resolve to that
/// namespace inside <c>App.Desktop</c>.
/// </summary>
public partial class DesktopApp : System.Windows.Application
{
}

