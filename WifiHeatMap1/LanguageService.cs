using System.Collections.Generic;

namespace WifiHeatMap1
{
    public class LanguageService
    {
        public string CurrentLanguage { get; private set; } = "EN";

        private readonly Dictionary<string, Dictionary<string, string>> translations =
            new Dictionary<string, Dictionary<string, string>>
        {
            {
                "EN",
                new Dictionary<string, string>
                {
                    { "loginTitle", "Welcome Back" },
                    { "loginSubtext", "Log in to your WiFi Heat Map account" },
                    { "emailLabel", "Email" },
                    { "passwordLabel", "Password" },
                    { "loginButton", "Log In" },
                    { "noAccount", "Don't have an account?" },
                    { "signUp", "Sign Up" },

                    { "signUpTitle", "Create Account" },
                    { "signUpSubtext", "Create your WiFi Heat Map account" },
                    { "nameLabel", "Name" },
                    { "confirmPasswordLabel", "Confirm Password" },
                    { "createAccountButton", "Create Account" },
                    { "alreadyHaveAccount", "Already have an account?" },

                    // Errors / Messages
                    { "enterName", "Please enter your name." },
                    { "enterEmail", "Please enter your email." },
                    { "validEmail", "Please enter a valid email." },
                    { "enterPassword", "Please enter a password." },
                    { "shortPassword", "Password must be at least 6 characters." },
                    { "passwordMismatch", "Passwords do not match." },
                    { "accountSuccess", "Account created successfully!" },
                    { "loginSuccess", "Logged in successfully!" },
                    { "invalidCredentials", "Invalid email or password." },
                    { "emailExists", "An account with this email already exists." },
                    { "connectionError", "Could not connect to server: " }
                }
            },
            {
                "RU",
                new Dictionary<string, string>
                {
                    { "loginTitle", "Добро пожаловать" },
                    { "loginSubtext", "Войдите в свой аккаунт WiFi Heat Map" },
                    { "emailLabel", "Электронная почта" },
                    { "passwordLabel", "Пароль" },
                    { "loginButton", "Войти" },
                    { "noAccount", "Нет аккаунта?" },
                    { "signUp", "Зарегистрироваться" },

                    { "signUpTitle", "Создание аккаунта" },
                    { "signUpSubtext", "Создайте аккаунт для использования WiFi Heat Map" },
                    { "nameLabel", "Имя" },
                    { "confirmPasswordLabel", "Подтверждение пароля" },
                    { "createAccountButton", "Создать аккаунт" },
                    { "alreadyHaveAccount", "Уже есть аккаунт?" },

                    // Errors / Messages
                    { "enterName", "Пожалуйста, введите ваше имя." },
                    { "enterEmail", "Пожалуйста, введите ваш email." },
                    { "validEmail", "Пожалуйста, введите корректный email." },
                    { "enterPassword", "Пожалуйста, введите пароль." },
                    { "shortPassword", "Пароль должен содержать не менее 6 символов." },
                    { "passwordMismatch", "Пароли не совпадают." },
                    { "accountSuccess", "Аккаунт успешно создан!" },
                    { "loginSuccess", "Успешная авторизация!" },
                    { "invalidCredentials", "Неверный email или пароль." },
                    { "emailExists", "Аккаунт с таким email уже существует." },
                    { "connectionError", "Не удалось подключиться к серверу: " }
                }
            }
        };

        public string GetText(string key)
        {
            if (translations[CurrentLanguage].TryGetValue(key, out var text))
            {
                return text;
            }
            return key;
        }

        public void ChangeLanguage(string language)
        {
            CurrentLanguage = language;
        }
    }
}