using System;
using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? NavigateToSubjectsRequested;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void NavigateToSubjects_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSubjectsRequested?.Invoke(this, EventArgs.Empty);
    }
}
