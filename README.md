# Crypto Trading Bot

Welcome to the Crypto Trading Bot! This bot is designed to automate cryptocurrency trading using various technical indicators and strategies.

## Features

### 1. Automated Trading
- **Buy and Sell Operations**: Automatically buys and sells cryptocurrencies based on predefined strategies and indicators.
- **Stop-Loss and Profit-Taking**: Implements stop-loss and profit-taking thresholds to manage risk and secure profits.
- **Trailing Stop-Loss**: Dynamically adjusts the stop-loss level as the price of a coin increases to lock in profits while protecting against significant losses.
- **Dollar-Cost Averaging (DCA)**: Automatically buys a fixed amount of a coin at regular intervals to reduce the impact of volatility.

### 2. Technical Indicators
- **Simple Moving Average (SMA)**: Calculates the SMA for a given period.
- **Exponential Moving Average (EMA)**: Calculates the EMA for a given period.
- **Relative Strength Index (RSI)**: Calculates the RSI to identify overbought and oversold conditions.
- **Moving Average Convergence Divergence (MACD)**: Calculates the MACD to identify trend changes.
- **Bollinger Bands**: Calculates Bollinger Bands to identify volatility and potential price reversals.
- **Average True Range (ATR)**: Calculates the ATR to measure market volatility.

### 3. Market Analysis
- **Real-Time Price Fetching**: Fetches real-time cryptocurrency prices from the CoinGecko API.
- **Market Sentiment Analysis**: Analyzes market sentiment based on RSI and other indicators.
- **Volatility Adjustment**: Adjusts stop-loss and profit-taking thresholds based on market volatility.

### 4. Portfolio Management
- **Balance and Portfolio Report**: Displays the current balance, portfolio worth, and detailed portfolio holdings.
- **Transaction History**: Shows the history of all buy and sell transactions with detailed information.
- **Database Statistics**: Provides statistics on the stored price data and transaction history.

### 5. User Interaction
- **Console Menu**: Interactive console menu for various operations such as viewing balance, transaction history, and database statistics.
- **Verbose Mode**: Option to view detailed balance and portfolio information.

### 6. Backtesting
- **Strategy Backtesting**: Allows backtesting of trading strategies using historical data to evaluate performance.

### 7. Machine Learning
- **Model Training**: Trains a machine learning model using historical data to predict future prices.
- **Model Retraining**: Allows retraining of the machine learning model through the console menu.

## Usage

### Running the Bot
To run the bot, execute the `Program.cs` file. The bot will start fetching real-time prices and perform automated trading based on the predefined strategies.

### Console Commands
- **C**: Clear the database and start over.
- **T**: View transaction history.
- **V**: View verbose balance and portfolio.
- **D**: Show database statistics.
- **P**: Show program parameters.
- **A**: Show analysis strategy.
- **B**: Buy a coin.
- **S**: Sell a coin.
- **K**: Backtest strategy.
- **R**: Retrain the model.
- **Q**: Quit the program.

### Configuration
The bot's parameters can be configured in the `Parameters` class:
- `CustomIntervalSeconds`: Interval time in seconds for fetching prices.
- `CustomPeriods`: Number of periods for analysis.
- `API_URL`: URL for fetching cryptocurrency prices.
- `dbPath`: Path to the SQLite database.
- `stopLossThreshold`: Stop-loss threshold percentage.
- `profitTakingThreshold`: Profit-taking threshold percentage.
- `maxInvestmentPerCoin`: Maximum investment amount per coin.
- `startingBalance`: Starting balance.
- `transactionFeeRate`: Transaction fee rate.
- `trailingStopLossPercentage`: Trailing stop-loss percentage.
- `dollarCostAveragingAmount`: Amount to invest in each DCA purchase.
- `dollarCostAveragingSecondsInterval`: Time interval in seconds between DCA purchases.

## Dependencies
- .NET 9
- Spectre.Console
- System.Data.SQLite

## License
This project is licensed under the GPLv3.