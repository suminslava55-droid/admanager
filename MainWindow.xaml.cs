using System.Windows;
using System.Windows.Controls;
using AdManager.Common;
using AdManager.Models;
using AdManager.ViewModels;

namespace AdManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OuTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AdOu ou && ViewModel is { } vm)
        {
            vm.SelectOuCommand.Execute(ou);
        }
    }

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.SetSelectedUsers(UsersGrid.SelectedItems.OfType<AdUser>());
    }

    private void GroupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.SetSelectedGroups(GroupsGrid.SelectedItems.OfType<AdGroup>());
    }

    private void KpiScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroll && scroll.ScrollableWidth > 0)
        {
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
