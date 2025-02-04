﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spectre.Console;

namespace Trader
{
    public static class Parameters
    {
        public const int CustomIntervalSeconds = 30; // Example interval time in seconds
        public const int CustomPeriods = 60; // Example number of periods
        public const string API_URL = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum,solana,binancecoin,cardano&vs_currencies=usd";
        public const string dbPath = "crypto_prices.db";
        public const decimal stopLossThreshold = -0.10m; // 10% loss
        public const decimal profitTakingThreshold = 0.20m; // 20% gain
        public const decimal maxInvestmentPerCoin = 3000m; // Example maximum investment amount per coin
        public const decimal startingBalance = 10000m; // Starting balance
    }

    public static class RuntimeContext
    {
        public static decimal balance = Parameters.startingBalance;
        public static Dictionary<string, List<decimal>> priceHistory = new Dictionary<string, List<decimal>>();
        public static Dictionary<string, decimal> portfolio = new Dictionary<string, decimal>();
        public static Dictionary<string, decimal> initialInvestments = new Dictionary<string, decimal>();
        public static Dictionary<string, decimal> totalQuantityPerCoin = new Dictionary<string, decimal>();
        public static Dictionary<string, decimal> totalCostPerCoin = new Dictionary<string, decimal>();
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

            bool firstRun = true;

            int analysisWindowSeconds = Parameters.CustomIntervalSeconds * Parameters.CustomPeriods;

            DateTime nextIterationTime = DateTime.UtcNow;
            Dictionary<string, decimal> prices = new Dictionary<string, decimal>();

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
                            firstRun = true;
                            PrintMenu();
                            continue;
                        }

                        if (key.Key == ConsoleKey.T)
                        {
                            ShowTransactionHistory();
                            PrintMenu();
                        }

                        if (key.Key == ConsoleKey.B)
                        {
                            ShowBalance(prices, true);
                            PrintMenu();
                        }

                        if (key.Key == ConsoleKey.S)
                        {
                            ShowDatabaseStats();
                            PrintMenu();
                        }

                        if (key.Key == ConsoleKey.Q)
                        {
                            AnsiConsole.MarkupLine("[bold red]Exiting the program...[/]");
                            break;
                        }

                        if (key.Key == ConsoleKey.P)
                        {
                            PrintProgramParameters();
                            PrintMenu();
                        }
                    }

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
                        AnalyzeIndicators(prices, Parameters.CustomPeriods, analysisWindowSeconds);
                        ShowBalance(prices, false);

                        PrintMenu();

                        // Update the next iteration time
                        nextIterationTime = DateTime.UtcNow.AddSeconds(Parameters.CustomIntervalSeconds);
                        AnsiConsole.MarkupLine($"\n[bold yellow]=== Next update in {Parameters.CustomIntervalSeconds} seconds... ===[/]");
                    }
                    else
                    {
                        // Optionally, you can sleep for a short duration to prevent CPU overuse
                        await Task.Delay(100); // Sleep for 100 milliseconds
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
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
            AnsiConsole.MarkupLine("Press [bold green]'B'[/] to view verbose balance and portfolio.");
            AnsiConsole.MarkupLine("Press [bold green]'S'[/] to show database statistics.");
            AnsiConsole.MarkupLine("Press [bold green]'P'[/] to show program parameters.");
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
            table.AddRow("[bold cyan]Starting Balance[/]", RuntimeContext.balance.ToString("C"));
            table.AddRow("[bold cyan]Max Investment Per Coin[/]", Parameters.maxInvestmentPerCoin.ToString("C"));

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("\n[bold yellow]==========================[/]");
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
				balance DECIMAL(18,8) NOT NULL,
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

                // Load the latest balance from the Transactions table
                string balanceQuery = "SELECT balance FROM Transactions ORDER BY timestamp DESC LIMIT 1;";
                using (var balanceCmd = new SQLiteCommand(balanceQuery, conn))
                {
                    var result = balanceCmd.ExecuteScalar();
                    if (result != null)
                    {
                        RuntimeContext.balance = Convert.ToDecimal(result);
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
            SELECT name, SUM(CASE WHEN type = 'BUY' THEN quantity * price ELSE 0 END) AS investment
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
                var prices = new Dictionary<string, decimal>();

                foreach (var coin in data ?? new Dictionary<string, Dictionary<string, decimal>>())
                {
                    string name = coin.Key;
                    decimal price = coin.Value["usd"];
                    prices[name] = price;

                    if (!RuntimeContext.priceHistory.ContainsKey(name))
                        RuntimeContext.priceHistory[name] = new List<decimal>();

                    RuntimeContext.priceHistory[name].Add(price);
                    if (RuntimeContext.priceHistory[name].Count > Parameters.CustomPeriods)
                        RuntimeContext.priceHistory[name].RemoveAt(0);
                }
                return prices;
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

        public static void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal balance, decimal? gainLoss)
        {
            using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
            {
                conn.Open();
                string insertQuery = "INSERT INTO Transactions (type, name, quantity, price, balance, gain_loss) VALUES (@type, @name, @quantity, @price, @balance, @gainLoss);";
                using (var cmd = new SQLiteCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@name", coinName);
                    cmd.Parameters.AddWithValue("@quantity", quantity);
                    cmd.Parameters.AddWithValue("@price", price);
                    cmd.Parameters.AddWithValue("@balance", balance);
                    cmd.Parameters.AddWithValue("@gainLoss", gainLoss.HasValue ? (object)gainLoss.Value : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void ShowBalance(Dictionary<string, decimal> prices, bool verbose = true)
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]=== Balance{(verbose ? " and portfolio" : string.Empty)} Report ===[/]");

            decimal portfolioWorth = 0;
            decimal totalInvestment = 0;

            // Calculate the total portfolio value and total investment
            foreach (var coin in RuntimeContext.portfolio)
            {
                if (coin.Value > 0)
                {
                    decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
                    decimal value = coin.Value * currentPrice;
                    portfolioWorth += value;

                    decimal initialInvestment = RuntimeContext.initialInvestments.ContainsKey(coin.Key) ? RuntimeContext.initialInvestments[coin.Key] : 0;
                    totalInvestment += initialInvestment;
                }
            }

            if (verbose)
            {
                if (portfolioWorth == 0)
                {
                    AnsiConsole.MarkupLine("[bold red]\nNo holdings in the portfolio.[/]");
                }
                else
                {
                    var table = new Table();
                    table.AddColumn("Coin");
                    table.AddColumn("Units Held");
                    table.AddColumn("Current Price");
                    table.AddColumn("Current Value");
                    table.AddColumn("Initial Investment");
                    table.AddColumn("Profit/Loss");
                    table.AddColumn("Percentage of Portfolio");

                    foreach (var coin in RuntimeContext.portfolio)
                    {
                        if (coin.Value > 0)
                        {
                            decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
                            decimal value = coin.Value * currentPrice;
                            decimal initialInvestment = RuntimeContext.initialInvestments.ContainsKey(coin.Key) ? RuntimeContext.initialInvestments[coin.Key] : 0;
                            decimal profitOrLoss = value - initialInvestment;

                            decimal percentageOfPortfolio = portfolioWorth > 0 ? (value / portfolioWorth) * 100 : 0;
                            decimal profitOrLossPercentage = initialInvestment > 0 ? (profitOrLoss / initialInvestment) * 100 : 0;

                            string profitOrLossStr = profitOrLoss >= 0 ? $"[green]{profitOrLoss:C} ({profitOrLossPercentage:N2}%)[/]" : $"[red]{profitOrLoss:C} ({profitOrLossPercentage:N2}%)[/]";

                            table.AddRow(
                                coin.Key.ToUpper(),
                                coin.Value.ToString("N4"),
                                currentPrice.ToString("C"),
                                value.ToString("C"),
                                initialInvestment.ToString("C"),
                                profitOrLossStr,
                                $"{percentageOfPortfolio:N2}%"
                            );
                        }
                    }

                    AnsiConsole.Write(table);
                }
            }

            decimal totalWorth = RuntimeContext.balance + portfolioWorth;
            decimal initialBalance = 10000m; // Starting balance
            decimal totalProfitOrLoss = totalWorth - initialBalance;
            decimal percentageChange = initialBalance > 0 ? (totalProfitOrLoss / initialBalance) * 100 : 0;

            // Display the total investment across all coins
            AnsiConsole.MarkupLine($"\n[bold yellow]Total Investment across all coins: {totalInvestment:C}[/]");
            AnsiConsole.MarkupLine($"Current balance: [bold green]{RuntimeContext.balance:C}[/]");
            AnsiConsole.MarkupLine($"Current portfolio worth: [bold green]{portfolioWorth:C}[/]");
            AnsiConsole.MarkupLine($"Total worth: [bold green]{totalWorth:C}[/]");
            AnsiConsole.MarkupLine(totalProfitOrLoss >= 0
                ? $"[bold green]Gains: {totalProfitOrLoss:C} ({percentageChange:N2}%)[/]"
                : $"[bold red]Losses: {Math.Abs(totalProfitOrLoss):C} ({percentageChange:N2}%)[/]");

            AnsiConsole.MarkupLine($"\n[bold yellow]=== End of balance{(verbose ? " and portfolio" : string.Empty)} Report ===[/]");
        }

        private static void ShowTransactionHistory()
        {
            AnsiConsole.MarkupLine("\n[bold yellow]=== Transactions History ===[/]\n");

            using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
            {
                conn.Open();
                string query = @"
            SELECT name, type, quantity, price, gain_loss, timestamp
            FROM Transactions
            ORDER BY name, timestamp;";
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        var table = new Table();
                        table.AddColumn("Coin");
                        table.AddColumn("Type");
                        table.AddColumn("Quantity");
                        table.AddColumn("Price");
                        table.AddColumn("Gain/Loss");
                        table.AddColumn("Timestamp");

                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            string type = reader.GetString(1);
                            decimal quantity = reader.GetDecimal(2);
                            decimal price = reader.GetDecimal(3);
                            object gainLossObj = reader.GetValue(4);
                            decimal? gainLoss = gainLossObj != DBNull.Value ? (decimal?)gainLossObj : null;
                            DateTime timestamp = reader.GetDateTime(5);

                            string gainLossStr = gainLoss.HasValue
                                ? (gainLoss.Value >= 0 ? $"[green]{gainLoss.Value:C}[/]" : $"[red]{gainLoss.Value:C}[/]")
                                : "N/A";

                            table.AddRow(
                                name.ToUpper(),
                                type,
                                quantity.ToString("N4"),
                                price.ToString("C"),
                                gainLossStr,
                                timestamp.ToString("yyyy-MM-dd HH:mm:ss")
                            );
                        }

                        AnsiConsole.Write(table);
                    }
                }
            }
            AnsiConsole.MarkupLine("\n[bold yellow]=== End of Transactions History ===[/]");
        }

		private static void AnalyzeIndicators(Dictionary<string, decimal> prices, int customPeriods, int analysisWindowSeconds)
		{
			DateTime startAnalysisTimeStamp = DateTime.Now.ToUniversalTime();

			AnsiConsole.MarkupLine($"\n[bold yellow]=== Market Analysis Report - Period Index: {++RuntimeContext.currentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			foreach (var coin in prices)
			{
				bool operationsAllowed = true;

				AnsiConsole.MarkupLine($"\n[bold cyan]{coin.Key.ToUpper()}[/]:");

				if (!RuntimeContext.priceHistory.ContainsKey(coin.Key))
					continue;

				var recentHistoryData = GetRecentHistoryRows(coin.Key, Parameters.CustomPeriods); //GetRecentHistorySeconds(coin.Key, analysisWindowSeconds);

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

				decimal rsi = CalculateRSI(recentHistory, recentHistory.Count);
				decimal sma = CalculateSMA(recentHistory, recentHistory.Count);
				decimal ema = CalculateEMA(recentHistory, recentHistory.Count);
				decimal macd = CalculateMACD(recentHistory);
				decimal priceChangeWindow = CalculatePriceChange(recentHistory);

				// Retrieve the first data timestamp and calculate the time difference from now
				DateTime firstTimestamp = recentHistoryData.First().Timestamp; // (coin.Key, analysisWindowSeconds);
				TimeSpan timeDifference = DateTime.UtcNow - firstTimestamp;

				table.AddRow("Current Price", $"[bold green]${coin.Value:N2}[/]");
				table.AddRow("First Price Timestamp", $"[bold green]{firstTimestamp:yyyy-MM-dd HH:mm:ss}[/]");
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

				// Market sentiment analysis
				string sentiment = "NEUTRAL";
				if (rsi < 30) sentiment = "OVERSOLD";
				else if (rsi > 70) sentiment = "OVERBOUGHT";

				table.AddRow("Market Sentiment", $"[bold green]{sentiment}[/]");

				AnsiConsole.Write(table);

				var operationsTable = new Table();
				operationsTable.AddColumn("Operation");
				operationsTable.AddColumn("Details");

				// Stop-loss and profit-taking strategy
				if (RuntimeContext.portfolio.ContainsKey(coin.Key) && RuntimeContext.portfolio[coin.Key] > 0)
				{
					decimal initialInvestment = RuntimeContext.initialInvestments.ContainsKey(coin.Key) ? RuntimeContext.initialInvestments[coin.Key] : 0;
					decimal currentValue = RuntimeContext.portfolio[coin.Key] * coin.Value;
					decimal profitOrLoss = (currentValue - initialInvestment) / initialInvestment;

					if (profitOrLoss <= Parameters.stopLossThreshold)
					{
						operationsTable.AddRow("STOP-LOSS", $"[bold red]Selling {coin.Key} to prevent further loss.[/]");
						tradeOperations.Sell(coin.Key, coin.Value);
					}
					else if (profitOrLoss >= Parameters.profitTakingThreshold)
					{
						operationsTable.AddRow("PROFIT-TAKING", $"[bold green]Selling {coin.Key} to secure profit.[/]");
						tradeOperations.Sell(coin.Key, coin.Value);
					}
				}

				// Trading signals with confidence levels
				if (rsi < 30 && coin.Value < sma && coin.Value < ema && macd < 0)
				{
					decimal confidence = (30 - rsi) / 30 * 100;
					operationsTable.AddRow("BUY Signal", $"[bold green]Confidence: {confidence:N2}%[/]");

					if (RuntimeContext.balance > 0)
					{
						if (operationsAllowed)
						{
							tradeOperations.Buy(coin.Key, null, coin.Value);
						}
						else
						{
							operationsTable.AddRow("BUY Operation", $"[bold red]Skipping buy operation because analysis is not valid.[/]");
						}
					}
				}
				else if (rsi > 70 && RuntimeContext.portfolio.ContainsKey(coin.Key) && RuntimeContext.portfolio[coin.Key] > 0 && coin.Value > sma && coin.Value > ema && macd > 0)
				{
					decimal confidence = (rsi - 70) / 30 * 100;
					operationsTable.AddRow("SELL Signal", $"[bold cyan]Confidence: {confidence:N2}%[/]");

					if (operationsAllowed)
					{
						tradeOperations.Sell(coin.Key, coin.Value);
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

			AnsiConsole.MarkupLine($"\n[bold yellow]=== End of Analysis - Period Index: {RuntimeContext.currentPeriodIndex} ===[/]");
		}


		private static decimal CalculateEMA(List<decimal> prices, int periods)
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

        private static decimal CalculateMACD(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0)
                throw new ArgumentException("Invalid input for MACD calculation.");

            int shortPeriod = 12;
            int longPeriod = 26;
            int signalPeriod = 9;

            decimal shortEMA = CalculateEMA(prices, shortPeriod);
            decimal longEMA = CalculateEMA(prices, longPeriod);
            decimal macd = shortEMA - longEMA;

            List<decimal> macdHistory = new List<decimal>();
            for (int i = 0; i < prices.Count; i++)
            {
                decimal shortEma = CalculateEMA(prices.Take(i + 1).ToList(), shortPeriod);
                decimal longEma = CalculateEMA(prices.Take(i + 1).ToList(), longPeriod);
                macdHistory.Add(shortEma - longEma);
            }

            decimal signalLine = CalculateEMA(macdHistory, signalPeriod);
            decimal macdHistogram = macd - signalLine;

            return macdHistogram;
        }

        private static List<(decimal Price, DateTime Timestamp)> GetRecentHistorySeconds(string coin, int seconds)
        {
            var recentHistory = new List<(decimal Price, DateTime Timestamp)>();
            using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
            {
                conn.Open();
                string query = @"
            SELECT price, timestamp FROM Prices
            WHERE name = @name AND timestamp >= datetime('now', @seconds || ' seconds')
            ORDER BY timestamp DESC;"; // Order by timestamp in descending order to get the latest rows
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", coin);
                    cmd.Parameters.AddWithValue("@seconds", -seconds);
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

        private static DateTime GetFirstTimestampSeconds(string coin, int seconds)
        {
            using (var conn = new SQLiteConnection($"Data Source={Parameters.dbPath};Version=3;"))
            {
                conn.Open();
                string query = @"
                SELECT timestamp FROM Prices
                WHERE name = @name AND timestamp >= datetime('now', @seconds || ' seconds')
                ORDER BY timestamp ASC LIMIT 1;";
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", coin);
                    cmd.Parameters.AddWithValue("@seconds", -seconds);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return reader.GetDateTime(0);
                        }
                    }
                }
            }
            return DateTime.MinValue; // Return a default value if no timestamp is found
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