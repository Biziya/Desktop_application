using Backend.Repositories.CoreInformation.BluetoothDevice;
using Backend.Repositories.CoreInformation.Drillhole;
using Backend.Repositories.CoreInformation.SqliteDatabase;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

// Core Information page
builder.Services.AddScoped<IDrillholeRepository, DrillholeRepository>();
builder.Services.AddScoped<BluetoothDeviceRepository>();
builder.Services.AddSingleton<SqliteDatabaseRepository>(); // Singleton keeps the discovery cache.

var app = builder.Build();

app.UseSwagger(); // Serves /swagger/v1/swagger.json
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Backend v1"));

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
    {
        app.Logger.LogInformation("Swagger UI: {SwaggerUrl}", $"{url.TrimEnd('/')}/swagger");
    }
});

app.Run();
