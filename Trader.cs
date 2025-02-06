using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
			bool performResetDatabase = args.Contains("-c");

			AnsiConsole.MarkupLine("[bold yellow]=== Welcome to the Crypto Trading Bot ===[/]");

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

			PrintProgramParameters();

			// Load the model if it exists
			if (File.Exists("model.zip"))
			{
				mlService.LoadModel("model.zip");
				AnsiConsole.MarkupLine("[bold green]\n=== Model loaded successfully! ===[/]");
			}
			else
			{
				TrainAiModel(mlService);
			}

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
							ShowBalance(runtimeContext.CurrentPrices, true, true);
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
							AnalyzeIndicators(runtimeContext.CurrentPrices, settingsService.Settings.CustomPeriods, settingsService.Settings.CustomIntervalSeconds * settingsService.Settings.CustomPeriods, true);
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

						if (key.Key == ConsoleKey.K)
						{
							BacktestStrategy(databaseService.LoadHistoricalData());
							PrintMenu();
						}

						if (key.Key == ConsoleKey.R)
						{
							TrainAiModel(mlService);
							PrintMenu();
						}
					}

					// Check if the background task is running, and if not, restart it
					if (iterationTask.IsCompleted)
					{
						AnsiConsole.MarkupLine("[bold red]Background task stopped. Quitting...[/]");
						break;
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

		private void ResetDatabase()
		{
			if (File.Exists(_settingsService.Settings.DbPath))
			{
				File.Delete(_settingsService.Settings.DbPath);
			}

			_databaseService.InitializeRuntimeContext();

			AnsiConsole.MarkupLine("[bold red]Database has been reset. Starting over...[/]");
		}


	}
}
