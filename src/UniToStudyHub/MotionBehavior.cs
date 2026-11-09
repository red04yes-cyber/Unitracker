using System.Runtime.CompilerServices;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CP.AnimationRx;

namespace UniToStudyHub;

/// <summary>
/// Small WPF attached behavior for coordinated, cancellable hover and entrance motion.
/// It deliberately stays in the presentation layer and does not involve ViewModels.
/// </summary>
public static class MotionBehavior
{
    private sealed class ElementMotionState
    {
        public SerialDisposable HoverAnimation { get; } = new();
        public SerialDisposable EntranceAnimation { get; } = new();
        public bool HoverHandlersAttached { get; set; }
        public bool EntranceHandlersAttached { get; set; }
        public bool EntranceStarted { get; set; }
    }

    private static readonly ConditionalWeakTable<FrameworkElement, ElementMotionState> States = new();

    public static readonly DependencyProperty HoverLiftEnabledProperty =
        DependencyProperty.RegisterAttached(
            "HoverLiftEnabled",
            typeof(bool),
            typeof(MotionBehavior),
            new PropertyMetadata(false, OnHoverLiftEnabledChanged));

    public static readonly DependencyProperty EntranceEnabledProperty =
        DependencyProperty.RegisterAttached(
            "EntranceEnabled",
            typeof(bool),
            typeof(MotionBehavior),
            new PropertyMetadata(false, OnEntranceEnabledChanged));

    public static void SetHoverLiftEnabled(DependencyObject element, bool value) =>
        element.SetValue(HoverLiftEnabledProperty, value);

    public static bool GetHoverLiftEnabled(DependencyObject element) =>
        (bool)element.GetValue(HoverLiftEnabledProperty);

    public static void SetEntranceEnabled(DependencyObject element, bool value) =>
        element.SetValue(EntranceEnabledProperty, value);

    public static bool GetEntranceEnabled(DependencyObject element) =>
        (bool)element.GetValue(EntranceEnabledProperty);

    private static void OnHoverLiftEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element)
            return;

        var state = States.GetOrCreateValue(element);
        if ((bool)args.NewValue)
        {
            if (state.HoverHandlersAttached)
                return;

            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.Unloaded += OnUnloaded;
            state.HoverHandlersAttached = true;
        }
        else if (state.HoverHandlersAttached)
        {
            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            element.Unloaded -= OnUnloaded;
            state.HoverAnimation.Dispose();
            state.HoverHandlersAttached = false;
        }
    }

    private static void OnEntranceEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element)
            return;

        var state = States.GetOrCreateValue(element);
        if ((bool)args.NewValue)
        {
            if (state.EntranceHandlersAttached)
                return;

            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            state.EntranceHandlersAttached = true;
        }
        else if (state.EntranceHandlersAttached)
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            state.EntranceAnimation.Dispose();
            state.EntranceHandlersAttached = false;
            state.EntranceStarted = false;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is not FrameworkElement element)
            return;

        var state = States.GetOrCreateValue(element);
        state.HoverAnimation.Disposable = element
            .ScaleTo(180, 1.025, 1.025, Ease.SineOut, Ease.SineOut)
            .Subscribe();
    }

    private static void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is not FrameworkElement element)
            return;

        var state = States.GetOrCreateValue(element);
        state.HoverAnimation.Disposable = element
            .ScaleTo(220, 1.0, 1.0, Ease.SineInOut, Ease.SineInOut)
            .Subscribe();
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element)
            return;

        var state = States.GetOrCreateValue(element);
        if (state.EntranceStarted)
            return;

        state.EntranceStarted = true;
        element.SetCurrentValue(UIElement.OpacityProperty, 0.0);

        if (ReferenceEquals(element.RenderTransform, Transform.Identity))
            element.RenderTransform = new TranslateTransform(0, 10);

        state.EntranceAnimation.Disposable = new[]
        {
            element.OpacityTo(420, 1.0, Ease.SineOut),
            element.TranslateTo(420, 0, 0, Ease.ExpoOut, Ease.ExpoOut)
        }.Parallel().Subscribe();
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element ||
            !States.TryGetValue(element, out var state))
            return;

        state.HoverAnimation.Disposable = Disposable.Empty;
        state.EntranceAnimation.Disposable = Disposable.Empty;
        state.EntranceStarted = false;
    }
}
