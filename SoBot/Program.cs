using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Events;
using SorceryBot.Features.Cards;
using SorceryBot.Infrastructure.Config;
using SorceryBot.Infrastructure.DataAccess.CardData;
using SorceryBot.Infrastructure.Logging;
using static SorceryBot.Features.Cards.Prices;

namespace SorceryBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddJsonFile("BotToken.Private.json");
            builder.Services.Configure<BotTokenSettings>(builder.Configuration.GetRequiredSection("BotTokenSettings"));
            builder.Services.Configure<TcgPlayerSettings>(builder.Configuration.GetRequiredSection("TcgPlayer"));

            builder.Services.AddSerilog((services, lc) => lc
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache();

            builder.Services.AddHostedService<BotStartupService>();
            builder.Services.AddSingleton<ICardRepository, FileCardRepository>();
            builder.Services.AddSingleton<TcgPlayerDataProvider>();

            var logLevel = (Log.Logger.IsEnabled(LogEventLevel.Debug)) ? LogSeverity.Debug : LogSeverity.Info;
            var clientConfig = new DiscordSocketConfig { MessageCacheSize = 5, LogLevel = logLevel, GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds };
            builder.Services.AddSingleton(clientConfig);
            builder.Services.AddSingleton<DiscordSocketClient>();
            builder.Services.AddSingleton(new InteractionServiceConfig() { LogLevel = LogSeverity.Info, AutoServiceScopes = true });
            builder.Services.AddSingleton<FaqRepoistory>();

            builder.Services.AddSingleton(services =>
            {
                var client = services.GetRequiredService<DiscordSocketClient>();
                var interactionServiceConfig = new InteractionServiceConfig() { UseCompiledLambda = true, LogLevel = LogSeverity.Info, AutoServiceScopes = true };
                return new InteractionService(client, interactionServiceConfig);
            });

            builder.Services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<Program>();
                cfg.AddOpenBehavior(typeof(QueryLoggingPipeline<,>));
            });

            var host = builder.Build();
            await PrecacheTcgPrices(host);

            host.MapDiscord();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "An unhandelded error occourred.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task PrecacheTcgPrices(IHost host)
    {
        Log.Information("Fetching TcgPlayer price data.");
        var tcgPlayerData = host.Services.GetRequiredService<TcgPlayerDataProvider>();
        var cards = await tcgPlayerData.GetTcgPlayerCards();
        Log.Information("Loaded {count} card prices from TcgPlayer.", cards.Count);

    }
}
