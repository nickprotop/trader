using Spectre.Console;

namespace Trader
{
    public interface ITradeOperations
    {
        string[] Buy(string coinName, decimal? quantity, decimal price);

        string[] Sell(string coinName, decimal price);
    }

	public class SimulationTradeOperations : ITradeOperations
	{
		public Action<string, string, decimal, decimal, decimal, decimal?>? _recordTransactionAction { get; set; }

		public SimulationTradeOperations(Action<string, string, decimal, decimal, decimal, decimal?> recordTransactionAction)
		{
			_recordTransactionAction = recordTransactionAction;
		}

		public string[] Buy(string coinName, decimal? quantity, decimal price)
		{
			decimal amountToBuy = quantity ?? ((RuntimeContext.balance / 10) / price); // Buy with 10% of the balance
			decimal cost = amountToBuy * price;
			decimal fee = cost * Parameters.transactionFeeRate;
			decimal totalCost = cost + fee;

			// Calculate the current investment in the coin
			decimal currentInvestment = RuntimeContext.initialInvestments.ContainsKey(coinName) ? RuntimeContext.initialInvestments[coinName] : 0;

			// Check if the new investment exceeds the maximum limit
			if (currentInvestment + totalCost > Parameters.maxInvestmentPerCoin)
			{
				return new string[] { $"[bold red]Cannot buy {amountToBuy} of {coinName} at {price} each. Maximum investment limit of {Parameters.maxInvestmentPerCoin:C} exceeded.[/]" };
			}

			if (RuntimeContext.balance >= totalCost)
			{
				RuntimeContext.balance -= totalCost;

				// Update portfolio
				if (RuntimeContext.portfolio.ContainsKey(coinName))
					RuntimeContext.portfolio[coinName] += amountToBuy;
				else
					RuntimeContext.portfolio[coinName] = amountToBuy;

				// Update initial investments
				if (RuntimeContext.initialInvestments.ContainsKey(coinName))
					RuntimeContext.initialInvestments[coinName] += cost;
				else
					RuntimeContext.initialInvestments[coinName] = cost;

				// Update total quantity and cost for cost basis calculation
				if (RuntimeContext.totalQuantityPerCoin.ContainsKey(coinName))
				{
					RuntimeContext.totalQuantityPerCoin[coinName] += amountToBuy;
					RuntimeContext.totalCostPerCoin[coinName] += cost;
				}
				else
				{
					RuntimeContext.totalQuantityPerCoin[coinName] = amountToBuy;
					RuntimeContext.totalCostPerCoin[coinName] = cost;
				}

				// Record the transaction in the database
				_recordTransactionAction?.Invoke("BUY", coinName, amountToBuy, price, fee, null);

				return new string[] { $"[bold green]Bought {amountToBuy} of {coinName} at {price} each. Fee: {fee:C}. New balance: {RuntimeContext.balance:C}[/]" };
			}
			else
			{
				return new string[] { $"[bold red]Insufficient balance to buy {amountToBuy} of {coinName} at {price} each.[/]" };
			}
		}

		public string[] Sell(string coinName, decimal price)
		{
			if (RuntimeContext.portfolio.ContainsKey(coinName) && RuntimeContext.portfolio[coinName] > 0)
			{
				decimal quantityToSell = RuntimeContext.portfolio[coinName];
				decimal amountToSell = quantityToSell * price;
				decimal fee = amountToSell * Parameters.transactionFeeRate;
				decimal netAmountToSell = amountToSell - fee;

				// Calculate average cost basis
				decimal averageCostBasis = RuntimeContext.totalCostPerCoin[coinName] / RuntimeContext.totalQuantityPerCoin[coinName];
				decimal costOfSoldCoins = averageCostBasis * quantityToSell;

				// Calculate gain or loss
				decimal gainOrLoss = netAmountToSell - costOfSoldCoins;

				// Update balance and portfolio
				RuntimeContext.balance += netAmountToSell;
				RuntimeContext.portfolio[coinName] = 0;

				// Update total quantity and cost
				RuntimeContext.totalQuantityPerCoin[coinName] -= quantityToSell;
				RuntimeContext.totalCostPerCoin[coinName] -= costOfSoldCoins;

				// Adjust initial investments
				if (RuntimeContext.initialInvestments.ContainsKey(coinName))
				{
					RuntimeContext.initialInvestments[coinName] -= costOfSoldCoins;
					if (RuntimeContext.initialInvestments[coinName] <= 0)
					{
						RuntimeContext.initialInvestments.Remove(coinName);
					}
				}

				AnsiConsole.MarkupLine($"[bold cyan]Sold {quantityToSell} of {coinName} at {price} each. Fee: {fee:C}. New balance: {RuntimeContext.balance:C}[/]");
				AnsiConsole.MarkupLine($"Transaction Gain/Loss: {(gainOrLoss > 0 ? "[bold green]" : "[bold red]")}{gainOrLoss:C}[/]");

				// Record the transaction in the database with gain or loss
				_recordTransactionAction?.Invoke("SELL", coinName, quantityToSell, price, fee, gainOrLoss);

				return new string[] { $"[bold cyan]Sold {quantityToSell} of {coinName} at {price} each. Fee: {fee:C}. New balance: {RuntimeContext.balance:C}[/]" };
			}
			else
			{
				return new string[] { $"[bold red]No holdings to sell for {coinName}.[/]" };
			}
		}


	}



}