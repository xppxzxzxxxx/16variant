using System.Security.Cryptography;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using DrivingSchool.Core;
using DrivingSchool.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
#if NET8_0
using Microsoft.OpenApi.Models;
#else
using Microsoft.OpenApi;
#endif
using Serilog;

namespace DrivingSchool.Api;

public static class SchoolHost
{
    public static WebApplication Build(string[] args, X509Certificate2? desktopCertificate = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(SchoolHost).Assembly.GetName().Name
        });

        if (desktopCertificate is not null)
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(desktopCertificate)));
        else
        {
            string urls = builder.Configuration["urls"] ?? "https://localhost:7130";
            if (urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(url => !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Для API указывайте только HTTPS-адреса.");
            builder.WebHost.UseUrls(urls);
        }

        int loginLimit = builder.Configuration.GetValue("Security:LoginAttemptsPerMinute", 5);
        if (loginLimit is < 1 or > 1000)
            throw new InvalidOperationException("Security:LoginAttemptsPerMinute должен быть от 1 до 1000.");
        string? configuredKey = builder.Configuration["Jwt:Key"];
        if (configuredKey is not null && Encoding.UTF8.GetByteCount(configuredKey) < 32)
            throw new InvalidOperationException("Jwt:Key должен содержать не менее 32 байтов.");

        var path = DatabasePath(builder.Configuration);
        builder.Services.AddDbContext<SchoolDb>(options => SchoolDatabase.Configure(options, path));
        builder.Services.AddScoped<SchoolStore>();
        builder.Services.AddScoped<ILessonRepository>(services => services.GetRequiredService<SchoolStore>());
        builder.Services.AddScoped<IStudentRepository>(services => services.GetRequiredService<SchoolStore>());
        builder.Services.AddScoped<IUserRepository>(services => services.GetRequiredService<SchoolStore>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            const string bearer = "Bearer";
            options.AddSecurityDefinition(bearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
            });
#if NET8_0
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = bearer } }] = []
            });
#else
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(bearer, document)] = []
            });
#endif
        });
        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            foreach (var converter in SchoolFiles.JsonOptions.Converters)
                options.JsonSerializerOptions.Converters.Add(converter);
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            foreach (var converter in SchoolFiles.JsonOptions.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });

        var key = new SymmetricSecurityKey(configuredKey is null
            ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(configuredKey));
        builder.Services.AddSingleton(new SessionTokens(key));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = SessionTokens.Issuer,
                ValidAudience = SessionTokens.Audience,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "login",
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var id = context.Principal?.FindFirst("jti")?.Value;
                    var revoked = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    if (id is null || revoked.TryGetValue(id, out _))
                        context.Fail("Сеанс завершён.");
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Staff", policy =>
            policy.RequireRole(Roles.Admin, Roles.Manager));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginLimit,
                    Window = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true
                }));
        });

        builder.Services.AddSerilog((_, logger) => logger.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(Path.GetDirectoryName(path)!, "logs", "api-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true));

        var app = builder.Build();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(handler => handler.Run(HandleError));
        app.UseStatusCodePages();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapSchoolEndpoints();
        if (app.Environment.IsDevelopment())
            app.MapGet("/api/debug/requests", (ClaimsPrincipal user, ILessonRepository lessons, CancellationToken ct) =>
                lessons.FindLessons(SessionTokens.User(user), null, null, ct))
                .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        return app;
    }

    public static async Task Initialize(WebApplication app, bool embedded = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await SchoolDatabase.Initialize(scope.ServiceProvider.GetRequiredService<SchoolDb>(),
            DatabasePath(app.Configuration), app.Configuration.GetValue<bool?>("School:SeedDemo") ??
                (embedded && app.Configuration["School:Database"] is null));
    }

    private static string DatabasePath(IConfiguration config) =>
        Path.GetFullPath(config["School:Database"] ?? SchoolDatabase.DefaultPath);

    private static Task HandleError(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        return (error switch
        {
            CsvImportException csv => Results.Problem(statusCode: 400, title: "Ошибка импорта CSV",
                detail: csv.Message, extensions: new Dictionary<string, object?> { ["errors"] = csv.Errors }),
            RuleException => Results.Problem(statusCode: 400, title: "Проверьте данные", detail: error.Message),
            BadHttpRequestException bad => Results.Problem(statusCode: bad.StatusCode, title: "Некорректный запрос"),
            LessonConflictException or DbUpdateConcurrencyException => Results.Problem(statusCode: 409, title: "Занятие изменилось",
                detail: "Обновите список, откройте занятие и повторите действие."),
            DbUpdateException => Results.Problem(statusCode: 500, title: "Ошибка сохранения данных"),
            _ => Results.Problem(statusCode: 500, title: "Внутренняя ошибка автошколы")
        }).ExecuteAsync(context);
    }
}
