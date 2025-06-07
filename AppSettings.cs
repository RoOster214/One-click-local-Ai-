using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace PhoenixOfflineAI
{
    public class AppSettings
    {
        // Appearance settings
        public string Theme { get; set; } = "Dark";
        public float ChatFontSize { get; set; } = 10f;
        public bool ShowTimestamps { get; set; } = true;
        public string MessageColors { get; set; } = "Default";

        // Privacy & Storage settings
        public bool SaveChatHistory { get; set; } = true;
        public string AutoClearHistory { get; set; } = "Never";
        public string LogLevel { get; set; } = "Basic";
        public string StorageLocation { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhoenixOfflineAI");

        // Singleton instance
        private static AppSettings _instance;
        public static AppSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }
        public bool UseCustomModel { get; set; } = false;
        public string CustomModelPath { get; set; } = "";
        public string DefaultVisionModel { get; set; } = "llava:7b";
        public string DefaultCodeModel { get; set; } = "codellama:7b";
        public string DefaultGeneralModel { get; set; } = "llama3.2:3b";
        // File path for settings
        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhoenixOfflineAI",
            "settings.json");

        // Load settings from file
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception)
            {
                // Fallback to default settings on error
            }
            return new AppSettings();
        }

        // Save settings to file
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception)
            {
                // Silently fail - settings aren't critical
            }
        }

        // Apply theme to a form
        public void ApplyTheme(Form form)
        {
            switch (Theme)
            {
                case "Dark":
                    form.BackColor = Color.FromArgb(32, 32, 32);
                    form.ForeColor = Color.White;
                    break;
                case "Light":
                    form.BackColor = Color.White;
                    form.ForeColor = Color.Black;
                    break;
                case "System":
                    // Check system theme
                    bool isDarkMode = IsSystemInDarkMode();
                    form.BackColor = isDarkMode ? Color.FromArgb(32, 32, 32) : Color.White;
                    form.ForeColor = isDarkMode ? Color.White : Color.Black;
                    break;
            }
        }

        // Helper method to check system theme
        private bool IsSystemInDarkMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value != null && value is int appsUseLightTheme)
                        {
                            return appsUseLightTheme == 0;
                        }
                    }
                }
            }
            catch { }

            return false; // Default to light mode if we can't detect
        }
    }
}
internal class AppSettings
{  
}

