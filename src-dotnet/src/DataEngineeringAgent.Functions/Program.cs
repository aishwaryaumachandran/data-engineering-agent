using Azure.Identity;
using Azure.Storage.Files.DataLake;
using Azure.AI.OpenAI;
using DataEngineeringAgent.Core.Configuration;
using DataEngineeringAgent.Core.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        var isLocal = string.Equals(config["RunMode"], "Local", StringComparison.OrdinalIgnoreCase);

        // Bind configuration sections
        services.AddOptions<OpenAiOptions>()
            .Bind(config.GetSection(OpenAiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (isLocal)
        {
            // Local mode: bind LocalOptions, skip cloud SDK config validation
            services.AddOptions<LocalOptions>()
                .Bind(config.GetSection(LocalOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Still need these bound for DI (placeholder values), but skip validation
            services.AddOptions<AdlsOptions>()
                .Bind(config.GetSection(AdlsOptions.SectionName));
            services.AddOptions<CosmosOptions>()
                .Bind(config.GetSection(CosmosOptions.SectionName));
            services.AddOptions<DatabricksOptions>()
                .Bind(config.GetSection(DatabricksOptions.SectionName));
        }
        else
        {
            services.AddOptions<AdlsOptions>()
                .Bind(config.GetSection(AdlsOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<CosmosOptions>()
                .Bind(config.GetSection(CosmosOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<DatabricksOptions>()
                .Bind(config.GetSection(DatabricksOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        // Azure credential (singleton) — needed for OpenAI in both modes
        var credential = new DefaultAzureCredential();
        services.AddSingleton(credential);

        // Azure OpenAI client — used in both modes
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            return new AzureOpenAIClient(new Uri(opts.Endpoint), credential);
        });

        if (isLocal)
        {
            // Local services
            services.AddSingleton<IAdlsService, LocalStorageService>();
            services.AddSingleton<ICosmosService, LocalCosmosService>();
            services.AddSingleton<IDatabricksService, LocalSparkService>();
        }
        else
        {
            // Cloud SDK clients
            services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<AdlsOptions>>().Value;
                return new DataLakeServiceClient(
                    new Uri($"https://{opts.AccountName}.dfs.core.windows.net"),
                    credential);
            });

            services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
                return new CosmosClient(opts.Endpoint, credential, new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                    },
                });
            });

            // HttpClient with Polly retry for Databricks
            services.AddHttpClient("Databricks")
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

            services.AddSingleton<IAdlsService, AdlsService>();
            services.AddSingleton<IDatabricksService, DatabricksService>();

            services.AddSingleton<ICosmosService>(sp =>
            {
                var cosmosClient = sp.GetRequiredService<CosmosClient>();
                var opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CosmosService>>();
                return new CosmosService(cosmosClient, opts.DatabaseName, logger);
            });
        }

        // Shared services (same in both modes)
        services.AddSingleton<IProfilingService, ProfilingService>();
        services.AddSingleton<IApprovedCodeService, ApprovedCodeService>();
        services.AddSingleton<IOpenAiService, OpenAiService>();
        services.AddSingleton<IIntegrityService, IntegrityService>();

        // Application Insights (only when connection string is configured)
        var aiConnectionString = config["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrEmpty(aiConnectionString))
        {
            services.AddApplicationInsightsTelemetryWorkerService();
        }
    })
    .Build();

host.Run();
