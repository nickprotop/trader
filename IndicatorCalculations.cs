using System;
using System.Collections.Generic;
using System.Linq;

namespace Trader
{
	public static class IndicatorCalculations
	{
		public static decimal CalculateSMA(List<decimal> prices, int periods)
		{
			if (prices.Count < periods)
				return prices.Average();

			return prices.TakeLast(periods).Average();
		}

		public static decimal CalculateEMASingle(List<decimal> prices, int periods)
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

		public static decimal CalculateRSI(List<decimal> prices, int periods = 14)
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

		public static (decimal macdValue, int bestShortPeriod, int bestLongPeriod, int bestSignalPeriod) CalculateMACD(List<decimal> prices)
		{
			var (bestShortPeriod, bestLongPeriod, bestSignalPeriod) = FindBestMACDPeriods(prices);
			List<decimal> macdLine = CalculateMACDLine(prices, bestShortPeriod, bestLongPeriod);
			List<decimal> signalLine = CalculateEMA(macdLine, bestSignalPeriod);

			decimal macdValue = macdLine.Last() - signalLine.Last();
			return (macdValue, bestShortPeriod, bestLongPeriod, bestSignalPeriod);
		}

		public static (decimal middleBand, decimal upperBand, decimal lowerBand) CalculateBollingerBands(List<decimal> prices, int period, decimal multiplier = 2)
		{
			if (prices.Count < period)
				return (0, 0, 0);

			decimal sma = CalculateSMA(prices, period);
			decimal stdDev = (decimal)Math.Sqrt((double)prices.TakeLast(period).Select(p => (p - sma) * (p - sma)).Sum() / period);

			decimal upperBand = sma + (multiplier * stdDev);
			decimal lowerBand = sma - (multiplier * stdDev);

			return (sma, upperBand, lowerBand);
		}

		public static decimal CalculateATR(List<decimal> highPrices, List<decimal> lowPrices, List<decimal> closePrices, int period)
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

		public static decimal CalculateVolatility(List<decimal> prices, int period)
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

		public static decimal CalculatePriceChange(List<decimal> history)
		{
			if (history.Count < 2) return 0; // Need at least 2 data points to calculate change
			var oldPrice = history.Last();
			var currentPrice = history.First();
			return ((currentPrice - oldPrice) / oldPrice) * 100;
		}
	}
}
