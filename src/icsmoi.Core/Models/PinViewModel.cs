using System;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace icsmoi.Models;

/// <summary>
/// A single connector pin on a node.
/// Extends ConnectorViewModelBase to satisfy NodifyM.Avalonia's connection machinery.
/// </summary>
public partial class PinViewModel : ConnectorViewModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Constant used for an input pin while nothing is wired into it —
    /// <c>GraphEvaluator</c> seeds every input pin with this before wire values
    /// overwrite it, and node templates that expose an input field show it only
    /// while <see cref="ConnectorViewModelBase.IsConnected"/> is false. Persisted with
    /// the pin (pins are restored from JSON, not rebuilt by the node constructor).
    /// </summary>
    [ObservableProperty]
    private double _value;

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
