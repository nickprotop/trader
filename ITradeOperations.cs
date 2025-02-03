using Spectre.Console;

namespace Trader
{
    public interface ITradeOperations
    {
        void Buy(string coinName, decimal? quantity, decimal price);

        void Sell(string coinName, decimal price);
    }

    public class SimulationTradeOperations : ITradeOperations
    {
        public Action<string, string, decimal, decimal, decimal, decimal?>? _recordTransactionAction { get; set; }

        public SimulationTradeOperations(Action<string, string, decimal, decimal, decimal, decimal?> recordTransactionAction)
        {
            _recordTransactionAction = recordTransactionAction;
        }

        public void Buy(string coinName, decimal? quantity, decimal price)
        {
            decimal amountToBuy = quantity ?? ((RuntimeContext.balance / 10) / price); // Buy with 10% of the balance
            decimal cost = amountToBuy * price;

            // Calculate the current investment in the coin
            decimal currentInvestment = RuntimeContext.initialInvestments.ContainsKey(coinName) ? RuntimeContext.initialInvestments[coinName] : 0;

            // Check if the new investment exceeds the maximum limit
            if (currentInvestment + cost > Parameters.maxInvestmentPerCoin)
            {
                AnsiConsole.MarkupLine($"[bold red]Cannot buy {amountToBuy} of {coinName} at {price} each. Maximum investment limit of {Parameters.maxInvestmentPerCoin:C} exceeded.[/]");
                return;
            }

            if (RuntimeContext.balance >= cost)
            {
                RuntimeContext.balance -= cost;

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

                AnsiConsole.MarkupLine($"[bold green]Bought {amountToBuy} of {coinName} at {price} each. New balance: {RuntimeContext.balance:C}[/]");

                // Record the transaction in the database
                _recordTransactionAction?.Invoke("BUY", coinName, amountToBuy, price, RuntimeContext.balance, null);
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]Insufficient balance to buy {amountToBuy} of {coinName} at {price} each.[/]");
            }
        }

        public void Sell(string coinName, decimal price)
        {
            if (RuntimeContext.portfolio.ContainsKey(coinName) && RuntimeContext.portfolio[coinName] > 0)
            {
                decimal quantityToSell = RuntimeContext.portfolio[coinName];
                decimal amountToSell = quantityToSell * price;

                // Calculate average cost basis
                decimal averageCostBasis = RuntimeContext.totalCostPerCoin[coinName] / RuntimeContext.totalQuantityPerCoin[coinName];
                decimal costOfSoldCoins = averageCostBasis * quantityToSell;

                // Calculate gain or loss
                decimal gainOrLoss = amountToSell - costOfSoldCoins;

                // Update balance and portfolio
                RuntimeContext.balance += amountToSell;
                RuntimeContext.portfolio[coinName] = 0;

                // Update total quantity and cost
                RuntimeContext.totalQuantityPerCoin[coinName] -= quantityToSell;
                RuntimeContext.totalCostPerCoin[coinName] -= costOfSoldCoins;

                AnsiConsole.MarkupLine($"[bold cyan]Sold {quantityToSell} of {coinName} at {price} each. New balance: {RuntimeContext.balance:C}[/]");
                AnsiConsole.MarkupLine($"Transaction Gain/Loss: {(gainOrLoss > 0 ? "[bold green]" : "[bold red]")}{gainOrLoss:C}[/]");

                // Record the transaction in the database with gain or loss
                _recordTransactionAction?.Invoke("SELL", coinName, quantityToSell, price, RuntimeContext.balance, gainOrLoss);
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]No holdings to sell for {coinName}.[/]");
            }
        }
    }
}