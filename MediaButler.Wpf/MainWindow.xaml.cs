using System.Windows;

namespace MediaButler.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        Web.Services = services;
    }
}
