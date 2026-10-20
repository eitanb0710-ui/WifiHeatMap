using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BCrypt.Net;
using Google.Apis.Auth;
using Microsoft.Data.Sqlite;

namespace WifiHeatMapServer
{
    internal class Program
    {
        private const int Port = 5000;
        private const string DbPath = @"C:\MyDatabase\users.db";

        // Replace with your actual Google Client ID from Google Cloud Console
        private const string GoogleClientId = "493364826582-rtolus8ijpt1fv36f36lp7j3qeqe6cna.apps.googleusercontent.com";

        static async Task Main(string[] args)
        {
            InitializeDatabase();

            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine($"[Server Started] Listening for TCP socket connections on port {Port}...");

            while (true)
            {
                // Accept incoming socket connection
                TcpClient client = await listener.AcceptTcpClientAsync();

                // Spawn a new asynchronous task for each connected client
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private static void InitializeDatabase()
        {
            try
            {
                string? dir = Path.GetDirectoryName(DbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;"))
                {
                    conn.Open();
                    string createTableQuery = @"
                        CREATE TABLE IF NOT EXISTS Users (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL,
                            Email TEXT UNIQUE NOT NULL,
                            Password TEXT NOT NULL
                        );";

                    using (var cmd = new SQLiteCommand(createTableQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                Console.WriteLine("[Database] SQLite initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database Error]: {ex.Message}");
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream))
            using (StreamWriter writer = new StreamWriter(stream) { AutoFlush = true })
            {
                try
                {
                    // Read incoming command line from socket
                    string? request = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(request)) return;

                    Console.WriteLine($"[Socket Received]: {request}");

                    // Route request and generate response string
                    string response = await ProcessCommandAsync(request);

                    Console.WriteLine($"[Socket Sent]: {response}");
                    await writer.WriteLineAsync(response);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Socket Error]: {ex.Message}");
                }
            }
        }

        private static async Task<string> ProcessCommandAsync(string request)
        {
            string[] parts = request.Split('|');
            string command = parts[0];

            switch (command)
            {
                case "GOOGLE_AUTH":
                    if (parts.Length < 2) return "ERROR|MissingToken";
                    bool isGoogleValid = await ProcessGoogleAuthAsync(parts[1]);
                    return isGoogleValid ? "OK" : "ERROR|InvalidGoogleToken";

                case "LOGIN":
                    if (parts.Length < 3) return "ERROR|MissingParameters";
                    return await ProcessLoginAsync(parts[1], parts[2]);

                case "SIGNUP":
                    if (parts.Length < 4) return "ERROR|MissingParameters";
                    return await ProcessSignUpAsync(parts[1], parts[2], parts[3]);

                default:
                    return "ERROR|UnknownCommand";
            }
        }

        private static async Task<bool> ProcessGoogleAuthAsync(string idToken)
        {
            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { GoogleClientId }
                };

                GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
                string email = payload.Email;
                string name = payload.Name ?? "Google User";

                using (var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;"))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email", conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", email);
                        long count = (long)await cmd.ExecuteScalarAsync();

                        if (count == 0)
                        {
                            using (var insertCmd = new SQLiteCommand("INSERT INTO Users (Name, Email, Password) VALUES (@Name, @Email, 'GOOGLE_OAUTH')", conn))
                            {
                                insertCmd.Parameters.AddWithValue("@Name", name);
                                insertCmd.Parameters.AddWithValue("@Email", email);
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Google Auth Error]: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> ProcessSignUpAsync(string name, string email, string plainPassword)
        {
            try
            {
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(plainPassword);

                using (var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;"))
                {
                    await conn.OpenAsync();

                    using (var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@Email", email);
                        long count = (long)await checkCmd.ExecuteScalarAsync();
                        if (count > 0) return "ERROR|EmailAlreadyExists";
                    }

                    using (var insertCmd = new SQLiteCommand("INSERT INTO Users (Name, Email, Password) VALUES (@Name, @Email, @Password)", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@Name", name);
                        insertCmd.Parameters.AddWithValue("@Email", email);
                        insertCmd.Parameters.AddWithValue("@Password", hashedPassword);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }
                return "OK";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignUp Error]: {ex.Message}");
                return "ERROR|DatabaseError";
            }
        }

        private static async Task<string> ProcessLoginAsync(string email, string plainPassword)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;"))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SQLiteCommand("SELECT Password FROM Users WHERE Email = @Email", conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", email);
                        object? result = await cmd.ExecuteScalarAsync();

                        if (result == null) return "ERROR|InvalidCredentials";

                        string storedHash = result.ToString() ?? "";

                        if (storedHash == "GOOGLE_OAUTH")
                        {
                            return "ERROR|UseGoogleAuth";
                        }

                        if (BCrypt.Net.BCrypt.Verify(plainPassword, storedHash))
                        {
                            return "OK";
                        }
                        else
                        {
                            return "ERROR|InvalidCredentials";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Login Error]: {ex.Message}");
                return "ERROR|DatabaseError";
            }
        }
    }
}