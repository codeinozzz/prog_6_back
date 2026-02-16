using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.Hubs;
using BattleTanks_Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BattleTanks API",
        Version = "v1",
        Description = "REST API for BattleTanks multiplayer game with JWT authentication"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token"
    });

    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSignalR();

builder.Services.AddDbContext<BattleTanksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis — opcional: si no está disponible el juego funciona igual
IConnectionMultiplexer? redisConnection = null;
try
{
    var connStr = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    redisConnection = ConnectionMultiplexer.Connect(connStr);
}
catch (Exception ex)
{
    Console.WriteLine($"[Redis] Not available: {ex.Message}");
}
#pragma warning disable CS8634
builder.Services.AddSingleton<IConnectionMultiplexer?>(_ => redisConnection);
#pragma warning restore CS8634
builder.Services.AddSingleton<IRedisHistoryService, RedisHistoryService>();

// MQTT — registrado como singleton Y como hosted service
builder.Services.AddSingleton<MqttGameService>();
builder.Services.AddSingleton<IMqttGameService>(sp => sp.GetRequiredService<MqttGameService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttGameService>());

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();
