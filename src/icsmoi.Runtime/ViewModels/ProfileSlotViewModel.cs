using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using icsmoi.Models;

namespace icsmoi.Runtime.ViewModels;

/// <summary>
/// One of the Runtime's profile slots: an empty slot, or a profile file (loaded, or failed to load).
/// Its commands just forward to the owning <see cref="StatusWindowViewModel"/>, which holds all the state
/// that spans slots (which one is active, the engine, the settings file).
/// </summary>
public sealed partial class ProfileSlotViewModel : ObservableObject
{
    private readonly StatusWindowViewModel _owner;

    public ProfileSlotViewModel(StatusWindowViewModel owner, int index)
    {
        _owner = owner;
        Index = index;
    }

    /// <summary>Zero-based position.</summary>
    public int Index { get; }

    /// <summary>The loaded profile, or <c>null</c> for an empty slot / a file that couldn't be read.</summary>
    public FfbProfile? Profile { get; private set; }

    /// <summary>Last-write time and length of the file when it was last read — the poll compares against this.</summary>
    public (DateTime LastWriteUtc, long Length)? Stamp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(FileName), nameof(Name), nameof(Details))]
    private string? _filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(Details))]
    private string? _loadError;

    [ObservableProperty]
    private bool _isActive;

    public bool IsEmpty => FilePath is null;

    public bool HasError => LoadError is not null;

    /// <summary>The profile's own name; falls back to the file name while it hasn't loaded.</summary>
    public string Name => Profile?.ProfileName is { Length: > 0 } name ? name : FileName;

    /// <summary>File name without the <c>.icsmoi.json</c> suffix.</summary>
    public string FileName
    {
        get
        {
            if (FilePath is null) return "";
            var file = Path.GetFileName(FilePath);
            const string suffix = ".icsmoi.json";
            return file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? file[..^suffix.Length] : file;
        }
    }

    /// <summary>The muted second line: the load error if there is one, otherwise the file and its size.</summary>
    public string Details
    {
        get
        {
            if (LoadError is not null) return LoadError;
            if (Profile is null) return "";
            var nodes = Profile.Nodes.Count;
            return $"{Path.GetFileName(FilePath)} · {nodes} node{(nodes == 1 ? "" : "s")}";
        }
    }

    /// <summary>Sets the loaded profile (or <c>null</c> plus an error) and refreshes everything derived from it.</summary>
    public void SetLoaded(FfbProfile? profile, string? error)
    {
        Profile = profile;
        LoadError = error;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(IsLoaded));
    }

    public bool IsLoaded => Profile is not null;

    [RelayCommand]
    private void Select() => _owner.SelectSlot(this);

    [RelayCommand]
    private void Edit() => _owner.EditSlot(this);

    [RelayCommand]
    private void Load() => _owner.RequestLoadInto(this);

    [RelayCommand]
    private void Clear() => _owner.ClearSlot(this);
}
