using System;
using System.Text.Json.Serialization;
using Avalonia;
using NodifyM.Avalonia.ViewModelBase;

namespace icsmooi.Models;

/// <summary>
/// A single connector pin on a node.
/// Extends ConnectorViewModelBase to satisfy NodifyM.Avalonia's connection machinery.
/// </summary>
public partial class PinViewModel : ConnectorViewModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shadows base Anchor with [JsonIgnore] — Anchor is runtime-only, never persisted.</summary>
    [JsonIgnore]
    public new Point Anchor
    {
        get => base.Anchor;
        set => base.Anchor = value;
    }

    /// <summary>Convenience bool view of the Flow enum; set once at construction via init.</summary>
    [JsonIgnore]
    public bool IsInput
    {
        get => Flow == ConnectorFlow.Input;
        init => Flow = value ? ConnectorFlow.Input : ConnectorFlow.Output;
    }
}
