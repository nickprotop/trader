# trader

The code in Program.cs is a cryptocurrency trading simulation program that fetches cryptocurrency prices, stores them in a SQLite database, and performs analysis to simulate buying and selling based on certain indicators. Here's a breakdown of how it works:
Initialization
1.	Constants and Variables:
•	API_URL: URL to fetch cryptocurrency prices.
•	dbPath: Path to the SQLite database file.
•	customIntervalSeconds, customPeriods: Parameters for the analysis interval and periods.
•	stopLossThreshold, profitTakingThreshold: Thresholds for stop-loss and profit-taking strategies.
•	priceHistory, balance, portfolio, initialInvestments: Data structures to store price history, balance, portfolio holdings, and initial investments.
2.	IsConsoleAvailable: Checks if the console is available to avoid exceptions in environments without a console.
Main Execution Loop
3.	Main Method:
•	Initializes the database and optionally clears previous transactions.
•	Enters an infinite loop to fetch prices, store indicators, analyze them, and simulate trades.
•	Handles console input to reset the database if the 'C' key is pressed.
Database Operations
4.	InitializeDatabase: Creates the database and tables if they don't exist, loads historical prices, balance, portfolio, and initial investments from the database.
5.	ResetDatabase: Deletes the database file and reinitializes it, resetting the balance and clearing the portfolio and initial investments.
6.	StoreIndicatorsInDatabase: Stores the fetched prices and calculated indicators (SMA, EMA, RSI, MACD) in the database.
Fetching and Analyzing Prices
7.	GetCryptoPrices: Fetches current cryptocurrency prices from the API and updates the price history.
8.	AnalyzeIndicators: Analyzes the fetched prices using various indicators and simulates buy/sell actions based on the analysis.
Trading Simulation
9.	SimulateBuy: Simulates buying a cryptocurrency, updates the balance and portfolio, and records the transaction in the database.
10.	SimulateSell: Simulates selling a cryptocurrency, updates the balance and portfolio, and records the transaction in the database.
11.	RecordTransaction: Records a buy or sell transaction in the database.
Indicator Calculations
12.	CalculateSMA: Calculates the Simple Moving Average (SMA) for a given period.
13.	CalculateEMA: Calculates the Exponential Moving Average (EMA) for a given period.
14.	CalculateRSI: Calculates the Relative Strength Index (RSI) for a given period.
15.	CalculateMACD: Calculates the Moving Average Convergence Divergence (MACD) indicator.
Utility Methods
16.	ShowDatabaseStats: Displays statistics from the database.
17.	ShowBalance: Displays the current balance and portfolio worth.
18.	PrintProgramParameters: Prints the program parameters.
19.	GetRecentHistorySeconds: Retrieves recent price history for a given time window.
20.	GetFirstTimestampSeconds: Retrieves the first timestamp within a given time window.
21.	CalculatePriceChange: Calculates the percentage price change over a given history.
Summary
The program continuously fetches cryptocurrency prices, stores them in a database, and performs analysis to simulate trading actions based on predefined strategies. It uses various technical indicators to make buy/sell decisions and maintains a record of transactions and portfolio performance.