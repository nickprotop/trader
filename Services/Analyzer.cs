using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using trader.Models;
using Trader;

namespace trader.Services
{
	public class Analyzer : IAnalyzer
	{
		private readonly ITradeOperations _tradeOperations;
		private readonly IMachineLearningService _mlService;
		private readonly IDatabaseService _databaseService;
		private readonly ISettingsService _settingsService;
		private readonly RuntimeContext _runtimeContext;

		public Analyzer(ITradeOperations tradeOperations,
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

		public List<string> AnalyzeIndicators(int customPeriods, int analysisWindowSeconds, bool analysisOnly)
		{
			DateTime startAnalysisTimeStamp = DateTime.Now.ToUniversalTime();

			List<string> operationsPerformed = new List<string>();

			if (!analysisOnly) _runtimeContext.CurrentPeriodIndex++;

			AnsiConsole.MarkupLine($"\n[bold yellow]=== Market Analysis Report - Period Counter: {_runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			foreach (var coin in _databaseService.GetAllCoinNames())
			{
				bool operationsAllowed = true;

				AnsiConsole.MarkupLine($"\n[bold cyan]{coin.ToUpper()}[/]:");

				if (!_runtimeContext.CurrentPrices.ContainsKey(coin))
					continue;

				decimal currentPrice = _runtimeContext.CurrentPrices[coin];

				var recentHistoryData = _databaseService.GetRecentPrices(coin, _settingsService.Settings.CustomPeriods);

				var table = new Table();
				table.AddColumn("Indicator");
				table.AddColumn("Value");

				if (recentHistoryData.Count < 2) // Need at least 2 data points to calculate change
				{
					table.AddRow("Analysis Status", $"[bold red]Insufficient data points for {coin} analysis.[/]");
					AnsiConsole.Write(table);
					continue;
				}

				var recentHistory = recentHistoryData.Select(x => x.Price).ToList();

				if (recentHistory.Count < customPeriods)
				{
					table.AddRow("Analysis Status", $"[bold red]Insufficient data points for {coin} analysis.[/]");
					operationsAllowed = false;
				}

				// Check if the timeframe is suitable for analysis
				DateTime earliestTimestamp = recentHistoryData.First().Timestamp;
				TimeSpan timeframe = DateTime.UtcNow - earliestTimestamp;

				if (_settingsService.Settings.CheckForValidTimeIntervalToPerformAnalysis)
				{
					int bufferSeconds = _settingsService.Settings.CustomIntervalSeconds * 2; // Buffer for capture delays
					if (timeframe.TotalSeconds < (analysisWindowSeconds - bufferSeconds) || timeframe.TotalSeconds > (analysisWindowSeconds + bufferSeconds))
					{
						table.AddRow("Analysis Status", $"[bold red]Not valid timeframe for {coin} analysis. Required: {analysisWindowSeconds} seconds, Available: {timeframe.TotalSeconds} seconds (including buffer of {bufferSeconds} seconds).[/]");
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

				table.AddRow("Current Price", $"[bold green]${currentPrice:N2}[/]");
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
				if (currentPrice > upperBand)
				{
					table.AddRow("Alert", $"[bold red]{coin.ToUpper()} price crossed above the upper Bollinger Band![/]");
				}
				else if (currentPrice < lowerBand)
				{
					table.AddRow("Alert", $"[bold red]{coin.ToUpper()} price crossed below the lower Bollinger Band![/]");
				}

				// Alert logic for ATR
				//decimal atrThreshold = 0.05m; // Example threshold for ATR
				//if (atr > atrThreshold)
				//{
					//table.AddRow("Alert", $"[bold red]{coin.Key.ToUpper()} ATR value is high, indicating high volatility![/]");
				//}

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
						Timestamp = DateTime.Now,
						Price = (float)currentPrice,
						SMA = (float)sma,
						EMA = (float)ema,
						RSI = (float)rsi,
						MACD = (float)macd,
						BollingerLower = (float)lowerBand,
						BollingerUpper = (float)upperBand,
						ATR = (float)atr,
						Volatility = (float)volatility
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
					if (_runtimeContext.Portfolio.ContainsKey(coin) && _runtimeContext.Portfolio[coin] > 0)
					{
						decimal initialInvestment = _runtimeContext.InitialInvestments.ContainsKey(coin) ? _runtimeContext.InitialInvestments[coin] : 0;
						decimal currentValue = _runtimeContext.Portfolio[coin] * currentPrice;
						decimal fee = currentValue * _settingsService.Settings.TransactionFeeRate;
						decimal profitOrLoss = (currentValue - initialInvestment - fee) / initialInvestment;

						if (profitOrLoss <= adjustedStopLoss)
						{
							string message = $"[bold red]Selling {coin.ToUpper()} to prevent further loss.[/]Selling {coin.ToUpper()} to prevent further loss.";

							operationsPerformed.Add($"STOP-LOSS: {message}");
							operationsTable.AddRow("STOP-LOSS", message);

							var sellResult = _tradeOperations.Sell(coin, currentPrice);
							foreach (var result in sellResult)
							{
								operationsPerformed.Add($"STOP-LOSS: SELL Result: {result}");
								operationsTable.AddRow("SELL Result", result);
							}
						}
						else if (profitOrLoss >= adjustedProfitTaking)
						{
							string message = $"[bold green]Selling {coin.ToUpper()} to secure profit.[/]";

							operationsPerformed.Add($"PROFIT-TAKING: {message}");
							operationsTable.AddRow("PROFIT-TAKING", message);

							var sellResult = _tradeOperations.Sell(coin, currentPrice);
							foreach (var result in sellResult)
							{
								operationsPerformed.Add($"PROFIT-TAKING: SELL Result: {result}");
								operationsTable.AddRow("SELL Result", result);
							}
						}
					}

					// Trailing Stop-Loss
					UpdateTrailingStopLoss(coin, currentPrice, _settingsService.Settings.TrailingStopLossPercentage);
					decimal trailingStopLoss = _databaseService.GetTrailingStopLoss(coin) ?? decimal.MaxValue;
					if (currentPrice <= trailingStopLoss)
					{
						var message = $"[bold red]Selling {coin.ToUpper()} due to trailing stop-loss.[/]";
						
						operationsPerformed.Add($"TRAILING STOP-LOSS: {message}");
						operationsTable.AddRow("TRAILING STOP-LOSS", message);
						var sellResult = _tradeOperations.Sell(coin, currentPrice);
						foreach (var result in sellResult)
						{
							operationsPerformed.Add($"TRAILING STOP-LOSS: SELL Result: {result}");
							operationsTable.AddRow("SELL Result", result);
						}
					}

					// Dollar-Cost Averaging (DCA)
					string[] dollarCostAveraging = ExecuteDCA(coin, _settingsService.Settings.DollarCostAveragingAmount, currentPrice, TimeSpan.FromSeconds(_settingsService.Settings.DollarCostAveragingSecondsInterval));
					foreach (var result in dollarCostAveraging)
					{
						operationsPerformed.Add($"DollarCostAveraging: {result}");
						operationsTable.AddRow("DollarCostAveraging", result);
					}

					// Trading signals with confidence levels
					if (rsi < 30 && currentPrice < sma && currentPrice < ema && macd < 0 && currentPrice < lowerBand && atr > 0)
					{
						decimal confidence = (30 - rsi) / 30 * 100;
						operationsTable.AddRow("BUY Signal", $"[bold green]Confidence: {confidence:N2}%[/]");

						if (_runtimeContext.Balance > 0)
						{
							if (operationsAllowed)
							{
								var buyResult = _tradeOperations.Buy(coin, null, currentPrice);
								foreach (var result in buyResult)
								{
									operationsTable.AddRow("BUY Result", result);
									operationsPerformed.Add($"BUY Signal: {result}");
								}
							}
							else
							{
								operationsTable.AddRow("BUY Operation", $"[bold red]Skipping buy operation because analysis is not valid.[/]");
							}
						}
					}
					else if (rsi > 70 && _runtimeContext.Portfolio.ContainsKey(coin) && _runtimeContext.Portfolio[coin] > 0 && currentPrice > sma && currentPrice > ema && macd > 0 && currentPrice > upperBand && atr > 0)
					{
						decimal confidence = (rsi - 70) / 30 * 100;

						decimal initialInvestment = _runtimeContext.InitialInvestments.ContainsKey(coin) ? _runtimeContext.InitialInvestments[coin] : 0;
						decimal currentValue = _runtimeContext.Portfolio[coin] * currentPrice;
						decimal fee = currentValue * _settingsService.Settings.TransactionFeeRate;
						decimal profitOrLoss = (currentValue - initialInvestment - fee) / initialInvestment;

						if (profitOrLoss <= 0 && confidence < 90)
						{
							operationsTable.AddRow("SELL Signal", $"[bold red]Prevent sell because of loss and Confidence < 90% ({confidence:N2}%)[/]");
							continue;
						}

						operationsTable.AddRow("SELL Signal", $"[bold cyan]Confidence: {confidence:N2}%[/]");

						if (operationsAllowed)
						{
							var sellResult = _tradeOperations.Sell(coin, currentPrice);
							foreach (var result in sellResult)
							{
								operationsPerformed.Add($"SELL Signal: {result}");
								operationsTable.AddRow("SELL Result", result);
							}
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

			AnsiConsole.MarkupLine($"\n[bold yellow]=== End of Analysis - Period Counter: {_runtimeContext.CurrentPeriodIndex} - TimeStamp: {startAnalysisTimeStamp} ===[/]");

			return operationsPerformed;
		}

		private void UpdateTrailingStopLoss(string coin, decimal currentPrice, decimal trailingPercentage)
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

		private string[] ExecuteDCA(string coin, decimal amount, decimal currentPrice, TimeSpan interval)
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
