using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using XtsApiClient;

class Program
{
    static async Task Main(string[] args)
    {
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

            // Get OHLC
            var res = await xt.GetOhlcAsync(
                exchangeSegment: 1, // NSECM = 1 (int in C#)
                exchangeInstrumentID: 22,
                startTime: "Dec 02 2024 091500",
                endTime: "Dec 02 2024 133000",
                compressionValue: 60
            );

            // Print raw response
            Console.WriteLine("OHLC Data:");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(res, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));

            // Logout
            await xt.MarketdataLogoutAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
    }
}