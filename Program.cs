using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spectre.Console;
using System.Reflection.Metadata;
using System.Diagnostics;

namespace Trader
{
	public static class Parameters
	{
		public const int CustomIntervalSeconds = 30; // Example interval time in seconds
		public const int CustomPeriods = 60; // Example number of periods
		public const string API_URL = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum,solana,binancecoin,cardano&vs_currencies=usd";
		public const string dbPath = "crypto_prices.db";
		public const decimal stopLossThreshold = -0.05m; // 5% loss
		public const decimal profitTakingThreshold = 0.05m; // 5% gain
		public const decimal maxInvestmentPerCoin = 3000m; // Example maximum investment amount per coin
		public const decimal startingBalance = 10000m; // Starting balance
		public const decimal transactionFeeRate = 0.01m; // 1% transaction fee
	}

	public static class RuntimeContext
	{
		public static decimal balance = Parameters.startingBalance;
		public static Dictionary<string, List<decimal>> priceHistory = new Dictionary<string, List<decimal>>();
		public static Dictionary<string, decimal> portfolio = new Dictionary<string, decimal>();
		public static Dictionary<string, decimal> initialInvestments = new Dictionary<string, decimal>();
		public static Dictionary<string, decimal> totalQuantityPerCoin = new Dictionary<string, decimal>();
		public static Dictionary<string, decimal> totalCostPerCoin = new Dictionary<string, decimal>();
		public static Dictionary<string, decimal> currentPrices = new Dictionary<string, decimal>();
		public static int currentPeriodIndex = 0;
	}

	public class Program
	{
		private static readonly HttpClient client = new HttpClient();
		private static readonly bool isConsoleAvailable = IsConsoleAvailable();
		private static ITradeOperations tradeOperations = new SimulationTradeOperations(RecordTransaction);

		private static async Task Main(string[] args)
		{
			Console.OutputEncoding = System.Text.Encoding.UTF8;

			bool clearPreviousTransactions = args.Contains("-c");

			AnsiConsole.MarkupLine("[bold yellow]=== Welcome to the Crypto Trading Bot ===[/]");

			// Do not load previous transactions
			if (clearPreviousTransactions)
			{
				AnsiConsole.MarkupLine("[bold red]\nClearing database...[/]");
				ResetDatabase();
			}

			InitializeDatabase(clearPreviousTransactions);

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
							ShowBalance(RuntimeContext.currentPrices, true, true);
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
							//BacktestStrategy(LoadHistoricalData());
							AnalyzeIndicators(RuntimeContext.currentPrices, Parameters.CustomPeriods, Parameters.CustomIntervalSeconds * Parameters.CustomPeriods, true);
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
					}

					// Check if the background task is running, and if not, restart it
					if (iterationTask.IsCompleted)
					{
						AnsiConsole.MarkupLine("[bold red]Background task stopped. Restarting...[/]");
						cts = new CancellationTokenSource();
						token = cts.Token;
						iterationTask = StartBackgroundTask(token);
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

		private static Task StartBackgroundTask(CancellationToken token)
		{
			return Task.Run(async () =>
			{
				bool firstRun = true;
				DateTime nextIterationTime = DateTime.UtcNow;
				Dictionary<string, decimal> prices = new Dictionary<string, decimal>();

				try
				{
					while (!token.IsCancellationRequested)
					{
						if (firstRun)
						{
							PrintProgramParameters();
							firstRun = false;
						}

						// Check if it's time for the next iteration
						if (DateTime.UtcNow >= nextIterationTime)
						{
							prices = await GetCryptoPrices();
							StoreIndicatorsInDatabase(prices);
							AnalyzeIndicators(prices, Parameters.CustomPeriods, Parameters.CustomIntervalSeconds * Parameters.CustomPeriods, false);

							PrintMenu();

							// Update the next iteration time
							nextIterationTime = DateTime.UtcNow.AddSeconds(Parameters.CustomIntervalSeconds);
							AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update in {Parameters.CustomIntervalSeconds} seconds... ===[/]");
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

		private static void SellCoinFromPortfolio()
		{
			if (RuntimeContext.portfolio.Count == 0)
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

			foreach (var coin in RuntimeContext.portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = RuntimeContext.currentPrices.ContainsKey(coin.Key) ? RuntimeContext.currentPrices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;

					// Calculate gain or loss
					decimal averageCostBasis = RuntimeContext.totalCostPerCoin[coin.Key] / RuntimeContext.totalQuantityPerCoin[coin.Key];
					decimal costOfHeldCoins = averageCostBasis * coin.Value;
					decimal gainOrLoss = value - costOfHeldCoins;

					// Calculate gain or loss including fee
					decimal fee = value * Parameters.transactionFeeRate;
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
					.AddChoices(RuntimeContext.portfolio.Keys.Where(k => RuntimeContext.portfolio[k] > 0).Append("Cancel").ToArray())
			);

			if (coinToSell == "Cancel")
			{
				AnsiConsole.MarkupLine("[bold yellow]Sell operation canceled.[/]");
				return;
			}

			if (RuntimeContext.portfolio.ContainsKey(coinToSell) && RuntimeContext.portfolio[coinToSell] > 0)
			{
				decimal currentPrice = RuntimeContext.currentPrices[coinToSell];
				decimal quantityToSell = RuntimeContext.portfolio[coinToSell];
				decimal totalValue = quantityToSell * currentPrice;

				// Calculate gain or loss
				decimal averageCostBasis = RuntimeContext.totalCostPerCoin[coinToSell] / RuntimeContext.totalQuantityPerCoin[coinToSell];
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

		private static void BuyCoin()
		{
			var availableCoins = RuntimeContext.currentPrices.Keys.ToList();

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
				decimal currentPrice = RuntimeContext.currentPrices[coin];
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

			decimal price = RuntimeContext.currentPrices[coinToBuy];
			decimal quantityToBuy = AnsiConsole.Prompt(
				new TextPrompt<decimal>($"Enter the quantity of {coinToBuy.ToUpper()} to buy (0 to cancel):")
			);

			if (quantityToBuy == 0) return;

			decimal totalCost = quantityToBuy * price;

			if (RuntimeContext.balance >= totalCost)
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
			AnsiConsole.MarkupLine("Press [bold green]'Q'[/] to quit the program.");
			AnsiConsole.MarkupLine("\n[bold yellow]============[/]");
		}

		private static void PrintProgramParameters()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== App Parameters ===[/]\n");

			var table = new Table();
			table.AddColumn(new TableColumn("[bold yellow]Parameter[/]").LeftAligned());
			table.AddColumn(new TableColumn("[bold yellow]Value[/]").LeftAligned());

			table.AddRow("[bold cyan]API URL[/]", Parameters.API_URL);
			table.AddRow("[bold cyan]Database Path[/]", Parameters.dbPath);
			table.AddRow("[bold cyan]Period interval (Seconds)[/]", Parameters.CustomIntervalSeconds.ToString());
			table.AddRow("[bold cyan]Analyze Periods/seconds[/]", $"{Parameters.CustomPeriods}/{Parameters.CustomIntervalSeconds * Parameters.CustomPeriods}");
			table.AddRow("[bold cyan]Stop-Loss Threshold[/]", Parameters.stopLossThreshold.ToString("P"));
			table.AddRow("[bold cyan]Profit-Taking Threshold[/]", Parameters.profitTakingThreshold.ToString("P"));
			table.AddRow("[bold cyan]Starting Balance[/]", Parameters.startingBalance.ToString("C"));
			table.AddRow("[bold cyan]Max Investment Per Coin[/]", Parameters.maxInvestmentPerCoin.ToString("C"));
			table.AddRow("[bold cyan]Transaction Fee Rate[/]", Parameters.transactionFeeRate.ToString("P"));

			AnsiConsole.Write(table);
			AnsiConsole.MarkupLine("\n[bold yellow]==========================[/]");
		}

		private static void BacktestStrategy(List<HistoricalData> historicalData)
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Backtest strategy analysis ===[/]\n");

			decimal initialBalance = Parameters.startingBalance;
			decimal balance = initialBalance;
			decimal portfolioValue = 0;
			decimal totalInvestment = 0;
			decimal totalProfitOrLoss = 0;

			Dictionary<string, decimal> portfolio = new Dictionary<string, decimal>();
			object lockObject = new object();

			historicalData = historicalData.TakeLast(30).ToList();

			Parallel.ForEach(historicalData, data =>
			{
				var prices = new Dictionary<string, decimal> { { data.Name, data.Price } };
				var recentHistory = historicalData.Where(h => h.Name == data.Name && h.Timestamp <= data.Timestamp)
												  .Select(h => h.Price).ToList();

				if (recentHistory.Count < Parameters.CustomPeriods)
					return;

				decimal rsi = CalculateRSI(recentHistory, recentHistory.Count);
				decimal sma = CalculateSMA(recentHistory, recentHistory.Count);
				decimal ema = CalculateEMASingle(recentHistory, recentHistory.Count);
				var (macd, bestShortPeriod, bestLongPeriod, bestSignalPeriod) = CalculateMACD(recentHistory);

				lock (lockObject)
				{
					// Trading signals
					if (rsi < 30 && data.Price < sma && data.Price < ema && macd < 0)
					{
						// Buy signal
						decimal quantity = balance / data.Price;
						balance -= quantity * data.Price;
						if (!portfolio.ContainsKey(data.Name))
							portfolio[data.Name] = 0;
						portfolio[data.Name] += quantity;
						totalInvestment += quantity * data.Price;
					}
					else if (rsi > 70 && portfolio.ContainsKey(data.Name) && portfolio[data.Name] > 0 && data.Price > sma && data.Price > ema && macd > 0)
					{
						// Sell signal
						decimal quantity = portfolio[data.Name];
						balance += quantity * data.Price;
						portfolio[data.Name] = 0;
						totalProfitOrLoss += (quantity * data.Price) - totalInvestment;
					}

					// Update portfolio value
					portfolioValue = portfolio.Sum(p => p.Value * data.Price);
				}
			});

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

		private static List<HistoricalData> LoadHistoricalData()
		{
			var historicalData = new List<HistoricalData>();

			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
			{
				conn.Open();
				string query = "SELECT name, price, timestamp FROM Prices ORDER BY timestamp ASC;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							historicalData.Add(new HistoricalData
							{
								Name = reader.GetString(0),
								Price = reader.GetDecimal(1),
								Timestamp = reader.GetDateTime(2)
							});
						}
					}
				}
			}

			return historicalData;
		}

		private static void ResetDatabase()
		{
			if (File.Exists(Parameters.dbPath))
			{
				File.Delete(Parameters.dbPath);
			}

			InitializeDatabase(true);

			RuntimeContext.balance = Parameters.startingBalance;
			RuntimeContext.portfolio.Clear();
			RuntimeContext.initialInvestments.Clear();
			RuntimeContext.totalQuantityPerCoin.Clear();
			RuntimeContext.totalCostPerCoin.Clear();
			RuntimeContext.currentPeriodIndex = 0;

			AnsiConsole.MarkupLine("[bold red]Database has been reset. Starting over...[/]");
		}

		private static void ShowDatabaseStats()
		{
			AnsiConsole.MarkupLine("\n[bold yellow]=== Database statistics ===[/]\n");

			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
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
							int inMemoryCount = RuntimeContext.priceHistory.ContainsKey(name) ? RuntimeContext.priceHistory[name].Count : 0;

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


		private static decimal CalculateSMA(string name, int period)
		{
			if (!RuntimeContext.priceHistory.ContainsKey(name) || RuntimeContext.priceHistory[name].Count < period)
				return 0;
			return RuntimeContext.priceHistory[name].TakeLast(period).Average();
		}

		private static void InitializeDatabase(bool clearPreviousTransactions)
		{
			if (!File.Exists(Parameters.dbPath))
			{
				SQLiteConnection.CreateFile(Parameters.dbPath);
			}

			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
			{
				conn.Open();
				string createTableQuery = @"
    CREATE TABLE IF NOT EXISTS Prices (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL,
        price DECIMAL(18,8) NOT NULL,
        sma DECIMAL(18,8),
        ema DECIMAL(18,8),
        rsi DECIMAL(18,8),
        macd DECIMAL(18,8),
        timestamp DATETIME DEFAULT (datetime('now', 'utc'))
    );
    CREATE TABLE IF NOT EXISTS Transactions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        type TEXT NOT NULL,
        name TEXT NOT NULL,
        quantity DECIMAL(18,8) NOT NULL,
        price DECIMAL(18,8) NOT NULL,
        fee DECIMAL(18,8) NOT NULL DEFAULT 0.0,
        gain_loss DECIMAL(18,8),
        timestamp DATETIME DEFAULT (datetime('now', 'utc'))
    );";
				using (var cmd = new SQLiteCommand(createTableQuery, conn))
				{
					cmd.ExecuteNonQuery();
				}

				// Load historical prices into priceHistory
				RuntimeContext.priceHistory.Clear();

				string selectQuery = $"SELECT name, price FROM Prices ORDER BY timestamp DESC;";
				using (var selectCmd = new SQLiteCommand(selectQuery, conn))
				{
					using (var reader = selectCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = reader.GetString(0);
							decimal price = reader.GetDecimal(1);

							if (!RuntimeContext.priceHistory.ContainsKey(name))
								RuntimeContext.priceHistory[name] = new List<decimal>();

							if (RuntimeContext.priceHistory[name].Count < Parameters.CustomPeriods)
								RuntimeContext.priceHistory[name].Insert(0, price); // Insert at the beginning to maintain order
						}
					}
				}

				if (clearPreviousTransactions)
				{
					// Clear the Transactions table
					string clearTransactionsQuery = "DELETE FROM Transactions;";
					using (var clearCmd = new SQLiteCommand(clearTransactionsQuery, conn))
					{
						clearCmd.ExecuteNonQuery();
					}
					return;
				}

				// Calculate the balance from transactions
				RuntimeContext.balance = Parameters.startingBalance;
				string transactionsQuery = "SELECT type, quantity, price, fee, gain_loss FROM Transactions ORDER BY timestamp ASC;";
				using (var transactionsCmd = new SQLiteCommand(transactionsQuery, conn))
				{
					using (var reader = transactionsCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string type = reader.GetString(0);
							decimal quantity = reader.GetDecimal(1);
							decimal price = reader.GetDecimal(2);
							decimal fee = reader.GetDecimal(3);
							decimal? gainLoss = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4);

							if (type == "BUY")
							{
								RuntimeContext.balance -= (quantity * price) + fee;
							}
							else if (type == "SELL")
							{
								RuntimeContext.balance += (quantity * price) - fee;
								if (gainLoss.HasValue)
								{
									RuntimeContext.balance += gainLoss.Value;
								}
							}
						}
					}
				}

				// Load the portfolio from the Transactions table
				string portfolioQuery = @"
    SELECT name, SUM(CASE WHEN type = 'BUY' THEN quantity ELSE -quantity END) AS quantity
    FROM Transactions
    GROUP BY name
    HAVING quantity > 0;";
				using (var portfolioCmd = new SQLiteCommand(portfolioQuery, conn))
				{
					using (var reader = portfolioCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = reader.GetString(0);
							decimal quantity = reader.GetDecimal(1);
							RuntimeContext.portfolio[name] = quantity;
						}
					}
				}

				// Load the initial investments from the Transactions table
				string investmentsQuery = @"
    SELECT name, 
           SUM(CASE WHEN type = 'BUY' THEN quantity * price ELSE 0 END) - 
           SUM(CASE WHEN type = 'SELL' THEN quantity * price ELSE 0 END) AS investment
    FROM Transactions
    GROUP BY name;";
				using (var investmentsCmd = new SQLiteCommand(investmentsQuery, conn))
				{
					using (var reader = investmentsCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = reader.GetString(0);
							decimal investment = reader.GetDecimal(1);
							RuntimeContext.initialInvestments[name] = investment;
						}
					}
				}

				// Load total quantity and cost for cost basis calculations
				string costBasisQuery = @"
    SELECT name,
        SUM(CASE WHEN type = 'BUY' THEN quantity ELSE -quantity END) AS totalQuantity,
        SUM(CASE WHEN type = 'BUY' THEN quantity * price ELSE -quantity * price END) AS totalCost
    FROM Transactions
    GROUP BY name;";
				using (var costBasisCmd = new SQLiteCommand(costBasisQuery, conn))
				{
					using (var reader = costBasisCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = reader.GetString(0);
							decimal totalQuantity = reader.GetDecimal(1);
							decimal totalCost = reader.GetDecimal(2);
							RuntimeContext.totalQuantityPerCoin[name] = totalQuantity;
							RuntimeContext.totalCostPerCoin[name] = totalCost;
						}
					}
				}
			}
		}


		private static async Task<Dictionary<string, decimal>> GetCryptoPrices()
		{
			try
			{
				AnsiConsole.MarkupLine("\n[bold yellow]=== Fetching cryptocurrency prices... ===[/]");

				var response = await client.GetStringAsync(Parameters.API_URL);
				var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(response);
				RuntimeContext.currentPrices = new Dictionary<string, decimal>();

				foreach (var coin in data ?? new Dictionary<string, Dictionary<string, decimal>>())
				{
					string name = coin.Key;
					decimal price = coin.Value["usd"];
					RuntimeContext.currentPrices[name] = price;

					if (!RuntimeContext.priceHistory.ContainsKey(name))
						RuntimeContext.priceHistory[name] = new List<decimal>();

					RuntimeContext.priceHistory[name].Add(price);
					if (RuntimeContext.priceHistory[name].Count > Parameters.CustomPeriods)
						RuntimeContext.priceHistory[name].RemoveAt(0);
				}
				return RuntimeContext.currentPrices;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				return new Dictionary<string, decimal>();
			}
		}

		private static void StoreIndicatorsInDatabase(Dictionary<string, decimal> prices)
		{
			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
			{
				conn.Open();
				foreach (var coin in prices)
				{
					decimal sma = CalculateSMA(coin.Key, 20);
					decimal ema = CalculateEMA(coin.Key, 12);
					decimal rsi = CalculateRSI(coin.Key, 14);
					decimal macd = CalculateMACD(coin.Key);

					string insertQuery = "INSERT INTO Prices (name, price, sma, ema, rsi, macd) VALUES (@name, @price, @sma, @ema, @rsi, @macd);";
					using (var cmd = new SQLiteCommand(insertQuery, conn))
					{
						cmd.Parameters.AddWithValue("@name", coin.Key);
						cmd.Parameters.AddWithValue("@price", coin.Value);
						cmd.Parameters.AddWithValue("@sma", sma);
						cmd.Parameters.AddWithValue("@ema", ema);
						cmd.Parameters.AddWithValue("@rsi", rsi);
						cmd.Parameters.AddWithValue("@macd", macd);
						cmd.ExecuteNonQuery();
					}
				}
			}
		}

		public static void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal fee, decimal? gainLoss)
		{
			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
			{
				conn.Open();
				string insertQuery = "INSERT INTO Transactions (type, name, quantity, price, fee, gain_loss) VALUES (@type, @name, @quantity, @price, @fee, @gainLoss);";
				using (var cmd = new SQLiteCommand(insertQuery, conn))
				{
					cmd.Parameters.AddWithValue("@type", type);
					cmd.Parameters.AddWithValue("@name", coinName);
					cmd.Parameters.AddWithValue("@quantity", quantity);
					cmd.Parameters.AddWithValue("@price", price);
					cmd.Parameters.AddWithValue("@fee", fee);
					cmd.Parameters.AddWithValue("@gainLoss", gainLoss.HasValue ? (object)gainLoss.Value : DBNull.Value);
					cmd.ExecuteNonQuery();
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
			foreach (var coin in RuntimeContext.portfolio)
			{
				if (coin.Value > 0)
				{
					decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
					decimal value = coin.Value * currentPrice;
					portfolioWorth += value;

					decimal initialInvestment = RuntimeContext.totalCostPerCoin.ContainsKey(coin.Key) ? RuntimeContext.totalCostPerCoin[coin.Key] : 0;
					totalInvestment += initialInvestment;
				}
			}

			decimal totalWorth = RuntimeContext.balance + portfolioWorth;
			decimal initialBalance = Parameters.startingBalance; // Starting balance
			decimal totalProfitOrLoss = totalWorth - initialBalance;
			decimal percentageChange = initialBalance > 0 ? (totalProfitOrLoss / initialBalance) * 100 : 0;

			// Calculate current investment gain or loss
			decimal currentInvestmentGainOrLoss = portfolioWorth - totalInvestment;
			decimal currentInvestmentPercentageChange = totalInvestment > 0 ? (currentInvestmentGainOrLoss / totalInvestment) * 100 : 0;

			// Calculate total gain or loss from all transactions
			decimal totalTransactionGainOrLoss = 0;
			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
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
			decimal currentGainOrLossIncludingFees = currentInvestmentGainOrLoss - (portfolioWorth * Parameters.transactionFeeRate);

			// Create a table for balance information
			var balanceTable = new Table();
			balanceTable.AddColumn("Description");
			balanceTable.AddColumn("Value");

			balanceTable.AddRow("Total Investment across all coins", $"[bold cyan]{totalInvestment:C}[/]");
			balanceTable.AddRow("Current balance", $"[bold green]{RuntimeContext.balance:C}[/]");
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

					foreach (var coin in RuntimeContext.portfolio)
					{
						if (coin.Value > 0)
						{
							decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
							decimal value = coin.Value * currentPrice;
							decimal initialInvestment = RuntimeContext.totalCostPerCoin.ContainsKey(coin.Key) ? RuntimeContext.totalCostPerCoin[coin.Key] : 0;
							decimal profitOrLoss = value - initialInvestment;

							// Calculate profit/loss including fee
							decimal fee = value * Parameters.transactionFeeRate;
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

			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
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

			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
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

			if (!analysisOnly) RuntimeContext.currentPeriodIndex++;

			AnsiConsole.MarkupLine($"\n[bold yellow]=== Market Analysis Report - Period Counter: {RuntimeContext.currentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			bool operationPerfomed = false;

			foreach (var coin in prices)
			{
				bool operationsAllowed = true;

				AnsiConsole.MarkupLine($"\n[bold cyan]{coin.Key.ToUpper()}[/]:");

				if (!RuntimeContext.priceHistory.ContainsKey(coin.Key))
					continue;

				var recentHistoryData = GetRecentHistoryRows(coin.Key, Parameters.CustomPeriods);

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
				int bufferSeconds = Parameters.CustomIntervalSeconds; // Buffer for capture delays
				if (timeframe.TotalSeconds < (analysisWindowSeconds - bufferSeconds) || timeframe.TotalSeconds > (analysisWindowSeconds + bufferSeconds))
				{
					table.AddRow("Analysis Status", $"[bold red]Not valid timeframe for {coin.Key} analysis. Required: {analysisWindowSeconds} seconds, Available: {timeframe.TotalSeconds} seconds (including buffer of {bufferSeconds} seconds).[/]");
					AnsiConsole.Write(table);
					continue;
				}

				decimal rsi = CalculateRSI(recentHistory, recentHistory.Count);
				decimal sma = CalculateSMA(recentHistory, recentHistory.Count);
				decimal ema = CalculateEMASingle(recentHistory, recentHistory.Count);
				var (macd, bestShortPeriod, bestLongPeriod, bestSignalPeriod) = CalculateMACD(recentHistory);
				decimal priceChangeWindow = CalculatePriceChange(recentHistory);

				// Calculate Bollinger Bands
				var (middleBand, upperBand, lowerBand) = CalculateBollingerBands(recentHistory, customPeriods);

				// Calculate ATR (assuming high, low, and close prices are available)
				var highPrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual high prices
				var lowPrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual low prices
				var closePrices = recentHistoryData.Select(x => x.Price).ToList(); // Replace with actual close prices
				decimal atr = CalculateATR(highPrices, lowPrices, closePrices, customPeriods);

				// Calculate volatility
				decimal volatility = CalculateVolatility(recentHistory, customPeriods);
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

				AnsiConsole.Write(table);

				var operationsTable = new Table();
				operationsTable.AddColumn("Operation");
				operationsTable.AddColumn("Details");

				if (!analysisOnly)
				{
					// Stop-loss and profit-taking strategy
					if (RuntimeContext.portfolio.ContainsKey(coin.Key) && RuntimeContext.portfolio[coin.Key] > 0)
					{
						decimal initialInvestment = RuntimeContext.initialInvestments.ContainsKey(coin.Key) ? RuntimeContext.initialInvestments[coin.Key] : 0;
						decimal currentValue = RuntimeContext.portfolio[coin.Key] * coin.Value;
						decimal fee = currentValue * Parameters.transactionFeeRate;
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

					// Trading signals with confidence levels
					if (rsi < 30 && coin.Value < sma && coin.Value < ema && macd < 0 && coin.Value < lowerBand && atr > 0)
					{
						decimal confidence = (30 - rsi) / 30 * 100;
						operationsTable.AddRow("BUY Signal", $"[bold green]Confidence: {confidence:N2}%[/]");

						if (RuntimeContext.balance > 0)
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
					else if (rsi > 70 && RuntimeContext.portfolio.ContainsKey(coin.Key) && RuntimeContext.portfolio[coin.Key] > 0 && coin.Value > sma && coin.Value > ema && macd > 0 && coin.Value > upperBand && atr > 0)
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

			AnsiConsole.MarkupLine($"\n[bold yellow]=== End of Analysis - Period Counter: {RuntimeContext.currentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");
		}

		private static (decimal middleBand, decimal upperBand, decimal lowerBand) CalculateBollingerBands(List<decimal> prices, int period, decimal multiplier = 2)
		{
			if (prices.Count < period)
				return (0, 0, 0);

			decimal sma = CalculateSMA(prices, period);
			decimal stdDev = (decimal)Math.Sqrt((double)prices.TakeLast(period).Select(p => (p - sma) * (p - sma)).Sum() / period);

			decimal upperBand = sma + (multiplier * stdDev);
			decimal lowerBand = sma - (multiplier * stdDev);

			return (sma, upperBand, lowerBand);
		}

		private static decimal CalculateATR(List<decimal> highPrices, List<decimal> lowPrices, List<decimal> closePrices, int period)
		{
			if (highPrices.Count < period || lowPrices.Count < period || closePrices.Count < period)
				return 0;

			var trueRanges = new List<decimal>();
			for (int i = 1; i < highPrices.Count; i++)
			{
				decimal highLow = highPrices[i] - lowPrices[i];
				decimal highClose = Math.Abs(highPrices[i] - closePrices[i - 1]);
				decimal lowClose = Math.Abs(lowPrices[i] - closePrices[i - 1]);
				trueRanges.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
			}

			return trueRanges.TakeLast(period).Average();
		}

		private static decimal CalculateVolatility(List<decimal> prices, int period)
		{
			if (prices.Count < period)
				return 0;

			var returns = new List<decimal>();
			for (int i = 1; i < period; i++)
			{
				returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
			}

			decimal averageReturn = returns.Average();
			decimal sumOfSquaresOfDifferences = returns.Select(val => (val - averageReturn) * (val - averageReturn)).Sum();
			decimal standardDeviation = (decimal)Math.Sqrt((double)(sumOfSquaresOfDifferences / returns.Count));

			return standardDeviation;
		}

		private static (decimal stopLossThreshold, decimal profitTakingThreshold) AdjustThresholdsBasedOnVolatility(decimal volatility)
		{
			decimal baseStopLoss = Parameters.stopLossThreshold;
			decimal baseProfitTaking = Parameters.profitTakingThreshold;

			// Adjust thresholds based on volatility
			decimal adjustedStopLoss = baseStopLoss * (1 + volatility);
			decimal adjustedProfitTaking = baseProfitTaking * (1 + volatility);

			return (adjustedStopLoss, adjustedProfitTaking);
		}

		private static (int bestShortPeriod, int bestLongPeriod, int bestSignalPeriod) FindBestMACDPeriods(List<decimal> prices)
		{
			int bestShortPeriod = 12;
			int bestLongPeriod = 26;
			int bestSignalPeriod = 9;
			decimal bestPerformance = decimal.MinValue;
			object lockObject = new object();

			Parallel.For(5, 16, shortPeriod =>
			{
				Parallel.For(20, 31, longPeriod =>
				{
					Parallel.For(5, 16, signalPeriod =>
					{
						if (shortPeriod >= longPeriod) return;

						decimal performance = EvaluateMACDPerformance(prices, shortPeriod, longPeriod, signalPeriod);
						lock (lockObject)
						{
							if (performance > bestPerformance)
							{
								bestPerformance = performance;
								bestShortPeriod = shortPeriod;
								bestLongPeriod = longPeriod;
								bestSignalPeriod = signalPeriod;
							}
						}
					});
				});
			});

			return (bestShortPeriod, bestLongPeriod, bestSignalPeriod);
		}

		private static decimal EvaluateMACDPerformance(List<decimal> prices, int shortPeriod, int longPeriod, int signalPeriod)
		{
			List<decimal> macdLine = CalculateMACDLine(prices, shortPeriod, longPeriod);
			List<decimal> signalLine = CalculateEMA(macdLine, signalPeriod);

			decimal performance = 0;
			bool inPosition = false;
			decimal entryPrice = 0;

			for (int i = signalPeriod; i < macdLine.Count; i++)
			{
				if (macdLine[i] > signalLine[i] && !inPosition)
				{
					inPosition = true;
					entryPrice = prices[i];
				}
				else if (macdLine[i] < signalLine[i] && inPosition)
				{
					inPosition = false;
					performance += prices[i] - entryPrice;
				}
			}

			return performance;
		}

		private static List<decimal> CalculateMACDLine(List<decimal> prices, int shortPeriod, int longPeriod)
		{
			List<decimal> macdLine = new List<decimal>();
			List<decimal> shortEMA = CalculateEMAList(prices, shortPeriod);
			List<decimal> longEMA = CalculateEMAList(prices, longPeriod);

			for (int i = 0; i < prices.Count; i++)
			{
				macdLine.Add(shortEMA[i] - longEMA[i]);
			}

			return macdLine;
		}

		private static List<decimal> CalculateEMAList(List<decimal> prices, int periods)
		{
			List<decimal> emaList = new List<decimal>();
			decimal multiplier = 2m / (periods + 1);
			decimal ema = prices[0]; // Start with the first price as the initial EMA

			for (int i = 0; i < prices.Count; i++)
			{
				if (i == 0)
				{
					emaList.Add(ema);
				}
				else
				{
					ema = ((prices[i] - ema) * multiplier) + ema;
					emaList.Add(ema);
				}
			}

			return emaList;
		}

		private static List<decimal> CalculateEMA(List<decimal> prices, int periods)
		{
			List<decimal> emaList = new List<decimal>();
			decimal multiplier = 2m / (periods + 1);
			decimal ema = prices[0]; // Start with the first price as the initial EMA

			for (int i = 0; i < prices.Count; i++)
			{
				if (i == 0)
				{
					emaList.Add(ema);
				}
				else
				{
					ema = ((prices[i] - ema) * multiplier) + ema;
					emaList.Add(ema);
				}
			}

			return emaList;
		}

		private static (decimal macdValue, int bestShortPeriod, int bestLongPeriod, int bestSignalPeriod) CalculateMACD(List<decimal> prices)
		{
			var (bestShortPeriod, bestLongPeriod, bestSignalPeriod) = FindBestMACDPeriods(prices);
			List<decimal> macdLine = CalculateMACDLine(prices, bestShortPeriod, bestLongPeriod);
			List<decimal> signalLine = CalculateEMA(macdLine, bestSignalPeriod);

			decimal macdValue = macdLine.Last() - signalLine.Last();
			return (macdValue, bestShortPeriod, bestLongPeriod, bestSignalPeriod);
		}

		private static decimal CalculateEMASingle(List<decimal> prices, int periods)
		{
			if (prices == null || prices.Count == 0 || periods <= 0)
				throw new ArgumentException("Invalid input for EMA calculation.");

			decimal multiplier = 2m / (periods + 1);
			decimal ema = prices[0]; // Start with the first price as the initial EMA

			for (int i = 1; i < prices.Count; i++)
			{
				ema = ((prices[i] - ema) * multiplier) + ema;
			}

			return ema;
		}

		private static List<(decimal Price, DateTime Timestamp)> GetRecentHistoryRows(string coin, int rowCount)
		{
			var recentHistory = new List<(decimal Price, DateTime Timestamp)>();
			using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
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

		private static decimal CalculateSMA(List<decimal> prices, int periods)
		{
			if (prices.Count < periods)
				return prices.Average();

			return prices.TakeLast(periods).Average();
		}

		private static decimal CalculatePriceChange(List<decimal> history)
		{
			if (history.Count < 2) return 0; // Need at least 2 data points to calculate change
			var oldPrice = history.Last();
			var currentPrice = history.First();
			return ((currentPrice - oldPrice) / oldPrice) * 100;
		}

		private static decimal CalculateRSI(List<decimal> prices, int periods = 14)
		{
			if (prices.Count < periods)
				return 50; // Neutral RSI if not enough data

			var gains = new List<decimal>();
			var losses = new List<decimal>();

			for (int i = 1; i < prices.Count; i++)
			{
				var difference = prices[i] - prices[i - 1];
				if (difference >= 0)
				{
					gains.Add(difference);
					losses.Add(0);
				}
				else
				{
					gains.Add(0);
					losses.Add(-difference);
				}
			}

			var avgGain = gains.TakeLast(periods).Average();
			var avgLoss = losses.TakeLast(periods).Average();

			if (avgLoss == 0)
				return 100;

			var rs = avgGain / avgLoss;
			return 100 - (100 / (1 + rs));
		}

		private static decimal CalculateEMA(string name, int period)
		{
			if (!RuntimeContext.priceHistory.ContainsKey(name) || RuntimeContext.priceHistory[name].Count < period)
				return 0;
			decimal smoothing = 2m / (period + 1);
			decimal ema = RuntimeContext.priceHistory[name].Take(period).Average();
			foreach (var price in RuntimeContext.priceHistory[name].Skip(period))
			{
				ema = (price - ema) * smoothing + ema;
			}
			return ema;
		}

		private static decimal CalculateRSI(string name, int period)
		{
			if (!RuntimeContext.priceHistory.ContainsKey(name) || RuntimeContext.priceHistory[name].Count < period + 1)
				return 0;
			decimal gain = 0, loss = 0;
			for (int i = 1; i <= period; i++)
			{
				decimal change = RuntimeContext.priceHistory[name][^i] - RuntimeContext.priceHistory[name][^(i + 1)];
				if (change > 0) gain += change;
				else loss -= change;
			}
			if (loss == 0) return 100;
			decimal rs = gain / loss;
			return 100 - (100 / (1 + rs));
		}

		private static decimal CalculateMACD(string name)
		{
			return CalculateEMA(name, 12) - CalculateEMA(name, 26);
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
	}
}
