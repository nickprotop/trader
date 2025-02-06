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
		private static readonly HttpClient client = new HttpClient();
		private static readonly bool isConsoleAvailable = IsConsoleAvailable();

		private static ITradeOperations? tradeOperations;
		private static IMachineLearningService? mlService;
		private static IDatabaseService? databaseService;
		private static ISettingsService? settingsService;
		private static RuntimeContext? runtimeContext;

		private readonly ITradeOperations _tradeOperations;
		private readonly IMachineLearningService _mlService;

		public Program(ITradeOperations tradeOperations, IMachineLearningService mlService)
		{
			_tradeOperations = tradeOperations;
			_mlService = mlService;
		}

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

			tradeOperations = host.Services.GetRequiredService<ITradeOperations>();
			mlService = host.Services.GetRequiredService<IMachineLearningService>();
			databaseService = host.Services.GetRequiredService<IDatabaseService>();
			settingsService = host.Services.GetRequiredService<ISettingsService>();
			runtimeContext = host.Services.GetRequiredService<RuntimeContext>();

			var trader = host.Services.GetRequiredService<Trader>();			

			bool performResetDatabase = args.Contains("-c");

			AnsiConsole.MarkupLine("[bold yellow]=== Welcome to the Crypto Trading Bot ===[/]");

			if (performResetDatabase)
			{
				AnsiConsole.MarkupLine("[bold red]\nClearing database...[/]");
				ResetDatabase();
			}
			else
			{
				try
				{
					databaseService.InitializeRuntimeContext();
				}
				catch (Exception ex)
				{
					AnsiConsole.MarkupLine($"[bold red]Error while initialize database: {ex.Message}[/]");

					var resetDatabase = AnsiConsole.Confirm("Do you want to reset the database and start over?");

					if (resetDatabase)
					{
						ResetDatabase();
					}
					else
					{
						AnsiConsole.MarkupLine("[bold red]Exiting the program...[/]");
						return;
					}
				}
			}

			PrintProgramParameters();

			// Load the model if it exists
			if (File.Exists("model.zip"))
			{
				mlService.LoadModel("model.zip");
				AnsiConsole.MarkupLine("[bold green]\n=== Model loaded successfully! ===[/]");
			}
			else
			{
				TrainAiModel(mlService);
			}

			var cts = new CancellationTokenSource();
			var token = cts.Token;

			var iterationTask = StartBackgroundTask(token);

			try
			{
				while (true)
				{
					// Check for console input
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(intercept: true);
						if (key.Key == ConsoleKey.C)
						{
							ResetDatabase();
							PrintMenu();
							continue;
						}

						if (key.Key == ConsoleKey.T)
						{
							ShowTransactionHistory();
							PrintMenu();
						}

						if (key.Key == ConsoleKey.V)
						{
							ShowBalance(runtimeContext.CurrentPrices, true, true);
							PrintMenu();
						}

						if (key.Key == ConsoleKey.D)
						{
							ShowDatabaseStats();
							PrintMenu();
						}

						if (key.Key == ConsoleKey.Q)
						{
							AnsiConsole.MarkupLine("[bold red]Exiting the program...[/]");
							cts.Cancel();
							break;
						}

						if (key.Key == ConsoleKey.P)
						{
							PrintProgramParameters();
							PrintMenu();
						}

						if (key.Key == ConsoleKey.A)
						{
							AnalyzeIndicators(runtimeContext.CurrentPrices, settingsService.Settings.CustomPeriods, settingsService.Settings.CustomIntervalSeconds * settingsService.Settings.CustomPeriods, true);
							PrintMenu();
						}

						if (key.Key == ConsoleKey.S)
						{
							SellCoinFromPortfolio();
							PrintMenu();
						}

						if (key.Key == ConsoleKey.B)
						{
							BuyCoin();
							PrintMenu();
						}

						if (key.Key == ConsoleKey.K)
						{
							BacktestStrategy(databaseService.LoadHistoricalData());
							PrintMenu();
						}

						if (key.Key == ConsoleKey.R)
						{
							TrainAiModel(mlService);
							PrintMenu();
						}
					}

					// Check if the background task is running, and if not, restart it
					if (iterationTask.IsCompleted)
					{
						AnsiConsole.MarkupLine("[bold red]Background task stopped. Quitting...[/]");
						break;
					}

					await Task.Delay(100); // Sleep for 100 milliseconds to prevent CPU overuse
				}
			}
			catch (Exception ex)
			{
				AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
			}
			finally
			{
				// Ensure the background task is properly disposed of when exiting
				await iterationTask;
				cts.Dispose();
			}
		}

		private static void TrainAiModel(IMachineLearningService mlService)
		{
			AnsiConsole.MarkupLine("[bold cyan]\n=== Retraining AI model... ===\n[/]");

			try
			{
				var historicalData = databaseService.LoadHistoricalData();

				// Prepare the training data
				historicalData = historicalData.Where(h => !(h.RSI == 0 && h.EMA == 0 && h.SMA == 0 && h.MACD == 0)).ToList();

				var trainingData = historicalData.Select(h => new CryptoData
				{
					Price = (float)h.Price,
					SMA = (float)h.SMA,
					EMA = (float)h.EMA,
					RSI = (float)h.RSI,
					MACD = (float)h.MACD,
					BollingerUpper = (float)h.BollingerUpper,
					BollingerLower = (float)h.BollingerLower,
					ATR = (float)h.ATR,
					Volatility = (float)h.Volatility,
					Label = (float)h.Price // Use the price as the label for simplicity
				}).ToList();
				mlService.TrainModel(trainingData);

				mlService.SaveModel("model.zip"); // Save the model to a file
				AnsiConsole.MarkupLine("[bold cyan]=== Model retrained and saved successfully! ===[/]");
			}
			catch (Exception ex)
			{
				AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}\n[/]");
				AnsiConsole.MarkupLine("[bold cyan]=== End of model retrain ===[/]");
			}
		}

		private static Task StartBackgroundTask(CancellationToken token)
		{
			return Task.Run(async () =>
			{
				DateTime nextIterationTime = DateTime.UtcNow;
				Dictionary<string, decimal> prices = new Dictionary<string, decimal>();

				try
				{
					while (!token.IsCancellationRequested)
					{
						// Check if it's time for the next iteration
						if (DateTime.UtcNow >= nextIterationTime)
						{
							prices = await GetCryptoPrices();
							if (prices.Count == 0)
							{
								nextIterationTime = DateTime.UtcNow.AddSeconds(settingsService.Settings.CustomIntervalSeconds);
								continue;
							}

							StoreIndicatorsInDatabase(prices);
							AnalyzeIndicators(prices, settingsService.Settings.CustomPeriods, settingsService.Settings.CustomIntervalSeconds * settingsService.Settings.CustomPeriods, false);

							PrintMenu();

							// Update the next iteration time
							nextIterationTime = DateTime.UtcNow.AddSeconds(settingsService.Settings.CustomIntervalSeconds);
							AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update in {settingsService.Settings.CustomIntervalSeconds} seconds... ===[/]");
						}
						else
						{
							// Optionally, you can sleep for a short duration to prevent CPU overuse
							await Task.Delay(100, token); // Sleep for 100 milliseconds
						}
					}
				}
				catch (OperationCanceledException)
				{
					AnsiConsole.MarkupLine("[bold red]Background task was canceled.[/]");
				}
				catch (Exception ex)
				{
					AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
				}
			}, token);
		}

		public static string[] ExecuteDCA(string coin, decimal amount, decimal currentPrice, TimeSpan interval)
		{
			DateTime? lastPurchaseTime = GetLastPurchaseTime(coin);
			if (!lastPurchaseTime.HasValue || (DateTime.UtcNow - lastPurchaseTime.Value) >= interval)
			{
				decimal quantity = amount / currentPrice;
				var buyResult = tradeOperations.Buy(coin, quantity, currentPrice);
				SaveDCAConfig(coin, DateTime.UtcNow);

				return buyResult;
			}

			return new string[] { };
		}

		private static void SellCoinFromPortfolio()
		{
			if (runtimeContext.Portfolio.Count == 0)
			{
				AnsiConsole.MarkupLine("[bold red]No coins in the portfolio to sell.[/]");
				return;
			}

			var table = new Table();
			table.AddColumn("Coin");
			table.AddColumn("Units Held");
			table.AddColumn("Current Price");
			table.AddColumn("Current Value");
			table.AddColumn("Gain/Loss");
			table.AddColumn("Gain/Loss Including Fee");

			foreach (var coin in runtimeContext.Portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = runtimeContext.CurrentPrices.ContainsKey(coin.Key) ? runtimeContext.CurrentPrices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;

					// Calculate gain or loss
					decimal averageCostBasis = runtimeContext.TotalCostPerCoin[coin.Key] / runtimeContext.TotalQuantityPerCoin[coin.Key];
					decimal costOfHeldCoins = averageCostBasis * coin.Value;
					decimal gainOrLoss = value - costOfHeldCoins;

					// Calculate gain or loss including fee
					decimal fee = value * settingsService.Settings.TransactionFeeRate;
					decimal gainOrLossIncludingFee = gainOrLoss - fee;

					string gainOrLossStr = gainOrLoss >= 0 ? $"[green]{gainOrLoss:C}[/]" : $"[red]{gainOrLoss:C}[/]";
					string gainOrLossIncludingFeeStr = gainOrLossIncludingFee >= 0 ? $"[green]{gainOrLossIncludingFee:C}[/]" : $"[red]{gainOrLossIncludingFee:C}[/]";

					table.AddRow(
						coin.Key.ToUpper(),
						coin.Value.ToString("N4"),
						currentPrice.ToString("C"),
						value.ToString("C"),
						gainOrLossStr,
						gainOrLossIncludingFeeStr
					);
				}
			}

			AnsiConsole.Write(table);

			var coinToSell = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select a coin to sell (or [bold red]Cancel[/]):")
					.PageSize(10)
					.AddChoices(runtimeContext.Portfolio.Keys.Where(k => runtimeContext.Portfolio[k] > 0).Append("Cancel").ToArray())
			);

			if (coinToSell == "Cancel")
			{
				AnsiConsole.MarkupLine("[bold yellow]Sell operation canceled.[/]");
				return;
			}

			if (runtimeContext.Portfolio.ContainsKey(coinToSell) && runtimeContext.Portfolio[coinToSell] > 0)
			{
				decimal currentPrice = runtimeContext.CurrentPrices[coinToSell];
				decimal quantityToSell = runtimeContext.Portfolio[coinToSell];
				decimal totalValue = quantityToSell * currentPrice;

				// Calculate gain or loss
				decimal averageCostBasis = runtimeContext.TotalCostPerCoin[coinToSell] / runtimeContext.TotalQuantityPerCoin[coinToSell];
				decimal costOfSoldCoins = averageCostBasis * quantityToSell;
				decimal gainOrLoss = totalValue - costOfSoldCoins;

				var sellResult = tradeOperations.Sell(coinToSell, currentPrice);
				foreach (var result in sellResult)
				{
					AnsiConsole.MarkupLine(result);
				}
			}
			else
			{
				AnsiConsole.MarkupLine("[bold red]Invalid selection or no units to sell.[/]");
			}
		}

		public static void UpdateTrailingStopLoss(string coin, decimal currentPrice, decimal trailingPercentage)
		{
			decimal? stopLoss = GetTrailingStopLoss(coin);
			if (!stopLoss.HasValue)
			{
				stopLoss = currentPrice * (1 - trailingPercentage);
				SaveTrailingStopLoss(coin, stopLoss.Value);
			}
			else
			{
				decimal newStopLoss = currentPrice * (1 - trailingPercentage);
				if (newStopLoss > stopLoss.Value)
				{
					SaveTrailingStopLoss(coin, newStopLoss);
				}
			}
		}

		private static void BuyCoin()
		{
			var availableCoins = runtimeContext.CurrentPrices.Keys.ToList();

			if (availableCoins.Count == 0)
			{
				AnsiConsole.MarkupLine("[bold red]No coins available to buy.[/]");
				return;
			}

			var table = new Table();
			table.AddColumn("Coin");
			table.AddColumn("Current Price");

			foreach (var coin in availableCoins)
			{
				decimal currentPrice = runtimeContext.CurrentPrices[coin];
				table.AddRow(coin.ToUpper(), currentPrice.ToString("C"));
			}

			AnsiConsole.Write(table);

			var coinToBuy = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select a coin to buy (or [bold red]Cancel[/]):")
					.PageSize(10)
					.AddChoices(availableCoins.Append("Cancel").ToArray())
			);

			if (coinToBuy == "Cancel")
			{
				AnsiConsole.MarkupLine("[bold yellow]Buy operation canceled.[/]");
				return;
			}

			decimal price = runtimeContext.CurrentPrices[coinToBuy];
			decimal quantityToBuy = AnsiConsole.Prompt(
				new TextPrompt<decimal>($"Enter the quantity of {coinToBuy.ToUpper()} to buy (0 to cancel):")
			);

			if (quantityToBuy == 0) return;

			decimal totalCost = quantityToBuy * price;

			if (runtimeContext.Balance >= totalCost)
			{
				var buyResult = tradeOperations.Buy(coinToBuy, quantityToBuy, price);
				foreach (var result in buyResult)
				{
					AnsiConsole.MarkupLine(result);
				}
			}
			else
			{
				AnsiConsole.MarkupLine("[bold red]Insufficient balance to complete the purchase.[/]");
			}
		}

		private static bool IsConsoleAvailable()
		{
			try
			{
				int windowHeight = Console.WindowHeight;
				return true;
			}
			catch (IOException)
			{
				return false;
			}
		}

		private static void PrintMenu()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Menu ===[/]\n");
			AnsiConsole.MarkupLine("Press [bold green]'C'[/] to clear the database and start over.");
			AnsiConsole.MarkupLine("Press [bold green]'T'[/] to view transaction history.");
			AnsiConsole.MarkupLine("Press [bold green]'V'[/] to view verbose balance and portfolio.");
			AnsiConsole.MarkupLine("Press [bold green]'D'[/] to show database statistics.");
			AnsiConsole.MarkupLine("Press [bold green]'P'[/] to show program parameters.");
			AnsiConsole.MarkupLine("Press [bold green]'A'[/] to show analysis strategy");
			AnsiConsole.MarkupLine("Press [bold green]'B'[/] to buy a coin.");
			AnsiConsole.MarkupLine("Press [bold green]'S'[/] to sell a coin.");
			AnsiConsole.MarkupLine("Press [bold green]'K'[/] to backtest strategy.");
			AnsiConsole.MarkupLine("Press [bold green]'R'[/] to retrain the model.");
			AnsiConsole.MarkupLine("");
			AnsiConsole.MarkupLine("Press [bold green]'Q'[/] to quit the program.");
			AnsiConsole.MarkupLine("\n[bold yellow]============[/]");
		}

		private static void PrintProgramParameters()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== App Parameters ===[/]\n");

			var table = new Table();
			table.AddColumn(new TableColumn("[bold yellow]Parameter[/]").LeftAligned());
			table.AddColumn(new TableColumn("[bold yellow]Value[/]").LeftAligned());

			table.AddRow("[bold cyan]API URL[/]", settingsService.Settings.API_URL);
			table.AddRow("[bold cyan]Database Path[/]", settingsService.Settings.DbPath);
			table.AddRow("[bold cyan]Period interval (Seconds)[/]", settingsService.Settings.CustomIntervalSeconds.ToString());
			table.AddRow("[bold cyan]Analyze Periods/seconds[/]", $"{settingsService.Settings.CustomPeriods}/{settingsService.Settings.CustomIntervalSeconds * settingsService.Settings.CustomPeriods}");
			table.AddRow("[bold cyan]Stop-Loss Threshold[/]", settingsService.Settings.StopLossThreshold.ToString("P"));
			table.AddRow("[bold cyan]Profit-Taking Threshold[/]", settingsService.Settings.ProfitTakingThreshold.ToString("P"));
			table.AddRow("[bold cyan]Starting Balance[/]", settingsService.Settings.StartingBalance.ToString("C"));
			table.AddRow("[bold cyan]Max Investment Per Coin[/]", settingsService.Settings.MaxInvestmentPerCoin.ToString("C"));
			table.AddRow("[bold cyan]Transaction Fee Rate[/]", settingsService.Settings.TransactionFeeRate.ToString("P"));
			table.AddRow("[bold cyan]Trailing stop loss percentage[/]", settingsService.Settings.TrailingStopLossPercentage.ToString("P"));
			table.AddRow("[bold cyan]Dollar cost averaging amount[/]", settingsService.Settings.DollarCostAveragingAmount.ToString("C"));
			table.AddRow("[bold cyan]Dollar cost averaging time interval (seconds)[/]", settingsService.Settings.DollarCostAveragingSecondsInterval.ToString());

			AnsiConsole.Write(table);
			AnsiConsole.MarkupLine("\n[bold yellow]==========================[/]");
		}

		private static void BacktestStrategy(List<HistoricalData> historicalData)
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Backtest strategy analysis ===[/]\n");

			decimal initialBalance = settingsService.Settings.StartingBalance;
			decimal balance = initialBalance;
			decimal portfolioValue = 0;
			decimal totalProfitOrLoss = 0;

			Dictionary<string, decimal> portfolio = new Dictionary<string, decimal>();
			Dictionary<string, decimal> initialInvestments = new Dictionary<string, decimal>();
			Dictionary<string, decimal> totalQuantityPerCoin = new Dictionary<string, decimal>();
			Dictionary<string, decimal> totalCostPerCoin = new Dictionary<string, decimal>();

			var groupedData = historicalData.GroupBy(h => h.Name).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Timestamp).ToList());

			foreach (var coinData in groupedData)
			{
				string coinName = coinData.Key;
				List<HistoricalData> coinHistory = coinData.Value;

				for (int i = settingsService.Settings.CustomPeriods; i < coinHistory.Count; i++)
				{
					var recentHistory = coinHistory.Skip(i - settingsService.Settings.CustomPeriods).Take(settingsService.Settings.CustomPeriods).Select(h => h.Price).ToList();
					var data = coinHistory[i];

					decimal rsi = IndicatorCalculations.CalculateRSI(recentHistory, recentHistory.Count);
					decimal sma = IndicatorCalculations.CalculateSMA(recentHistory, recentHistory.Count);
					decimal ema = IndicatorCalculations.CalculateEMASingle(recentHistory, recentHistory.Count);
					var (macd, bestShortPeriod, bestLongPeriod, bestSignalPeriod) = IndicatorCalculations.CalculateMACD(recentHistory);
					decimal priceChangeWindow = IndicatorCalculations.CalculatePriceChange(recentHistory);

					// Calculate Bollinger Bands
					var (middleBand, upperBand, lowerBand) = IndicatorCalculations.CalculateBollingerBands(recentHistory, settingsService.Settings.CustomPeriods);

					// Calculate ATR (assuming high, low, and close prices are available)
					var highPrices = recentHistory; // Replace with actual high prices
					var lowPrices = recentHistory; // Replace with actual low prices
					var closePrices = recentHistory; // Replace with actual close prices
					decimal atr = IndicatorCalculations.CalculateATR(highPrices, lowPrices, closePrices, settingsService.Settings.CustomPeriods);

					// Calculate volatility
					decimal volatility = IndicatorCalculations.CalculateVolatility(recentHistory, settingsService.Settings.CustomPeriods);
					var (adjustedStopLoss, adjustedProfitTaking) = AdjustThresholdsBasedOnVolatility(volatility);

					// Trading signals
					if (rsi < 30 && data.Price < sma && data.Price < ema && macd < 0 && data.Price < lowerBand && atr > 0)
					{
						// Buy signal
						decimal quantity = balance / data.Price;
						decimal cost = quantity * data.Price;
						decimal fee = cost * settingsService.Settings.TransactionFeeRate;
						decimal totalCost = cost + fee;

						if (balance >= totalCost)
						{
							balance -= totalCost;

							// Update portfolio
							if (portfolio.ContainsKey(coinName))
								portfolio[coinName] += quantity;
							else
								portfolio[coinName] = quantity;

							// Update initial investments
							if (initialInvestments.ContainsKey(coinName))
								initialInvestments[coinName] += cost;
							else
								initialInvestments[coinName] = cost;

							// Update total quantity and cost for cost basis calculation
							if (totalQuantityPerCoin.ContainsKey(coinName))
							{
								totalQuantityPerCoin[coinName] += quantity;
								totalCostPerCoin[coinName] += cost;
							}
							else
							{
								totalQuantityPerCoin[coinName] = quantity;
								totalCostPerCoin[coinName] = cost;
							}
						}
					}
					else if (rsi > 70 && portfolio.ContainsKey(coinName) && portfolio[coinName] > 0 && data.Price > sma && data.Price > ema && macd > 0 && data.Price > upperBand && atr > 0)
					{
						// Sell signal
						decimal quantityToSell = portfolio[coinName];
						decimal amountToSell = quantityToSell * data.Price;
						decimal fee = amountToSell * settingsService.Settings.TransactionFeeRate;
						decimal netAmountToSell = amountToSell - fee;

						// Calculate average cost basis
						decimal averageCostBasis = totalCostPerCoin[coinName] / totalQuantityPerCoin[coinName];
						decimal costOfSoldCoins = averageCostBasis * quantityToSell;

						// Calculate gain or loss
						decimal gainOrLoss = netAmountToSell - costOfSoldCoins;

						// Update balance and portfolio
						balance += netAmountToSell;
						portfolio[coinName] = 0;

						// Update total quantity and cost
						totalQuantityPerCoin[coinName] -= quantityToSell;
						totalCostPerCoin[coinName] -= costOfSoldCoins;

						// Adjust initial investments
						if (initialInvestments.ContainsKey(coinName))
						{
							initialInvestments[coinName] -= costOfSoldCoins;
							if (initialInvestments[coinName] <= 0)
							{
								initialInvestments.Remove(coinName);
							}
						}

						totalProfitOrLoss += gainOrLoss;
					}

					// Update portfolio value
					portfolioValue = portfolio.Sum(p => p.Value * data.Price);
				}
			}

			decimal finalBalance = balance + portfolioValue;
			decimal totalReturn = (finalBalance - initialBalance) / initialBalance * 100;

			// Create a table for backtest results
			var table = new Table();
			table.AddColumn("Metric");
			table.AddColumn("Value");

			table.AddRow("Initial Balance", $"{initialBalance:C}");
			table.AddRow("Final Balance", $"{finalBalance:C}");
			table.AddRow("Total Return", $"{totalReturn:N2}%");
			table.AddRow("Total Profit/Loss", $"{totalProfitOrLoss:C}");

			AnsiConsole.Write(table);

			AnsiConsole.MarkupLine("\n[bold yellow]=== End of backtest strategy analysis ===[/]");
		}

		private static void ResetDatabase()
		{
			if (File.Exists(settingsService.Settings.DbPath))
			{
				File.Delete(settingsService.Settings.DbPath);
			}

			databaseService.InitializeRuntimeContext();

			AnsiConsole.MarkupLine("[bold red]Database has been reset. Starting over...[/]");
		}

		private static void ShowDatabaseStats()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Database statistics ===[/]\n");

			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string statsQuery = @"
        SELECT name, COUNT(*) AS count, MIN(price) AS minPrice, MAX(price) AS maxPrice, AVG(price) AS avgPrice
        FROM Prices
        GROUP BY name;";
				using (var cmd = new SQLiteCommand(statsQuery, conn))
				{
					using (var reader = cmd.ExecuteReader())
					{
						var table = new Table();
						table.AddColumn("Coin");
						table.AddColumn("Data Points");
						table.AddColumn("Min Price");
						table.AddColumn("Max Price");
						table.AddColumn("Avg Price");
						table.AddColumn("InMemory PriceHistory");

						while (reader.Read())
						{
							string name = reader.GetString(0);
							int count = reader.GetInt32(1);
							decimal minPrice = reader.GetDecimal(2);
							decimal maxPrice = reader.GetDecimal(3);
							decimal avgPrice = reader.GetDecimal(4);
							int inMemoryCount = runtimeContext.PriceHistory.ContainsKey(name) ? runtimeContext.PriceHistory[name].Count : 0;

							table.AddRow(
								name.ToUpper(),
								count.ToString(),
								$"${minPrice:N2}",
								$"${maxPrice:N2}",
								$"${avgPrice:N2}",
								inMemoryCount.ToString()
							);
						}

						AnsiConsole.Write(table);
					}
				}

				// Transaction statistics
				string transactionStatsQuery = @"
        SELECT
            COUNT(*) AS totalTransactions,
            SUM(CASE WHEN type = 'BUY' THEN 1 ELSE 0 END) AS totalBuys,
            SUM(CASE WHEN type = 'SELL' THEN 1 ELSE 0 END) AS totalSells,
            SUM(gain_loss) AS totalGainLoss
        FROM Transactions;";
				using (var cmd = new SQLiteCommand(transactionStatsQuery, conn))
				{
					using (var reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							int totalTransactions = reader.GetInt32(0);

							if (totalTransactions == 0)
							{
								AnsiConsole.MarkupLine("\n[bold red]No transactions recorded in the database.[/]");
								return;
							}
							else
							{
								int totalBuys = reader.GetInt32(1);
								int totalSells = reader.GetInt32(2);
								decimal totalGainLoss = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);

								var transactionTable = new Table();
								transactionTable.AddColumn("Statistic");
								transactionTable.AddColumn("Value");

								transactionTable.AddRow("Total Transactions", totalTransactions.ToString());
								transactionTable.AddRow("Total Buys", totalBuys.ToString());
								transactionTable.AddRow("Total Sells", totalSells.ToString());
								transactionTable.AddRow("Total Gain/Loss", $"${totalGainLoss:N2}");

								AnsiConsole.Write(transactionTable);
							}
						}
					}
				}

				// Display total fees
				decimal totalFees = CalculateTotalFees();
				AnsiConsole.MarkupLine($"\n[bold yellow]Total Fees Incurred: [/][bold cyan]{totalFees:C}[/]");
			}
			AnsiConsole.MarkupLine("\n[bold yellow]=== End of Database statistics ===[/]");
		}

		

		public static void SaveDCAConfig(string coin, DateTime lastPurchaseTime)
		{
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = @"
            INSERT INTO DCAConfig (coin, lastPurchaseTime)
            VALUES (@coin, @lastPurchaseTime)
            ON CONFLICT(coin) DO UPDATE SET lastPurchaseTime = @lastPurchaseTime;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					cmd.Parameters.AddWithValue("@coin", coin);
					cmd.Parameters.AddWithValue("@lastPurchaseTime", lastPurchaseTime);
					cmd.ExecuteNonQuery();
				}
			}
		}

		public static DateTime? GetLastPurchaseTime(string coin)
		{
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = "SELECT lastPurchaseTime FROM DCAConfig WHERE coin = @coin;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					cmd.Parameters.AddWithValue("@coin", coin);
					using (var reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							return reader.GetDateTime(0);
						}
					}
				}
			}
			return null;
		}

		public static void SaveTrailingStopLoss(string coin, decimal stopLoss)
		{
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = @"
            INSERT INTO TrailingStopLossConfig (coin, stopLoss)
            VALUES (@coin, @stopLoss)
            ON CONFLICT(coin) DO UPDATE SET stopLoss = @stopLoss;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					cmd.Parameters.AddWithValue("@coin", coin);
					cmd.Parameters.AddWithValue("@stopLoss", stopLoss);
					cmd.ExecuteNonQuery();
				}
			}
		}

		public static decimal? GetTrailingStopLoss(string coin)
		{
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = "SELECT stopLoss FROM TrailingStopLossConfig WHERE coin = @coin;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					cmd.Parameters.AddWithValue("@coin", coin);
					using (var reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							return reader.GetDecimal(0);
						}
					}
				}
			}
			return null;
		}

		private static async Task<Dictionary<string, decimal>> GetCryptoPrices()
		{
			try
			{
				AnsiConsole.MarkupLine("\n[bold yellow]=== Fetching cryptocurrency prices... ===[/]");

				var response = await client.GetStringAsync(settingsService.Settings.API_URL);
				var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(response);
				runtimeContext.CurrentPrices = new Dictionary<string, decimal>();

				foreach (var coin in data ?? new Dictionary<string, Dictionary<string, decimal>>())
				{
					string name = coin.Key;
					decimal price = coin.Value["usd"];
					runtimeContext.CurrentPrices[name] = price;

					if (!runtimeContext.PriceHistory.ContainsKey(name))
						runtimeContext.PriceHistory[name] = new List<decimal>();

					runtimeContext.PriceHistory[name].Add(price);
					if (runtimeContext.PriceHistory[name].Count > settingsService.Settings.CustomPeriods)
						runtimeContext.PriceHistory[name].RemoveAt(0);
				}
				return runtimeContext.CurrentPrices;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				return new Dictionary<string, decimal>();
			}
		}

		private static void StoreIndicatorsInDatabase(Dictionary<string, decimal> prices)
		{
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				foreach (var coin in prices)
				{
					var recentHistory = runtimeContext.PriceHistory[coin.Key];
					decimal rsi = IndicatorCalculations.CalculateRSI(recentHistory, recentHistory.Count);
					decimal sma = IndicatorCalculations.CalculateSMA(recentHistory, recentHistory.Count);
					decimal ema = IndicatorCalculations.CalculateEMASingle(recentHistory, recentHistory.Count);
					var (macd, _, _, _) = IndicatorCalculations.CalculateMACD(recentHistory);
					var (bollingerUpper, bollingerLower, _) = IndicatorCalculations.CalculateBollingerBands(recentHistory, settingsService.Settings.CustomPeriods);
					decimal atr = IndicatorCalculations.CalculateATR(recentHistory, recentHistory, recentHistory, settingsService.Settings.CustomPeriods);
					decimal volatility = IndicatorCalculations.CalculateVolatility(recentHistory, settingsService.Settings.CustomPeriods);

					string insertQuery = @"
					INSERT INTO Prices (name, price, rsi, sma, ema, macd, bollingerUpper, bollingerLower, atr, volatility)
					VALUES (@name, @price, @rsi, @sma, @ema, @macd, @bollingerUpper, @bollingerLower, @atr, @volatility);";
					using (var cmd = new SQLiteCommand(insertQuery, conn))
					{
						cmd.Parameters.AddWithValue("@name", coin.Key);
						cmd.Parameters.AddWithValue("@price", coin.Value);
						cmd.Parameters.AddWithValue("@rsi", rsi);
						cmd.Parameters.AddWithValue("@sma", sma);
						cmd.Parameters.AddWithValue("@ema", ema);
						cmd.Parameters.AddWithValue("@macd", macd);
						cmd.Parameters.AddWithValue("@bollingerUpper", bollingerUpper);
						cmd.Parameters.AddWithValue("@bollingerLower", bollingerLower);
						cmd.Parameters.AddWithValue("@atr", atr);
						cmd.Parameters.AddWithValue("@volatility", volatility);
						cmd.ExecuteNonQuery();
					}
				}
			}
		}

		private static void ShowBalance(Dictionary<string, decimal> prices, bool verbose = true, bool showTitle = true)
		{
			if (showTitle) AnsiConsole.MarkupLine($"\n[bold yellow]=== Balance{(verbose ? " and portfolio" : string.Empty)} Report ===[/]\n");

			decimal portfolioWorth = 0;
			decimal totalInvestment = 0;
			decimal totalFees = CalculateTotalFees(); // Calculate total fees

			// Calculate the total portfolio value and total investment
			foreach (var coin in runtimeContext.Portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;
					portfolioWorth += value;

					decimal initialInvestment = runtimeContext.TotalCostPerCoin.ContainsKey(coin.Key) ? runtimeContext.TotalCostPerCoin[coin.Key] : 0;
					totalInvestment += initialInvestment;
				}
			}

			decimal totalWorth = runtimeContext.Balance + portfolioWorth;
			decimal initialBalance = settingsService.Settings.StartingBalance; // Starting balance
			decimal totalProfitOrLoss = totalWorth - initialBalance;
			decimal percentageChange = initialBalance > 0 ? (totalProfitOrLoss / initialBalance) * 100 : 0;

			// Calculate current investment gain or loss
			decimal currentInvestmentGainOrLoss = portfolioWorth - totalInvestment;
			decimal currentInvestmentPercentageChange = totalInvestment > 0 ? (currentInvestmentGainOrLoss / totalInvestment) * 100 : 0;

			// Calculate total gain or loss from all transactions
			decimal totalTransactionGainOrLoss = 0;
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = "SELECT gain_loss FROM Transactions WHERE gain_loss IS NOT NULL;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							totalTransactionGainOrLoss += reader.GetDecimal(0);
						}
					}
				}
			}
			decimal totalTransactionPercentageChange = initialBalance > 0 ? (totalTransactionGainOrLoss / initialBalance) * 100 : 0;

			// Calculate total gain/loss including fees
			decimal totalGainOrLossIncludingFees = totalProfitOrLoss - totalFees;

			// Calculate current gain/loss including fees
			decimal currentGainOrLossIncludingFees = currentInvestmentGainOrLoss - (portfolioWorth * settingsService.Settings.TransactionFeeRate);

			// Create a table for balance information
			var balanceTable = new Table();
			balanceTable.AddColumn("Description");
			balanceTable.AddColumn("Value");

			balanceTable.AddRow("Total Investment across all coins", $"[bold cyan]{totalInvestment:C}[/]");
			balanceTable.AddRow("Current balance", $"[bold green]{runtimeContext.Balance:C}[/]");
			balanceTable.AddRow("Current portfolio worth", $"[bold green]{portfolioWorth:C}[/]");
			balanceTable.AddRow("Total worth", $"[bold green]{totalWorth:C}[/]");
			balanceTable.AddRow(totalTransactionGainOrLoss >= 0
				? "Total Transaction Gain (Excluding Current Portfolio)"
				: "Total Transaction Loss (Excluding Current Portfolio)", totalTransactionGainOrLoss >= 0
				? $"[bold green]{totalTransactionGainOrLoss:C} ({totalTransactionPercentageChange:N2}%)[/]"
				: $"[bold red]{Math.Abs(totalTransactionGainOrLoss):C} ({totalTransactionPercentageChange:N2}%)[/]");
			balanceTable.AddRow(currentInvestmentGainOrLoss >= 0
				? "Current Portfolio Gain"
				: "Current Portfolio Loss", currentInvestmentGainOrLoss >= 0
				? $"[bold green]{currentInvestmentGainOrLoss:C} ({currentInvestmentPercentageChange:N2}%)[/]"
				: $"[bold red]{Math.Abs(currentInvestmentGainOrLoss):C} ({currentInvestmentPercentageChange:N2}%)[/]");
			balanceTable.AddRow(totalProfitOrLoss >= 0
				? "Overall Gains"
				: "Overall Losses", totalProfitOrLoss >= 0
				? $"[bold green]{totalProfitOrLoss:C} ({percentageChange:N2}%)[/]"
				: $"[bold red]{Math.Abs(totalProfitOrLoss):C} ({percentageChange:N2}%)[/]");
			balanceTable.AddRow("Total Fees Incurred", $"[bold cyan]{totalFees:C}[/]");
			balanceTable.AddRow(totalGainOrLossIncludingFees >= 0
				? "Overall Gains Including Fees"
				: "Overall Losses Including Fees", totalGainOrLossIncludingFees >= 0
				? $"[bold green]{totalGainOrLossIncludingFees:C}[/]"
				: $"[bold red]{Math.Abs(totalGainOrLossIncludingFees):C}[/]");
			balanceTable.AddRow(currentGainOrLossIncludingFees >= 0
				? "Current Gain Including Fees"
				: "Current Loss Including Fees", currentGainOrLossIncludingFees >= 0
				? $"[bold green]{currentGainOrLossIncludingFees:C}[/]"
				: $"[bold red]{Math.Abs(currentGainOrLossIncludingFees):C}[/]");

			AnsiConsole.Write(balanceTable);

			if (verbose)
			{
				var portfolioTable = new Table();

				if (portfolioWorth == 0)
				{
					portfolioTable.AddColumn("Portfolio");
					portfolioTable.AddRow("[bold red]No holdings in the portfolio.[/]");
				}
				else
				{
					portfolioTable.AddColumn("Coin");
					portfolioTable.AddColumn("Units Held");
					portfolioTable.AddColumn("Current Price");
					portfolioTable.AddColumn("Current Value");
					portfolioTable.AddColumn("Initial Investment");
					portfolioTable.AddColumn("Profit/Loss");
					portfolioTable.AddColumn("Profit/Loss Including Fee");
					portfolioTable.AddColumn("Percentage of Portfolio");

					foreach (var coin in runtimeContext.Portfolio)
					{
						if (coin.Value > 0)
						{
							decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
							decimal value = coin.Value * currentPrice;
							decimal initialInvestment = runtimeContext.TotalCostPerCoin.ContainsKey(coin.Key) ? runtimeContext.TotalCostPerCoin[coin.Key] : 0;
							decimal profitOrLoss = value - initialInvestment;

							// Calculate profit/loss including fee
							decimal fee = value * settingsService.Settings.TransactionFeeRate;
							decimal profitOrLossIncludingFee = profitOrLoss - fee;

							decimal percentageOfPortfolio = portfolioWorth > 0 ? (value / portfolioWorth) * 100 : 0;
							decimal profitOrLossPercentage = initialInvestment > 0 ? (profitOrLoss / initialInvestment) * 100 : 0;

							string profitOrLossStr = profitOrLoss >= 0 ? $"[green]{profitOrLoss:C} ({profitOrLossPercentage:N2}%)[/]" : $"[red]{profitOrLoss:C} ({profitOrLossPercentage:N2}%)[/]";
							string profitOrLossIncludingFeeStr = profitOrLossIncludingFee >= 0 ? $"[green]{profitOrLossIncludingFee:C}[/]" : $"[red]{profitOrLossIncludingFee:C}[/]";

							portfolioTable.AddRow(
								coin.Key.ToUpper(),
								coin.Value.ToString("N4"),
								currentPrice.ToString("C"),
								value.ToString("C"),
								initialInvestment.ToString("C"),
								profitOrLossStr,
								profitOrLossIncludingFeeStr,
								$"{percentageOfPortfolio:N2}%"
							);
						}
					}
				}

				AnsiConsole.Write(portfolioTable);
			}

			if (showTitle) AnsiConsole.MarkupLine($"\n[bold yellow]=== End of balance{(verbose ? " and portfolio" : string.Empty)} Report ===[/]");
		}

		private static void ShowTransactionHistory()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Transactions History ===[/]\n");

			var (sortColumn, sortOrder) = PromptForSortOptions();
			bool showAllRecords = PromptForShowAllRecords();
			int recordsPerPage = showAllRecords ? int.MaxValue : PromptForRecordsPerPage();
			int currentPage = 1;
			int totalRecords = 0;
			decimal totalFees = 0;

			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();

				// Get the total number of records
				string countQuery = "SELECT COUNT(*) FROM Transactions;";
				using (var countCmd = new SQLiteCommand(countQuery, conn))
				{
					totalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
				}

				int totalPages = (int)Math.Ceiling((double)totalRecords / recordsPerPage);

				while (true)
				{
					int offset = (currentPage - 1) * recordsPerPage;

					string query = $@"
            SELECT name, type, quantity, price, fee, gain_loss, timestamp
            FROM Transactions
            ORDER BY {sortColumn} {sortOrder}
            LIMIT {recordsPerPage} OFFSET {offset};";

					using (var cmd = new SQLiteCommand(query, conn))
					{
						using (var reader = cmd.ExecuteReader())
						{
							var table = new Table();
							table.AddColumn("Coin");
							table.AddColumn("Type");
							table.AddColumn("Quantity");
							table.AddColumn("Price");
							table.AddColumn("Fee");
							table.AddColumn("Gain/Loss");
							table.AddColumn("Timestamp");

							while (reader.Read())
							{
								string name = reader.GetString(0);
								string type = reader.GetString(1);
								decimal quantity = reader.GetDecimal(2);
								decimal price = reader.GetDecimal(3);
								decimal fee = reader.GetDecimal(4);
								object gainLossObj = reader.GetValue(5);
								decimal? gainLoss = gainLossObj != DBNull.Value ? (decimal?)gainLossObj : null;
								DateTime timestamp = reader.GetDateTime(6);

								totalFees += fee;

								string gainLossStr = gainLoss.HasValue
									? (gainLoss.Value >= 0 ? $"[green]{gainLoss.Value:C}[/]" : $"[red]{gainLoss.Value:C}[/]")
									: "N/A";

								table.AddRow(
									name.ToUpper(),
									type,
									quantity.ToString("N4"),
									price.ToString("C"),
									fee.ToString("C"),
									gainLossStr,
									timestamp.ToString("yyyy-MM-dd HH:mm:ss")
								);
							}

							// Add a footer row to display the total fees
							table.AddEmptyRow();
							table.AddRow(
								new Markup("[bold yellow]Total Fees[/]"),
								new Markup(""),
								new Markup(""),
								new Markup(""),
								new Markup($"[bold cyan]{totalFees:C}[/]"),
								new Markup(""),
								new Markup("")
							);

							AnsiConsole.Write(table);
						}
					}

					if (showAllRecords)
					{
						break;
					}

					AnsiConsole.MarkupLine($"\n[bold yellow]Page {currentPage} of {totalPages}[/]");

					if (currentPage < totalPages)
					{
						var nextPage = AnsiConsole.Prompt(
							new SelectionPrompt<string>()
								.Title("Select an option:")
								.AddChoices("Next Page", "Exit")
						);

						if (nextPage == "Next Page")
						{
							currentPage++;
						}
						else
						{
							break;
						}
					}
					else
					{
						break;
					}
				}
			}

			AnsiConsole.MarkupLine("\n[bold yellow]=== End of Transactions History ===[/]");
		}

		private static (string column, string order) PromptForSortOptions()
		{
			var columns = new[] { "timestamp", "name", "type", "quantity", "price", "fee", "gain_loss" };
			var sortColumn = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select a column to sort by:")
					.AddChoices(columns)
			);

			var sortOrder = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Select sort order:")
					.AddChoices("ASC", "DESC")
			);

			return (sortColumn, sortOrder);
		}

		private static int PromptForRecordsPerPage()
		{
			return AnsiConsole.Prompt(
				new TextPrompt<int>("Enter the number of records per page:")
					.Validate(records =>
					{
						return records > 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Number of records must be greater than zero.[/]");
					})
			);
		}

		private static bool PromptForShowAllRecords()
		{
			var choice = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("Do you want to show all records or paginate?")
					.AddChoices("Show All", "Paginate")
			);

			return choice == "Show All";
		}

		private static decimal CalculateTotalFees()
		{
			decimal totalFees = 0;

			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = "SELECT SUM(fee) FROM Transactions;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					var result = cmd.ExecuteScalar();
					if (result != DBNull.Value && result != null)
					{
						totalFees = Convert.ToDecimal(result);
					}
				}
			}

			return totalFees;
		}

		private static void AnalyzeIndicators(Dictionary<string, decimal> prices, int customPeriods, int analysisWindowSeconds, bool analysisOnly)
		{
			DateTime startAnalysisTimeStamp = DateTime.Now.ToUniversalTime();

			if (!analysisOnly) runtimeContext.CurrentPeriodIndex++;

			AnsiConsole.MarkupLine($"\n[bold yellow]=== Market Analysis Report - Period Counter: {runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			bool operationPerfomed = false;

			foreach (var coin in prices)
			{
				bool operationsAllowed = true;

				AnsiConsole.MarkupLine($"\n[bold cyan]{coin.Key.ToUpper()}[/]:");

				if (!runtimeContext.PriceHistory.ContainsKey(coin.Key))
					continue;

				var recentHistoryData = GetRecentHistoryRows(coin.Key, settingsService.Settings.CustomPeriods);

				var table = new Table();
				table.AddColumn("Indicator");
				table.AddColumn("Value");

				if (recentHistoryData.Count < 2) // Need at least 2 data points to calculate change
				{
					table.AddRow("Analysis Status", $"[bold red]Insufficient data points for {coin.Key} analysis.[/]");
					AnsiConsole.Write(table);
					continue;
				}

				var recentHistory = recentHistoryData.Select(x => x.Price).ToList();

				if (recentHistory.Count < customPeriods)
				{
					table.AddRow("Analysis Status", $"[bold red]Insufficient data points for {coin.Key} analysis.[/]");
					operationsAllowed = false;
				}

				// Check if the timeframe is suitable for analysis
				DateTime earliestTimestamp = recentHistoryData.First().Timestamp;
				TimeSpan timeframe = DateTime.UtcNow - earliestTimestamp;

				if (runtimeContext.CheckForValidTimeInterval)
				{
					int bufferSeconds = settingsService.Settings.CustomIntervalSeconds * 2; // Buffer for capture delays
					if (timeframe.TotalSeconds < (analysisWindowSeconds - bufferSeconds) || timeframe.TotalSeconds > (analysisWindowSeconds + bufferSeconds))
					{
						table.AddRow("Analysis Status", $"[bold red]Not valid timeframe for {coin.Key} analysis. Required: {analysisWindowSeconds} seconds, Available: {timeframe.TotalSeconds} seconds (including buffer of {bufferSeconds} seconds).[/]");
						AnsiConsole.Write(table);
						continue;
					}
				}

				decimal rsi = IndicatorCalculations.CalculateRSI(recentHistory, recentHistory.Count);
				decimal sma = IndicatorCalculations.CalculateSMA(recentHistory, recentHistory.Count);
				decimal ema = IndicatorCalculations.CalculateEMASingle(recentHistory, recentHistory.Count);
				var (macd, bestShortPeriod, bestLongPeriod, bestSignalPeriod) = IndicatorCalculations.CalculateMACD(recentHistory);
				decimal priceChangeWindow = IndicatorCalculations.CalculatePriceChange(recentHistory);

				// Calculate Bollinger Bands
				var (middleBand, upperBand, lowerBand) = IndicatorCalculations.CalculateBollingerBands(recentHistory, customPeriods);

				// Calculate ATR (assuming high, low, and close prices are available)
				var highPrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual high prices
				var lowPrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual low prices
				var closePrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual close prices
				decimal atr = IndicatorCalculations.CalculateATR(highPrices, lowPrices, closePrices, customPeriods);

				// Calculate volatility
				decimal volatility = IndicatorCalculations.CalculateVolatility(recentHistory, customPeriods);
				var (adjustedStopLoss, adjustedProfitTaking) = AdjustThresholdsBasedOnVolatility(volatility);

				table.AddRow("Current Price", $"[bold green]${coin.Value:N2}[/]");
				table.AddRow("First Price Timestamp", $"[bold green]{earliestTimestamp:yyyy-MM-dd HH:mm:ss}[/]");
				table.AddRow("Last Price Timestamp", $"[bold green]{recentHistoryData.Last().Timestamp:yyyy-MM-dd HH:mm:ss}[/]");
				table.AddRow("Time span", $"[bold green]{(recentHistoryData.Last().Timestamp - recentHistoryData.First().Timestamp).ToString()}[/]");

				string periodsIncludedColor = recentHistory.Count < customPeriods ? "red" : "green";
				table.AddRow("Periods included", $"[bold {periodsIncludedColor}]{recentHistory.Count()}/{customPeriods}[/]");

				string priceChangeColor = priceChangeWindow >= 0 ? "green" : "red";
				table.AddRow($"Price Change", $"[bold {priceChangeColor}]{priceChangeWindow:N2}%[/]");

				table.AddRow($"RSI ({recentHistory.Count})", $"[bold green]{rsi:N2}[/]");
				table.AddRow($"SMA ({recentHistory.Count})", $"[bold green]${sma:N2}[/]");
				table.AddRow($"EMA ({recentHistory.Count})", $"[bold green]${ema:N2}[/]");

				table.AddRow("MACD", $"[bold green]${macd:N2}[/]");
				table.AddRow("Best Short Period", $"[bold green]{bestShortPeriod}[/]");
				table.AddRow("Best Long Period", $"[bold green]{bestLongPeriod}[/]");
				table.AddRow("Best Signal Period", $"[bold green]{bestSignalPeriod}[/]");

				table.AddRow("Bollinger Bands", $"[bold green]Middle: {middleBand:N2}, Upper: {upperBand:N2}, Lower: {lowerBand:N2}[/]");
				table.AddRow("ATR", $"[bold green]{atr:N2}[/]");

				table.AddRow("Volatility", $"[bold green]{volatility:P2}[/]");
				table.AddRow("Adjusted Stop-Loss Threshold", $"[bold green]{adjustedStopLoss:P2}[/]");
				table.AddRow("Adjusted Profit-Taking Threshold", $"[bold green]{adjustedProfitTaking:P2}[/]");

				// Alert logic for Bollinger Bands
				if (coin.Value > upperBand)
				{
					table.AddRow("Alert", $"[bold red]{coin.Key.ToUpper()} price crossed above the upper Bollinger Band![/]");
				}
				else if (coin.Value < lowerBand)
				{
					table.AddRow("Alert", $"[bold red]{coin.Key.ToUpper()} price crossed below the lower Bollinger Band![/]");
				}

				// Alert logic for ATR
				decimal atrThreshold = 0.05m; // Example threshold for ATR
				if (atr > atrThreshold)
				{
					table.AddRow("Alert", $"[bold red]{coin.Key.ToUpper()} ATR value is high, indicating high volatility![/]");
				}

				// Market sentiment analysis
				string sentiment = "NEUTRAL";
				if (rsi < 30) sentiment = "OVERSOLD";
				else if (rsi > 70) sentiment = "OVERBOUGHT";

				table.AddRow("Market Sentiment", $"[bold green]{sentiment}[/]");

				// Use the machine learning model to predict future price
				try
				{
					var cryptoData = new CryptoData
					{
						Price = (float)coin.Value,
						SMA = (float)sma,
						EMA = (float)ema,
						RSI = (float)rsi,
						MACD = (float)macd
					};

					float? predictedPrice = mlService?.Predict(cryptoData);
					table.AddRow("Predicted Price", $"[bold green]${predictedPrice:N2}[/]");
				}
				catch (Exception ex)
				{
					table.AddRow("Prediction", $"[bold red]Error: {ex.Message}[/]");
				}

				AnsiConsole.Write(table);

				var operationsTable = new Table();
				operationsTable.AddColumn("Operation");
				operationsTable.AddColumn("Details");

				if (!analysisOnly && operationsAllowed)
				{
					// Stop-loss and profit-taking strategy
					if (runtimeContext.Portfolio.ContainsKey(coin.Key) && runtimeContext.Portfolio[coin.Key] > 0)
					{
						decimal initialInvestment = runtimeContext.InitialInvestments.ContainsKey(coin.Key) ? runtimeContext.InitialInvestments[coin.Key] : 0;
						decimal currentValue = runtimeContext.Portfolio[coin.Key] * coin.Value;
						decimal fee = currentValue * settingsService.Settings.TransactionFeeRate;
						decimal profitOrLoss = (currentValue - initialInvestment - fee) / initialInvestment;

						if (profitOrLoss <= adjustedStopLoss)
						{
							operationsTable.AddRow("STOP-LOSS", $"[bold red]Selling {coin.Key.ToUpper()} to prevent further loss.[/]");
							var sellResult = tradeOperations.Sell(coin.Key, coin.Value);
							foreach (var result in sellResult)
							{
								operationsTable.AddRow("SELL Result", result);
							}
							operationPerfomed = true;
						}
						else if (profitOrLoss >= adjustedProfitTaking)
						{
							operationsTable.AddRow("PROFIT-TAKING", $"[bold green]Selling {coin.Key.ToUpper()} to secure profit.[/]");
							var sellResult = tradeOperations.Sell(coin.Key, coin.Value);
							foreach (var result in sellResult)
							{
								operationsTable.AddRow("SELL Result", result);
							}
							operationPerfomed = true;
						}
					}

					// Trailing Stop-Loss
					UpdateTrailingStopLoss(coin.Key, coin.Value, settingsService.Settings.TrailingStopLossPercentage);
					decimal trailingStopLoss = GetTrailingStopLoss(coin.Key) ?? decimal.MaxValue;
					if (coin.Value <= trailingStopLoss)
					{
						operationsTable.AddRow("TRAILING STOP-LOSS", $"[bold red]Selling {coin.Key.ToUpper()} due to trailing stop-loss.[/]");
						var sellResult = tradeOperations.Sell(coin.Key, coin.Value);
						foreach (var result in sellResult)
						{
							operationsTable.AddRow("SELL Result", result);
						}
						operationPerfomed = true;
					}

					// Dollar-Cost Averaging (DCA)
					string[] dollarCostAveraging = ExecuteDCA(coin.Key, settingsService.Settings.DollarCostAveragingAmount, coin.Value, TimeSpan.FromSeconds(settingsService.Settings.DollarCostAveragingSecondsInterval));
					foreach (var result in dollarCostAveraging)
					{
						operationsTable.AddRow("DollarCostAveraging", result);
						operationPerfomed = true;
					}

					// Trading signals with confidence levels
					if (rsi < 30 && coin.Value < sma && coin.Value < ema && macd < 0 && coin.Value < lowerBand && atr > 0)
					{
						decimal confidence = (30 - rsi) / 30 * 100;
						operationsTable.AddRow("BUY Signal", $"[bold green]Confidence: {confidence:N2}%[/]");

						if (runtimeContext.Balance > 0)
						{
							if (operationsAllowed)
							{
								var buyResult = tradeOperations.Buy(coin.Key, null, coin.Value);
								foreach (var result in buyResult)
								{
									operationsTable.AddRow("BUY Result", result);
								}
								operationPerfomed = true;
							}
							else
							{
								operationsTable.AddRow("BUY Operation", $"[bold red]Skipping buy operation because analysis is not valid.[/]");
							}
						}
					}
					else if (rsi > 70 && runtimeContext.Portfolio.ContainsKey(coin.Key) && runtimeContext.Portfolio[coin.Key] > 0 && coin.Value > sma && coin.Value > ema && macd > 0 && coin.Value > upperBand && atr > 0)
					{
						decimal confidence = (rsi - 70) / 30 * 100;
						operationsTable.AddRow("SELL Signal", $"[bold cyan]Confidence: {confidence:N2}%[/]");

						if (operationsAllowed)
						{
							var sellResult = tradeOperations.Sell(coin.Key, coin.Value);
							foreach (var result in sellResult)
							{
								operationsTable.AddRow("SELL Result", result);
							}
							operationPerfomed = true;
						}
						else
						{
							operationsTable.AddRow("SELL Operation", $"[bold red]Skipping sell operation because analysis is not valid.[/]");
						}
					}

					if (operationsTable.Rows.Count > 0)
					{
						AnsiConsole.Write(operationsTable);
					}
				}
			}

			if (operationPerfomed)
			{
				AnsiConsole.MarkupLine($"\n[bold cyan]Current balance[/]:");
				ShowBalance(prices, true, false);
			}

			AnsiConsole.MarkupLine($"\n[bold yellow]=== End of Analysis - Period Counter: {runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");
		}

		private static (decimal stopLossThreshold, decimal profitTakingThreshold) AdjustThresholdsBasedOnVolatility(decimal volatility)
		{
			decimal baseStopLoss = settingsService.Settings.StopLossThreshold;
			decimal baseProfitTaking = settingsService.Settings.ProfitTakingThreshold;

			// Adjust thresholds based on volatility
			decimal adjustedStopLoss = baseStopLoss * (1 + volatility);
			decimal adjustedProfitTaking = baseProfitTaking * (1 + volatility);

			return (adjustedStopLoss, adjustedProfitTaking);
		}

		private static List<(decimal Price, DateTime Timestamp)> GetRecentHistoryRows(string coin, int rowCount)
		{
			var recentHistory = new List<(decimal Price, DateTime Timestamp)>();
			using (var conn = new SQLiteConnection($"Data Source={settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = @"
            SELECT price, timestamp FROM Prices
            WHERE name = @name
            ORDER BY timestamp DESC
            LIMIT @rowCount;"; // Limit the number of rows returned
				using (var cmd = new SQLiteCommand(query, conn))
				{
					cmd.Parameters.AddWithValue("@name", coin);
					cmd.Parameters.AddWithValue("@rowCount", rowCount);
					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							decimal price = reader.GetDecimal(0);
							DateTime timestamp = reader.GetDateTime(1);
							recentHistory.Add((price, timestamp));
						}
					}
				}
			}
			recentHistory.Reverse(); // Reverse the list to get chronological order
			return recentHistory;
		}
	}

	public class HistoricalData
	{
		public string Name { get; set; } = "###";
		public decimal Price { get; set; }
		public decimal SMA { get; set; }
		public decimal EMA { get; set; }
		public decimal RSI { get; set; }
		public decimal MACD { get; set; }
		public DateTime Timestamp { get; set; }
		public decimal BollingerUpper { get; set; }
		public decimal BollingerLower { get; set; }
		public decimal ATR { get; set; }
		public decimal Volatility { get; set; }
	}

	public class CryptoData
	{
		public float Price { get; set; }
		public float SMA { get; set; }
		public float EMA { get; set; }
		public float RSI { get; set; }
		public float MACD { get; set; }
		public float BollingerUpper { get; set; }
		public float BollingerLower { get; set; }
		public float ATR { get; set; }
		public float Volatility { get; set; }
		public float Label { get; set; } // The label is the target value (e.g., future price)

		public override string ToString()
		{
			return $"Price: {Price}, SMA: {SMA}, EMA: {EMA}, RSI: {RSI}, MACD: {MACD}, BollingerUpper: {BollingerUpper}, BollingerLower: {BollingerLower}, ATR: {ATR}, Volatility: {Volatility}, Label: {Label}";
		}
	}
}
