using ChatApp.Data;
using ChatApp.Data.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database: SQLite by default, SQL Server when connection string provided ──
var sqlServerConn = builder.Configuration.GetConnectionString("SqlServer");
var sqliteConn    = builder.Configuration.GetConnectionString("Sqlite")
                    ?? "Data Source=chat.db";

if (!string.IsNullOrWhiteSpace(sqlServerConn))
{
    builder.Services.AddDbContext<ChatDbContext>(options =>
        options.UseSqlServer(sqlServerConn,
            sql => sql.EnableRetryOnFailure()));
}
else
{
    builder.Services.AddDbContext<ChatDbContext>(options =>
        options.UseSqlite(sqliteConn));
}

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();

// MVC Controllers + OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS — allow the WPF clients on the same machine / network
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// Auto-create/migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.MapControllers();

app.Run();
