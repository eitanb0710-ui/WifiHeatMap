using System.Windows;

namespace WifiHeatMap1
{
    public partial class DashboardWindow : Window
    {
        public DashboardWindow()
        {
            InitializeComponent();

            // Wire up UI events
            BtnLogout.Click += BtnLogout_Click;
        }

        // Handles logging out and returning to the Login screen
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            MainWindow loginWindow = new MainWindow();
            loginWindow.Show();
            this.Close(); // Closes DashboardWindow
        }
    }
}