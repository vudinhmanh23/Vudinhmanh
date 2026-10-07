using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Ai;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;
using SalesInventory.Infrastructure.Storage;

namespace SalesInventory.Infrastructure;

public static class DependencyInjection
{
    // Registers persistence (EF Core), Identity, JWT token generation and repositories
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Identity: users, roles and password policy
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
        })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // JWT: options bound from configuration ("Jwt:Key" comes from user-secrets, never committed)
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddScoped<ITokenService, TokenService>();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // LLM assistant: options from the "Anthropic" section; the key may also come from the ANTHROPIC_API_KEY environment variable
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.PostConfigure<AnthropicOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.ApiKey))
            {
                o.ApiKey = configuration["ANTHROPIC_API_KEY"];
            }
        });
        services.AddHttpClient<IChatService, AnthropicChatService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // RAG: embeddings provider (Voyage by default; the key may come from VOYAGE_API_KEY), knowledge retrieval and ingestion
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.PostConfigure<EmbeddingOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.ApiKey))
            {
                o.ApiKey = configuration["VOYAGE_API_KEY"];
            }
        });
        services.Configure<KnowledgeOptions>(configuration.GetSection(KnowledgeOptions.SectionName));
        services.AddHttpClient<IEmbeddingService, HttpEmbeddingService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        // The limits on cost and use: bound from "AiSafety" and checked at startup, so a bad number stops the app
        services.AddOptions<AiSafetyOptions>().Bind(configuration.GetSection(AiSafetyOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiSafetyOptions>, AiSafetyOptionsValidator>();
        services.AddSingleton<PromptGuard>();
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IKnowledgeRetriever, RagRetriever>();
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

        // Product images from the internet: SSRF-safe downloader plus the Open Food Facts barcode lookup
        services.AddSingleton<IRemoteImageDownloader, SafeImageDownloader>();
        services.AddHttpClient<IProductImageLookup, OpenFoodFactsImageLookup>(client =>
        {
            client.BaseAddress = new Uri("https://world.openfoodfacts.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
            // Open Food Facts asks API clients to identify themselves
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SalesInventory/1.0");
        });

        return services;
    }
}
