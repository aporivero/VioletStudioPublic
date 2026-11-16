using System.Data;
using System.Data.Common;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VioletStudio.Web.Data;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5290");
builder.Services.AddHttpClient("video-metadata", client =>
{
    client.Timeout = TimeSpan.FromSeconds(7);
    client.MaxResponseContentBufferSize = 1_000_000;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VioletStudio/1.0 (+local metadata preview)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html;q=0.8");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli });
builder.Services.AddDbContextFactory<VioletDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("VioletDatabase"), sql => sql.EnableRetryOnFailure()));
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "work", "data-protection");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".VioletStudio.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromHours(12);
});
builder.Services.AddRateLimiter(options => options.AddPolicy("login", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "local",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

// Google OAuth removed; local username/password + SQL auth is used.

var app = builder.Build();
var localSettingsPath = Path.Combine(app.Environment.ContentRootPath, "admin.local.json");
var adminCredentials = File.Exists(localSettingsPath)
    ? JsonSerializer.Deserialize<AdminCredentials>(await File.ReadAllTextAsync(localSettingsPath), JsonOptions.Web)
    : null;
if (adminCredentials is null) app.Logger.LogError("Admin settings missing: admin.local.json.");

try
{
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<VioletDbContext>>().CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    // Best-effort compatibility for older local DBs (EnsureCreated doesn't migrate). Ignore failures.
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','Role') IS NULL ALTER TABLE Users ADD Role nvarchar(20) NOT NULL CONSTRAINT DF_Users_Role DEFAULT 'USER'"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','Status') IS NULL ALTER TABLE Users ADD Status nvarchar(20) NOT NULL CONSTRAINT DF_Users_Status DEFAULT 'ACTIVE'"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','ProLifetime') IS NULL ALTER TABLE Users ADD ProLifetime bit NOT NULL CONSTRAINT DF_Users_ProLifetime DEFAULT 0"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','ProfileImage') IS NULL ALTER TABLE Users ADD ProfileImage nvarchar(1024) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','DeviceId') IS NULL ALTER TABLE Users ADD DeviceId nvarchar(120) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','Provider') IS NULL ALTER TABLE Users ADD Provider nvarchar(30) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Users','ProviderSubject') IS NULL ALTER TABLE Users ADD ProviderSubject nvarchar(200) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('LicenseKeys','PlanCode') IS NULL ALTER TABLE LicenseKeys ADD PlanCode nvarchar(30) NOT NULL CONSTRAINT DF_LicenseKeys_PlanCode DEFAULT 'PRO_1M'"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('LicenseKeys','Note') IS NULL ALTER TABLE LicenseKeys ADD Note nvarchar(500) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Orders','PlanCode') IS NULL ALTER TABLE Orders ADD PlanCode nvarchar(30) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.PurchaseMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseMessages(
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        Contact nvarchar(120) NOT NULL,
        Package nvarchar(30) NOT NULL,
        Message nvarchar(2000) NOT NULL,
        Status nvarchar(20) NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_PurchaseMessages_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.DownloadRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DownloadRecords(
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        VideoTitle nvarchar(300) NOT NULL,
        Thumbnail nvarchar(1024) NULL,
        Platform nvarchar(40) NOT NULL,
        Resolution nvarchar(30) NOT NULL,
        Format nvarchar(20) NOT NULL,
        SourceUrl nvarchar(2048) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_DownloadRecords_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.AdCredits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdCredits(
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        CompletedTasks int NOT NULL CONSTRAINT DF_AdCredits_CompletedTasks DEFAULT 0,
        AvailableCredits int NOT NULL CONSTRAINT DF_AdCredits_AvailableCredits DEFAULT 0,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_AdCredits_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_AdCredits_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("UPDATE Users SET Role='ADMIN' WHERE LOWER(Email)='alponline77@gmail.com'"); } catch { }
    app.Logger.LogInformation("Violet SQL database is ready.");
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "SQL Server is unavailable or schema update failed. Start SQLEXPRESS and/or recreate local DB for full feature support.");
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    try { await next(); }
    catch (Exception ex) when (ex is DbException or DbUpdateException)
    {
        app.Logger.LogError(ex, "A Violet database operation failed.");
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "SQL Server veritabanı işlemi başarısız. SQLEXPRESS servisini ve şemayı kontrol edin (gerekirse yerel DB'yi sıfırlayın)." });
        }
    }
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (Path.GetExtension(context.File.Name) is ".html" or ".js" or ".css")
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    }
});
app.UseSession();
app.UseRateLimiter();

app.MapGet("/api/status", async (IDbContextFactory<VioletDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    return Results.Ok(new { database = await db.Database.CanConnectAsync() });
});

// Google login removed by request.

app.MapGet("/api/video/resolve", async (string url, IHttpClientFactory clients, IConfiguration configuration, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!TryGetAllowedMediaUri(url, out var mediaUri))
        return Results.BadRequest(new { error = "Geçerli bir video bağlantısı gir." });
    var metadataTask = ResolveVideoMetadataAsync(mediaUri, clients.CreateClient("video-metadata"), configuration["MetaOEmbedAccessToken"], cancellationToken);
    var formatsTask = ResolveVideoFormatsAsync(mediaUri, app.Environment.ContentRootPath, logger, cancellationToken);
    await Task.WhenAll(metadataTask, formatsTask);
    var info = await metadataTask;
    var formats = await formatsTask;
    return Results.Ok(new { info.Title, info.Provider, info.Author, info.ThumbnailUrl, qualities = formats.Qualities, formatError = formats.Error });
});

app.MapPost("/api/video/download", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var input = await ReadJson<DownloadRequest>(context);
    if (input is null || !TryGetAllowedMediaUri(input.Url ?? "", out var mediaUri)) return Results.BadRequest(new { error = "Geçerli bir video bağlantısı girin." });
    var isAudio = string.Equals(input.Mode, "audio", StringComparison.OrdinalIgnoreCase);
    var height = Math.Clamp(input.Height, 1, 4320);
    var adminSession = await IsAdmin(context, factory);
    var user = await CurrentUser(context, factory);
    var isPro = adminSession || (user is not null && IsProUser(user));
    if (!isAudio && height > 720 && !isPro)
        return Results.Json(new { error = "1080p ve üzeri için Pro üyeliği gerekir." }, statusCode: StatusCodes.Status403Forbidden);

    var executable = FindYtDlp(app.Environment.ContentRootPath);
    if (executable is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    var workDir = Path.Combine(app.Environment.ContentRootPath, "work", "downloads");
    Directory.CreateDirectory(workDir);
    var token = Guid.NewGuid().ToString("N");
    var outTemplate = Path.Combine(workDir, $"{token}-%(title).120B [%(id)s].%(ext)s");

    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
    EnsureToolPaths(start);
    foreach (var arg in BuildYtDlpCommonArgs()) start.ArgumentList.Add(arg);
    foreach (var arg in new[] { "--no-playlist", "--windows-filenames", "-o", outTemplate }) start.ArgumentList.Add(arg);
    if (isAudio)
    {
        foreach (var arg in new[] { "-f", "bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", "0" }) start.ArgumentList.Add(arg);
    }
    else
    {
        foreach (var arg in new[] { "-f", $"bv*[height<={height}]+ba/b[height<={height}]/b[height<={height}]/bv*+ba/b", "-S", "res,vcodec:h264,acodec:aac", "--merge-output-format", "mp4" }) start.ArgumentList.Add(arg);
    }
    start.ArgumentList.Add("--");
    start.ArgumentList.Add(mediaUri.AbsoluteUri);

    string? produced = null;
    try
    {
        using var process = Process.Start(start);
        if (process is null) return Results.Problem("İndirme başlatılamadı.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromHours(2));
        using var killDownload = timeout.Token.Register(() => KillQuietly(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        _ = await outputTask;
        var details = (await errorTask ?? "").Trim();
        if (process.ExitCode != 0)
        {
            app.Logger.LogWarning("yt-dlp download failed ({Height}p, exit {Code}): {Details}", height, process.ExitCode, details);
            return Results.BadRequest(new { error = FriendlyDownloadError(details) });
        }

        var files = Directory.GetFiles(workDir, $"{token}-*");
        produced = files.OrderByDescending(File.GetCreationTimeUtc).FirstOrDefault();
        if (produced is null || !File.Exists(produced))
            return Results.Problem("İndirme tamamlandı ancak dosya bulunamadı.");

        try
        {
            var current = await CurrentUser(context, factory);
            if (current is not null)
            {
                await using var db = await factory.CreateDbContextAsync();
                db.DownloadRecords.Add(new DownloadRecord
                {
                    UserId = current.Id,
                    VideoTitle = input.Title ?? (isAudio ? "Ses" : "Video"),
                    Thumbnail = input.Thumbnail,
                    Platform = input.Platform ?? DetectPlatformName(mediaUri),
                    Resolution = isAudio ? "MP3" : QualityLabel(height),
                    Format = isAudio ? "mp3" : "mp4",
                    SourceUrl = mediaUri.AbsoluteUri
                });
                await db.SaveChangesAsync();
            }
        }
        catch { }

        var downloadName = Path.GetFileName(produced);
        if (downloadName.StartsWith(token + "-", StringComparison.Ordinal)) downloadName = downloadName[(token.Length + 1)..];
        var contentType = isAudio ? "audio/mpeg" : "video/mp4";
        // Stream to browser so browser shows its own download UI/progress bar.
        var stream = new FileStream(produced, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        return Results.File(stream, contentType, downloadName, enableRangeProcessing: true);
    }
    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    {
        TryDeleteFile(produced);
        return Results.Problem("İndirme zaman aşımına uğradı.", statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (Exception ex) when (ex is IOException or Win32Exception)
    {
        TryDeleteFile(produced);
        return Results.Problem("İndirme işlemi başlatılamadı.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        TryDeleteFile(produced);
        throw;
    }
});

app.MapGet("/api/account/session", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    await context.Session.LoadAsync();
    var admin = await IsAdmin(context, factory);
    var user = await CurrentUser(context, factory);
    if (user is null && !admin) return Results.Ok(new { authenticated = false });
    if (user is not null)
    {
        var view = (dynamic)AccountView(user);
        return Results.Ok(new { authenticated = true, user = view.user, admin = admin });
    }
    return Results.Ok(new { authenticated = false, admin = true });
});

app.MapPost("/api/account/register", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var input = await ReadJson<RegisterRequest>(context);
    if (input is null) return Results.BadRequest(new { error = "Kayıt bilgileri geçersiz." });
    var email = (input.Email ?? "").Trim().ToLowerInvariant();
    var displayName = (input.DisplayName ?? "").Trim();
    if (email.Length > 320 || !IsEmail(email)) return Results.BadRequest(new { error = "Geçerli bir e-posta adresi yaz." });
    if (displayName.Length is < 2 or > 80) displayName = email.Split('@')[0][..Math.Min(email.Split('@')[0].Length, 80)];
    if ((input.Password ?? "").Length < 10) return Results.BadRequest(new { error = "Şifre en az 10 karakter olmalı." });

    await using var db = await factory.CreateDbContextAsync();
    if (await db.Users.AnyAsync(u => u.Email == email)) return Results.Conflict(new { error = "Bu e-posta ile hesap zaten kayıtlı." });
    var (salt, hash) = HashPassword(input.Password!);
    var role = email == "alponline77@gmail.com" ? "ADMIN" : "USER";
    var user = new UserAccount { Email = email, DisplayName = displayName, PasswordSalt = salt, PasswordHash = hash, Role = role, Provider = "local" };
    db.Users.Add(user);
    db.AdCredits.Add(new AdCredit { UserId = user.Id });
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "Bu e-posta ile hesap zaten kayıtlı." }); }
    await SignInUser(context, user);
    return Results.Created("/api/account/session", AccountView(user));
}).RequireRateLimiting("login");

app.MapPost("/api/account/login", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var input = await ReadJson<LoginRequest>(context);
    if (input is null) return Results.BadRequest(new { error = "Giriş bilgileri geçersiz." });
    var login = (input.Username ?? input.Email ?? "").Trim();
    var loginLower = login.ToLowerInvariant();
    await using var db = await factory.CreateDbContextAsync();
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == loginLower)
        ?? await db.Users.SingleOrDefaultAsync(u => u.DisplayName == login);
    if (user is null || user.Status == "SUSPENDED" || string.IsNullOrEmpty(user.PasswordSalt) || string.IsNullOrEmpty(user.PasswordHash) || !VerifyPassword(input.Password ?? "", user.PasswordSalt, user.PasswordHash, user.PasswordIterations))
        return Results.Json(new { error = "Kullanıcı adı/e-posta veya şifre yanlış." }, statusCode: StatusCodes.Status401Unauthorized);
    if (user.Email == "alponline77@gmail.com" && user.Role != "ADMIN") { user.Role = "ADMIN"; await db.SaveChangesAsync(); }
    await SignInUser(context, user);
    return Results.Ok(AccountView(user));
}).RequireRateLimiting("login");

app.MapGet("/api/payment-info", (IConfiguration config) =>
{
    return Results.Ok(new {
        iban = config["Payment:Iban"] ?? "",
        accountName = config["Payment:AccountName"] ?? "",
        note = "Pro satın alma ödemeleri bu IBAN hesabına manuel olarak yapılır. Ödeme sonrası iletişim bilgisi ve mesaj gönderin; admin size Pro key iletecektir."
    });
});

app.MapPost("/api/account/logout", async (HttpContext context) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    await context.Session.LoadAsync();
    context.Session.Remove("userId");
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/admin/login", async (HttpContext context) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (adminCredentials is null) return Results.Problem("Local admin settings are missing.", statusCode: 503);
    var input = await ReadJson<LoginRequest>(context);
    if (input is null || !FixedTimeTextEquals(input.Username ?? "", adminCredentials.Username)
        || !VerifyPassword(input.Password ?? "", adminCredentials.Salt, adminCredentials.PasswordHash, adminCredentials.Iterations))
        return Results.Json(new { error = "Kullanıcı adı veya parola yanlış." }, statusCode: StatusCodes.Status401Unauthorized);
    await context.Session.LoadAsync();
    context.Session.SetString("admin", adminCredentials.Username);
    return Results.Ok(new { ok = true, username = adminCredentials.Username });
}).RequireRateLimiting("login");

app.MapGet("/api/admin/session", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    var admin = await IsAdmin(context, factory);
    await context.Session.LoadAsync();
    var username = context.Session.GetString("admin");
    return Results.Ok(new { authenticated = admin, username });
});

app.MapPost("/api/admin/logout", async (HttpContext context) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    await context.Session.LoadAsync();
    context.Session.Remove("admin");
    return Results.Ok(new { ok = true });
});

// Keys
app.MapGet("/api/admin/keys", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!await IsAdmin(context, factory)) return AdminRequired();
    await using var db = await factory.CreateDbContextAsync();
    var keys = await db.LicenseKeys.OrderByDescending(k => k.CreatedAt).Select(k => new
    {
        code = k.Code,
        planCode = k.PlanCode,
        months = k.Months,
        status = k.Status,
        note = k.Note,
        createdAt = k.CreatedAt,
        redeemedAt = k.RedeemedAt,
        redeemedBy = k.RedeemedBy == null ? null : k.RedeemedBy.Email
    }).Take(500).ToListAsync();
    return Results.Ok(new { keys });
});

app.MapPost("/api/admin/keys", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await IsAdmin(context, factory)) return AdminRequired();
    var input = await ReadJson<CreateKeysRequest>(context);
    if (input is null) return Results.BadRequest(new { error = "Geçersiz istek." });
    var plan = (input.PlanCode ?? (input.Months switch { 1 => "PRO_1M", 5 => "PRO_5M", 0 => "LIFETIME", _ => "" })).ToUpperInvariant();
    if (plan is not ("PRO_1M" or "PRO_5M" or "LIFETIME")) return Results.BadRequest(new { error = "Geçersiz paket." });
    var qty = Math.Clamp(input.Quantity, 1, 20);
    var months = plan switch { "PRO_1M" => 1, "PRO_5M" => 5, "LIFETIME" => 0, _ => 1 };
    var created = new List<LicenseKey>();
    for (var i = 0; i < qty; i++)
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var hex = Convert.ToHexString(bytes);
        created.Add(new LicenseKey { Code = $"VIOLET-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..]}", Months = months, PlanCode = plan, Note = input.Note });
    }
    await using var db = await factory.CreateDbContextAsync();
    db.LicenseKeys.AddRange(created);
    await db.SaveChangesAsync();
    return Results.Json(new { keys = created.Select(KeyView) }, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/api/admin/users", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!await IsAdmin(context, factory)) return AdminRequired();
    await using var db = await factory.CreateDbContextAsync();
    var users = await db.Users.OrderByDescending(u => u.CreatedAt).Select(u => new
    {
        id = u.Id,
        email = u.Email,
        displayName = u.DisplayName,
        image = u.ProfileImage,
        createdAt = u.CreatedAt,
        role = u.Role,
        status = u.Status,
        proExpiresAt = u.ProExpiresAt,
        proLifetime = u.ProLifetime,
        orders = u.Orders.Count
    }).Take(500).ToListAsync();
    return Results.Ok(new { users });
});

app.MapGet("/api/admin/orders", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!await IsAdmin(context, factory)) return AdminRequired();
    await using var db = await factory.CreateDbContextAsync();
    var orders = await db.Orders.Include(o => o.User).OrderByDescending(o => o.CreatedAt).Take(500).Select(o => new
    {
        id = o.Id,
        email = o.User.Email,
        durationMonths = o.DurationMonths,
        planCode = o.PlanCode,
        amount = o.Amount,
        currency = o.Currency,
        status = o.Status,
        createdAt = o.CreatedAt,
        paidAt = o.PaidAt
    }).ToListAsync();
    return Results.Ok(new { orders });
});

app.MapGet("/api/admin/messages", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!await IsAdmin(context, factory)) return AdminRequired();
    await using var db = await factory.CreateDbContextAsync();
    var messages = await db.PurchaseMessages.Include(m => m.User).OrderByDescending(m => m.CreatedAt).Take(300).Select(m => new
    {
        id = m.Id,
        name = m.User.DisplayName,
        email = m.User.Email,
        contact = m.Contact,
        package = m.Package,
        message = m.Message,
        status = m.Status,
        createdAt = m.CreatedAt
    }).ToListAsync();
    return Results.Ok(new { messages });
});

app.MapPost("/api/admin/messages/{id}/status", async (Guid id, HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await IsAdmin(context, factory)) return AdminRequired();
    var input = await ReadJson<UpdateStatusRequest>(context);
    var status = (input?.Status ?? "").ToUpperInvariant();
    if (status is not ("NEW" or "REVIEWING" or "COMPLETED")) return Results.BadRequest(new { error = "Geçersiz durum." });
    await using var db = await factory.CreateDbContextAsync();
    var msg = await db.PurchaseMessages.FindAsync(id);
    if (msg is null) return Results.NotFound(new { error = "Mesaj bulunamadı." });
    msg.Status = status;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/admin/stats", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!await IsAdmin(context, factory)) return AdminRequired();
    await using var db = await factory.CreateDbContextAsync();
    var now = DateTimeOffset.UtcNow;
    var totalUsers = await db.Users.CountAsync();
    var activePro = await db.Users.CountAsync(u => u.ProLifetime || (u.ProExpiresAt != null && u.ProExpiresAt > now));
    var totalKeys = await db.LicenseKeys.CountAsync();
    var usedKeys = await db.LicenseKeys.CountAsync(k => k.Status == "Redeemed");
    var unusedKeys = await db.LicenseKeys.CountAsync(k => k.Status == "Active");
    var newMessages = await db.PurchaseMessages.CountAsync(m => m.Status == "NEW");
    var recentUsers = await db.Users.OrderByDescending(u => u.CreatedAt).Take(10).Select(u => new { u.Id, u.Email, u.DisplayName, u.ProfileImage, u.CreatedAt, u.Role, u.Status, u.ProLifetime, u.ProExpiresAt }).ToListAsync();
    return Results.Ok(new { totalUsers, activePro, freeUsers = Math.Max(0, totalUsers - activePro), totalKeys, usedKeys, unusedKeys, newMessages, recentUsers });
});

// User-facing
app.MapPost("/api/orders", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Sipariş oluşturmak için önce hesabına giriş yap." }, statusCode: StatusCodes.Status401Unauthorized);
    var input = await ReadJson<CreateOrderRequest>(context);
    if (input is null) return Results.BadRequest(new { error = "Geçersiz plan." });
    var plan = (input.PlanCode ?? "").ToUpperInvariant();
    var (amount, months) = plan switch
    {
        "PRO_1M" => (50m, 1),
        "PRO_5M" => (250m, 5),
        "LIFETIME" => (900m, 0),
        _ => (0m, -1)
    };
    if (months < 0) return Results.BadRequest(new { error = "Geçersiz plan süresi." });
    await using var db = await factory.CreateDbContextAsync();
    var order = new PurchaseOrder { UserId = user.Id, DurationMonths = months, PlanCode = plan, Amount = amount, Status = "PendingPayment" };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/api/orders/{order.Id}", new { order.Id, order.DurationMonths, planCode = order.PlanCode, order.Amount, order.Currency, order.Status, order.CreatedAt });
});

app.MapGet("/api/orders", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Hesabına giriş yap." }, statusCode: StatusCodes.Status401Unauthorized);
    await using var db = await factory.CreateDbContextAsync();
    var orders = await db.Orders.Where(o => o.UserId == user.Id).OrderByDescending(o => o.CreatedAt)
        .Select(o => new { o.Id, o.DurationMonths, planCode = o.PlanCode, o.Amount, o.Currency, o.Status, o.CreatedAt, o.PaidAt }).ToListAsync();
    return Results.Ok(new { orders });
});

app.MapPost("/api/messages", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Mesaj göndermek için giriş yapmalısın." }, statusCode: StatusCodes.Status401Unauthorized);
    var input = await ReadJson<PurchaseMessageRequest>(context);
    if (input is null) return Results.BadRequest(new { error = "Geçersiz veri." });
    var contact = (input.Contact ?? "").Trim();
    var pkg = (input.Package ?? "").ToUpperInvariant();
    var message = (input.Message ?? "").Trim();
    if (contact.Length < 3 || contact.Length > 120) return Results.BadRequest(new { error = "Geçerli bir iletişim bilgisi gir." });
    if (pkg is not ("PRO_1M" or "PRO_5M" or "LIFETIME")) return Results.BadRequest(new { error = "Geçersiz paket." });
    if (message.Length < 5 || message.Length > 2000) return Results.BadRequest(new { error = "Mesaj 5-2000 karakter olmalı." });
    await using var db = await factory.CreateDbContextAsync();
    db.PurchaseMessages.Add(new PurchaseMessage { UserId = user.Id, Contact = contact, Package = pkg, Message = message, Status = "NEW" });
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/library", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Giriş gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
    await using var db = await factory.CreateDbContextAsync();
    var items = await db.DownloadRecords.Where(d => d.UserId == user.Id).OrderByDescending(d => d.CreatedAt).Take(200)
        .Select(d => new { d.Id, d.VideoTitle, d.Thumbnail, d.Platform, d.Resolution, d.Format, d.SourceUrl, d.CreatedAt }).ToListAsync();
    return Results.Ok(new { items });
});

app.MapDelete("/api/library/{id}", async (Guid id, HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Giriş gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
    await using var db = await factory.CreateDbContextAsync();
    var item = await db.DownloadRecords.SingleOrDefaultAsync(d => d.Id == id && d.UserId == user.Id);
    if (item is null) return Results.NotFound();
    db.DownloadRecords.Remove(item);
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/license/redeem", async (HttpContext context, IDbContextFactory<VioletDbContext> factory) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Lisansı kullanmak için önce hesabına giriş yap." }, statusCode: StatusCodes.Status401Unauthorized);
    var input = await ReadJson<RedeemRequest>(context);
    if (string.IsNullOrWhiteSpace(input?.Code)) return Results.BadRequest(new { error = "Lisans kodu gerekli." });
    var deviceId = (input.DeviceId ?? "").Trim();

    await using var db = await factory.CreateDbContextAsync();
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var key = await db.LicenseKeys.SingleOrDefaultAsync(k => k.Code == input.Code.Trim().ToUpperInvariant());
    if (key is null) return Results.Json(new { error = "Bu lisans anahtarı bulunamadı." }, statusCode: StatusCodes.Status404NotFound);
    if (key.Status != "Active") return Results.Json(new { error = "Bu lisans anahtarı daha önce kullanılmış veya iptal edilmiş." }, statusCode: StatusCodes.Status409Conflict);
    var account = await db.Users.SingleAsync(u => u.Id == user.Id);
    // basic single-device binding if deviceId provided
    if (!string.IsNullOrEmpty(deviceId))
    {
        if (!string.IsNullOrEmpty(account.DeviceId) && account.DeviceId != deviceId)
            return Results.Json(new { error = "Bu hesap başka bir cihaza bağlı." }, statusCode: StatusCodes.Status409Conflict);
        account.DeviceId = deviceId;
    }
    if (key.PlanCode == "LIFETIME" || key.Months == 0)
    {
        account.ProLifetime = true;
        account.ProExpiresAt = null;
    }
    else
    {
        var start = account.ProExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow ? expiry : DateTimeOffset.UtcNow;
        var days = key.PlanCode == "PRO_5M" ? 150 : key.Months * 30;
        account.ProExpiresAt = start.AddDays(days);
    }
    key.Status = "Redeemed";
    key.RedeemedAt = DateTimeOffset.UtcNow;
    key.RedeemedByUserId = user.Id;
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Ok(new { ok = true, planCode = key.PlanCode, months = key.Months, expiresAt = account.ProExpiresAt, lifetime = account.ProLifetime });
});

app.MapGet("/api/ads/status", async (HttpContext context, IDbContextFactory<VioletDbContext> factory, IConfiguration config) =>
{
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Giriş gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
    await using var db = await factory.CreateDbContextAsync();
    var credit = await db.AdCredits.SingleOrDefaultAsync(c => c.UserId == user.Id);
    if (credit is null) { credit = new AdCredit { UserId = user.Id }; db.AdCredits.Add(credit); await db.SaveChangesAsync(); }
    var enabled = config.GetValue<bool>("Ads:Enabled");
    var adUrl = config["Ads:AdUrl"] ?? "";
    var secret = config["Ads:PostbackSecret"] ?? "";
    var required = Math.Max(1, config.GetValue<int?>("Ads:RequiredTasks") ?? 4);
    var rewardHours = Math.Max(1, config.GetValue<int?>("Ads:RewardHours") ?? 24);
    var configured = enabled && !string.IsNullOrWhiteSpace(adUrl) && !string.IsNullOrWhiteSpace(secret);
    return Results.Ok(new {
        configured,
        adUrl = configured ? adUrl : "",
        completedTasks = credit.CompletedTasks,
        availableCredits = credit.AvailableCredits,
        requiredTasks = required,
        rewardHours,
        note = configured
            ? "Reklamı açın. Görev tamamlaması yalnızca Adsterra sunucu (postback) doğrulamasıyla sayılır."
            : "Adsterra yapılandırması eksik: Ads:Enabled, Ads:AdUrl ve Ads:PostbackSecret ayarlayın; Adsterra panelinde postback URL tanımlayın."
    });
});

app.MapPost("/api/ads/start", async (HttpContext context, IDbContextFactory<VioletDbContext> factory, IConfiguration config) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Giriş gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
    var enabled = config.GetValue<bool>("Ads:Enabled");
    var adUrl = config["Ads:AdUrl"] ?? "";
    var secret = config["Ads:PostbackSecret"] ?? "";
    if (!enabled || string.IsNullOrWhiteSpace(adUrl) || string.IsNullOrWhiteSpace(secret))
        return Results.BadRequest(new { error = "Reklam sistemi yapılandırılmamış." });
    await using var db = await factory.CreateDbContextAsync();
    if (!await db.AdCredits.AnyAsync(c => c.UserId == user.Id))
    { db.AdCredits.Add(new AdCredit { UserId = user.Id }); await db.SaveChangesAsync(); }
    // userId'yi Adsterra link/postback makrolarında kullanacak şekilde kendi Adsterra ayarınıza göre ekleyin.
    return Results.Ok(new { ok = true, adUrl, userId = user.Id });
});

app.MapMethods("/api/ads/postback", new[] { "GET", "POST" }, async (HttpContext context, IDbContextFactory<VioletDbContext> factory, IConfiguration config) =>
{
    var secret = config["Ads:PostbackSecret"] ?? "";
    if (string.IsNullOrWhiteSpace(secret))
        return Results.BadRequest(new { error = "not configured" });
    var provided = context.Request.Query["secret"].ToString();
    if (string.IsNullOrEmpty(provided)) provided = context.Request.Headers["X-Postback-Secret"].ToString();
    if (!FixedTimeTextEquals(provided ?? "", secret))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var userIdStr = context.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userIdStr)) userIdStr = context.Request.Query["uid"].ToString();
    if (string.IsNullOrWhiteSpace(userIdStr)) userIdStr = context.Request.Query["sub_id"].ToString();
    if (!Guid.TryParse(userIdStr, out var userId))
        return Results.BadRequest(new { error = "missing or invalid userId" });

    var required = Math.Max(1, config.GetValue<int?>("Ads:RequiredTasks") ?? 4);
    await using var db = await factory.CreateDbContextAsync();
    var credit = await db.AdCredits.SingleOrDefaultAsync(c => c.UserId == userId);
    if (credit is null)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return Results.NotFound(new { error = "user not found" });
        credit = new AdCredit { UserId = userId };
        db.AdCredits.Add(credit);
    }
    // Not: benzersiz işlem/click kimliği saklanmadığı için tekrar eden postback'ler tam olarak ayıklanamayabilir.
    // Mümkünse Adsterra'da her tamamlanan işlem için tekil postback yapılandırın.
    credit.CompletedTasks += 1;
    while (credit.CompletedTasks >= required)
    {
        credit.CompletedTasks -= required;
        credit.AvailableCredits += 1;
    }
    credit.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true, completedTasks = credit.CompletedTasks, availableCredits = credit.AvailableCredits, requiredTasks = required });
});

app.MapPost("/api/ads/redeem", async (HttpContext context, IDbContextFactory<VioletDbContext> factory, IConfiguration config) =>
{
    if (!HasSameOrigin(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var user = await CurrentUser(context, factory);
    if (user is null) return Results.Json(new { error = "Giriş gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
    var rewardHours = Math.Max(1, config.GetValue<int?>("Ads:RewardHours") ?? 24);
    await using var db = await factory.CreateDbContextAsync();
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var credit = await db.AdCredits.SingleOrDefaultAsync(c => c.UserId == user.Id);
    if (credit is null || credit.AvailableCredits <= 0)
        return Results.BadRequest(new { error = "Kullanılabilir ödül kredisi yok. Gerekli reklam görevlerini tamamlayın." });
    var account = await db.Users.SingleAsync(u => u.Id == user.Id);
    if (account.ProLifetime) return Results.BadRequest(new { error = "Hesabınız zaten sınırsız Pro." });
    var start = account.ProExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow ? expiry : DateTimeOffset.UtcNow;
    account.ProExpiresAt = start.AddHours(rewardHours);
    credit.AvailableCredits -= 1;
    credit.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return Results.Ok(new { ok = true, proExpiresAt = account.ProExpiresAt, rewardHours, availableCredits = credit.AvailableCredits });
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult AdminRequired() => Results.Json(new { error = "Admin oturumu gerekli." }, statusCode: StatusCodes.Status401Unauthorized);
static bool IsProUser(UserAccount user) => user.ProLifetime || (user.ProExpiresAt.HasValue && user.ProExpiresAt > DateTimeOffset.UtcNow);
static bool IsAdminUser(UserAccount? user, bool adminSession) => adminSession || string.Equals(user?.Role, "ADMIN", StringComparison.OrdinalIgnoreCase) || string.Equals(user?.Email, "alponline77@gmail.com", StringComparison.OrdinalIgnoreCase);
static object AccountView(UserAccount user) => new {
    authenticated = true,
    user = new {
        id = user.Id,
        email = user.Email,
        displayName = user.DisplayName,
        image = user.ProfileImage,
        role = user.Role,
        proExpiresAt = user.ProExpiresAt,
        proLifetime = user.ProLifetime,
        isPro = IsProUser(user)
    }
};
static object KeyView(LicenseKey key) => new { code = key.Code, planCode = key.PlanCode, months = key.Months, status = key.Status, createdAt = key.CreatedAt, note = key.Note, redeemedAt = key.RedeemedAt };

static async Task<T?> ReadJson<T>(HttpContext context)
{
    try { return await context.Request.ReadFromJsonAsync<T>(); }
    catch (JsonException) { return default; }
}

static bool IsEmail(string email)
{
    try { return new System.Net.Mail.MailAddress(email).Address == email && email.Contains('.'); }
    catch (FormatException) { return false; }
}

static (string Salt, string Hash) HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
}

static bool VerifyPassword(string password, string saltText, string hashText, int iterations)
{
    try
    {
        var salt = Convert.FromBase64String(saltText);
        var expected = Convert.FromBase64String(hashText);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    catch (FormatException) { return false; }
}

static bool FixedTimeTextEquals(string left, string right)
{
    var a = System.Text.Encoding.UTF8.GetBytes(left);
    var b = System.Text.Encoding.UTF8.GetBytes(right);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

static bool HasSameOrigin(HttpContext context)
{
    var origin = context.Request.Headers["Origin"].ToString();
    return string.IsNullOrEmpty(origin) || (Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
        && string.Equals(parsed.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase));
}

static async Task SignInUser(HttpContext context, UserAccount user)
{
    await context.Session.LoadAsync();
    context.Session.SetString("userId", user.Id.ToString());
}

static async Task<UserAccount?> CurrentUser(HttpContext context, IDbContextFactory<VioletDbContext> factory)
{
    await context.Session.LoadAsync();
    if (!Guid.TryParse(context.Session.GetString("userId"), out var id)) return null;
    await using var db = await factory.CreateDbContextAsync();
    return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
}

static async Task<bool> IsAdmin(HttpContext context, IDbContextFactory<VioletDbContext>? factory = null)
{
    await context.Session.LoadAsync();
    if (!string.IsNullOrEmpty(context.Session.GetString("admin"))) return true;
    if (factory is null) return false;
    var user = await CurrentUser(context, factory);
    return IsAdminUser(user, false);
}

static bool TryGetAllowedMediaUri(string rawUrl, out Uri mediaUri)
{
    mediaUri = null!;
    if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(parsed.UserInfo)) return false;
    var host = parsed.Host.ToLowerInvariant();
    var allowed = new[] { "youtube.com", "youtu.be", "instagram.com", "tiktok.com", "x.com", "twitter.com" };
    if (!allowed.Any(domain => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))) return false;
    mediaUri = parsed;
    return true;
}

static string DetectPlatformName(Uri mediaUri)
{
    var host = mediaUri.Host.ToLowerInvariant();
    if (host.Contains("youtube") || host.Contains("youtu.be")) return "YouTube";
    if (host.Contains("instagram")) return "Instagram";
    if (host.Contains("tiktok")) return "TikTok";
    if (host.Contains("x.com") || host.Contains("twitter")) return "X";
    return "Video";
}

static async Task<VideoFormatInfo> ResolveVideoFormatsAsync(Uri mediaUri, string contentRoot, ILogger logger, CancellationToken cancellationToken)
{
    var executable = FindYtDlp(contentRoot);
    if (executable is null) return new([], "Video çözümleyici (yt-dlp) bulunamadı.");
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8
    };
    EnsureToolPaths(start);
    foreach (var arg in BuildYtDlpCommonArgs()) start.ArgumentList.Add(arg);
    foreach (var arg in new[] { "--dump-single-json", "--skip-download", "--no-playlist", "--", mediaUri.AbsoluteUri }) start.ArgumentList.Add(arg);
    try
    {
        using var process = Process.Start(start);
        if (process is null) return new([], "Video çözümlenemedi.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var killResolve = timeout.Token.Register(() => KillQuietly(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = (await outputTask ?? "").Trim();
        var stderr = (await errorTask ?? "").Trim();
        if (process.ExitCode != 0)
        {
            var msg = FriendlyYtDlpError(string.IsNullOrWhiteSpace(stderr) ? output : stderr);
            logger.LogWarning("yt-dlp resolve failed ({Host}, exit {Code}): {Details}", mediaUri.Host, process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? output : stderr);
            return new([], msg);
        }
        if (string.IsNullOrWhiteSpace(output))
            return new([], "Çözümleyici boş yanıt döndürdü. Bağlantıyı ve interneti kontrol edin.");

        var jsonText = ExtractJsonObject(output);
        if (jsonText is null)
        {
            logger.LogWarning("yt-dlp resolve returned non-JSON output for {Host}: {Preview}", mediaUri.Host, output.Length > 300 ? output[..300] : output);
            if (!string.IsNullOrWhiteSpace(stderr)) return new([], FriendlyYtDlpError(stderr));
            return new([], "Çözümleyici format yanıtı okunamadı. yt-dlp güncel olmayabilir veya site erişimi engellenmiş olabilir.");
        }

        using var json = JsonDocument.Parse(jsonText);
        var heights = json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array
            ? formats.EnumerateArray()
                .Select(f =>
                {
                    if (f.ValueKind != JsonValueKind.Object) return 0;
                    if (!f.TryGetProperty("height", out var heightElement) || heightElement.ValueKind != JsonValueKind.Number || !heightElement.TryGetInt32(out var height)) return 0;
                    if (f.TryGetProperty("vcodec", out var codec) && codec.ValueKind == JsonValueKind.String &&
                        string.Equals(codec.GetString(), "none", StringComparison.OrdinalIgnoreCase)) return 0;
                    return height;
                })
                .Where(h => h > 0)
                .Distinct().OrderByDescending(h => h).ToArray()
            : [];
        var qualities = heights.Select(h => new VideoQuality(QualityLabel(h), h)).ToArray();
        return new(qualities, qualities.Length == 0 ? "Bu video için indirilebilir video formatı bulunamadı." : null);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new([], "Video çözümleme zaman aşımına uğradı."); }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or Win32Exception)
    {
        logger.LogError(ex, "Could not read yt-dlp format data for {Host}.", mediaUri.Host);
        return ex switch
        {
            JsonException => new([], "Çözümleyici format yanıtı okunamadı. yt-dlp aracını güncellemeyi deneyin (tools/yt-dlp.exe)."),
            InvalidOperationException => new([], "Çözümleyici yanıtı okunamadı."),
            _ => new([], "yt-dlp başlatılamadı; tools klasöründeki dosyaları kontrol edin.")
        };
    }
}

static string? ExtractJsonObject(string output)
{
    var start = output.IndexOf('{');
    if (start < 0) return null;
    var end = output.LastIndexOf('}');
    if (end <= start) return null;
    var candidate = output[start..(end + 1)];
    try { using var _ = JsonDocument.Parse(candidate); return candidate; } catch { }
    // try to find a valid JSON object by trimming trailing garbage
    for (int i = end; i > start; i--)
    {
        if (output[i] != '}') continue;
        var c = output[start..(i + 1)];
        try { using var _ = JsonDocument.Parse(c); return c; } catch { }
    }
    return null;
}

static void KillQuietly(Process process)
{
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
}

static void TryDeleteFile(string? path)
{
    try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
}

static string[] BuildYtDlpCommonArgs()
{
    var args = new List<string> { "--ignore-config", "--no-warnings", "--no-update" };
    // Only request Node runtime if available; forcing it on systems without Node can break extraction.
    if (IsCommandAvailable("node")) { args.Add("--js-runtimes"); args.Add("node"); args.Add("--remote-components"); args.Add("ejs:github"); }
    return args.ToArray();
}

static bool IsCommandAvailable(string name)
{
    try
    {
        var psi = new ProcessStartInfo(name, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(1500);
        return true;
    }
    catch { return false; }
}

static void EnsureToolPaths(ProcessStartInfo start)
{
    try
    {
        var path = start.EnvironmentVariables.ContainsKey("PATH") ? (start.EnvironmentVariables["PATH"] ?? "") : (Environment.GetEnvironmentVariable("PATH") ?? "");
        var extras = new[] { @"C:\ffmpeg\bin", Path.Combine(AppContext.BaseDirectory, "tools"), Path.Combine(Directory.GetCurrentDirectory(), "tools") };
        foreach (var ex in extras)
        {
            if (Directory.Exists(ex) && !path.Contains(ex, StringComparison.OrdinalIgnoreCase))
                path = ex + ";" + path;
        }
        start.EnvironmentVariables["PATH"] = path;
    }
    catch { }
}

static string FriendlyDownloadError(string? details)
{
    var text = (details ?? "").Trim();
    var error = text.ToLowerInvariant();
    if (text.Length == 0) return "Video indirilemedi. Bağlantıyı ve videonun erişilebilir olduğunu kontrol et.";
    if (error.Contains("requested format is not available")) return "YouTube bu çözünürlük için indirilebilir akış vermedi. Bir alt kaliteyi dene.";
    if (error.Contains("ffmpeg") && (error.Contains("not installed") || error.Contains("not found"))) return "FFmpeg bulunamadı. Yüksek kaliteler video ve sesi birleştirmek için FFmpeg ister; kurup PATH'e ekledikten sonra Visual Studio'yu yeniden başlat.";
    if (error.Contains("javascript") || error.Contains("ejs") || error.Contains("js runtime")) return "YouTube çözümlemesi için Node.js bileşeni çalışmadı. Node.js kurulu ve PATH içinde olmalı.";
    if (error.Contains("no space left") || error.Contains("errno 28")) return "Diskte yeterli yer yok.";
    return text.Length > 500 ? text[^500..] : text;
}

static string? FindYtDlp(string contentRoot)
{
    var executable = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
    if (File.Exists(executable)) return executable;
    executable = Path.Combine(contentRoot, "tools", "yt-dlp.exe");
    return File.Exists(executable) ? executable : null;
}

static string GetDownloadsDirectory()
{
    if (OperatingSystem.IsWindows())
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var configured = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
        if (!string.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured);
    }
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

static string QualityLabel(int height) => height switch
{
    >= 2160 => "4K",
    >= 1440 => "2K",
    _ => $"{height}p"
};

static string FriendlyYtDlpError(string? details)
{
    var error = (details ?? "").ToLowerInvariant();
    if (error.Contains("resolve") || error.Contains("getaddrinfo") || error.Contains("timed out") || error.Contains("connection") || error.Contains("network")) return "Video sitesine bağlanılamadı. İnternet erişimini kontrol edip tekrar dene.";
    if (error.Contains("javascript") || error.Contains("ejs") || error.Contains("runtime")) return "YouTube çözümlemesi için Node.js bileşeni çalışmadı. Node.js kurulu ve PATH içinde olmalı.";
    if (error.Contains("unsupported url") || error.Contains("no suitable extractor")) return "Bu bağlantı desteklenen video sitelerinden biri değil.";
    if (error.Contains("login") || error.Contains("sign in") || error.Contains("private video") || error.Contains("members-only")) return "Video özel veya giriş gerektiriyor; herkese açık bir bağlantı dene.";
    return "Bu video için kalite listesi alınamadı. Video herkese açık olmalı ve desteklenen bir siteden gelmeli.";
}

static async Task<VideoMetadata> ResolveVideoMetadataAsync(Uri mediaUri, HttpClient client, string? instagramToken, CancellationToken cancellationToken)
{
    var host = mediaUri.Host.ToLowerInvariant();
    var provider = host switch
    {
        "youtu.be" or "youtube.com" or "www.youtube.com" or "m.youtube.com" => "YouTube",
        "tiktok.com" or "www.tiktok.com" or "vm.tiktok.com" => "TikTok",
        "instagram.com" or "www.instagram.com" => "Instagram",
        "x.com" or "www.x.com" or "twitter.com" or "www.twitter.com" => "X",
        _ => "Video"
    };
    var fallbackTitle = provider == "Video" ? Path.GetFileNameWithoutExtension(mediaUri.AbsolutePath.TrimEnd('/')) : $"{provider} videosu";
    if (string.IsNullOrWhiteSpace(fallbackTitle)) fallbackTitle = "Video bağlantısı";
    string? author = null; string? title = null; string? thumbnail = null; string? endpoint = null;

    if (provider == "YouTube") endpoint = $"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(mediaUri.AbsoluteUri)}";
    else if (provider == "TikTok") endpoint = $"https://www.tiktok.com/oembed?url={Uri.EscapeDataString(mediaUri.AbsoluteUri)}";
    else if (provider == "X") endpoint = $"https://publish.twitter.com/oembed?omit_script=true&url={Uri.EscapeDataString(mediaUri.AbsoluteUri)}";
    else if (provider == "Instagram" && !string.IsNullOrWhiteSpace(instagramToken))
        endpoint = $"https://graph.facebook.com/v27.0/instagram_oembed?url={Uri.EscapeDataString(mediaUri.AbsoluteUri)}&access_token={Uri.EscapeDataString(instagramToken)}";

    if (endpoint is not null)
    {
        try
        {
            using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var data = await JsonSerializer.DeserializeAsync<OEmbedVideo>(stream, JsonOptions.Web, cancellationToken);
                title = CleanMetadata(data?.Title);
                author = CleanMetadata(data?.AuthorName);
                if (IsSafeThumbnail(data?.ThumbnailUrl)) thumbnail = data!.ThumbnailUrl;
                if (provider == "X" && title is null) title = TextFromEmbedHtml(data?.Html);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { }
    }

    if (provider == "Instagram" && endpoint is null)
    {
        try
        {
            using var response = await client.GetAsync(mediaUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                title = ReadMeta(html, "og:title") ?? ReadMeta(html, "twitter:title");
                var image = ReadMeta(html, "og:image") ?? ReadMeta(html, "twitter:image");
                if (IsSafeThumbnail(image)) thumbnail = image;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
    }

    if (provider == "YouTube" && thumbnail is null)
    {
        var id = GetYouTubeId(mediaUri);
        if (id is not null) thumbnail = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
    }
    return new VideoMetadata(title ?? fallbackTitle, provider, author, thumbnail);
}

static string? GetYouTubeId(Uri uri)
{
    var host = uri.Host.ToLowerInvariant();
    var id = host == "youtu.be" ? uri.AbsolutePath.Trim('/') :
        uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)
            : System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
    return id is not null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{6,20}$") ? id : null;
}

static string? CleanMetadata(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    var clean = Regex.Replace(System.Net.WebUtility.HtmlDecode(value), "<[^>]+>", " ").Trim();
    return clean.Length > 240 ? clean[..240] : clean;
}

static string? TextFromEmbedHtml(string? html)
{
    if (string.IsNullOrWhiteSpace(html)) return null;
    return CleanMetadata(Regex.Replace(html, "<script[^>]*>[\\s\\S]*?</script>|<[^>]+>", " ", RegexOptions.IgnoreCase));
}

static string? ReadMeta(string html, string property)
{
    var escaped = Regex.Escape(property);
    var patterns = new[]
    {
        $"<meta[^>]*(?:property|name)\\s*=\\s*['\\\"]{escaped}['\\\"][^>]*content\\s*=\\s*['\\\"]([^'\\\"]+)['\\\"][^>]*>",
        $"<meta[^>]*content\\s*=\\s*['\\\"]([^'\\\"]+)['\\\"][^>]*(?:property|name)\\s*=\\s*['\\\"]{escaped}['\\\"][^>]*>"
    };
    foreach (var pattern in patterns)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success) return CleanMetadata(match.Groups[1].Value);
    }
    return null;
}

static bool IsSafeThumbnail(string? value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https") return false;
    var host = uri.Host.ToLowerInvariant();
    return host == "i.ytimg.com" || host.EndsWith(".ytimg.com") || host.EndsWith(".tiktokcdn.com") || host.EndsWith(".tiktokcdn-us.com") || host == "pbs.twimg.com" || host.EndsWith(".cdninstagram.com") || host.EndsWith(".fbcdn.net");
}

sealed class AdminCredentials { public string Username { get; set; } = ""; public string Salt { get; set; } = ""; public string PasswordHash { get; set; } = ""; public int Iterations { get; set; } = 210000; }
sealed class LoginRequest { public string? Username { get; set; } public string? Email { get; set; } public string? Password { get; set; } }
sealed class RegisterRequest { public string? Email { get; set; } public string? DisplayName { get; set; } public string? Password { get; set; } }
sealed class CreateKeysRequest { public int Months { get; set; } public int Quantity { get; set; } public string? PlanCode { get; set; } public string? Note { get; set; } }
sealed class CreateOrderRequest { public int Months { get; set; } public string? PlanCode { get; set; } }
sealed class RedeemRequest { public string? Code { get; set; } public string? DeviceId { get; set; } }
sealed class DownloadRequest { public string? Url { get; set; } public string? Mode { get; set; } public int Height { get; set; } public string? Title { get; set; } public string? Thumbnail { get; set; } public string? Platform { get; set; } }
sealed class PurchaseMessageRequest { public string? Contact { get; set; } public string? Package { get; set; } public string? Message { get; set; } }
sealed class UpdateStatusRequest { public string? Status { get; set; } }
sealed class VideoMetadata(string title, string provider, string? author, string? thumbnailUrl)
{
    public string Title { get; } = title;
    public string Provider { get; } = provider;
    public string? Author { get; } = author;
    public string? ThumbnailUrl { get; } = thumbnailUrl;
}
sealed record VideoQuality(string Label, int Height);
sealed record VideoFormatInfo(VideoQuality[] Qualities, string? Error);
sealed class OEmbedVideo { public string? Title { get; set; } public string? AuthorName { get; set; } public string? ThumbnailUrl { get; set; } public string? Html { get; set; } }
static class JsonOptions { public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true }; }
