# Stock Market Analyzer

An AI-powered stock market analysis tool built with ASP.NET Core Blazor Server and the `Twf.Flow` workflow engine.

## Features

1. **Trend Analysis** — Identifies bullish/bearish/sideways trends, moving averages, and technical indicators
2. **Recommendation** — Generates Buy/Sell/Hold recommendations with target prices and confidence scores
3. **Risk Assessment** — Identifies volatility, drawdown risk, and other investment risk factors
4. **Interactive Charts** — Price history visualization using Chart.js with volume overlay
5. **My Watchlist** — Save and manage favorite stocks with persistent JSON storage

## Architecture

```
Browser → Blazor Server → API Controllers → Services → Twf.Flow Workflows → LLM
                         → Yahoo Finance (stock data)
                         → JSON file (watchlist persistence)
```

## Prerequisites

- .NET 10.0 SDK
- OpenAI API key (for AI analysis)

## Setup

1. Create `appsettings.local.json` in the project root:

```json
{
  "OpenAI": {
    "ApiKey": "your-actual-openai-api-key"
  }
}
```

2. Run the application:

```bash
dotnet run
```

3. Open `https://localhost:7200` in your browser.

## How It Works

1. **Enter a stock symbol** (e.g., AAPL, MSFT, TSLA) in the search field
2. **Click Analyze** — the system fetches real-time data from Yahoo Finance
3. **Three AI stages run sequentially**:
   - Trend Analysis — identifies price direction and indicators
   - Recommendation — generates investment advice with confidence
   - Risk Assessment — identifies risk factors and severity
4. **View results** — trend card, recommendation card, risk card, and interactive chart
5. **Add to Watchlist** — save the stock for tracking

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | ASP.NET Core 10 + Blazor Server |
| Workflow | Twf.Flow 1.0.0 |
| Stock Data | Yahoo Finance v8 API (free) |
| Charts | Chart.js 4.x via JSInterop |
| AI | OpenAI Chat Completions API |
| Persistence | JSON file-based watchlist |

## Project Structure

```
StockMarketAnalyzer/
├── Controllers/          # API endpoints (StockController, WatchlistController)
├── Services/             # Business logic
│   ├── LlmService.cs           # OpenAI API wrapper
│   ├── YahooFinanceService.cs   # Yahoo Finance data fetching
│   ├── AnalysisWorkflowService.cs  # Twf.Flow analysis pipeline
│   └── WatchlistService.cs      # JSON file watchlist persistence
├── Models/               # Data models
├── Components/           # Blazor UI
│   ├── StockAnalyzerWidget.razor  # Main analysis UI
│   ├── StockChart.razor           # Chart.js wrapper
│   └── WatchlistWidget.razor      # Watchlist management
└── Constants.cs          # AI prompts and messages
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/Stock/analyze` | SSE: Full AI analysis with streaming progress |
| GET | `/api/Stock/quote/{symbol}` | Quick quote (no AI) |
| GET | `/api/Stock/history/{symbol}` | Historical price data |
| GET | `/api/Watchlist` | Get all watchlist items |
| POST | `/api/Watchlist` | Add symbol to watchlist |
| DELETE | `/api/Watchlist/{symbol}` | Remove symbol from watchlist |
| POST | `/api/Watchlist/refresh` | Refresh all watchlist prices |

## Twf.Flow Workflow

The analysis pipeline uses a `Twf.Flow` workflow with 6 steps:

```
1. ValidateInput (FilterNode)  → ensure symbol is non-empty
2. FetchData (AddStep)         → call Yahoo Finance for quote + history
3. AnalyzeTrend (AddStep)      → LLM trend analysis
4. GenerateRecommendation      → LLM buy/sell/hold with reasoning
5. AssessRisk (AddStep)        → LLM risk identification
6. EmitResult (AddStep)        → assemble and deliver via SSE
```
