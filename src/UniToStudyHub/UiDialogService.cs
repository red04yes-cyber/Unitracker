using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UniToStudyHub;

/// <summary>
/// Branded, theme-aware replacement for native message boxes.
/// Keeps MessageBoxResult semantics so existing confirmation flows remain unchanged.
/// </summary>
internal static class UiDialogService
{
    private sealed record DialogAction(
        MessageBoxResult Result,
        string Label,
        bool IsPrimary,
        bool IsCancel = false);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image) =>
        ShowCore(owner, message, caption, buttons, image, MessageBoxResult.None);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult) =>
        ShowCore(owner, message, caption, buttons, image, defaultResult);

    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image) =>
        ShowCore(Application.Current?.MainWindow, message, caption, buttons, image, MessageBoxResult.None);

    private static MessageBoxResult ShowCore(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult requestedDefault)
    {
        var actions = GetActions(buttons);
        var fallback = GetDefaultResult(actions, requestedDefault);
        var result = MessageBoxResult.None;

        var dialog = new Window
        {
            Title = caption,
            Width = 560,
            MinWidth = 440,
            MaxWidth = 640,
            MaxHeight = 680,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = false,
            FontFamily = new FontFamily("Segoe UI Variable"),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TranslateTransform(0, 8)
        };

        if (owner is not null && owner.IsLoaded)
            dialog.Owner = owner;

        var isClosing = false;
        void CloseWithResult(MessageBoxResult selected)
        {
            if (isClosing) return;
            isClosing = true;
            result = selected;

            var fadeOut = new DoubleAnimation(
                dialog.Opacity, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                if (dialog.IsVisible)
                    dialog.DialogResult = true;
            };
            dialog.BeginAnimation(Window.OpacityProperty, fadeOut);

            if (dialog.RenderTransform is TranslateTransform translate)
                translate.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(translate.Y, -4, TimeSpan.FromMilliseconds(120)));
        }

        var surface = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            Margin = new Thickness(18),
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 7,
                Opacity = 0.2
            }
        };
        surface.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconTile = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(13),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = GetIconGlyph(image),
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var iconForegroundKey = image is MessageBoxImage.Warning or MessageBoxImage.Error
            ? "WarningBrush"
            : "AccentBrush";
        var iconBackgroundKey = image is MessageBoxImage.Warning or MessageBoxImage.Error
            ? "WarningSoftBrush"
            : "AccentSoftBrush";
        iconTile.SetResourceReference(Border.BackgroundProperty, iconBackgroundKey);
        ((TextBlock)iconTile.Child).SetResourceReference(TextBlock.ForegroundProperty, iconForegroundKey);
        header.Children.Add(iconTile);

        var heading = new StackPanel { Margin = new Thickness(16, 1, 10, 0) };
        heading.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 410
        });
        var intro = new TextBlock
        {
            Text = GetImageLabel(image),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0)
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, iconForegroundKey);
        heading.Children.Add(intro);

        var body = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 11, 0, 0),
            MaxWidth = 430
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var bodyScroller = new ScrollViewer
        {
            Content = body,
            MaxHeight = 360,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        heading.Children.Add(bodyScroller);
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var closeButton = new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            MinHeight = 30,
            Padding = new Thickness(0),
            FontSize = 19,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Chiudi"
        };
        closeButton.SetResourceReference(FrameworkElement.StyleProperty, "QuietButtonStyle");
        closeButton.Click += (_, _) =>
        {
            CloseWithResult(GetEscapeResult(actions, fallback));
        };
        Grid.SetColumn(closeButton, 2);
        header.Children.Add(closeButton);
        layout.Children.Add(header);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 22, 0, 16)
        };
        separator.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        Grid.SetRow(separator, 1);
        layout.Children.Add(separator);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = action.Label,
                MinWidth = action.Label.Length > 10 ? 112 : 88,
                Height = 40,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = action.Result == fallback
            };

            var styleKey = action.IsPrimary &&
                           (image is MessageBoxImage.Warning or MessageBoxImage.Error) &&
                           action.Result == MessageBoxResult.Yes
                ? "DangerButtonStyle"
                : action.IsPrimary
                    ? null
                    : "SecondaryButtonStyle";

            if (styleKey is not null)
                button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);

            button.Click += (_, _) =>
            {
                CloseWithResult(action.Result);
            };
            actionPanel.Children.Add(button);
        }

        Grid.SetRow(actionPanel, 2);
        layout.Children.Add(actionPanel);
        surface.Child = layout;
        dialog.Content = surface;

        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            CloseWithResult(GetEscapeResult(actions, fallback));
            args.Handled = true;
        };
        dialog.Closing += (_, _) =>
        {
            if (!isClosing)
                result = GetEscapeResult(actions, fallback);
        };
        dialog.Loaded += (_, _) =>
        {
            dialog.BeginAnimation(Window.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
            if (dialog.RenderTransform is TranslateTransform translate)
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(190)));
        };

        _ = dialog.ShowDialog();
        return result == MessageBoxResult.None ? fallback : result;
    }

    private static IReadOnlyList<DialogAction> GetActions(MessageBoxButton buttons) =>
        buttons switch
        {
            MessageBoxButton.OK => new[]
            {
                new DialogAction(MessageBoxResult.OK, "Va bene", true)
            },
            MessageBoxButton.OKCancel => new[]
            {
                new DialogAction(MessageBoxResult.Cancel, "Annulla", false, true),
                new DialogAction(MessageBoxResult.OK, "Conferma", true)
            },
            MessageBoxButton.YesNo => new[]
            {
                new DialogAction(MessageBoxResult.No, "No", false),
                new DialogAction(MessageBoxResult.Yes, "Sì", true)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                new DialogAction(MessageBoxResult.Cancel, "Annulla", false, true),
                new DialogAction(MessageBoxResult.No, "No", false),
                new DialogAction(MessageBoxResult.Yes, "Sì", true)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, "Tipo di pulsanti non supportato.")
        };

    private static MessageBoxResult GetDefaultResult(
        IReadOnlyList<DialogAction> actions,
        MessageBoxResult requested)
    {
        if (requested != MessageBoxResult.None && actions.Any(x => x.Result == requested))
            return requested;

        // Preserve the native MessageBox default: the affirmative/first button.
        return actions.Last().Result;
    }

    private static MessageBoxResult GetEscapeResult(
        IReadOnlyList<DialogAction> actions,
        MessageBoxResult fallback) =>
        actions.FirstOrDefault(x => x.IsCancel)?.Result ??
        actions.FirstOrDefault(x => x.Result == MessageBoxResult.No)?.Result ??
        fallback;

    private static string GetIconGlyph(MessageBoxImage image) =>
        image switch
        {
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Error => "×",
            MessageBoxImage.Question => "?",
            _ => "i"
        };

    private static string GetImageLabel(MessageBoxImage image) =>
        image switch
        {
            MessageBoxImage.Warning => "ATTENZIONE",
            MessageBoxImage.Error => "OPERAZIONE NON RIUSCITA",
            MessageBoxImage.Question => "CONFERMA RICHIESTA",
            _ => "INFORMAZIONE"
        };
}
