using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using TextBox = System.Windows.Controls.TextBox;

namespace PaperTodo;

public sealed class TodoTextBox : TextBox
{
    public static readonly DependencyProperty IsDoneProperty =
        DependencyProperty.Register(
            nameof(IsDone),
            typeof(bool),
            typeof(TodoTextBox),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsDone
    {
        get => (bool)GetValue(IsDoneProperty);
        set => SetValue(IsDoneProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!IsDone || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var y = Math.Max(ActualHeight / 2.0, 10);
        var lineColor = Theme.BrightWeakTextBrush is SolidColorBrush solid
            ? Color.FromArgb(205, solid.Color.R, solid.Color.G, solid.Color.B)
            : Color.FromArgb(205, 138, 122, 99);
        var pen = new Pen(new SolidColorBrush(lineColor), 1.35);
        pen.Freeze();

        drawingContext.DrawLine(
            pen,
            new Point(3, y),
            new Point(Math.Max(3, ActualWidth - 3), y));
    }
}
