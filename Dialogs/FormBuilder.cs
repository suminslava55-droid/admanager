using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdManager.Dialogs;

/// <summary>Небольшой помощник для аккуратных двухколоночных форм в диалогах.</summary>
public static class FormBuilder
{
    public static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < rows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    public static TextBox AddField(Grid grid, int row, int column, string label, string value, string? hint = null)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 4, 8, 4),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, column * 3);

        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = hint
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, column * 3 + 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(box);
        return box;
    }

    public static TextBlock AddSection(Grid grid, int row, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            Margin = new Thickness(0, 12, 0, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        Grid.SetColumnSpan(block, 5);
        grid.Children.Add(block);
        return block;
    }

    public static TextBlock AddHint(Grid grid, int row, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        Grid.SetColumnSpan(block, 5);
        grid.Children.Add(block);
        return block;
    }
}
