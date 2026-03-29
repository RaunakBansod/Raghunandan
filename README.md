XtsConnect C# Implementation (Minor Version)
A lightweight C# implementation of the XTS API, ported from the original Python package. This project demonstrates the ability to handle RESTful API communication, WebSocket streaming, and financial data processing in a strictly typed environment.

# Features Implemented
Market Data Authentication: Secure login and session management via XTS API.

Equity OHLC Downloader: Automated retrieval of historical data for the Top 5 Nifty 50 constituents.

F&O Data Engine: 1-minute interval data fetching for Near-Month contracts (HDFCBANK & NIFTY).

Socket Streaming: Real-time data broadcasting using a robust WebSocket implementation.

# Tech Stack & Dependencies
Runtime: .NET 6.0 / 8.0

Language: C#

Key Packages:

Newtonsoft.Json: For high-performance JSON parsing.

Microsoft.Extensions.Logging: Standardized diagnostic logging.

DotNetEnv: Environment variable management for API credentials.

SocketIOClient / WebSockets: For real-time market data streaming.

# Project Structure
XtsConnect.cs: The core wrapper handling REST requests and API logic.

MarketSocket.cs: Handles the WebSocket lifecycle and event subscriptions.

Models/: Contains POCO classes for OHLC, Quotes, and Authentication responses.

Program.cs: The entry point demonstrating the full workflow.

# Setup & Usage
Clone the Repository:

Bash
git clone (https://github.com/RaunakBansod/Raghunandan)
Configure Environment:
Create a .env file in the root directory:

Bash
dotnet restore
dotnet build
Run:

Bash
dotnet run
# Implementation Details
Data Handling: Implemented asynchronous Task-based patterns to ensure the UI/Main thread remains non-blocking during heavy data downloads.

Logging: Integrated AddConsole() logging to provide real-time feedback on socket connectivity and API rate limits.
