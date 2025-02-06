using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Data.SQLite;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using trader.Services;

namespace Trader
{
	public class Program
	{
		private static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureServices((_, services) =>
					services.AddSingleton<ITradeOperations, SimulationTradeOperations>()
							.AddSingleton<IMachineLearningService, MachineLearningService>()
							.AddSingleton<IDatabaseService, DatabaseService>()
							.AddSingleton<ISettingsService, SettingsService>()
							.AddScoped<RuntimeContext>()
							.AddScoped<Trader>());

		public static async Task Main(string[] args)
		{
			Console.OutputEncoding = System.Text.Encoding.UTF8;

			var host = CreateHostBuilder(args).Build();

			var trader = host.Services.GetRequiredService<Trader>();

			await trader.Run(args);
		}
	}
}
