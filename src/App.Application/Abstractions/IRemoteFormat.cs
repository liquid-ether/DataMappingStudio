using App.Domain.Data;

namespace App.Application.Abstractions;

/// <summary>
/// Pluggable remote file format (Architecture §6a). Both per-writer change logs and materialized
/// snapshots are written through this same abstraction, selected by the <c>RemoteFormat</c> config
/// value. Implementations: Parquet (default), CSV, Excel.
/// </summary>
public interface IRemoteFormat
{
    /// <summary>Config name: <c>parquet</c> | <c>csv</c> | <c>excel</c>.</summary>
    string Name { get; }

    /// <summary>File extension including the dot, e.g. <c>.parquet</c>.</summary>
    string Extension { get; }

    void Write(string path, RemoteTable table);

    RemoteTable Read(string path);
}

/// <summary>Resolves the configured <see cref="IRemoteFormat"/> by name.</summary>
public interface IRemoteFormatProvider
{
    IRemoteFormat Resolve(string name);

    IRemoteFormat Default { get; }
}
