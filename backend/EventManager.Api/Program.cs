using System.Text;
using System.Threading.RateLimiting;
using EventManager.Api.Auth;
using EventManager.Api.Events;
using EventManager.Api.Infrastructure;
using EventManager.Api.Persistence;
using EventManager.Api.Projections;
using EventManager.Api.Services;
using EventManager.Domain.Engines;
using EventManager.Payments;
using EventManager.Sync;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
var jwt = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwt);
if (string.IsNullOrEmpty(jwt.SigningKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:SigningKey must be provided via secrets in non-Development environments.");
    jwt.SigningKey = "dev-only-signing-key-change-me-please-32chars";
}
builder.Services.AddSingleton(jwt);

// ---- Persistence ----
var connString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=eventmanager;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

// ---- Identity (Q1=C: Identity plane) ----
builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.Password.RequiredLength = 8;                    // + breached-password validator (SP-5)
    o.SignIn.RequireConfirmedEmail = false;           // gate enforced explicitly at event create (BR-AUTH-4)
    o.Lockout.MaxFailedAccessAttempts = 5;            // BR-AUTH-2 / U3-NFR-S5
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Tokens.AuthenticatorTokenProvider = Microsoft.AspNetCore.Identity.TokenOptions.DefaultAuthenticatorProvider;
})
    .AddRoles<IdentityRole<long>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddPasswordValidator<BreachedPasswordValidator>();

// ---- Shared/domain singletons (U1/U2/U8) ----
builder.Services.AddSingleton<IEventSerializer>(new JsonEventSerializer());
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId: 0)); // cloud worker range
builder.Services.AddSingleton<IRoleAuthorizationPolicy, RoleAuthorizationPolicy>();
builder.Services.AddSingleton<IPaymentProvider>(new StubPaymentProvider());

// ---- U3 scoped components (share the request DbContext) ----
builder.Services.AddScoped<IEventStore, PostgresEventStore>();
builder.Services.AddScoped<CloudProjectionHost>();
builder.Services.AddScoped<EventWriter>();
builder.Services.AddScoped<IdempotencyStore>();
builder.Services.AddScoped<RefreshTokenStore>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<EventAuthorizer>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IEmailSender, OutboxEmailSender>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountDeletionGuard>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<OrganizerRoleService>();
builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<ResultsQueryService>();

// ---- U9 read/query API (API-local read authorization, U9-CON-1) ----
builder.Services.AddScoped<ReadAuthorizer>();
builder.Services.AddScoped<ReadEtagProvider>();
builder.Services.AddScoped<EventQueryService>();
builder.Services.AddScoped<DivisionQueryService>();
builder.Services.AddScoped<WeighInPolicyQueryService>();
builder.Services.AddScoped<RegistrantQueryService>();
builder.Services.AddScoped<OrganizerAccountQueryService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ---- AuthN/Z ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        };
    });
builder.Services.AddAuthorization();

// ---- Rate limiting (SP-4, U3-NFR-S8) ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("registration", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) }));
});

// ---- Health checks (RP-6) ----
builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("db", tags: ["ready"]);

builder.Services.AddControllers();

var app = builder.Build();

// ---- Pipeline ----
app.UseExceptionHandler(a => a.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError; // fail closed, generic (SP-7)
    await ctx.Response.WriteAsJsonAsync(new { title = "An unexpected error occurred." });
}));
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");                                    // liveness
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") }); // deep DB

// Dev convenience: apply migrations at startup (never destructive; expand/contract, RP-5).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

/// <summary>Assembly marker for validator discovery + WebApplicationFactory tests.</summary>
public partial class Program;
