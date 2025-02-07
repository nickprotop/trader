using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data.Entity.ModelConfiguration.Configuration;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using trader.Models;
using trader.Services;

namespace Trader
{
	public class Trader
	{
		private readonly ITradeOperations _tradeOperations;
		private readonly IMachineLearningService _mlService;
		private readonly IDatabaseService _databaseService;
		private readonly ISettingsService _settingsService;
		private readonly RuntimeContext _runtimeContext;
		private readonly HttpClient httpClient = new HttpClient();

		private bool isRunning = true;
		private int consoleWidth = Console.WindowWidth;
		private const int headerHeight = 4;
		private Window currentWindow = Window.MainMenu;
		private int visibleItems = Console.WindowHeight - headerHeight - footerHeight; // Content area height
		private int scrollPosition = 0;
		private const int footerHeight = 1;
		private string footerText = string.Empty;
		private string subMenuText = string.Empty;

		private readonly object consoleLock = new object();

		private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		private CancellationToken cancellationToken;

		private string[]? previousContent = null;
		private int previousScrollPosition = -1;

		private Dictionary<Window, string[]> contentList = new Dictionary<Window, string[]>
		{
			{ Window.MainMenu, new string[] { "Main Menu" } },
			{ Window.LiveAnalysis, new string[] { "" } },
			{ Window.Balance, new string[] { "Balance and Portfolio" } },
			{ Window.Transactions, new string[] { "Transaction History" } },
			{ Window.Operations, new string[] { "Operations" } },
			{ Window.Statistics, new string[] { "Database Statistics" } }
		};

		private enum Window
		{ MainMenu, LiveAnalysis, Balance, Transactions, Statistics, Operations }

		public Trader(
			ITradeOperations tradeOperations,
			IMachineLearningService mlService,
			IDatabaseService databaseService,
			ISettingsService settingsService,
			RuntimeContext runtimeContext)
		{
			_tradeOperations = tradeOperations;
			_mlService = mlService;
			_databaseService = databaseService;
			_settingsService = settingsService;
			_runtimeContext = runtimeContext;
		}

		public async Task Run(string[] args)
		{
			await Task.Delay(100); // Wait for the console to initialize

			cancellationToken = cancellationTokenSource.Token;

			DateTime nextIterationTime = DateTime.UtcNow;
			Dictionary<string, decimal> prices = new Dictionary<string, decimal>();

			bool performResetDatabase = args.Contains("-c");

			Console.CursorVisible = false;

			if (performResetDatabase)
			{
				AnsiConsole.MarkupLine("[bold red]\nClearing database...[/]");
				ResetDatabase();
			}
			else
			{
				try
				{
					_databaseService.InitializeRuntimeContext();
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

			contentList[Window.MainMenu] = CaptureAnsiConsoleMarkup(() =>
			{
				AnsiConsole.MarkupLine("[bold yellow]=== Welcome to the Crypto Trading Bot ===[/]");

				// Load the model if it exists
				if (File.Exists("model.zip"))
				{
					_mlService.LoadModel("model.zip");
					AnsiConsole.MarkupLine("[bold green]\n=== Model loaded successfully! ===[/]");
				}
				else
				{
					TrainAiModel(_mlService);
				}

				PrintProgramParameters();
			});

			var updateInfo = Task.Run(UpdateInfo);

			var backgroundTask = Task.Run(async () =>
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					if (DateTime.UtcNow >= nextIterationTime)
					{
						lock (consoleLock)
						{
							AddContentToExistingWindow(Window.LiveAnalysis, CaptureAnsiConsoleMarkup(() =>
							{
								prices = GetCryptoPrices().Result;
								if (prices.Count != 0)
								{
									StoreIndicatorsInDatabase(prices);
									AnalyzeIndicators(prices, _settingsService.Settings.CustomPeriods, _settingsService.Settings.CustomIntervalSeconds * _settingsService.Settings.CustomPeriods, false);

									AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update at {DateTime.UtcNow.AddSeconds(_settingsService.Settings.CustomIntervalSeconds)} seconds... ===[/]");
								}
							}));
						}

						// Update the next iteration time
						nextIterationTime = DateTime.UtcNow.AddSeconds(_settingsService.Settings.CustomIntervalSeconds);
					}

					await Task.Delay(100, cancellationToken);
				}
			}, cancellationToken);

			ActivateWindow(Window.MainMenu);

			try
			{
				while (isRunning)
				{
					lock (consoleLock)
					{
						HandleResize();
						DrawHeader();
						DrawFooter();
						DrawScrollableContent(currentWindow);
					}

					// Check for console input
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(intercept: true);

						await HandleInput(key.Key);
					}
				}
			}
			catch (Exception ex)
			{
				AnsiConsole.Clear();
				AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
			}
		}

		private void AddContentToExistingWindow(Window window, string[] newContent)
		{
			if (contentList.TryGetValue(window, out var existingContent))
			{
				contentList[window] = existingContent.Concat(newContent).ToArray();
			}
			else
			{
				contentList[window] = newContent;
			}
		}

		private string[] CaptureAnsiConsoleMarkup(Action action)
		{
			using var writer = new StringWriter();
			var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

			console.Profile.Width = Console.WindowWidth > 100 ? 100 : Console.WindowWidth;
			console.Profile.Height = Console.WindowHeight;

			// Temporarily replace the global AnsiConsole with our custom console
			var originalConsole = AnsiConsole.Console;
			AnsiConsole.Console = console;

			try
			{
				// Run the function using the custom console
				action.Invoke();
			}
			finally
			{
				// Restore the original AnsiConsole
				AnsiConsole.Console = originalConsole;
			}

			// Convert captured output to a string array
			return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		}

		private void DrawScrollableContent(Window window, bool forceRedraw = false)
		{
			if (contentList.TryGetValue(currentWindow, out string[]? content) && content != null)
			{
				if (!forceRedraw)
				{
					if (content.SequenceEqual(previousContent ?? new string[0]) && scrollPosition == previousScrollPosition)
					{
						// No need to redraw the content area
						return;
					}
				}

				ClearContentArea();
				Console.CursorVisible = false;

				if (content.Length == 0)
				{
					Console.SetCursorPosition(2, headerHeight);
					AnsiConsole.MarkupLine("[red]No content available.[/]");
					return;
				}

				int itemsToDisplay = Math.Min(visibleItems, content.Length);
				int endPosition = Math.Min(scrollPosition + itemsToDisplay, content.Length);

				for (int i = scrollPosition; i < endPosition; i++)
				{
					Console.SetCursorPosition(0, headerHeight + (i - scrollPosition));
					AnsiConsole.WriteLine($"{content[i]}");
				}

				// Update previous content and scroll position
				previousContent = content;
				previousScrollPosition = scrollPosition;
			}
		}

		private void ActivateWindow(Window window)
		{
			// Clear only the content area below the header
			ClearContentArea();

			currentWindow = window;

			previousContent = null;
			scrollPosition = 0;

			subMenuText = string.Empty;
			SetFooterText("(c) Nikolaos Protopapas");

			SetFooterText(string.Empty);

			switch (window)
			{
				case Window.MainMenu:
					subMenuText = "[red]R[/]eset database | Retrain A[cyan]I[/] model";
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				case Window.Balance:
					contentList[Window.Balance] = CaptureAnsiConsoleMarkup(() =>
					{
						ShowBalance(_runtimeContext.CurrentPrices, true, true);
					});
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				case Window.Statistics:
					contentList[Window.Statistics] = CaptureAnsiConsoleMarkup(() =>
					{
						ShowDatabaseStats();
					});
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				case Window.Transactions:

					ClearContentArea();

					var (sortColumn, sortOrder) = PromptForSortOptions();
					bool showAllRecords = PromptForShowAllRecords();
					int recordsPerPage = showAllRecords ? int.MaxValue : PromptForRecordsPerPage();

					contentList[Window.Transactions] = CaptureAnsiConsoleMarkup(() =>
					{
						ShowTransactionHistory(sortColumn, sortOrder, showAllRecords, recordsPerPage);
					});
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				default:
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;
			}

			Console.CursorVisible = false;
		}

		private async Task UpdateInfo()
		{
			while (isRunning)
			{
				// Do update work here
				await Task.Delay(100);
			}
		}

		private void DrawHeader()
		{
			Console.SetCursorPosition(0, 0);
			AnsiConsole.Write(
				new Panel(
						$"[bold cyan]M[/]ain | Live [bold cyan]A[/]nalysis ({_runtimeContext.CurrentPeriodIndex}) | [bold cyan]B[/]alance | [bold cyan]S[/]tatistics | [bold cyan]T[/]ransactions | [bold green]Time: {DateTime.Now:HH:mm:ss}[/]\nSubmenu: {subMenuText}")
					.Header("| [blue]Crypto Trading Bot[/] |")
					.RoundedBorder()
					.Expand()
			);
		}

		private void DrawFooter()
		{
			Console.SetCursorPosition(0, Console.WindowHeight - footerHeight);
			string scrollingHints = "Use Up/Down arrows, Page Up/Down, Home/End to scroll";
			string footer = $"[white on blue]{scrollingHints} | [red]Q[/]uit | {footerText.PadRight(Console.WindowWidth - scrollingHints.Length - 7 - 3)}[/]";
			AnsiConsole.Markup(footer);
		}

		private void SetFooterText(string text)
		{
			footerText = text;
			DrawFooter();
		}

		private async Task HandleInput(ConsoleKey key)
		{
			if (key == ConsoleKey.DownArrow)
			{
				if (scrollPosition < contentList[currentWindow].Length - visibleItems)
				{
					scrollPosition++;
				}
			}

			if (key == ConsoleKey.UpArrow)
			{
				if (scrollPosition > 0)
				{
					scrollPosition--;
				}
			}

			if (key == ConsoleKey.PageDown)
			{
				if (scrollPosition < contentList[currentWindow].Length - visibleItems)
				{
					scrollPosition = Math.Min(scrollPosition + visibleItems, contentList[currentWindow].Length - visibleItems);
				}
			}

			if (key == ConsoleKey.PageUp)
			{
				if (scrollPosition > 0)
				{
					scrollPosition = Math.Max(scrollPosition - visibleItems, 0);
				}
			}

			if (key == ConsoleKey.Home)
			{
				scrollPosition = 0;
			}

			if (key == ConsoleKey.End)
			{
				scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
			}

			if (key == ConsoleKey.M)
			{
				ActivateWindow(Window.MainMenu);
			}

			if (key == ConsoleKey.B)
			{
				ActivateWindow(Window.Balance);
			}

			if (key == ConsoleKey.S)
			{
				ActivateWindow(Window.Statistics);
			}

			if (key == ConsoleKey.A)
			{
				ActivateWindow(Window.LiveAnalysis);
			}

			if (key == ConsoleKey.T)
			{
				ActivateWindow(Window.Transactions);
			}

			if (key == ConsoleKey.Q)
			{
				AnsiConsole.Clear();

				AnsiConsole.MarkupLine("[bold red]Exiting the program...[/]");
				cancellationTokenSource.Cancel();
				isRunning = false;
			}

			if (currentWindow == Window.MainMenu)
			{
				if (key == ConsoleKey.R)
				{
					ClearContentArea();

					var prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
					{
						Title = "Reset database?",
						PageSize = 10,
						HighlightStyle = new Style(foreground: Color.Green)
					}.AddChoices("Yes", "No"));

					if (prompt == "Yes")
					{
						AddContentToExistingWindow(Window.MainMenu, CaptureAnsiConsoleMarkup(() =>
						{
							ResetDatabase();
						}));
					}

					DrawScrollableContent(Window.MainMenu, true);
				}

				if (key == ConsoleKey.I)
				{
					var trainModel = Task.Run(() =>
					{
						List<string> trainModelOutput = TrainAiModel(_mlService);
						string[] strings = trainModelOutput.ToArray();

						if (currentWindow == Window.MainMenu)
						{
							AddContentToExistingWindow(Window.MainMenu, strings);
							DrawScrollableContent(Window.MainMenu, true);
						}
					});
				}
			}
		}

		private void HandleResize()
		{
			if (consoleWidth != Console.WindowWidth || Console.WindowHeight != visibleItems + headerHeight + footerHeight)
			{
				consoleWidth = Console.WindowWidth;
				visibleItems = Console.WindowHeight - headerHeight - footerHeight;
				Console.Clear();
				previousContent = null;
				DrawHeader();
				DrawFooter();
			}
		}

		private void ClearContentArea()
		{
			for (int i = headerHeight; i < Console.WindowHeight; i++)
			{
				Console.SetCursorPosition(0, i);
				Console.Write(new string(' ', Console.WindowWidth));
			}
			Console.SetCursorPosition(0, headerHeight);
		}

		private void PrintProgramParameters()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== App Parameters ===[/]\n");

			var table = new Table();
			table.AddColumn(new TableColumn("[bold yellow]Parameter[/]").LeftAligned());
			table.AddColumn(new TableColumn("[bold yellow]Value[/]").LeftAligned());

			table.AddRow("[bold cyan]API URL[/]", _settingsService.Settings.API_URL);
			table.AddRow("[bold cyan]Database Path[/]", _settingsService.Settings.DbPath);
			table.AddRow("[bold cyan]Period interval (Seconds)[/]", _settingsService.Settings.CustomIntervalSeconds.ToString());
			table.AddRow("[bold cyan]Analyze Periods/seconds[/]", $"{_settingsService.Settings.CustomPeriods}/{_settingsService.Settings.CustomIntervalSeconds * _settingsService.Settings.CustomPeriods}");
			table.AddRow("[bold cyan]Stop-Loss Threshold[/]", _settingsService.Settings.StopLossThreshold.ToString("P"));
			table.AddRow("[bold cyan]Profit-Taking Threshold[/]", _settingsService.Settings.ProfitTakingThreshold.ToString("P"));
			table.AddRow("[bold cyan]Starting Balance[/]", _settingsService.Settings.StartingBalance.ToString("C"));
			table.AddRow("[bold cyan]Max Investment Per Coin[/]", _settingsService.Settings.MaxInvestmentPerCoin.ToString("C"));
			table.AddRow("[bold cyan]Transaction Fee Rate[/]", _settingsService.Settings.TransactionFeeRate.ToString("P"));
			table.AddRow("[bold cyan]Trailing stop loss percentage[/]", _settingsService.Settings.TrailingStopLossPercentage.ToString("P"));
			table.AddRow("[bold cyan]Dollar cost averaging amount[/]", _settingsService.Settings.DollarCostAveragingAmount.ToString("C"));
			table.AddRow("[bold cyan]Dollar cost averaging time interval (seconds)[/]", _settingsService.Settings.DollarCostAveragingSecondsInterval.ToString());

			AnsiConsole.Write(table);
			AnsiConsole.MarkupLine("\n[bold yellow]==========================[/]");
		}

		private void ResetDatabase()
		{
			if (File.Exists(_settingsService.Settings.DbPath))
			{
				File.Delete(_settingsService.Settings.DbPath);
			}

			_databaseService.InitializeRuntimeContext();

			AnsiConsole.MarkupLine("[bold red]Database has been reset. Starting over...[/]");
		}

		private List<string> TrainAiModel(IMachineLearningService mlService)
		{
			List<string> output = new List<string>();

			output.Add("\n=== Train AI model... ===");

			try
			{
				var historicalData = _databaseService.LoadHistoricalData();

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
					Label = (float)h.Price
				}).ToList();
				mlService.TrainModel(trainingData);

				mlService.SaveModel("model.zip");
				output.Add("=== Model tained and saved successfully! ===");
			}
			catch (Exception ex)
			{
				output.Add($"Error: {ex.Message}\n]");
				output.Add("=== End of model train ===");
			}

			return output;
		}

		private async Task<Dictionary<string, decimal>> GetCryptoPrices()
		{
			try
			{
				AnsiConsole.MarkupLine("\n[bold yellow]=== Fetching cryptocurrency prices... ===[/]");

				var response = await httpClient.GetStringAsync(_settingsService.Settings.API_URL);
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
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				return new Dictionary<string, decimal>();
			}
		}

		private void StoreIndicatorsInDatabase(Dictionary<string, decimal> prices)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				foreach (var coin in prices)
				{
					var recentHistory = _databaseService.GetRecentPrices(coin.Key, 60);

					List<decimal> recentPrices = recentHistory.Select(pt => pt.Price).ToList();

					decimal rsi = IndicatorCalculations.CalculateRSI(recentPrices, recentHistory.Count);
					decimal sma = IndicatorCalculations.CalculateSMA(recentPrices, recentHistory.Count);
					decimal ema = IndicatorCalculations.CalculateEMASingle(recentPrices, recentHistory.Count);
					var (macd, _, _, _) = IndicatorCalculations.CalculateMACD(recentPrices);
					var (bollingerUpper, bollingerLower, _) = IndicatorCalculations.CalculateBollingerBands(recentPrices, recentPrices.Count);
					decimal atr = IndicatorCalculations.CalculateATR(recentPrices, recentPrices.Count);
					decimal volatility = IndicatorCalculations.CalculateVolatility(recentPrices, recentPrices.Count);

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

		private void BacktestStrategy(List<HistoricalData> historicalData)
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Backtest strategy analysis ===[/]\n");

			decimal initialBalance = _settingsService.Settings.StartingBalance;
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

				for (int i = _settingsService.Settings.CustomPeriods; i < coinHistory.Count; i++)
				{
					var recentHistory = coinHistory.Skip(i - _settingsService.Settings.CustomPeriods).Take(_settingsService.Settings.CustomPeriods).Select(h => h.Price).ToList();
					var data = coinHistory[i];

					decimal rsi = IndicatorCalculations.CalculateRSI(recentHistory, recentHistory.Count);
					decimal sma = IndicatorCalculations.CalculateSMA(recentHistory, recentHistory.Count);
					decimal ema = IndicatorCalculations.CalculateEMASingle(recentHistory, recentHistory.Count);
					var (macd, bestShortPeriod, bestLongPeriod, bestSignalPeriod) = IndicatorCalculations.CalculateMACD(recentHistory);
					decimal priceChangeWindow = IndicatorCalculations.CalculatePriceChange(recentHistory);

					// Calculate Bollinger Bands
					var (middleBand, upperBand, lowerBand) = IndicatorCalculations.CalculateBollingerBands(recentHistory, _settingsService.Settings.CustomPeriods);

					// Calculate ATR (assuming high, low, and close prices are available)
					decimal atr = IndicatorCalculations.CalculateATR(recentHistory, recentHistory.Count);

					// Calculate volatility
					decimal volatility = IndicatorCalculations.CalculateVolatility(recentHistory, _settingsService.Settings.CustomPeriods);
					var (adjustedStopLoss, adjustedProfitTaking) = AdjustThresholdsBasedOnVolatility(volatility);

					// Trading signals
					if (rsi < 30 && data.Price < sma && data.Price < ema && macd < 0 && data.Price < lowerBand && atr > 0)
					{
						// Buy signal
						decimal quantity = balance / data.Price;
						decimal cost = quantity * data.Price;
						decimal fee = cost * _settingsService.Settings.TransactionFeeRate;
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
						decimal fee = amountToSell * _settingsService.Settings.TransactionFeeRate;
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

		private void SellCoinFromPortfolio()
		{
			if (_runtimeContext.Portfolio.Count == 0)
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

			foreach (var coin in _runtimeContext.Portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = _runtimeContext.CurrentPrices.ContainsKey(coin.Key) ? _runtimeContext.CurrentPrices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;

					// Calculate gain or loss
					decimal averageCostBasis = _runtimeContext.TotalCostPerCoin[coin.Key] / _runtimeContext.TotalQuantityPerCoin[coin.Key];
					decimal costOfHeldCoins = averageCostBasis * coin.Value;
					decimal gainOrLoss = value - costOfHeldCoins;

					// Calculate gain or loss including fee
					decimal fee = value * _settingsService.Settings.TransactionFeeRate;
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
					.AddChoices(_runtimeContext.Portfolio.Keys.Where(k => _runtimeContext.Portfolio[k] > 0).Append("Cancel").ToArray())
			);

			if (coinToSell == "Cancel")
			{
				AnsiConsole.MarkupLine("[bold yellow]Sell operation canceled.[/]");
				return;
			}

			if (_runtimeContext.Portfolio.ContainsKey(coinToSell) && _runtimeContext.Portfolio[coinToSell] > 0)
			{
				decimal currentPrice = _runtimeContext.CurrentPrices[coinToSell];
				decimal quantityToSell = _runtimeContext.Portfolio[coinToSell];
				decimal totalValue = quantityToSell * currentPrice;

				// Calculate gain or loss
				decimal averageCostBasis = _runtimeContext.TotalCostPerCoin[coinToSell] / _runtimeContext.TotalQuantityPerCoin[coinToSell];
				decimal costOfSoldCoins = averageCostBasis * quantityToSell;
				decimal gainOrLoss = totalValue - costOfSoldCoins;

				var sellResult = _tradeOperations.Sell(coinToSell, currentPrice);
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

		private void BuyCoin()
		{
			var availableCoins = _runtimeContext.CurrentPrices.Keys.ToList();

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
				decimal currentPrice = _runtimeContext.CurrentPrices[coin];
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

			decimal price = _runtimeContext.CurrentPrices[coinToBuy];
			decimal quantityToBuy = AnsiConsole.Prompt(
				new TextPrompt<decimal>($"Enter the quantity of {coinToBuy.ToUpper()} to buy (0 to cancel):")
			);

			if (quantityToBuy == 0) return;

			decimal totalCost = quantityToBuy * price;

			if (_runtimeContext.Balance >= totalCost)
			{
				var buyResult = _tradeOperations.Buy(coinToBuy, quantityToBuy, price);
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

		private void ShowTransactionHistory(string? sortColumn, string? sortOrder, bool showAllRecords, int recordsPerPage)
		{
			int currentPage = 1;
			int totalRecords = 0;
			decimal totalFees = 0;

			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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
		}

		private void ShowBalance(Dictionary<string, decimal> prices, bool verbose = true, bool showTitle = true)
		{
			if (showTitle) AnsiConsole.MarkupLine($"\n[bold yellow]=== Balance{(verbose ? " and portfolio" : string.Empty)} Report ===[/]\n");

			decimal portfolioWorth = 0;
			decimal totalInvestment = 0;
			decimal totalFees = _databaseService.CalculateTotalFees(); // Calculate total fees

			// Calculate the total portfolio value and total investment
			foreach (var coin in _runtimeContext.Portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;
					portfolioWorth += value;

					decimal initialInvestment = _runtimeContext.TotalCostPerCoin.ContainsKey(coin.Key) ? _runtimeContext.TotalCostPerCoin[coin.Key] : 0;
					totalInvestment += initialInvestment;
				}
			}

			decimal totalWorth = _runtimeContext.Balance + portfolioWorth;
			decimal initialBalance = _settingsService.Settings.StartingBalance; // Starting balance
			decimal totalProfitOrLoss = totalWorth - initialBalance;
			decimal percentageChange = initialBalance > 0 ? (totalProfitOrLoss / initialBalance) * 100 : 0;

			// Calculate current investment gain or loss
			decimal currentInvestmentGainOrLoss = portfolioWorth - totalInvestment;
			decimal currentInvestmentPercentageChange = totalInvestment > 0 ? (currentInvestmentGainOrLoss / totalInvestment) * 100 : 0;

			// Calculate total gain or loss from all transactions
			decimal totalTransactionGainOrLoss = 0;
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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
			decimal currentGainOrLossIncludingFees = currentInvestmentGainOrLoss - (portfolioWorth * _settingsService.Settings.TransactionFeeRate);

			// Create a table for balance information
			var balanceTable = new Table();
			balanceTable.AddColumn("Description");
			balanceTable.AddColumn("Value");

			balanceTable.AddRow("Total Investment across all coins", $"[bold cyan]{totalInvestment:C}[/]");
			balanceTable.AddRow("Current balance", $"[bold green]{_runtimeContext.Balance:C}[/]");
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

					foreach (var coin in _runtimeContext.Portfolio)
					{
						if (coin.Value > 0)
						{
							decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
							decimal value = coin.Value * currentPrice;
							decimal initialInvestment = _runtimeContext.TotalCostPerCoin.ContainsKey(coin.Key) ? _runtimeContext.TotalCostPerCoin[coin.Key] : 0;
							decimal profitOrLoss = value - initialInvestment;

							// Calculate profit/loss including fee
							decimal fee = value * _settingsService.Settings.TransactionFeeRate;
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

		private void ShowDatabaseStats()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Database statistics ===[/]\n");

			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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
							int inMemoryCount = _runtimeContext.CachedPrices.ContainsKey(name) ? _runtimeContext.CachedPrices[name].Count : 0;

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
				decimal totalFees = _databaseService.CalculateTotalFees();
				AnsiConsole.MarkupLine($"\n[bold yellow]Total Fees Incurred: [/][bold cyan]{totalFees:C}[/]");
			}
			AnsiConsole.MarkupLine("\n[bold yellow]=== End of Database statistics ===[/]");
		}

		private void AnalyzeIndicators(Dictionary<string, decimal> prices, int customPeriods, int analysisWindowSeconds, bool analysisOnly)
		{
			DateTime startAnalysisTimeStamp = DateTime.Now.ToUniversalTime();

			if (!analysisOnly) _runtimeContext.CurrentPeriodIndex++;

			AnsiConsole.MarkupLine($"\n[bold yellow]=== Market Analysis Report - Period Counter: {_runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			bool operationPerfomed = false;

			foreach (var coin in prices)
			{
				bool operationsAllowed = true;

				AnsiConsole.MarkupLine($"\n[bold cyan]{coin.Key.ToUpper()}[/]:");

				if (!_runtimeContext.CachedPrices.ContainsKey(coin.Key))
					continue;

				var recentHistoryData = _databaseService.GetRecentPrices(coin.Key, _settingsService.Settings.CustomPeriods);

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

				if (_runtimeContext.CheckForValidTimeInterval)
				{
					int bufferSeconds = _settingsService.Settings.CustomIntervalSeconds * 2; // Buffer for capture delays
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
				var (middleBand, upperBand, lowerBand) = IndicatorCalculations.CalculateBollingerBands(recentHistory, recentHistory.Count);

				// Calculate ATR (assuming high, low, and close prices are available)
				decimal atr = IndicatorCalculations.CalculateATR(recentHistory, recentHistory.Count);

				// Calculate volatility
				decimal volatility = IndicatorCalculations.CalculateVolatility(recentHistory, recentHistory.Count);
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

					float? predictedPrice = _mlService?.Predict(cryptoData);
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
					if (_runtimeContext.Portfolio.ContainsKey(coin.Key) && _runtimeContext.Portfolio[coin.Key] > 0)
					{
						decimal initialInvestment = _runtimeContext.InitialInvestments.ContainsKey(coin.Key) ? _runtimeContext.InitialInvestments[coin.Key] : 0;
						decimal currentValue = _runtimeContext.Portfolio[coin.Key] * coin.Value;
						decimal fee = currentValue * _settingsService.Settings.TransactionFeeRate;
						decimal profitOrLoss = (currentValue - initialInvestment - fee) / initialInvestment;

						if (profitOrLoss <= adjustedStopLoss)
						{
							operationsTable.AddRow("STOP-LOSS", $"[bold red]Selling {coin.Key.ToUpper()} to prevent further loss.[/]");
							var sellResult = _tradeOperations.Sell(coin.Key, coin.Value);
							foreach (var result in sellResult)
							{
								operationsTable.AddRow("SELL Result", result);
							}
							operationPerfomed = true;
						}
						else if (profitOrLoss >= adjustedProfitTaking)
						{
							operationsTable.AddRow("PROFIT-TAKING", $"[bold green]Selling {coin.Key.ToUpper()} to secure profit.[/]");
							var sellResult = _tradeOperations.Sell(coin.Key, coin.Value);
							foreach (var result in sellResult)
							{
								operationsTable.AddRow("SELL Result", result);
							}
							operationPerfomed = true;
						}
					}

					// Trailing Stop-Loss
					UpdateTrailingStopLoss(coin.Key, coin.Value, _settingsService.Settings.TrailingStopLossPercentage);
					decimal trailingStopLoss = _databaseService.GetTrailingStopLoss(coin.Key) ?? decimal.MaxValue;
					if (coin.Value <= trailingStopLoss)
					{
						operationsTable.AddRow("TRAILING STOP-LOSS", $"[bold red]Selling {coin.Key.ToUpper()} due to trailing stop-loss.[/]");
						var sellResult = _tradeOperations.Sell(coin.Key, coin.Value);
						foreach (var result in sellResult)
						{
							operationsTable.AddRow("SELL Result", result);
						}
						operationPerfomed = true;
					}

					// Dollar-Cost Averaging (DCA)
					string[] dollarCostAveraging = ExecuteDCA(coin.Key, _settingsService.Settings.DollarCostAveragingAmount, coin.Value, TimeSpan.FromSeconds(_settingsService.Settings.DollarCostAveragingSecondsInterval));
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

						if (_runtimeContext.Balance > 0)
						{
							if (operationsAllowed)
							{
								var buyResult = _tradeOperations.Buy(coin.Key, null, coin.Value);
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
					else if (rsi > 70 && _runtimeContext.Portfolio.ContainsKey(coin.Key) && _runtimeContext.Portfolio[coin.Key] > 0 && coin.Value > sma && coin.Value > ema && macd > 0 && coin.Value > upperBand && atr > 0)
					{
						decimal confidence = (rsi - 70) / 30 * 100;
						operationsTable.AddRow("SELL Signal", $"[bold cyan]Confidence: {confidence:N2}%[/]");

						if (operationsAllowed)
						{
							var sellResult = _tradeOperations.Sell(coin.Key, coin.Value);
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

			AnsiConsole.MarkupLine($"\n[bold yellow]=== End of Analysis - Period Counter: {_runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");
		}

		public void UpdateTrailingStopLoss(string coin, decimal currentPrice, decimal trailingPercentage)
		{
			decimal? stopLoss = _databaseService.GetTrailingStopLoss(coin);
			if (!stopLoss.HasValue)
			{
				stopLoss = currentPrice * (1 - trailingPercentage);
				_databaseService.SaveTrailingStopLoss(coin, stopLoss.Value);
			}
			else
			{
				decimal newStopLoss = currentPrice * (1 - trailingPercentage);
				if (newStopLoss > stopLoss.Value)
				{
					_databaseService.SaveTrailingStopLoss(coin, newStopLoss);
				}
			}
		}

		public string[] ExecuteDCA(string coin, decimal amount, decimal currentPrice, TimeSpan interval)
		{
			DateTime? lastPurchaseTime = _databaseService.GetLastPurchaseTime(coin);
			if (!lastPurchaseTime.HasValue || (DateTime.UtcNow - lastPurchaseTime.Value) >= interval)
			{
				decimal quantity = amount / currentPrice;
				var buyResult = _tradeOperations.Buy(coin, quantity, currentPrice);
				_databaseService.SaveDCAConfig(coin, DateTime.UtcNow);

				return buyResult;
			}

			return new string[] { };
		}

		private (decimal stopLossThreshold, decimal profitTakingThreshold) AdjustThresholdsBasedOnVolatility(decimal volatility)
		{
			decimal baseStopLoss = _settingsService.Settings.StopLossThreshold;
			decimal baseProfitTaking = _settingsService.Settings.ProfitTakingThreshold;

			// Adjust thresholds based on volatility
			decimal adjustedStopLoss = baseStopLoss * (1 + volatility);
			decimal adjustedProfitTaking = baseProfitTaking * (1 + volatility);

			return (adjustedStopLoss, adjustedProfitTaking);
		}
	}
}
