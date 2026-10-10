using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Models;
using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class AttachmentItemViewModel : ObservableObject
{
    private static readonly ResourceLoader Resources = new();
    private readonly Func<AttachmentItemViewModel, Task> _open;
    private readonly Func<AttachmentItemViewModel, Task> _remove;

    internal AttachmentItemViewModel(
        TaskAttachment model,
        Func<AttachmentItemViewModel, Task> open,
        Func<AttachmentItemViewModel, Task> remove)
    {
        Model = model;
        _open = open;
        _remove = remove;
        SizeText = FormatSize(model.SizeBytes);
    }

    internal TaskAttachment Model { get; }

    public string FileName => Model.FileName;

    public string SizeText { get; }

    public string OpenAutomationId => $"Todo.Detail.AttachmentOpen.{Model.Id}";

    public string RemoveAutomationId => $"Todo.Detail.AttachmentRemove.{Model.Id}";

    [RelayCommand]
    private Task OpenAsync()
    {
        return _open(this);
    }

    [RelayCommand]
    private Task RemoveAsync()
    {
        return _remove(this);
    }

    private static string FormatSize(long sizeBytes)
    {
        (string key, double value) = sizeBytes switch
        {
            < 1024 => ("Detail_SizeBytes", sizeBytes),
            < 1024 * 1024 => ("Detail_SizeKilobytes", sizeBytes / 1024d),
            _ => ("Detail_SizeMegabytes", sizeBytes / (1024d * 1024d)),
        };
        return string.Format(
            DisplayCulture.Current,
            Resources.GetString(key),
            value);
    }
}
