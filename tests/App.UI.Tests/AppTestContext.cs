using Microsoft.AspNetCore.Components;
using Bunit;

namespace App.UI.Tests;

/// <summary>
/// Base bUnit context for the UI tests. QuickGrid loads a JS module and (when virtualized) drives
/// rendering from JS measurements, neither of which exist in bUnit — so we run JS interop in loose
/// mode and cascade <c>GridVirtualize=false</c> to every rendered component, which makes the grids
/// render all rows synchronously for assertions. Production still virtualizes (the default is true).
/// </summary>
public abstract class AppTestContext : BunitContext
{
    protected AppTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderTree.Add<CascadingValue<bool>>(parameters => parameters
            .Add(p => p.Name, "GridVirtualize")
            .Add(p => p.Value, false)
            .Add(p => p.IsFixed, true));
    }
}
