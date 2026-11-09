using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CP.AnimationRx.Reactive;

namespace UniToStudyHub;

/// <summary>
/// Replaces native MessageBox windows with a consistent, theme-aware WPF dialog.
/// The API mirrors MessageBox so existing confirmation semantics remain intact.
/// </summary>
public static class UiDialogService
{
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        string? details = null)
    {
        var dialog = new PremiumDialogWindow(message, caption, button, icon, defaultResult, details);
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    public static MessageBoxResult Show(
        string message,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        string? details = null) =>
        Show(Application.Current?.MainWindow, message, caption, button, icon, defaultResult, details);

    private sealed record DialogButton(
        string Label,
        MessageBoxResult Result,
        string StyleKey,
        bool IsDefault);

    private sealed class PremiumDialogWindow : Window
    {
        private readonly Border _surface;
        private readonly MessageBoxResult _escapeResult;
        private bool _closing;

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public PremiumDialogWindow(
            string message,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            string? details)
        {
            Title = string.IsNullOrWhiteSpace(caption) ? "UniTo StudyHub" : caption;
            Width = 520;
            MinWidth = 420;
            MaxWidth = 640;
            MinHeight = 190;
            MaxHeight = 620;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = true;
            Focusable = true;
            FontFamily = new FontFamily("Segoe UI Variable");
            Foreground = BrushResource("InkBrush");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _escapeResult = buttons switch
            {
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.OK
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconSurface = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(13),
                Background = IconBackground(icon),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconSurface.Child = new TextBlock
            {
                Text = IconGlyph(icon),
                Foreground = IconForeground(icon),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(iconSurface);

            var titleStack = new StackPanel
            {
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleStack, 1);
            titleStack.Children.Add(new TextBlock
            {
                Text = Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushResource("InkBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = IconEyebrow(icon),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushResource("MutedBrush"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            header.Children.Add(titleStack);
            layout.Children.Add(header);

            var messageViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 18, 0, 0),
                MaxHeight = 290,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    LineHeight = 20,
                    Foreground = BrushResource("InkBrush"),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetRow(messageViewer, 1);
            layout.Children.Add(messageViewer);

            if (!string.IsNullOrWhiteSpace(details))
            {
                var detailsPanel = new Expander
                {
                    Header = "Dettagli tecnici",
                    Margin = new Thickness(0, 12, 0, 0),
                    Content = new ScrollViewer
                    {
                        MaxHeight = 150,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = details,
                            FontSize = 10,
                            Foreground = BrushResource("MutedBrush"),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                };
                var detailsContainer = new StackPanel { Children = { detailsPanel } };
                Grid.SetRow(detailsContainer, 2);
                layout.Children.Add(detailsContainer);
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, string.IsNullOrWhiteSpace(details) ? 22 : 18, 0, 0)
            };
            foreach (var spec in BuildButtons(buttons, icon, defaultResult))
            {
                var actionButton = new Button
                {
                    Content = spec.Label,
                    MinWidth = 88,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(17, 8, 17, 8),
                    IsDefault = spec.IsDefault,
                    Style = (Style)FindResource(spec.StyleKey)
                };
                actionButton.Click += (_, _) => Complete(spec.Result);
                buttonPanel.Children.Add(actionButton);
            }
            Grid.SetRow(buttonPanel, 2);
            layout.Children.Add(buttonPanel);

            _surface = new Border
            {
                Background = BrushResource("ElevatedPanelBrush"),
                BorderBrush = BrushResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(19),
                Padding = new Thickness(24),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    BlurRadius = 30,
                    ShadowDepth = 8,
                    Opacity = 0.22
                },
                Child = layout,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.96, 0.96)
            };
            Content = _surface;

            Loaded += OnLoaded;
            KeyDown += OnKeyDown;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            _surface.Opacity = 0;
            var entrance = new[]
            {
                _surface.OpacityTo(250, 1.0, Ease.ExpoOut),
                _surface.ScaleTo(270, 1.0, 1.0, Ease.ExpoOut, Ease.ExpoOut)
            }.Parallel();
            entrance.Subscribe();
        }

        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape || _closing)
                return;

            args.Handled = true;
            Complete(_escapeResult);
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs args)
        {
            if (!_closing)
                Result = _escapeResult;
        }

        private void Complete(MessageBoxResult result)
        {
            if (_closing)
                return;

            _closing = true;
            Result = result;
            var exit = new[]
            {
                _surface.OpacityTo(145, 0.0, Ease.SineIn),
                _surface.ScaleTo(155, 0.985, 0.985, Ease.SineIn, Ease.SineIn)
            }.Parallel();

            exit.Subscribe(
                _ => { },
                _ => Dispatcher.BeginInvoke(new Action(CloseWithResult)),
                () => Dispatcher.BeginInvoke(new Action(CloseWithResult)));
        }

        private void CloseWithResult()
        {
            if (IsVisible)
                DialogResult = Result;
        }

        private Brush BrushResource(string key) => (Brush)FindResource(key);

        private Brush IconForeground(MessageBoxImage icon) => BrushResource(icon switch
        {
            MessageBoxImage.Error => "DangerBrush",
            MessageBoxImage.Warning => "WarningBrush",
            MessageBoxImage.Information => "InfoBrush",
            MessageBoxImage.Question => "AccentBrush",
            _ => "MutedBrush"
        });

        private Brush IconBackground(MessageBoxImage icon) => BrushResource(icon switch
        {
            MessageBoxImage.Error => "DangerSoftBrush",
            MessageBoxImage.Warning => "WarningSoftBrush",
            MessageBoxImage.Information => "InfoSoftBrush",
            MessageBoxImage.Question => "AccentSoftBrush",
            _ => "SubtleBrush"
        });

        private static string IconGlyph(MessageBoxImage icon) => icon switch
        {
            MessageBoxImage.Error => "×",
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Information => "i",
            MessageBoxImage.Question => "?",
            _ => "•"
        };

        private static string IconEyebrow(MessageBoxImage icon) => icon switch
        {
            MessageBoxImage.Error => "OPERAZIONE NON RIUSCITA",
            MessageBoxImage.Warning => "ATTENZIONE",
            MessageBoxImage.Information => "INFORMAZIONE",
            MessageBoxImage.Question => "CONFERMA RICHIESTA",
            _ => "UNITO STUDYHUB"
        };

        private static IReadOnlyList<DialogButton> BuildButtons(
            MessageBoxButton buttons,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
        {
            var destructive = icon == MessageBoxImage.Warning || icon == MessageBoxImage.Error;
            var affirmativeStyle = destructive ? "DangerButtonStyle" : "PrimaryButtonStyle";
            var specs = buttons switch
            {
                MessageBoxButton.OKCancel => new[]
                {
                    new DialogButton("Annulla", MessageBoxResult.Cancel, "QuietButtonStyle", false),
                    new DialogButton("Continua", MessageBoxResult.OK, affirmativeStyle, true)
                },
                MessageBoxButton.YesNo => new[]
                {
                    new DialogButton("No", MessageBoxResult.No, "SecondaryButtonStyle", false),
                    new DialogButton("Sì", MessageBoxResult.Yes, affirmativeStyle, true)
                },
                MessageBoxButton.YesNoCancel => new[]
                {
                    new DialogButton("Annulla", MessageBoxResult.Cancel, "QuietButtonStyle", false),
                    new DialogButton("No", MessageBoxResult.No, "SecondaryButtonStyle", false),
                    new DialogButton("Sì", MessageBoxResult.Yes, affirmativeStyle, true)
                },
                _ => new[]
                {
                    new DialogButton("Va bene", MessageBoxResult.OK, "PrimaryButtonStyle", true)
                }
            };

            if (defaultResult == MessageBoxResult.None)
                return specs;

            var found = specs.Any(spec => spec.Result == defaultResult);
            return found
                ? specs.Select(spec => spec with { IsDefault = spec.Result == defaultResult }).ToArray()
                : specs;
        }
    }
}
