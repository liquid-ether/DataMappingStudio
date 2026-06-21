using App.Application.Abstractions;

namespace App.Infrastructure.Remote.Formats;

/// <summary>Resolves a remote format by its config name; Parquet is the default (Architecture §6a).</summary>
public sealed class RemoteFormatProvider : IRemoteFormatProvider
{
    private readonly Dictionary<string, IRemoteFormat> _byName;

    public RemoteFormatProvider(IEnumerable<IRemoteFormat> formats)
    {
        _byName = formats.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        Default = _byName.GetValueOrDefault("parquet") ?? _byName.Values.First();
    }

    public IRemoteFormat Default { get; }

    public IRemoteFormat Resolve(string name)
        => _byName.TryGetValue(name, out IRemoteFormat? format)
            ? format
            : throw new ArgumentException($"Unknown remote format '{name}'. Known: {string.Join(", ", _byName.Keys)}.");
}
