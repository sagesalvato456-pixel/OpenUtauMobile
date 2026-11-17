using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenLoidFlame.ViewModels;

namespace OpenLoidFlame.Controls;

public partial class ExpressionsPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    public ExpressionsPopup()
    {
        InitializeComponent();
    }

    private void AddClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExpressionsPopupViewModel vm || sender is not Button button) return;
        vm.Add();
        if (vm.IsTrackOverride) button.ContextMenu?.Open(button);
    }
}