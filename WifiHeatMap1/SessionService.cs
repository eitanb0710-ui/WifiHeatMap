using System;
using System.IO;

namespace WifiHeatMap1
{
    public static class SessionService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WifiHeatMap"
        );
        private static readonly string SessionFilePath = Path.Combine(AppDataFolder, "session.dat");

        // Save session locally on successful login
        public static void SaveSession(string userEmail)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                File.WriteAllText(SessionFilePath, userEmail);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
            }
        }

        // Retrieve saved user session email on startup
        public static string? GetSavedSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    string savedEmail = File.ReadAllText(SessionFilePath).Trim();
                    if (!string.IsNullOrEmpty(savedEmail))
                    {
                        return savedEmail;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read session: {ex.Message}");
            }

            return null;
        }

        // Clear session on explicit Logout
        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear session: {ex.Message}");
            }
        }
    }
}