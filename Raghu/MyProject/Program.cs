using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using DotNetEnv;
using XtsApiClient;
// using MarketSocket;
   
class Program
{
    static async Task Main(string[] args)
    {        
        Env.Load();     // Load the file
        string API_key = Environment.GetEnvironmentVariable("API_KEY");
        string API_secret = Environment.GetEnvironmentVariable("API_SECRET");
        string API_source = Environment.GetEnvironmentVariable("API_SOURCE");
        string API_root = Environment.GetEnvironmentVariable("API_URL");

        var xt = new XtsConnect(
            API_key,
            API_secret,
            API_source,
            API_root
        );

        try
        {
            // Login
            await xt.MarketdataLoginAsync();

            List<string> top_5_nifty = new List<string> {"HDFCBANK", "RELIANCE", "BHARTIARTL", "ICICIBANK", "INFY"};
            for (int i = 0; i < 5; i++)
            {
                var current_company = await xt.SearchByScriptnameAsync(
                    searchString: top_5_nifty[i]
                );
                
                var response_of_current_company = (System.Text.Json.JsonElement)current_company["result"]!;
                var eqIds = response_of_current_company.EnumerateArray()
                            .Where(item => item.GetProperty("Series").GetString() == "EQ"   // Equity
                                   && item.GetProperty("ExchangeSegment").GetInt32() == 1   // NSECM = 1
                                   && item.GetProperty("Name").GetString() == top_5_nifty[i])
                            .Select(item => item.GetProperty("ExchangeInstrumentID").GetInt64())
                            .ToList();

                var response_1 = await xt.GetOhlcAsync(
                    exchangeSegment: 1,  // For NSECM
                    exchangeInstrumentID: eqIds[0],
                    startTime: "Mar 27 2025 090000",
                    endTime: "Mar 27 2025 130000",
                    compressionValue: 14400
                );

                var response_2 = (System.Text.Json.JsonElement)response_1["result"]!;
                string ohlc_data = response_2
                                    .GetProperty("dataReponse")
                                    .GetString();
                string[] parts = ohlc_data.Split('|');
                string open = parts[1];
                string high = parts[2];
                string low = parts[3];
                string close = parts[4];

                Console.WriteLine($"OHLC Data for : {top_5_nifty[i]} \nOpen: {open}, High: {high}, Low: {low}, Close: {close}\n");
            }

            List<string> future_options_symbol = new List<string> {"HDFCBANK", "NIFTY"};
            List<string> future_options_series = new List<string> {"FUTSTK", "FUTIDX"};
            for(int i=0; i<2; i++)
            {
                var expiry_dates = await xt.GetExpiryDateAsync(
                    exchangeSegment: 2,      // NSEFO
                    series: future_options_series[i], 
                    symbol: future_options_symbol[i]
                );

                var resultElement = (System.Text.Json.JsonElement)expiry_dates["result"]!;
                var nearest_date_future = resultElement.EnumerateArray()
                                            .Select(d => DateTime.Parse(d.GetString()!))
                                            .Where(d => d.Date > DateTime.Today)
                                            .Min().ToString("ddMMMyyyy");
                
                var response_1 = await xt.GetFutureSymbolAsync(
                    exchangeSegment: 2, 
                    series: future_options_series[i], 
                    symbol: future_options_symbol[i], 
                    expiryDate: nearest_date_future
                );
                
                var response_2 = (System.Text.Json.JsonElement)response_1["result"]!;
                long instrumentId = response_2[0]
                                    .GetProperty("ExchangeInstrumentID")
                                    .GetInt64();

                var response_3 = await xt.GetOhlcAsync(
                    exchangeSegment: 2,  // For NSEFO
                    exchangeInstrumentID: instrumentId,
                    startTime: "Mar 1 2026 000000",
                    endTime: "Mar 30 2026 200000",
                    compressionValue: 60
                );

                var response_4 = (System.Text.Json.JsonElement)response_3["result"]!;
                string ohlc_data = response_4
                                    .GetProperty("dataReponse")
                                    .GetString();
                var csv_data = ohlc_data.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(r => {
                                        var parts = r.Split('|');
                                        if (long.TryParse(parts[0], out long unixTime))
                                            parts[0] = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                                        
                                        return string.Join(",", parts).TrimEnd(',');
                                    }).Prepend("Timestamp,          Open, High, Low, Close, Volume, Open Interest");

                string fileName = $"F&O_price_{future_options_symbol[i]}.csv";
                System.IO.File.WriteAllLines(fileName, csv_data);
                Console.WriteLine($"F&O Price Data saved in {fileName}");
            }

            Console.WriteLine("Start");
            // var handler = new MarketDataSocketClient();
            // var obj = new XTSMarketDataSocketClient(xt.Token, xt.UserID, API_root, handler);
            // await obj.Connect();

            // Console.WriteLine("Socket connected. Press Enter to disconnect and logout...");
            // Console.ReadLine();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
        finally
        {
            try
            {
                await xt.MarketdataLogoutAsync();
                Console.WriteLine("Logged out successfully.");
            }
            catch (Exception logoutEx)
            {
                Console.WriteLine("Logout error: " + logoutEx.Message);
            }
        }          
    }
}