using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EduTech.Api.Hangfire;
using EduTech.Attendance;
using EduTech.Auth;
using EduTech.Workforce;
using EduTech.Compliance;
using EduTech.Fees;
using EduTech.Grades;
using EduTech.Identity;
using EduTech.Membership;
using EduTech.People;
using EduTech.Organization;
using EduTech.Admissions;
using EduTech.Notifications;
using EduTech.Students;
using EduTech.School;
using EduTech.Shared.Audit;
using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Caching;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Features;
using EduTech.Shared.HealthChecks;
using EduTech.Shared.Identity;
using EduTech.Shared.Middleware;
using EduTech.Shared.Models;
using EduTech.Shared.Observability;
using EduTech.Shared.Persistence;
using EduTech.Shared.Security;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RedisRateLimiting;
using RedisRateLimiting.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

// ─── Serilog: bootstrap early so startup errors are captured ──────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Override the noisy per-request framework logs, but NOT Microsoft.Hosting.Lifetime —
    // that's what prints "Now listening on…" / "Application started" to the terminal.
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("Logs/edutech-.json", rollingInterval: RollingInterval.Day)
    .CreateLogger();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ─── Configuration helpers ────────────────────────────────────────────────────
ConfigurationManager config = builder.Configuration;

// EDD-012 B2c.3a — ONE signing key for every identity/portal token (owner · staff · parent · identity):
// the per-portal keys are retired. Platform-admin keeps its own key — a distinct internal trust boundary
// (admin.schoolflow.com), so a leak of the user key can never forge an admin token. Falls back to the
// legacy staff key so a pre-config deploy keeps validating; in-flight tokens signed with an old key fail
// and silently refresh (refresh tokens are opaque DB rows, unaffected) — no hard logout.
string signingKey  = config["Jwt:SigningKey"] ?? config["Jwt:StaffSigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is missing");
string adminKey    = config["Jwt:PlatformAdminSigningKey"] ?? throw new InvalidOperationException("Jwt:PlatformAdminSigningKey is missing");
string jwtIssuer   = config["Jwt:Issuer"]                  ?? "EduTech";
string jwtAudience = config["Jwt:Audience"]                ?? "EduTechApp";

// ─── HTTP context + request context ──────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IEduTechRequestContext, EduTechRequestContext>();

// ─── Persistence: Dapper + PostgreSQL connection factory ──────────────────────
builder.Services.AddEduTechPersistence(config);

// ─── Redis (optional): one shared multiplexer for cache + distributed rate limiting ───
// Probed once. Reachable → Redis cache/limiter; absent or unreachable → in-memory fallback,
// so local dev runs with no Redis container.
IConnectionMultiplexer? redis = TryConnectRedis(config);
builder.Services.AddEduTechCaching(redis);

// ─── Authentication: the portal schemes now validate with the SAME signing key (B2c.3a). The scheme
// names are retained until B2c.3b unifies them; only the key is shared here. Platform-admin stays on
// its own key. ──────────────────────────────────────────────────────────────
builder.Services.AddAuthentication()
    .AddJwtBearer("StaffAuth", options =>
    {
        options.TokenValidationParameters = BuildTokenParams(signingKey, jwtIssuer, jwtAudience);
        ReadTokenFromCookie(options);
    })
    .AddJwtBearer("SchoolAuth", options =>
    {
        options.TokenValidationParameters = BuildTokenParams(signingKey, jwtIssuer, jwtAudience);
        ReadTokenFromCookie(options);
    })
    .AddJwtBearer("ParentAuth", options =>
    {
        options.TokenValidationParameters = BuildTokenParams(signingKey, jwtIssuer, jwtAudience);
        ReadTokenFromCookie(options);
    })
    .AddJwtBearer("IdentityAuth", options =>
    {
        options.TokenValidationParameters = BuildTokenParams(signingKey, jwtIssuer, jwtAudience);
        ReadTokenFromCookie(options);
    })
    .AddJwtBearer("PlatformAdminAuth", options =>
    {
        options.TokenValidationParameters = BuildTokenParams(adminKey, jwtIssuer, jwtAudience);
        ReadTokenFromCookie(options);
    });

// ─── Authorization: named policies map portals to schemes ────────────────────
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("StaffOnly", policy => policy
        .AddAuthenticationSchemes("StaffAuth")
        .RequireAuthenticatedUser()
        .RequireClaim("user_type", UserTypes.Staff))
    .AddPolicy("SchoolOnly", policy => policy
        .AddAuthenticationSchemes("SchoolAuth")
        .RequireAuthenticatedUser()
        .RequireClaim("user_type", UserTypes.School))
    .AddPolicy("ParentOnly", policy => policy
        .AddAuthenticationSchemes("ParentAuth")
        .RequireAuthenticatedUser()
        .RequireClaim("user_type", UserTypes.Parent))
    .AddPolicy("PlatformAdminOnly", policy => policy
        .AddAuthenticationSchemes("PlatformAdminAuth")
        .RequireAuthenticatedUser()
        .RequireClaim("user_type", UserTypes.PlatformAdmin))
    // Staff OR parent. The user_type gate is EXPLICIT (EDD-012 B2c.3a): with one signing key the scheme
    // list no longer excludes other personas, so authorization states the restriction itself.
    .AddPolicy("ComplianceActor", policy => policy
        .AddAuthenticationSchemes("StaffAuth", "ParentAuth")
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => PortalGates.IsStaffOrParent(ctx.User)))
    // School-management endpoints (students, classes, calendar): the owner OR a staff member with an
    // active school. Both tokens carry school_id; the explicit user_type gate keeps parents/identities
    // out now that one signing key validates every token (per-action capabilities gate staff further).
    .AddPolicy("SchoolPortal", policy => policy
        .AddAuthenticationSchemes("SchoolAuth", "StaffAuth")
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => PortalGates.IsSchoolOrStaff(ctx.User)))
    // "Are you an authenticated person?" — any session (identity-scope or portal) qualifies; the
    // persona is irrelevant. Identity-surface endpoints (/auth/me, select-context, onboarding) use this.
    .AddPolicy("AuthenticatedIdentity", policy => policy
        .AddAuthenticationSchemes("SchoolAuth", "StaffAuth", "ParentAuth", "IdentityAuth")
        .RequireAuthenticatedUser());

// ─── Rate limiting ────────────────────────────────────────────────────────────
int generalWindow = config.GetValue<int>("RateLimit:GeneralWindowSeconds", 60);
int generalMax    = config.GetValue<int>("RateLimit:GeneralMaxRequests", 100);
int loginWindow   = config.GetValue<int>("RateLimit:LoginWindowSeconds", 60);
int loginMax      = config.GetValue<int>("RateLimit:LoginMaxRequests", 10);
int otpWindow     = config.GetValue<int>("RateLimit:OtpWindowSeconds", 300);
int otpMax        = config.GetValue<int>("RateLimit:OtpMaxRequests", 3);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Same policy names as before, so every [EnableRateLimiting(...)] attribute is untouched.
    // Redis-backed when available (counters shared across instances); in-memory otherwise.
    AddWindowLimiter(options, "general", generalWindow, generalMax, redis); // 100 req/min per IP
    AddWindowLimiter(options, "login",   loginWindow,   loginMax,   redis); // 10/min — brute force
    AddWindowLimiter(options, "otp",     otpWindow,     otpMax,     redis); // 3/5min — OTP bombing
});

// ─── Health checks ────────────────────────────────────────────────────────────
IHealthChecksBuilder healthChecks = builder.Services.AddHealthChecks();
if (redis is not null)
{
    healthChecks.AddCheck<RedisHealthCheck>("redis", tags: new[] { "cache" });
}

// ─── CORS ─────────────────────────────────────────────────────────────────────
string[] allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("EduTechCors", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ─── Controllers ─────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Domain enums travel as their snake_case STRING on the wire (matching the DB + frontend),
        // never as integers. The naming policy mirrors EnumStringHandler so JSON and storage agree.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    });

// Funnel model-binding / JSON failures (e.g. a malformed enum in a request body) through the same
// ApiError shape the rest of the app uses, instead of the framework's default ValidationProblem.
// We surface only field NAMES — never the attempted value — to avoid echoing untrusted input.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        List<ValidationError> errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => new ValidationError { Field = entry.Key, Message = "Invalid or missing value." })
            .ToList();

        ApiError error = new ApiError
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Message = "One or more fields are invalid.",
            ErrorCode = ErrorCodes.ValidationError,
            ValidationErrors = errors.Count > 0 ? errors : null,
            Path = context.HttpContext.Request.Path
        };

        return new BadRequestObjectResult(error);
    };
});

// ─── Swagger (dev only) ───────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "EduTech API",
        Version = "v1"
    });

    // Allow pasting a Bearer token in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "Enter your JWT token. The 'Bearer ' prefix is added automatically."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─── Module registrations (uncomment as each module is built) ─────────────────
builder.Services.AddSingleton<IFieldEncryptor, AesFieldEncryptor>();
builder.Services.AddIdentityVerification(config);   // shared Dojah/stub seam (school KYC + compliance)
builder.Services.AddFeatureFlags();
builder.Services.AddSlackNotifications(config);     // error alerts -> Slack (real) or logs (dev)
builder.Services.AddDomainEvents();                 // Observer: publisher; modules register their handlers
builder.Services.AddAuditLog();                     // Observer: writes every auditable event to the trail
builder.Services.AddCapabilityResolution();         // EDD-013 B2b — the single server-side authorization API
builder.Services.AddAuthModule();
builder.Services.AddWorkforceModule();
builder.Services.AddIdentityModule();   // EDD-001 Sprint 1 — global identities (unified auth lands Sprint 2)
builder.Services.AddMembershipModule(); // EDD-007 Sprint B1 — canonical belonging edge (adult lifecycle)
builder.Services.AddPeopleModule();     // EDD-008/009 Sprint C — Position catalog + Employment
builder.Services.AddOrganizationModule(); // EDD-010 Sprint D — platform root (shadow root)
builder.Services.AddAdmissionsModule();   // EDD-014 — first Layer-3 module (vertical slices)
builder.Services.AddNotificationsModule(config);
builder.Services.AddSchoolModule(config);
builder.Services.AddComplianceModule(config);
builder.Services.AddStudentsModule(config);
builder.Services.AddAttendanceModule(config);
builder.Services.AddGradesModule(config);
builder.Services.AddFeesModule(config);
// builder.Services.AddStudentsModule(config);
// builder.Services.AddComplianceModule(config);

// ─── Build ────────────────────────────────────────────────────────────────────
WebApplication app = builder.Build();

// ─── Auto-migrate (DEV only; flip Database:AutoMigrate off for launch) ─────────
if (config.GetValue<bool>("Database:AutoMigrate"))
{
    string? migrationsDir = DatabaseMigrator.ResolveMigrationsDirectory(app.Environment.ContentRootPath);
    if (migrationsDir is null)
    {
        app.Logger.LogWarning("Auto-migrate is on but no Database/*.sql folder was found; skipping.");
    }
    else
    {
        DatabaseMigrator migrator = new DatabaseMigrator(
            config.GetConnectionString("Default")!,
            app.Services.GetRequiredService<ILogger<DatabaseMigrator>>());
        await migrator.ApplyPendingAsync(migrationsDir, config["Database:BaselineThrough"]);
    }
}

// ─── Swagger UI (dev only) ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "EduTech API v1");
        options.RoutePrefix = "swagger";
    });

    // Hangfire dashboard — DEV ONLY (allow-all). Production access gated behind Platform Admin later.
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
    });
}

// ─── Security headers ─────────────────────────────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers.XFrameOptions          = "DENY";
    context.Response.Headers.XContentTypeOptions    = "nosniff";
    context.Response.Headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains; preload";
    context.Response.Headers.CacheControl           = "no-store";
    context.Response.Headers.Pragma                 = "no-cache";
    context.Response.Headers.ContentSecurityPolicy  = "frame-ancestors 'none'";
    await next(context);
});

// ─── Middleware pipeline (order matters) ──────────────────────────────────────
app.UseStaticFiles(); // serves the local-disk file-storage fallback (/uploads) in dev
app.UseCors("EduTechCors");
app.UseAuthentication();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthorization();
// Logging is OUTERMOST so it logs every request (incl. errors, with IP + final status);
// the exception handler sits inside it, converting exceptions to responses first.
app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// ─── Module endpoint registrations (uncomment as each module is built) ────────
// app.MapAuthEndpoints();
// app.MapSchoolEndpoints();
// app.MapStaffEndpoints();
// app.MapStudentsEndpoints();
// app.MapGradesEndpoints();
// app.MapFeesEndpoints();
// app.MapAttendanceEndpoints();
// app.MapStoreEndpoints();
// app.MapNotificationsEndpoints();
// app.MapComplianceEndpoints();
// app.MapPlatformAdminEndpoints();

// Idempotently ensure the known release feature flags exist (default OFF) so the CMS can list them.
// Best-effort: a missing table (migration not yet run) logs a warning rather than blocking startup.
using (IServiceScope scope = app.Services.CreateScope())
{
    try
    {
        IFeatureFlagService featureFlags = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
        await featureFlags.EnsureSeededAsync();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Feature flag seeding skipped (has 0009_feature_flags.sql been run?).");
    }

    // Daily calendar sweep: provisions first calendars and PREPARES term/session transitions (the
    // school confirms the actual move). 02:00 UTC = 03:00 WAT, before any school day starts.
    try
    {
        IRecurringJobManager recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<EduTech.Students.Academics.Transition.CalendarRollForwardJob>(
            "calendar-roll-forward", job => job.RunAsync(CancellationToken.None), Cron.Daily(2));
        recurringJobs.AddOrUpdate<EduTech.Auth.Unified.IdentityReconciliationJob>(
            "identity-reconciliation", job => job.RunAsync(CancellationToken.None), Cron.Daily(3));
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Calendar roll-forward job not scheduled (is Hangfire storage reachable?).");
    }
}

app.MapHealthChecks("/health");
app.MapControllers();
app.Run();

// ─── Helpers ──────────────────────────────────────────────────────────────────
static TokenValidationParameters BuildTokenParams(string signingKey, string issuer, string audience)
{
    return new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidIssuer              = issuer,
        ValidateAudience         = true,
        ValidAudience            = audience,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew                = TimeSpan.Zero
    };
}

// Lets a scheme accept the access token from the httpOnly `sf_access` cookie when there's no
// Authorization header (Cross-Cutting Auth §X.2). The wrong portal's token simply fails validation.
static void ReadTokenFromCookie(JwtBearerOptions options)
{
    // Keep our short claim names ("role", "user_type", …) as-is — without this the handler remaps
    // "role" to ClaimTypes.Role, so FindFirst("role") returns null and every role check silently fails.
    options.MapInboundClaims = false;

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (string.IsNullOrEmpty(context.Token))
            {
                context.Token = context.Request.Cookies["sf_access"];
            }

            return Task.CompletedTask;
        }
    };
}

// One fixed-window policy: Redis-backed (shared across instances) when a multiplexer is available,
// otherwise the in-process limiter. Same limits either way.
static void AddWindowLimiter(RateLimiterOptions options, string policyName, int windowSeconds,
    int permitLimit, IConnectionMultiplexer? redis)
{
    if (redis is not null)
    {
        options.AddRedisFixedWindowLimiter(policyName, limiterOptions =>
        {
            limiterOptions.ConnectionMultiplexerFactory = () => redis;
            limiterOptions.PermitLimit = permitLimit;
            limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
        });
    }
    else
    {
        options.AddFixedWindowLimiter(policyName, limiterOptions =>
        {
            limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
            limiterOptions.PermitLimit = permitLimit;
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = 0;
        });
    }
}

// Connects to Redis if a connection string is present and reachable at startup; otherwise returns
// null so the app falls back to in-memory cache + rate limiting (no Redis container needed in dev).
static IConnectionMultiplexer? TryConnectRedis(IConfiguration configuration)
{
    string? connectionString = configuration.GetConnectionString("Redis");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return null;
    }

    try
    {
        return ConnectionMultiplexer.Connect(connectionString);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Redis configured but unreachable at startup; using in-memory cache + rate limiting.");
        return null;
    }
}
