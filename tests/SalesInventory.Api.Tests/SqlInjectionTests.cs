using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

using Xunit.Abstractions;

namespace SalesInventory.Api.Tests;

// SQL injection: attack strings such as   '; DROP TABLE Products; --   are sent as input to code that queries the database through EF
// Core. The tests do not just check that nothing broke; they look at the exact SQL that reaches SQL Server and show that the attack
// string is there only as a parameter VALUE, never as SQL text. A control test then shows that the same string DOES drop a table
// when the SQL is built by joining strings, so the attack string is a real one and the safety comes from how the query is made.
//
// The "raw input" endpoints (/test/sql/...) exist only inside this test host. They are written to accept a client string and query
// with it, once the safe way and once the deliberately unsafe way (on a scratch table), and they are not part of the API.
public class SqlInjectionTests : IClassFixture<SqlInjectionTests.SqlInjectionFactory>
{
    private readonly SqlInjectionFactory _factory;
    private readonly ITestOutputHelper _output;

    public SqlInjectionTests(SqlInjectionFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    // Not a check but a window: prints what SQL Server actually receives for the same attack string written four ways.
    // Run it with:  dotnet test --filter ShowTheSql --logger "console;verbosity=detailed"
    [Fact]
    public async Task ShowTheSql_WhatSqlServerReceivesForTheSameAttackString()
    {
        // Arrange
        const string attack = "'; DROP TABLE Products; --";
        Scratch("IF OBJECT_ID(N'dbo.ScratchItems') IS NULL CREATE TABLE ScratchItems (Id int IDENTITY PRIMARY KEY, Name nvarchar(200) NOT NULL);");
        var client = _factory.CreateClient();

        // Act + Assert (each call is shown; the control one only touches the scratch table)
        foreach (var (action, label) in new[]
                 {
                     ("by-name", "EF Core LINQ  Where(p => p.Name == input)"),
                     ("search", "EF Core LINQ  Where(p => p.Name.Contains(input))"),
                     ("by-name-raw", "FromSql($\"... WHERE Name = {input}\")  (interpolated, parameterized)"),
                     ("scratch-delete-safe", "ExecuteSql($\"DELETE ... WHERE Name = {input}\")  (parameterized)")
                 })
        {
            var answer = await Call(client, action, attack);
            _output.WriteLine($"--- {label}");
            foreach (var command in answer.Commands)
            {
                _output.WriteLine("SQL text : " + Compact(command.Text));
                foreach (var parameter in command.Parameters)
                {
                    _output.WriteLine($"parameter: {parameter.Name} = {parameter.Value}");
                }
            }
        }

        var unsafeAnswer = await Call(client, "scratch-delete-unsafe", "x'; DROP TABLE ScratchItems; --");
        _output.WriteLine("--- JOINING STRINGS (never do this):  \"DELETE ... WHERE Name = '\" + input + \"'\"");
        _output.WriteLine("SQL text : " + Compact(Assert.Single(unsafeAnswer.Commands).Text));
        _output.WriteLine("parameters: none, so the attack string is part of the SQL");
        Assert.Equal(0, TableExists("ScratchItems"));
    }

    private static string Compact(string sql) => System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ").Trim();

    public static IEnumerable<object[]> Payloads()
    {
        yield return new object[] { "'; DROP TABLE Products; --" };
        yield return new object[] { "Robert'); DROP TABLE Products;--" };
        yield return new object[] { "' OR '1'='1" };                                  // "log in as anyone" / "return every row"
        yield return new object[] { "' OR 1=1 --" };
        yield return new object[] { "'; DELETE FROM Products; --" };
        yield return new object[] { "' UNION SELECT name, NULL, NULL FROM sys.tables --" };
        yield return new object[] { "'; EXEC xp_cmdshell('dir'); --" };
        yield return new object[] { "'; WAITFOR DELAY '0:0:8'; --" };                  // time-based: would make the answer 8 seconds late
        yield return new object[] { "\\'; DROP TABLE Products; --" };
        yield return new object[] { "ʼ; DROP TABLE Products; --" };                    // a look-alike apostrophe (U+02BC)
        yield return new object[] { "%' OR 1=1 --" };
    }

    // ---- 1. a query written with EF Core LINQ ----

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task LinqEquality_AttackString_TravelsAsAParameterValueAndNeverBecomesSql(string payload)
    {
        // Arrange: how many products there are, and a table that must survive
        var productsBefore = ProductCount();
        var client = _factory.CreateClient();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var answer = await Call(client, "by-name", payload);
        stopwatch.Stop();

        // Assert: the query ran once, as ONE statement with the payload in a parameter
        var command = Assert.Single(answer.Commands);
        Assert.Contains("FROM [Products]", command.Text);
        Assert.Contains("WHERE [p].[Name] = @", command.Text);
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(payload, parameter.Value);                // the attack string, unchanged, as DATA
        AssertNoAttackInSql(command.Text);                      // and none of it in the SQL text

        // "' OR '1'='1" matches no product name: it is compared as a name, it does not change the logic
        Assert.Empty(answer.Rows);

        // Nothing happened to the database, and a WAITFOR was not executed
        Assert.Equal(1, TableExists("Products"));
        Assert.Equal(productsBefore, ProductCount());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"the answer took {stopwatch.Elapsed.TotalSeconds:0.0} s: a WAITFOR may have run");
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task LinqContains_AttackString_IsSearchedAsTextAndFindsNothing(string payload)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var answer = await Call(client, "search", payload);

        // Assert
        var command = Assert.Single(answer.Commands);
        Assert.Contains("LIKE", command.Text);
        Assert.Contains("ESCAPE", command.Text);
        // EF wraps the term in % ... % for the LIKE and escapes the characters that mean something to LIKE (\ % _ [), so the value
        // is the payload as TEXT, with its own wildcards switched off
        var escaped = payload.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        Assert.Equal($"%{escaped}%", Assert.Single(command.Parameters).Value);
        AssertNoAttackInSql(command.Text);
        Assert.Empty(answer.Rows);
        Assert.Equal(1, TableExists("Products"));
    }

    [Fact]
    public async Task LinqContains_PercentAndUnderscoreInTheSearch_AreLiteralCharactersNotWildcards()
    {
        // Arrange: one product that really contains a percent sign, one that does not
        Add("100% Cotton Shirt");
        Add("Plain Shirt");
        var client = _factory.CreateClient();

        // Act
        var percent = await Call(client, "search", "%");
        var wildcardInjection = await Call(client, "search", "%' OR 1=1 --");

        // Assert: "%" finds only the product with a percent sign (it would find both if it were a LIKE wildcard)
        try
        {
            Assert.Equal(new[] { "100% Cotton Shirt" }, percent.Rows);
            Assert.Empty(wildcardInjection.Rows);
        }
        finally
        {
            RemoveByName("100% Cotton Shirt", "Plain Shirt");
        }
    }

    // ---- 2. raw SQL written the right way: FromSql with an interpolated string is parameterized ----

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task FromSqlInterpolated_AttackString_IsAlsoSentAsAParameter(string payload)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var answer = await Call(client, "by-name-raw", payload);

        // Assert: the {name} in the C# string became @p0 in the SQL, and the payload is the value of @p0
        var command = Assert.Single(answer.Commands);
        Assert.Contains("WHERE Name = @p0", command.Text);
        Assert.Equal(payload, Assert.Single(command.Parameters).Value);
        AssertNoAttackInSql(command.Text);
        Assert.Empty(answer.Rows);
        Assert.Equal(1, TableExists("Products"));
    }

    // ---- 3. the control: the same attack string against SQL made by joining strings ----

    [Fact]
    public async Task Concatenation_TheSameAttackString_ReallyDropsATable_WhichIsWhatTheSafeWayPrevents()
    {
        // Arrange: a scratch table (not a real one) with a couple of rows
        Scratch("CREATE TABLE ScratchItems (Id int IDENTITY PRIMARY KEY, Name nvarchar(200) NOT NULL); INSERT INTO ScratchItems (Name) VALUES (N'a'), (N'b');");
        var client = _factory.CreateClient();
        const string attack = "x'; DROP TABLE ScratchItems; --";

        // Act 1: the safe way, with the attack string
        var safe = await Call(client, "scratch-delete-safe", attack);

        // Assert 1: the table and its rows are untouched, and the statement had the text as a parameter
        Assert.Equal(1, TableExists("ScratchItems"));
        Assert.Equal(2, ScratchCount());
        Assert.Equal(attack, Assert.Single(Assert.Single(safe.Commands).Parameters).Value);

        // Act 2: the unsafe way (the SQL is built with "..." + name + "..."), with the very same string
        var unsafeAnswer = await Call(client, "scratch-delete-unsafe", attack);

        // Assert 2: the string was glued into the SQL and executed: the table is gone. This is the damage the safe way prevented.
        var command = Assert.Single(unsafeAnswer.Commands);
        Assert.Contains("DROP TABLE ScratchItems", command.Text);
        Assert.Empty(command.Parameters);
        Assert.Equal(0, TableExists("ScratchItems"));
    }

    // ---- 4. the real API endpoints that take a client string ----

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task RealEndpoints_AttackStringInSearchAndSkuLookup_ReturnNothingAndLeaveTheDatabaseIntact(string payload)
    {
        // Arrange
        var client = await AdminAsync();
        var productsBefore = ProductCount();
        var encoded = Uri.EscapeDataString(payload);

        // Act
        var search = await client.GetAsync($"/api/products/search?search={encoded}");
        var list = await client.GetAsync($"/api/products?search={encoded}");
        var bySku = await client.GetAsync($"/api/products/by-sku/{encoded}");

        // Assert: normal answers (found nothing), never an error from the database, and the table is where it was
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bySku.StatusCode);
        Assert.Equal(0, (await search.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, TableExists("Products"));
        Assert.Equal(productsBefore, ProductCount());
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task RealEndpoints_AttackStringAsAProductName_IsStoredAndReadBackAsPlainText(string payload)
    {
        // Arrange: a valid product whose NAME is the attack string (a name may contain any text; the SKU has a strict format)
        var client = await AdminAsync();
        var sku = $"INJ-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var body = new { name = payload, sku, unit = "cái", purchasePrice = 1m, salePrice = 2m, quantity = 1, categoryId = 1, supplierId = 1 };

        // Act
        var created = await client.PostAsJsonAsync("/api/products", body);

        // Assert: saved with the INSERT parameterized: the text is stored as it is, and nothing was executed
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<JsonElement>());
        var id = product.GetProperty("id").GetInt32();
        try
        {
            Assert.Equal(payload, product.GetProperty("name").GetString());
            var readBack = await client.GetFromJsonAsync<JsonElement>($"/api/products/{id}");
            Assert.Equal(payload, readBack.GetProperty("name").GetString());
            Assert.Equal(1, TableExists("Products"));
        }
        finally
        {
            Remove(id); // a product named exactly like the attack string would be found, correctly, by the lookups of the other tests
        }
    }

    [Theory]
    [InlineData("'; DROP TABLE Products; --")]
    [InlineData("ABC'; DELETE FROM Products; --")]
    public async Task RealEndpoints_AttackStringAsASku_IsRefusedByTheFormatRuleBeforeItReachesTheDatabase(string badSku)
    {
        // Arrange: the SKU rule only allows capital letters, digits and hyphens (3 to 32 characters)
        var client = await AdminAsync();
        var body = new { name = "Not saved", sku = badSku, unit = "cái", purchasePrice = 1m, salePrice = 2m, quantity = 1, categoryId = 1, supplierId = 1 };

        // Act
        var response = await client.PostAsJsonAsync("/api/products", body);

        // Assert: a 400 with a message about the SKU: a second layer of defence on top of the parameters
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Sku", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, TableExists("Products"));
    }

    // ---- helpers ----

    // None of the dangerous fragments of the payloads may be in the text of a statement the application sent
    private static void AssertNoAttackInSql(string sql)
    {
        foreach (var fragment in new[] { "DROP", "DELETE", "WAITFOR", "UNION", "xp_cmdshell", "sys.tables", "OR 1=1", "'1'='1'", "--" })
        {
            Assert.DoesNotContain(fragment, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<RawAnswer> Call(HttpClient client, string action, string input)
    {
        var response = await client.GetAsync($"/test/sql/{action}?input={Uri.EscapeDataString(input)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RawAnswer>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private int ProductCount()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Count();
    }

    private int TableExists(string table)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Database.SqlQueryRaw<int>($"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}') IS NULL THEN 0 ELSE 1 END AS [Value]").AsEnumerable().Single();
    }

    private int ScratchCount()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ScratchItems").AsEnumerable().Single();
    }

    private void Scratch(string sql)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRaw(sql);
    }

    private void Add(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Add(new Product { Name = name, Sku = $"S-{Guid.NewGuid():N}"[..16], Price = 1, StockQuantity = 1, CategoryId = 1, SupplierId = 1, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
    }

    private void Remove(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Where(p => p.Id == productId).ExecuteDelete();
    }

    private void RemoveByName(params string[] names)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Where(p => names.Contains(p.Name)).ExecuteDelete();
    }

    // What a test endpoint answers: the rows it found, and every statement it sent to the database with its parameters
    public sealed record RawAnswer(List<string> Rows, List<SentCommand> Commands);

    public sealed record SentCommand(string Text, List<SentParameter> Parameters);

    public sealed record SentParameter(string Name, string? Value);

    // Remembers the exact text and parameters of each command that goes to SQL Server
    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<SentCommand> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return new(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return new(result);
        }

        private void Record(DbCommand command)
        {
            var parameters = command.Parameters.Cast<DbParameter>().Select(p => new SentParameter(p.ParameterName, p.Value as string)).ToList();
            Commands.Add(new SentCommand(command.CommandText, parameters));
        }
    }

    // The standard test host plus the raw-input endpoints, which exist only here
    public sealed class SqlInjectionFactory : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, RawEndpointsFilter>());
        }
    }

    private sealed class RawEndpointsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseWhen(context => context.Request.Path.StartsWithSegments("/test/sql"), branch => branch.Run(Handle));
            next(app);
        };

        private static async Task Handle(HttpContext context)
        {
            var input = context.Request.Query["input"].ToString();
            var action = context.Request.Path.Value!.Split('/').Last();

            // A context of its own, with the recorder attached, on the same database as the application
            var recorder = new CommandRecorder();
            var connection = context.RequestServices.GetRequiredService<AppDbContext>().Database.GetConnectionString();
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(recorder).Options);

            List<string> rows = new();
            switch (action)
            {
                case "by-name":
                    // LINQ: EF turns the variable into a parameter
                    rows = await db.Products.Where(p => p.Name == input).Select(p => p.Name).ToListAsync();
                    break;

                case "search":
                    rows = await db.Products.Where(p => p.Name.Contains(input)).Select(p => p.Name).ToListAsync();
                    break;

                case "by-name-raw":
                    // Raw SQL, but with FromSql and an interpolated string: each {value} becomes a parameter
                    rows = await db.Products.FromSql($"SELECT * FROM Products WHERE Name = {input}").Select(p => p.Name).ToListAsync();
                    break;

                case "scratch-delete-safe":
                    await db.Database.ExecuteSqlAsync($"DELETE FROM ScratchItems WHERE Name = {input}");
                    break;

                case "scratch-delete-unsafe":
                    // DELIBERATELY UNSAFE, for the control test only: the client string is joined into the SQL text. Never write this.
#pragma warning disable EF1002
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM ScratchItems WHERE Name = '" + input + "'");
#pragma warning restore EF1002
                    break;

                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
            }

            await context.Response.WriteAsJsonAsync(new RawAnswer(rows, recorder.Commands), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }
}
