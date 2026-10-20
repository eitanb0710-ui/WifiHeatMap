using Google.Apis.Auth.OAuth2;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WifiHeatMap1
{
    public partial class MainWindow : Window
    {
        private string _currentLang = "EN";
        private const string ServerIp = "127.0.0.1";
        private const int ServerPort = 5000;

        private const string GoogleClientId = "493364826582-rtolus8ijpt1fv36f36lp7j3qeqe6cna.apps.googleusercontent.com";

        public MainWindow()
        {
            InitializeComponent();
            ApplyLanguage("EN");
        }


        private async Task<string> SendMessageToServerAsync(string message)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(ServerIp, ServerPort);

                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream))
                    using (StreamWriter writer = new StreamWriter(stream) { AutoFlush = true })
                    {
                        await writer.WriteLineAsync(message);
                        string? response = await reader.ReadLineAsync();
                        return response ?? "ERROR|EmptyResponse";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginErrorText.Text = "";
            string email = LoginEmailTextBox.Text.Trim();
            string password = LoginPasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                LoginErrorText.Text = _currentLang == "RU" ? "Заполните все поля" : "Please fill in all fields";
                return;
            }

            // Send command string through socket
            string response = await SendMessageToServerAsync($"LOGIN|{email}|{password}");

            if (response == "OK")
            {
                string successMsg = _currentLang == "RU" ? "Успешный вход!" : "Login Successful!";
                MessageBox.Show(successMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (response == "ERROR|UseGoogleAuth")
            {
                LoginErrorText.Text = _currentLang == "RU" ? "Войдите через Google" : "Please log in using Google Auth";
            }
            else
            {
                LoginErrorText.Text = _currentLang == "RU" ? "Неверный логин или пароль" : "Invalid email or password";
            }
        }

        private async void SignUpButton_Click(object sender, RoutedEventArgs e)
        {
            SignUpErrorText.Text = "";
            string name = NameTextBox.Text.Trim();
            string email = SignUpEmailTextBox.Text.Trim();
            string password = SignUpPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SignUpErrorText.Text = _currentLang == "RU" ? "Заполните все поля" : "Please fill in all fields";
                return;
            }

            if (password != confirmPassword)
            {
                SignUpErrorText.Text = _currentLang == "RU" ? "Пароли не совпадают" : "Passwords do not match";
                return;
            }

            // Send command string through socket
            string response = await SendMessageToServerAsync($"SIGNUP|{name}|{email}|{password}");

            if (response == "OK")
            {
                string successMsg = _currentLang == "RU" ? "Регистрация успешна! Войдите в аккаунт." : "Registration successful! Please sign in.";
                MessageBox.Show(successMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowLogin_Click(sender, e);
            }
            else if (response == "ERROR|EmailAlreadyExists")
            {
                SignUpErrorText.Text = _currentLang == "RU" ? "Email уже зарегистрирован" : "Email is already registered";
            }
            else
            {
                SignUpErrorText.Text = _currentLang == "RU" ? "Ошибка регистрации" : "Registration failed";
            }
        }

        private async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    new ClientSecrets { ClientId = GoogleClientId },
                    new[] { "email", "profile" },
                    "user",
                    CancellationToken.None
                );

                string idToken = credential.Token.IdToken;

                if (!string.IsNullOrEmpty(idToken))
                {
                    // Send command string through socket
                    string response = await SendMessageToServerAsync($"GOOGLE_AUTH|{idToken}");

                    if (response == "OK")
                    {
                        string successMsg = _currentLang == "RU" ? "Успешный вход через Google!" : "Google Sign-In Successful!";
                        MessageBox.Show(successMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        string failMsg = _currentLang == "RU" ? "Ошибка авторизации Google на сервере." : "Google Sign-In failed on server.";
                        MessageBox.Show($"{failMsg}\nResponse: {response}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Google Auth Error: {ex.Message}");
            }
        }

        private void SwitchLang_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string lang)
            {
                _currentLang = lang;
                ApplyLanguage(_currentLang);
            }
        }

        private void ApplyLanguage(string lang)
        {
            if (lang == "RU")
            {
                LoginTitleText.Text = "Вход";
                LoginSubtext.Text = "Войдите в свой аккаунт WiFi Heat Map";
                GoogleLoginText.Text = "Войти через Google";
                LoginOrText.Text = "или";
                LoginEmailLabel.Text = "Электронная почта";
                LoginPasswordLabel.Text = "Пароль";
                LoginBtn.Content = "Продолжить";
                NoAccountText.Text = "Нет аккаунта?";
                ShowSignUpBtn.Content = "Создать аккаунт";

                SignUpTitleText.Text = "Создать аккаунт";
                SignUpSubtext.Text = "Создайте аккаунт WiFi Heat Map";
                GoogleSignUpText.Text = "Зарегистрироваться через Google";
                SignUpOrText.Text = "или";
                NameLabel.Text = "Имя";
                SignUpEmailLabel.Text = "Электронная почта";
                SignUpPasswordLabel.Text = "Пароль";
                ConfirmPasswordLabel.Text = "Подтвердите пароль";
                SignUpBtn.Content = "Создать аккаунт";
                AlreadyAccountText.Text = "Уже есть аккаунт?";
                ShowLoginBtn.Content = "Войти";
            }
            else
            {
                LoginTitleText.Text = "Sign in";
                LoginSubtext.Text = "Log in to your WiFi Heat Map account";
                GoogleLoginText.Text = "Sign in with Google";
                LoginOrText.Text = "or";
                LoginEmailLabel.Text = "Email address";
                LoginPasswordLabel.Text = "Password";
                LoginBtn.Content = "Continue";
                NoAccountText.Text = "Don't have an account?";
                ShowSignUpBtn.Content = "Create account";

                SignUpTitleText.Text = "Create account";
                SignUpSubtext.Text = "Create your WiFi Heat Map account";
                GoogleSignUpText.Text = "Sign up with Google";
                SignUpOrText.Text = "or";
                NameLabel.Text = "Name";
                SignUpEmailLabel.Text = "Email address";
                SignUpPasswordLabel.Text = "Password";
                ConfirmPasswordLabel.Text = "Confirm Password";
                SignUpBtn.Content = "Create account";
                AlreadyAccountText.Text = "Already have an account?";
                ShowLoginBtn.Content = "Sign in";
            }
        }

        private void ShowSignUp_Click(object sender, RoutedEventArgs e)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            SignUpPanel.Visibility = Visibility.Visible;
            LoginErrorText.Text = "";
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e)
        {
            SignUpPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            SignUpErrorText.Text = "";
        }
    }
}