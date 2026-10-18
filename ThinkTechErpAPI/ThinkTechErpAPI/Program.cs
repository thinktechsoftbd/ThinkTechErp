using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using ThinkTechErpAPI.Security;
using ThinkTechErpService;

var currentDirectory = Directory.GetCurrentDirectory();
var webOptions = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = File.Exists(Path.Combine(currentDirectory, "appsettings.json"))
        ? currentDirectory
        : AppContext.BaseDirectory
};
var builder = WebApplication.CreateBuilder(webOptions);

// On Linux, systemd LoadCredential= exposes each secret as a file named after its
// configuration key (e.g. Authentication__Jwt__SigningKey) in $CREDENTIALS_DIRECTORY.
var credentialsDirectory = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY");
if (!string.IsNullOrWhiteSpace(credentialsDirectory))
    builder.Configuration.AddKeyPerFile(credentialsDirectory, optional: true);

if (builder.Environment.IsDevelopment())
{
    // Local development runs without any secret setup. Values from User Secrets or
    // environment variables are used when present; production must supply them
    // (see deploy/linux/README.md).
    var developmentDefaults = new Dictionary<string, string?>();
    if (string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Jwt:SigningKey"]))
        developmentDefaults["Authentication:Jwt:SigningKey"] = DevelopmentSigningKey.GetOrCreate();

    // Reuse the Sms connection until a least-privilege SmsApi login is configured.
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("SmsApi")))
        developmentDefaults["ConnectionStrings:SmsApi"] = builder.Configuration.GetConnectionString("Sms");

    builder.Configuration.AddInMemoryCollection(developmentDefaults);
}

var issuer = builder.Configuration["Authentication:Jwt:Issuer"];
var audience = builder.Configuration["Authentication:Jwt:Audience"];
var signingKey = builder.Configuration["Authentication:Jwt:SigningKey"];

var missingJwtSettings = new List<string>();
if (string.IsNullOrWhiteSpace(issuer))
    missingJwtSettings.Add("Authentication__Jwt__Issuer");
if (string.IsNullOrWhiteSpace(audience))
    missingJwtSettings.Add("Authentication__Jwt__Audience");
if (string.IsNullOrWhiteSpace(signingKey))
    missingJwtSettings.Add("Authentication__Jwt__SigningKey (minimum 32 bytes)");
else if (Encoding.UTF8.GetByteCount(signingKey) < 32)
    missingJwtSettings.Add("Authentication__Jwt__SigningKey (minimum 32 bytes)");

if (missingJwtSettings.Count > 0)
{
    throw new InvalidOperationException(
        $"Missing or invalid JWT settings: {string.Join(", ", missingJwtSettings)}. " +
        "Supply the signing key through environment variables or systemd credentials " +
        "(see deploy/linux/README.md); issuer/audience can also come from appsettings.");
}

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddThinkTechErpServices(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var subject = context.Principal?.FindFirst("sub")?.Value;
                var tokenId = context.Principal?.FindFirst("jti")?.Value;
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tokenId))
                    context.Fail("The access token is missing required identity claims.");

                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey!)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(SmsSecurityPolicies.Admin, policy =>
        policy.RequireAuthenticatedUser().RequireRole("sms_admin"));

    options.AddPolicy(SmsSecurityPolicies.ClientOrAdmin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new SmsClientAccessRequirement());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, SmsClientAccessHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SmsSecurityPolicies.RateLimit, context =>
    {
        var partitionKey = context.User.FindFirst("client_id")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown-client";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});
// nginx on the same host terminates TLS. Trust its X-Forwarded-* headers so the
// client IP (used by the rate limiter) and scheme are the real ones. Loopback
// proxies are trusted by default.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    await next();
});

app.MapControllers();

app.Run();
