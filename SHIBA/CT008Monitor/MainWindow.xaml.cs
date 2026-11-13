using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CT008Monitor
{
    public partial class MainWindow : Window
    {
        private readonly MonitorViewModel vm = new MonitorViewModel();
        private readonly DispatcherTimer timer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = vm;

            timer.Interval = TimeSpan.FromMilliseconds(vm.PollIntervalMs);
            timer.Tick += (sender, args) => vm.Poll();
            timer.Start();
            vm.Poll();

            Closing += (sender, args) =>
            {
                timer.Stop();
                vm.Dispose();
            };
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "SQLite database (*.sqlite;*.db)|*.sqlite;*.db|All files (*.*)|*.*";
            dialog.CheckFileExists = false;   // The database may not exist until a CT008 instance starts
            try
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(vm.DatabasePath);
            }
            catch
            {
            }

            if (dialog.ShowDialog(this) == true)
            {
                vm.DatabasePath = dialog.FileName;
                vm.Reconnect();
            }
        }

        private void BtnReconnect_Click(object sender, RoutedEventArgs e)
        {
            vm.Reconnect();
        }

        // The button sits in a grid row, so its DataContext is that row's detector
        private void BtnAggregate_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var row = element == null ? null : element.DataContext as DetectorRow;
            if (row != null)
            {
                vm.StartAggregation(row.Serial);
            }
        }
    }
}
