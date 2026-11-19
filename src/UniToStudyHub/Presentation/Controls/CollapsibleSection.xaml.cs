using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UniToStudyHub.Presentation.Controls;

public partial class CollapsibleSection : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(null, OnCountChanged));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(CollapsibleSection),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsExpandedChanged));

    public static readonly DependencyProperty SectionContentProperty =
        DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(CollapsibleSection),
            new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Count { get => (string?)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public object? SectionContent { get => GetValue(SectionContentProperty); set => SetValue(SectionContentProperty, value); }

    public CollapsibleSection()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyExpandedState(IsExpanded, animate: false);
    }

    private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CollapsibleSection section || section.CountLabel is null) return;
        section.CountLabel.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CollapsibleSection section)
            section.ApplyExpandedState((bool)e.NewValue, animate: section.IsLoaded);
    }

    private void ApplyExpandedState(bool expanded, bool animate)
    {
        if (SectionContentPresenter is null) return;

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var transform = (TranslateTransform)SectionContentPresenter.RenderTransform;
        var chevron = (RotateTransform)Chevron.RenderTransform;

        SectionContentPresenter.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        chevron.BeginAnimation(RotateTransform.AngleProperty, null);

        if (expanded)
        {
            SectionContentPresenter.Visibility = Visibility.Visible;
            if (!animate)
            {
                SectionContentPresenter.Opacity = 1;
                transform.Y = 0;
                chevron.Angle = 180;
                return;
            }

            SectionContentPresenter.Opacity = 0;
            transform.Y = 5;
            SectionContentPresenter.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(5, 0, duration) { EasingFunction = ease });
            chevron.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 180, duration) { EasingFunction = ease });
        }
        else
        {
            if (!animate)
            {
                SectionContentPresenter.Visibility = Visibility.Collapsed;
                SectionContentPresenter.Opacity = 0;
                transform.Y = 5;
                chevron.Angle = 0;
                return;
            }

            var fade = new DoubleAnimation(SectionContentPresenter.Opacity, 0, duration)
            {
                EasingFunction = ease
            };
            fade.Completed += (_, _) =>
            {
                if (!IsExpanded)
                    SectionContentPresenter.Visibility = Visibility.Collapsed;
            };
            SectionContentPresenter.BeginAnimation(UIElement.OpacityProperty, fade);
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(transform.Y, 5, duration) { EasingFunction = ease });
            chevron.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(180, 0, duration) { EasingFunction = ease });
        }
    }
}
