using System.Security.Claims;
using Backend.Controllers.CoreInformation.Drillhole;
using Backend.Repositories.CoreInformation.BluetoothDevice;
using Backend.Repositories.CoreInformation.Drillhole;
using Backend.Repositories.CoreInformation.SqliteDatabase;
using Backend.Repositories.Measurement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BackendDrillhole = Backend.Models.CoreInformation.Drillhole.DrillholeResponse;

namespace Ct007.Tests.Fixtures;

// The backend pipeline hosted in-process, mirroring Backend/Program.cs (not reusable: top-level statements, app.Run()).
// The content root is an empty temp folder, so Backend/appsettings.json and its connection string are never loaded.
public sealed class TestBackend : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly string contentRoot;

    private TestBackend(WebApplication app, string contentRoot, string baseUrl, LogSink logs)
    {
        this.app = app;
        this.contentRoot = contentRoot;
        BaseUrl = baseUrl;
        Logs = logs;
    }

    public string BaseUrl { get; }

    public LogSink Logs { get; }

    // Null repositories register the real ones (needing a connection string in settings). port 0 picks a free port.
    // authenticatedUser signs every request in, as Windows authentication in the hosting would.
    public static async Task<TestBackend> StartAsync(IDrillholeRepository? repository = null,
        IDictionary<string, string?>? settings = null, int port = 0, string environment = "Production",
        IMeasurementUploadRepository? uploads = null, string? authenticatedUser = null,
        IUploadedMeasurementRepository? uploaded = null)
    {
        var contentRoot = Directory.CreateTempSubdirectory("ct007-backend-").FullName;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = contentRoot,
            Args = []
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        var logs = new LogSink();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);

        // As Backend/Program.cs.
        builder.Services.AddControllers().AddApplicationPart(typeof(DrillholeController).Assembly);
        builder.Services.AddSwaggerGen();

        if (repository is null)
        {
            builder.Services.AddScoped<IDrillholeRepository, DrillholeRepository>();
        }
        else
        {
            builder.Services.AddSingleton(repository);
        }

        builder.Services.AddScoped<BluetoothDeviceRepository>();
        builder.Services.AddSingleton<SqliteDatabaseRepository>();

        if (uploads is null)
        {
            builder.Services.AddScoped<IMeasurementUploadRepository, MeasurementUploadRepository>();
        }
        else
        {
            builder.Services.AddSingleton(uploads);
        }

        if (uploaded is null)
        {
            builder.Services.AddScoped<IUploadedMeasurementRepository, UploadedMeasurementRepository>();
        }
        else
        {
            builder.Services.AddSingleton(uploaded);
        }

        var app = builder.Build();
        if (authenticatedUser is not null)
        {
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, authenticatedUser)], "Test"));
                return next();
            });
        }

        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Backend v1"));
        app.MapControllers();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestBackend(app, contentRoot, address.TrimEnd('/'), logs);
    }

    // Nothing listens on port 1, so opening fails with a real SqlException. The credentials are fake markers that
    // tests check never leak into a response.
    public const string RefusedSqlConnection =
        "Server=tcp:127.0.0.1,1;Database=CT007_Test;User ID=ct007_test_user;Password=NotARealPassword-1;Connect Timeout=1;ConnectRetryCount=0;Encrypt=False";

    /// <summary>A port nothing listens on right now (for "backend down" and later start on the same port).</summary>
    public static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();

        try
        {
            Directory.Delete(contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Fake drillhole source for the controller: fixed rows, an exception, or a wait that observes cancellation.</summary>
public sealed class FakeDrillholeRepository : IDrillholeRepository
{
    public IReadOnlyList<BackendDrillhole> Rows { get; set; } = [];

    public Exception? Throw { get; set; }

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Completes when a request's cancellation token fired while the repository was waiting.</summary>
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Calls;

    public async Task<IReadOnlyList<BackendDrillhole>> GetAllAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Cancelled.TrySetResult();
            throw;
        }

        return Throw is { } error ? throw error : Rows;
    }
}

public sealed class LogSink : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Category, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries { get { lock (entries) return entries.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink.entries) sink.entries.Add((logLevel, category, formatter(state, exception)));
        }
    }
}
