using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;

internal class Program
{
    private static readonly HttpClient client = new HttpClient();
    private const string API_URL = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum,solana,binancecoin,cardano&vs_currencies=eur";
    private static readonly string dbPath = "crypto_prices.db";

    private static readonly bool isConsoleAvailable = IsConsoleAvailable();

	private const int customIntervalSeconds = 30; // Example interval time in seconds
	private const int customPeriods = 60; // Example number of periods

	private const decimal stopLossThreshold = -0.10m; // 10% loss
	private const decimal profitTakingThreshold = 0.20m; // 20% gain

	private const decimal maxInvestmentPerCoin = 3000m; // Example maximum investment amount per coin

	private static Dictionary<string, List<decimal>> priceHistory = new Dictionary<string, List<decimal>>();
	private static decimal balance = 10000m; // Starting balance
	private static Dictionary<string, decimal> portfolio = new Dictionary<string, decimal>();
	private static Dictionary<string, decimal> initialInvestments = new Dictionary<string, decimal>();

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

	private static async Task Main(string[] args)
	{
		bool clearPreviousTransactions = args.Contains("-c");

        // Do not load previous transactions
		InitializeDatabase(true);
		ShowDatabaseStats();

        bool firstRun = true;

		int analysisWindowSeconds = customIntervalSeconds * customPeriods;

		try
		{
			while (true)
			{
				if (Console.KeyAvailable)
				{
					var key = Console.ReadKey(intercept: true);
					if (key.Key == ConsoleKey.C)
					{
						ResetDatabase();
                        firstRun = true;
						continue;
					}
				}

                if (firstRun)
                {
                    PrintProgramParameters();
                    firstRun = false;
                }

				var prices = await GetCryptoPrices();
				StoreIndicatorsInDatabase(prices);
				AnalyzeIndicators(prices, customPeriods, analysisWindowSeconds);
				ShowBalance(prices);
				Console.WriteLine($"\n=== Waiting {customIntervalSeconds} seconds before next period... ===");
				await Task.Delay(customIntervalSeconds * 1000);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}

	private static void PrintProgramParameters()
	{
		Console.WriteLine("\n=== Program Parameters ===");
		Console.WriteLine($"API URL: {API_URL}");
		Console.WriteLine($"Database Path: {dbPath}");
		Console.WriteLine($"Custom Interval Seconds: {customIntervalSeconds}");
		Console.WriteLine($"Custom Periods: {customPeriods}");
		Console.WriteLine($"Stop-Loss Threshold: {stopLossThreshold:P}");
		Console.WriteLine($"Profit-Taking Threshold: {profitTakingThreshold:P}");
		Console.WriteLine($"Starting Balance: {balance:C}");
		Console.WriteLine($"Max Investment Per Coin: {maxInvestmentPerCoin:C}");
		Console.WriteLine("==========================");
	}

	private static void ResetDatabase()
	{
		if (File.Exists(dbPath))
		{
			File.Delete(dbPath);
		}

		InitializeDatabase(true);
		balance = 10000m;
		portfolio.Clear();
		initialInvestments.Clear();
		Console.WriteLine("Database has been reset. Starting over...");
	}

	private static void ShowDatabaseStats()
    {
        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
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
                    Console.WriteLine("\n=== Database Statistics ===");
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        int count = reader.GetInt32(1);
                        decimal minPrice = reader.GetDecimal(2);
                        decimal maxPrice = reader.GetDecimal(3);
                        decimal avgPrice = reader.GetDecimal(4);

                        Console.WriteLine($"{name.ToUpper()}:");
                        Console.WriteLine($"  Data Points: {count}");
                        Console.WriteLine($"  Min Price: ${minPrice:N2}");
                        Console.WriteLine($"  Max Price: ${maxPrice:N2}");
                        Console.WriteLine($"  Avg Price: ${avgPrice:N2}");
                    }
                    Console.WriteLine("\n=== End of Statistics ===");
                }
            }
        }
    }

    private static decimal CalculateSMA(string name, int period)
    {
        if (!priceHistory.ContainsKey(name) || priceHistory[name].Count < period)
            return 0;
        return priceHistory[name].TakeLast(period).Average();
    }

	private static void InitializeDatabase(bool clearPreviousTransactions)
	{
		if (!File.Exists(dbPath))
		{
			SQLiteConnection.CreateFile(dbPath);
		}

		using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
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
                timestamp DATETIME DEFAULT (datetime('now', 'utc'))
            );";
			using (var cmd = new SQLiteCommand(createTableQuery, conn))
			{
				cmd.ExecuteNonQuery();
			}

			// Load historical prices into priceHistory
			string selectQuery = $"SELECT name, price FROM Prices ORDER BY timestamp DESC LIMIT {customPeriods};";
			using (var selectCmd = new SQLiteCommand(selectQuery, conn))
			{
				using (var reader = selectCmd.ExecuteReader())
				{
					while (reader.Read())
					{
						string name = reader.GetString(0);
						decimal price = reader.GetDecimal(1);

						if (!priceHistory.ContainsKey(name))
							priceHistory[name] = new List<decimal>();

						priceHistory[name].Insert(0, price); // Insert at the beginning to maintain order
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
					balance = Convert.ToDecimal(result);
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
						portfolio[name] = quantity;
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
						initialInvestments[name] = investment;
					}
				}
			}
		}
	}


	private static async Task<Dictionary<string, decimal>> GetCryptoPrices()
    {
        try
        {
			Console.WriteLine("\n=== Fetching cryptocurrency prices... ===");

			var response = await client.GetStringAsync(API_URL);
            var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(response);
            var prices = new Dictionary<string, decimal>();

            foreach (var coin in data ?? new Dictionary<string, Dictionary<string, decimal>>())
            {
                string name = coin.Key;
                decimal price = coin.Value["eur"];
                prices[name] = price;

                if (!priceHistory.ContainsKey(name))
                    priceHistory[name] = new List<decimal>();

                priceHistory[name].Add(price);
                if (priceHistory[name].Count > customPeriods)
                    priceHistory[name].RemoveAt(0);
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
        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
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

	private static void SimulateBuy(string coinName, decimal? quantity, decimal price)
	{
		decimal amountToBuy = quantity ?? ((balance / 10) / price); // Buy with 10% of the balance
		decimal cost = amountToBuy * price;

		// Calculate the current investment in the coin
		decimal currentInvestment = initialInvestments.ContainsKey(coinName) ? initialInvestments[coinName] : 0;

		// Check if the new investment exceeds the maximum limit
		if (currentInvestment + cost > maxInvestmentPerCoin)
		{
			Console.WriteLine($"Cannot buy {amountToBuy} of {coinName} at {price} each. Maximum investment limit of {maxInvestmentPerCoin:C} exceeded.");
			return;
		}

		if (balance >= cost)
		{
			balance -= cost;

			if (portfolio.ContainsKey(coinName))
			{
				portfolio[coinName] += amountToBuy;
			}
			else
			{
				portfolio[coinName] = amountToBuy;
			}

			if (initialInvestments.ContainsKey(coinName))
			{
				initialInvestments[coinName] += cost;
			}
			else
			{
				initialInvestments[coinName] = cost;
			}

			Console.WriteLine($"Bought {amountToBuy} of {coinName} at {price} each. New balance: {balance}");

			// Record the transaction in the database
			RecordTransaction("BUY", coinName, amountToBuy, price, balance);
		}
		else
		{
			Console.WriteLine($"Insufficient balance to buy {amountToBuy} of {coinName} at {price} each.");
		}
	}


	private static void SimulateSell(string coinName, decimal price)
	{
		if (portfolio.ContainsKey(coinName) && portfolio[coinName] > 0)
		{
			decimal quantity = portfolio[coinName];
			decimal amountToSell = quantity * price;

			balance += amountToSell;
			portfolio[coinName] = 0;
			Console.WriteLine($"Sold {quantity} of {coinName} at {price} each. New balance: {balance}");

			// Record the transaction in the database
			RecordTransaction("SELL", coinName, quantity, price, balance);
		}
		else
		{
			Console.WriteLine($"No holdings to sell for {coinName}.");
		}
	}

	private static void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal balance)
	{
		using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
		{
			conn.Open();
			string insertQuery = "INSERT INTO Transactions (type, name, quantity, price, balance) VALUES (@type, @name, @quantity, @price, @balance);";
			using (var cmd = new SQLiteCommand(insertQuery, conn))
			{
				cmd.Parameters.AddWithValue("@type", type);
				cmd.Parameters.AddWithValue("@name", coinName);
				cmd.Parameters.AddWithValue("@quantity", quantity);
				cmd.Parameters.AddWithValue("@price", price);
				cmd.Parameters.AddWithValue("@balance", balance);
				cmd.ExecuteNonQuery();
			}
		}
	}


	private static void ShowBalance(Dictionary<string, decimal> prices)
    {
		Console.WriteLine("\n=== Balance and portfolio Report ===");

		decimal portfolioWorth = 0;
        decimal totalInvestment = 0;

        // First, calculate the total portfolio value
        foreach (var coin in portfolio)
        {
            if (coin.Value > 0)
            {
                decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
                decimal value = coin.Value * currentPrice;
                portfolioWorth += value;
            }
        }

        if (portfolioWorth == 0)
        {
            Console.WriteLine("No holdings in the portfolio.");
            return;
        }

        Console.WriteLine("Detailed Portfolio Analysis:");
        foreach (var coin in portfolio)
        {
            if (coin.Value > 0)
            {
                decimal currentPrice = prices.ContainsKey(coin.Key) ? prices[coin.Key] : 0;
                decimal value = coin.Value * currentPrice;
                decimal initialInvestment = initialInvestments.ContainsKey(coin.Key) ? initialInvestments[coin.Key] : 0;
                decimal profitOrLoss = value - initialInvestment;
                totalInvestment += initialInvestment;

                decimal percentageOfPortfolio = portfolioWorth > 0 ? (value / portfolioWorth) * 100 : 0;
                decimal profitOrLossPercentage = initialInvestment > 0 ? (profitOrLoss / initialInvestment) * 100 : 0;

                Console.WriteLine($"{coin.Key}: {coin.Value} units, Current Value: {value:C}, Initial Investment: {initialInvestment:C}, Profit/Loss: {profitOrLoss:C} ({profitOrLossPercentage:N2}%), Percentage of Portfolio: {percentageOfPortfolio:N2}%");
            }
        }

        decimal totalWorth = balance + portfolioWorth;
        decimal initialBalance = 10000m; // Starting balance
        decimal totalProfitOrLoss = totalWorth - initialBalance;
        decimal percentageChange = initialBalance > 0 ? (totalProfitOrLoss / initialBalance) * 100 : 0;

        Console.WriteLine($"\nCurrent balance: {balance:C}");
        Console.WriteLine($"Current portfolio worth: {portfolioWorth:C}");
        Console.WriteLine($"Total worth: {totalWorth:C}");
        Console.WriteLine(totalProfitOrLoss >= 0 ? $"Gains: {totalProfitOrLoss:C} ({percentageChange:N2}%)" : $"Losses: {Math.Abs(totalProfitOrLoss):C} ({percentageChange:N2}%)");

		Console.WriteLine("\n=== End of balance and portfolio Report ===");
	}

	private static void AnalyzeIndicators(Dictionary<string, decimal> prices, int customPeriods, int analysisWindowSeconds)
	{
		Console.WriteLine("\n=== Market Analysis Report ===");
		Console.WriteLine($"Analysis Window: {analysisWindowSeconds} seconds ({customPeriods} periods of {customIntervalSeconds} seconds each)");

		foreach (var coin in prices)
		{
			if (!priceHistory.ContainsKey(coin.Key))
				continue;

			var recentHistory = GetRecentHistorySeconds(coin.Key, analysisWindowSeconds);

			if (recentHistory.Count < 2) // Need at least 2 data points to calculate change
				continue;

			decimal rsi = CalculateRSI(recentHistory);
			decimal sma = CalculateSMA(recentHistory, recentHistory.Count);
			decimal ema = CalculateEMA(recentHistory, recentHistory.Count);
			decimal macd = CalculateMACD(recentHistory);
			decimal priceChangeWindow = CalculatePriceChange(recentHistory);

			// Retrieve the first data timestamp and calculate the time difference from now
			DateTime firstTimestamp = GetFirstTimestampSeconds(coin.Key, analysisWindowSeconds);
			TimeSpan timeDifference = DateTime.UtcNow - firstTimestamp;

			Console.WriteLine($"\n{coin.Key.ToUpper()}:");
			Console.WriteLine($"  Current Price: ${coin.Value:N2}");
			Console.WriteLine($"  {timeDifference.TotalMinutes:N2}m Change: {priceChangeWindow:N2}%");
			Console.WriteLine($"  RSI ({recentHistory.Count}): {rsi:N2}");
			Console.WriteLine($"  SMA ({recentHistory.Count}): ${sma:N2}");
			Console.WriteLine($"  EMA ({recentHistory.Count}): ${ema:N2}");
			Console.WriteLine($"  MACD: ${macd:N2}");
			Console.WriteLine($"  Data Points Included in Analysis: {recentHistory.Count}");
			Console.WriteLine($"  First Data Timestamp: {firstTimestamp} (UTC)");

			// Market sentiment analysis
			string sentiment = "NEUTRAL";
			if (rsi < 30) sentiment = "OVERSOLD";
			else if (rsi > 70) sentiment = "OVERBOUGHT";

			Console.WriteLine($"  Market Sentiment: {sentiment}");

			// Stop-loss and profit-taking strategy
			if (portfolio.ContainsKey(coin.Key) && portfolio[coin.Key] > 0)
			{
				decimal initialInvestment = initialInvestments.ContainsKey(coin.Key) ? initialInvestments[coin.Key] : 0;
				decimal currentValue = portfolio[coin.Key] * coin.Value;
				decimal profitOrLoss = (currentValue - initialInvestment) / initialInvestment;

				if (profitOrLoss <= stopLossThreshold)
				{
					Console.WriteLine($"  STOP-LOSS Triggered: Selling {coin.Key} to prevent further loss.");
					SimulateSell(coin.Key, coin.Value);
				}
				else if (profitOrLoss >= profitTakingThreshold)
				{
					Console.WriteLine($"  PROFIT-TAKING Triggered: Selling {coin.Key} to secure profit.");
					SimulateSell(coin.Key, coin.Value);
				}
			}

			// Trading signals with confidence levels
			if (rsi < 30 && coin.Value < sma && coin.Value < ema && macd < 0)
			{
				decimal confidence = (30 - rsi) / 30 * 100;
				Console.WriteLine($"  BUY Signal (Confidence: {confidence:N2}%)");

				if (balance > 0)
				{
					SimulateBuy(coin.Key, null, coin.Value);
				}
			}
			else if (rsi > 70 && portfolio.ContainsKey(coin.Key) && portfolio[coin.Key] > 0 && coin.Value > sma && coin.Value > ema && macd > 0)
			{
				decimal confidence = (rsi - 70) / 30 * 100;
				Console.WriteLine($"  SELL Signal (Confidence: {confidence:N2}%)");

				SimulateSell(coin.Key, coin.Value);
			}
		}

		Console.WriteLine("\n=== End of Analysis ===");
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

    private static List<decimal> GetRecentHistorySeconds(string coin, int seconds)
    {
        var recentHistory = new List<decimal>();
        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            conn.Open();
            string query = @"
                SELECT price FROM Prices
                WHERE name = @name AND timestamp >= datetime('now', @seconds || ' seconds')
                ORDER BY timestamp DESC;";
            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@name", coin);
                cmd.Parameters.AddWithValue("@seconds", -seconds);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        recentHistory.Add(reader.GetDecimal(0));
                    }
                }
            }
        }
        return recentHistory;
    }

    private static DateTime GetFirstTimestampSeconds(string coin, int seconds)
    {
        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
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
        if (prices.Count < periods + 1)
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
        if (!priceHistory.ContainsKey(name) || priceHistory[name].Count < period)
            return 0;
        decimal smoothing = 2m / (period + 1);
        decimal ema = priceHistory[name].Take(period).Average();
        foreach (var price in priceHistory[name].Skip(period))
        {
            ema = (price - ema) * smoothing + ema;
        }
        return ema;
    }

    private static decimal CalculateRSI(string name, int period)
    {
        if (!priceHistory.ContainsKey(name) || priceHistory[name].Count < period + 1)
            return 0;
        decimal gain = 0, loss = 0;
        for (int i = 1; i <= period; i++)
        {
            decimal change = priceHistory[name][^i] - priceHistory[name][^(i + 1)];
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
    public string Name { get; set; }
    public decimal Price { get; set; }
    public decimal SMA { get; set; }
    public decimal EMA { get; set; }
    public decimal RSI { get; set; }
    public decimal MACD { get; set; }
    public DateTime Timestamp { get; set; }
}