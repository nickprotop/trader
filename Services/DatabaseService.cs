using System;
using System.Data.SQLite;
using trader.Models;
using Trader;

namespace trader.Services
{
	public class DatabaseService : IDatabaseService
	{
		private readonly ISettingsService _settingsService;
		private readonly RuntimeContext _runtimeContext;

		public DatabaseService(ISettingsService settingsService, RuntimeContext runtimeContext)
		{
			_settingsService = settingsService;
			_runtimeContext = runtimeContext;
		}

		public DateTime? GetLastPurchaseTime(string coin)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public void SaveTrailingStopLoss(string coin, decimal stopLoss)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public decimal? GetTrailingStopLoss(string coin)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public void SaveDCAConfig(string coin, DateTime lastPurchaseTime)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public List<(decimal Price, DateTime Timestamp)> GetRecentPrices(string coin, int rowCount)
		{
			var recentHistory = new List<(decimal Price, DateTime Timestamp)>();
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public decimal CalculateTotalFees()
		{
			decimal totalFees = 0;

			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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

		public void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal fee, decimal? gainLoss)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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
					cmd.Parameters.AddWithValue("@gainLoss", gainLoss.HasValue ? gainLoss.Value : DBNull.Value);
					cmd.ExecuteNonQuery();
				}
			}
		}

		public void InitializeRuntimeContext()
		{
			if (!File.Exists(_settingsService.Settings.DbPath))
			{
				SQLiteConnection.CreateFile(_settingsService.Settings.DbPath);
			}

			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
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
					bollingerUpper DECIMAL(18,8),
					bollingerLower DECIMAL(18,8),
					atr DECIMAL(18,8),
					volatility DECIMAL(18,8),
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
				);
				CREATE TABLE IF NOT EXISTS DCAConfig (
					coin TEXT PRIMARY KEY,
					lastPurchaseTime DATETIME
				);
				CREATE TABLE IF NOT EXISTS TrailingStopLossConfig (
					coin TEXT PRIMARY KEY,
					stopLoss DECIMAL(18,8)
				);";
				using (var cmd = new SQLiteCommand(createTableQuery, conn))
				{
					cmd.ExecuteNonQuery();
				}

				_runtimeContext.Balance = _settingsService.Settings.StartingBalance;
				_runtimeContext.Portfolio.Clear();
				_runtimeContext.InitialInvestments.Clear();
				_runtimeContext.TotalQuantityPerCoin.Clear();
				_runtimeContext.TotalCostPerCoin.Clear();
				_runtimeContext.CurrentPeriodIndex = 0;
				_runtimeContext.CachedPrices.Clear();

				string selectQuery = $"SELECT name, price FROM Prices ORDER BY timestamp DESC;";
				using (var selectCmd = new SQLiteCommand(selectQuery, conn))
				{
					using (var reader = selectCmd.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = reader.GetString(0);
							decimal price = reader.GetDecimal(1);

							if (!_runtimeContext.CachedPrices.ContainsKey(name))
								_runtimeContext.CachedPrices[name] = new List<decimal>();

							if (_runtimeContext.CachedPrices[name].Count < _settingsService.Settings.CustomPeriods)
								_runtimeContext.CachedPrices[name].Insert(0, price); // Insert at the beginning to maintain order
						}
					}
				}

				// Calculate the balance from transactions
				_runtimeContext.Balance = _settingsService.Settings.StartingBalance;
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
							decimal? gainLoss = reader.IsDBNull(4) ? null : reader.GetDecimal(4);

							if (type == "BUY")
							{
								_runtimeContext.Balance -= quantity * price + fee;
							}
							else if (type == "SELL")
							{
								_runtimeContext.Balance += quantity * price - fee;
								if (gainLoss.HasValue)
								{
									_runtimeContext.Balance += gainLoss.Value;
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
							_runtimeContext.Portfolio[name] = quantity;
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
							_runtimeContext.InitialInvestments[name] = investment;
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
							_runtimeContext.TotalQuantityPerCoin[name] = totalQuantity;
							_runtimeContext.TotalCostPerCoin[name] = totalCost;
						}
					}
				}
			}
		}

		public List<HistoricalData> LoadHistoricalData()
		{
			var historicalData = new List<HistoricalData>();

			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				string query = @"
				SELECT name, price, timestamp, ema, macd, rsi, sma, bollingerUpper, bollingerLower, atr, volatility
				FROM Prices
				ORDER BY timestamp ASC;";
				using (var cmd = new SQLiteCommand(query, conn))
				{
					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							var data = new HistoricalData
							{
								Name = reader.GetString(0),
								Price = reader.GetDecimal(1),
								Timestamp = reader.GetDateTime(2)
							};

							// Check for null values and handle them appropriately
							data.EMA = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
							data.MACD = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
							data.RSI = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
							data.SMA = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
							data.BollingerUpper = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7);
							data.BollingerLower = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8);
							data.ATR = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9);
							data.Volatility = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10);

							historicalData.Add(data);
						}
					}
				}
			}

			return historicalData;
		}

		public void StoreIndicatorsInDatabase(Dictionary<string, decimal> prices)
		{
			using (var conn = new SQLiteConnection($"Data Source={_settingsService.Settings.DbPath};Version=3;"))
			{
				conn.Open();
				foreach (var coin in prices)
				{
					var recentHistory = GetRecentPrices(coin.Key, 60);

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
	}
}
