using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Trader;

namespace trader.Services
{
	public class SourceService : ISourceService
	{
		private readonly HttpClient httpClient = new HttpClient();
		private readonly ISettingsService _settingsService;
		private readonly RuntimeContext _runtimeContext;
		public SourceService(ISettingsService settingsService, RuntimeContext runtimeContext) 
		{
			_settingsService = settingsService;
			_runtimeContext = runtimeContext;
		}

		public async Task<Dictionary<string, decimal>> GetCryptoPrices(PriceSource priceSource)
		{
			try
			{
				AnsiConsole.MarkupLine("\n[bold yellow]=== Fetching cryptocurrency prices... ===[/]");

				string response;

				switch (priceSource)
				{
					case PriceSource.coingecko:
						response = await httpClient.GetStringAsync(_settingsService.Settings.API_URL);
						var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(response);
						_runtimeContext.CurrentPrices = new Dictionary<string, decimal>();

						foreach (var coin in data ?? new Dictionary<string, Dictionary<string, decimal>>())
						{
							string name = coin.Key;
							decimal price = coin.Value["usd"];
							_runtimeContext.CurrentPrices[name] = price;

							if (!_runtimeContext.CachedPrices.ContainsKey(name))
								_runtimeContext.CachedPrices[name] = new List<decimal>();

							_runtimeContext.CachedPrices[name].Add(price);
							if (_runtimeContext.CachedPrices[name].Count > _settingsService.Settings.CustomPeriods)
								_runtimeContext.CachedPrices[name].RemoveAt(0);
						}
						return _runtimeContext.CurrentPrices;

					case PriceSource.coinbase:
						var coinSymbols = new List<string> { "BTC-USD", "ETH-USD", "LTC-USD" };
						var prices = new Dictionary<string, decimal>();

						foreach (var symbol in coinSymbols)
						{
							response = await httpClient.GetStringAsync($"https://api.coinbase.com/v2/prices/{symbol}/spot");
							var coinBaseData = JsonSerializer.Deserialize<CoinbasePriceResponse>(response);

							if (coinBaseData != null && coinBaseData.Data != null)
							{
								prices[symbol.Split('-')[0]] = coinBaseData.Data.Amount;
							}
						}

						_runtimeContext.CurrentPrices = prices;

						foreach (var coin in prices)
						{
							if (!_runtimeContext.CachedPrices.ContainsKey(coin.Key))
								_runtimeContext.CachedPrices[coin.Key] = new List<decimal>();

							_runtimeContext.CachedPrices[coin.Key].Add(coin.Value);
							if (_runtimeContext.CachedPrices[coin.Key].Count > _settingsService.Settings.CustomPeriods)
								_runtimeContext.CachedPrices[coin.Key].RemoveAt(0);
						}

						return prices;
					default:
						return new Dictionary<string, decimal>();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				return new Dictionary<string, decimal>();
			}
		}

		public class CoinbasePriceResponse
		{
			public CoinbasePriceData Data { get; set; }
		}

		public class CoinbasePriceData
		{
			public string Base { get; set; }
			public string Currency { get; set; }
			public decimal Amount { get; set; }
		}
	}
}
