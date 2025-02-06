using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace trader.Services
{
	public interface ISettingsService
	{
		public Settings Settings { get; }
		public void LoadSettings();
		public void SaveSettings();
	}
}
