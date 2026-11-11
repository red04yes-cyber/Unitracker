using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class AcademyView : UserControl
{
    public event RoutedEventHandler? ImportPdf_ClickRequested;
    private void ImportPdf_Click(object sender, RoutedEventArgs e) => ImportPdf_ClickRequested?.Invoke(sender, e);

    public AcademyView()
    {
        InitializeComponent();
    }
}
