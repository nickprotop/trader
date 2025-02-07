# Crypto Trading Bot

Welcome to the Crypto Trading Bot! This bot is designed to automate cryptocurrency trading using various technical indicators and strategies.

## Overview
The Trader application is designed to manage and analyze cryptocurrency trading data. It provides functionalities to track balances, cached prices, portfolio, initial investments, and more.

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

## UI Features
- **Main Menu**: Access to various functionalities including resetting the database, retraining the AI model, and viewing startup parameters.
- **Live Analysis**: Real-time analysis of cryptocurrency prices and indicators.
- **Balance and Portfolio**: View current balance, portfolio worth, and detailed investment statistics.
- **Transaction History**: View and sort transaction history with pagination support.
- **Operations**: Perform buy and sell operations with detailed prompts and confirmations.
- **Database Statistics**: View statistics about the stored price data and transaction history.

## Code Features
- **Technical Indicators**: Calculation of RSI, SMA, EMA, MACD, Bollinger Bands, ATR, and volatility.
- **Machine Learning**: Train and use machine learning models to predict future prices.
- **Backtesting**: Simulate trading strategies on historical data to evaluate performance.
- **Trailing Stop-Loss**: Automatically adjust stop-loss thresholds based on price movements.
- **Dollar-Cost Averaging (DCA)**: Automatically buy a fixed amount of cryptocurrency at regular intervals.
- **Error Handling**: Robust error handling and user prompts for critical operations.

## Styles and Patterns
- **Dependency Injection**: Use of dependency injection for managing services and operations.
- **Asynchronous Programming**: Extensive use of async/await for non-blocking operations.
- **Console UI**: Use of Spectre.Console for rich console output and user interactions.
- **Separation of Concerns**: Clear separation of different functionalities into services and models.
- **Configuration Management**: Centralized management of settings and configurations.

## Technologies Used
- **C# 13.0**: The application is developed using the latest features of C#.
- **.NET 9**: Target framework for building and running the application.
- **Microsoft.Extensions.DependencyInjection**: Dependency injection framework for managing service lifetimes.
- **Microsoft.Extensions.Hosting**: Hosting framework for building long-running applications.
- **Microsoft.ML**: Machine learning library for integrating ML models.
- **Spectre.Console**: Library for creating beautiful console applications.
- **System.Data.SQLite**: SQLite database provider for data storage.

## Getting Started
To get started with the Trader application, follow these steps:
1. Clone the repository.
2. Open the solution in Visual Studio 2022.
3. Build the solution to restore the necessary packages.
4. Run the application.

## Contributing
Contributions are welcome! Please fork the repository and submit pull requests for any enhancements or bug fixes.

## Dependencies
- .NET 9
- Spectre.Console
- System.Data.SQLite

## License
This project is licensed under the GPLv3.