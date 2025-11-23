using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Services;
using PicklePlay.Models; 
using PicklePlay.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IScheduleRepository, MySqlScheduleRepository>();

// Add Session services
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
});

// ⬇️ ADD SIGNALR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Database
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

// Configure EmailSettings from appsettings.json
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Configure PayPal from appsettings.json - ADD THIS
builder.Services.Configure<PayPalConfig>(builder.Configuration.GetSection("PayPal"));

// Custom Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ADD PAYMENT SERVICES - THESE ARE MISSING
builder.Services.AddScoped<IPaymentService, MockPaymentService>();
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddScoped<IEscrowService, EscrowService>();
// Add HttpClient for CAPTCHA validation
builder.Services.AddHttpClient();

// Add this line where you register other services (before builder.Build())
builder.Services.AddHostedService<ScheduleAutoEndService>();
builder.Services.AddHostedService<AutoReleaseEscrowService>();

// Add these lines where you register other services (before builder.Build())
builder.Services.AddScoped<RankAlgorithmService>();
builder.Services.AddScoped<RankMatchProcessingService>();
builder.Services.AddScoped<PicklePlay.Services.IAiPartnerService, PicklePlay.Services.AiPartnerService>();

var app = builder.Build();

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// CORRECT ORDER: Session -> Authentication -> Authorization
app.UseSession(); // Session FIRST
app.UseAuthentication(); 
app.UseAuthorization();

// ⬇️ MAP SIGNALR HUB
app.MapHub<ChatHub>("/chatHub");
app.MapHub<CommunityChatHub>("/communityChatHub");
app.MapHub<ScheduleChatHub>("/scheduleChatHub");

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();