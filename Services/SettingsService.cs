using System.IO;
using System.Text.Json;

namespace trader.Services
{
	public class SettingsService : ISettingsService
	{
		private Settings _settings;
		public Settings Settings => _settings;

		public SettingsService()
		{
			LoadSettings();
		}

		public void LoadSettings()
		{
			if (File.Exists("settings.json"))
			{
				var json = File.ReadAllText("settings.json");
				_settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
			}
			else
			{
				_settings = new Settings();
				SaveSettings();
			}
		}

		public void SaveSettings()
		{
			var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText("settings.json", json);
		}
	}
}
