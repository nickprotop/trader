using Microsoft.VisualBasic;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data.Entity.ModelConfiguration.Configuration;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO.Pipelines;
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
		private readonly IAnalyzer _analyzer;
		private readonly HttpClient httpClient = new HttpClient();

		private bool isRunning = true;
		private int consoleWidth = Console.WindowWidth;
		private const int headerHeight = 4;
		private Window currentWindow = Window.MainMenu;
		private int visibleItems = Console.WindowHeight - headerHeight - footerHeight;
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
			IAnalyzer analyzer,
			ITradeOperations tradeOperations,
			IMachineLearningService mlService,
			IDatabaseService databaseService,
			ISettingsService settingsService,
			RuntimeContext runtimeContext)
		{
			_analyzer = analyzer;
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
				AnsiConsole.MarkupLine("[bold green]> Welcome to the Crypto Trading Bot[/]");

				// Load the model if it exists
				if (File.Exists("model.zip"))
				{
					_mlService.LoadModel("model.zip");
					AnsiConsole.MarkupLine("[bold green]> AI Model loaded successfully from cache (Consider retrain it to be uptodate)![/]");
				}
				else
				{
					_mlService.TrainModel();
				}
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
							/*
							AddContentToExistingWindow(Window.LiveAnalysis, CaptureAnsiConsoleMarkup(() =>
							{
								prices = GetCryptoPrices().Result;
								if (prices.Count != 0)
								{
									_databaseService.StoreIndicatorsInDatabase(prices);
									var analyzerOperationsPerfomed = _analyzer.AnalyzeIndicators(prices, _settingsService.Settings.CustomPeriods, _settingsService.Settings.CustomIntervalSeconds * _settingsService.Settings.CustomPeriods, false);

									AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update at {DateTime.UtcNow.AddSeconds(_settingsService.Settings.CustomIntervalSeconds)} seconds... ===[/]");

									if (analyzerOperationsPerfomed.Count != 0)
									{
										string[] operations = analyzerOperationsPerfomed.ToArray();
										AddContentToExistingWindow(Window.Operations, operations);
									}
								}
							}));
							*/

							string[] analyzerOutput = CaptureAnsiConsoleMarkup(() =>
							{
								prices = GetCryptoPrices().Result;
								if (prices.Count != 0)
								{
									_databaseService.StoreIndicatorsInDatabase(prices);
									var analyzerOperationsPerfomed = _analyzer.AnalyzeIndicators(prices, _settingsService.Settings.CustomPeriods, _settingsService.Settings.CustomIntervalSeconds * _settingsService.Settings.CustomPeriods, false);

									AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update at {DateTime.UtcNow.AddSeconds(_settingsService.Settings.CustomIntervalSeconds)} seconds... ===[/]");

									if (analyzerOperationsPerfomed.Count != 0)
									{
										string[] operations = analyzerOperationsPerfomed.ToArray();
										AddContentToExistingWindow(Window.Operations, operations);
									}
								}

							});

							contentList[Window.LiveAnalysis] = analyzerOutput;
							if (currentWindow == Window.LiveAnalysis) scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
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

						HandleInput(key.Key);
					}
				}
			}
			catch (Exception ex)
			{
				AnsiConsole.Clear();
				AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
			}
		}

		private void HandleInput(ConsoleKey key)
		{
			bool handled = false;

			switch (key)
			{
				case ConsoleKey.DownArrow:
					if (scrollPosition < contentList[currentWindow].Length - visibleItems)
					{
						scrollPosition++;
						handled = true;
					}
					break;
				case ConsoleKey.UpArrow:
					if (scrollPosition > 0)
					{
						scrollPosition--;
						handled = true;
					}
					break;
				case ConsoleKey.PageDown:
					if (scrollPosition < contentList[currentWindow].Length - visibleItems)
					{
						scrollPosition = Math.Min(scrollPosition + visibleItems, contentList[currentWindow].Length - visibleItems);
					}
					handled = true;
					break;
				case ConsoleKey.PageUp:
					if (scrollPosition > 0)
					{
						scrollPosition = Math.Max(scrollPosition - visibleItems, 0);
					}
					handled = true;
					break;
				case ConsoleKey.Home:
					scrollPosition = 0;
					handled = true;
					break;
				case ConsoleKey.End:
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					handled = true;
					break;
				case ConsoleKey.M:
					ActivateWindow(Window.MainMenu);
					handled = true;
					break;
				case ConsoleKey.B:
					ActivateWindow(Window.Balance);
					handled = true;
					break;
				case ConsoleKey.S:
					ActivateWindow(Window.Statistics);
					handled = true;
					break;
				case ConsoleKey.A:
					ActivateWindow(Window.LiveAnalysis);
					handled = true;
					break;
				case ConsoleKey.T:
					ActivateWindow(Window.Transactions);
					handled = true;
					break;
				case ConsoleKey.O:
					ActivateWindow(Window.Operations);
					handled = true;
					break;
				case ConsoleKey.Q:
					AnsiConsole.Clear();

					AnsiConsole.MarkupLine("[bold red]Exiting the program...[/]");
					cancellationTokenSource.Cancel();
					isRunning = false;
					handled = true;
					break;
			}
			
			if (!handled)
			{
				switch (currentWindow)
				{
					case Window.MainMenu:
						HandleMainMenuKeyPress(key);
						break;
					case Window.LiveAnalysis:
						HandleLiveAnalysisKeyPress(key);
						break;
					case Window.Operations:
						HandleOperationsKeyPress(key);
						break;
				}
			}
		}

		private void HandleMainMenuKeyPress(ConsoleKey key)
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

				DrawScrollableContent(Window.MainMenu, true, true);
			}

			if (key == ConsoleKey.I)
			{
				var trainModel = Task.Run(() =>
				{
					lock (consoleLock)
					{
						AddContentToExistingWindow(Window.MainMenu, new string[] { $"> Training AI model. Please wait." });
						if (currentWindow == Window.MainMenu)
						{
							DrawScrollableContent(Window.MainMenu, true, true);
						}

						AddContentToExistingWindow(Window.MainMenu, CaptureAnsiConsoleMarkup(() =>
						{
							_mlService.TrainModel();
						}));

						if (currentWindow == Window.MainMenu)
						{
							DrawScrollableContent(Window.MainMenu, true, true);
						}
					}
				});
			}

			if (key == ConsoleKey.P)
			{
				lock (consoleLock)
				{
					AddContentToExistingWindow(Window.MainMenu, CaptureAnsiConsoleMarkup(() =>
					{
						PrintProgramParameters();
					}));
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
				}
			}
		}

		private void HandleOperationsKeyPress(ConsoleKey key)
		{
			if (key == ConsoleKey.U)
			{
				ClearContentArea();

				var availableCoins = _runtimeContext.CurrentPrices.Keys.ToList();

				if (availableCoins.Count == 0)
				{
					AnsiConsole.MarkupLine("[bold red]No coins available to buy.[/]");
					DrawScrollableContent(Window.Operations, true, true);
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
					DrawScrollableContent(Window.Operations, true, true);
					return;
				}

				decimal price = _runtimeContext.CurrentPrices[coinToBuy];
				decimal quantityToBuy = AnsiConsole.Prompt(
					new TextPrompt<decimal>($"Enter the quantity of {coinToBuy.ToUpper()} to buy (0 to cancel):")
				);

				if (quantityToBuy == 0)
				{
					DrawScrollableContent(Window.Operations, true, true);
					return;
				}

				decimal totalCost = quantityToBuy * price;

				AddContentToExistingWindow(Window.Operations, CaptureAnsiConsoleMarkup(() =>
				{
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
				}));

				DrawScrollableContent(Window.Operations, true, true);
			}

			if (key == ConsoleKey.E)
			{
				if (_runtimeContext.Portfolio.Count == 0)
				{
					AddContentToExistingWindow(Window.Operations, new string[] { "[bold red]No coins in the portfolio to sell.[/]" });
					DrawScrollableContent(Window.Operations, true, true);
					return;
				}

				ClearContentArea();

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
					DrawScrollableContent(Window.Operations, true, true);
					return;
				}

				AddContentToExistingWindow(Window.Operations, CaptureAnsiConsoleMarkup(() =>
				{
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
				}));

				DrawScrollableContent(Window.Operations, true, true);
			}
		}

		private void HandleLiveAnalysisKeyPress(ConsoleKey key)
		{
			if (key == ConsoleKey.C)
			{
				_settingsService.Settings.CheckForValidTimeIntervalToPerformAnalysis = !_settingsService.Settings.CheckForValidTimeIntervalToPerformAnalysis;
				subMenuText = $"[cyan]C[/]heck valid timeframe: {_settingsService.Settings.CheckForValidTimeIntervalToPerformAnalysis.ToString()}";
				DrawHeader();
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

			switch (window)
			{
				case Window.MainMenu:
					subMenuText = "[cyan]R[/][red]eset database[/] | Retrain A[cyan]I[/] model | Startup [cyan]P[/]arameters";
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				case Window.LiveAnalysis:
					subMenuText = $"[cyan]C[/]heck valid timeframe: {_settingsService.Settings.CheckForValidTimeIntervalToPerformAnalysis.ToString()}";
					scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);
					break;

				case Window.Operations:
					subMenuText = "B[cyan]u[/]y | S[cyan]e[/]ll";
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

			if (currentWindow == window)
			{
				DrawScrollableContent(window, true, true);
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

		private void DrawScrollableContent(Window window, bool forceRedraw = false, bool scrollToButton = false)
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

				if (scrollToButton) scrollPosition = Math.Max(contentList[currentWindow].Length - visibleItems, 0);

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
						$"[bold cyan]M[/]ain | Live [bold cyan]A[/]nalysis ({_runtimeContext.CurrentPeriodIndex}) | [bold cyan]B[/]alance | [bold cyan]S[/]tatistics | [bold cyan]T[/]ransactions | [bold cyan]O[/]perations | [bold green]Time: {DateTime.Now:HH:mm:ss}[/]\nSubmenu: {subMenuText}")
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
			for (int i = headerHeight; i < Console.WindowHeight - footerHeight; i++)
			{
				Console.SetCursorPosition(0, i);
				Console.Write(new string(' ', Console.WindowWidth));
			}
			Console.SetCursorPosition(0, headerHeight);
		}

		private void PrintProgramParameters()
		{
			AnsiConsole.MarkupLine("\n[bold cyan]Startup Parameters[/]\n");

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
		}

		private void ShowDatabaseStats()
		{
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
		}
	}
}
