using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication24.Data;
using WebApplication24.Hubs;
using WebApplication24.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Chat") ??
        "Server=(localdb)\\MSSQLLocalDB;Database=WebApplication24Chat;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"));
builder.Services.AddSingleton<IPasswordHasher<ChatUser>, PasswordHasher<ChatUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/register", async (RegisterRequest request, ChatDbContext db, IPasswordHasher<ChatUser> hasher, HttpContext http) =>
{
    var username = request.Username?.Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 24 ||
        username.Any(c => !char.IsLetterOrDigit(c) && c != '_') ||
        string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8 || request.Password.Length > 128)
        return Results.BadRequest(new { error = "Ім’я: 3–24 літери, цифри або _. Пароль: щонайменше 8 символів." });
    var normalized = username.ToUpperInvariant();
    if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized))
        return Results.Conflict(new { error = "Таке ім’я вже зайняте." });
    var user = new ChatUser { Username = username, NormalizedUsername = normalized };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    await SignIn(http, user);
    return Results.Ok(new { username = user.Username });
});

app.MapPost("/api/auth/login", async (LoginRequest request, ChatDbContext db, IPasswordHasher<ChatUser> hasher, HttpContext http) =>
{
    var normalized = request.Username?.Trim().ToUpperInvariant();
    var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        return Results.Json(new { error = "Ім’я користувача або пароль неправильні." }, statusCode: 401);
    await SignIn(http, user);
    return Results.Ok(new { username = user.Username });
});

app.MapPost("/api/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});
app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new { username = user.Identity!.Name }))
    .RequireAuthorization();

app.MapGet("/api/users", async (ChatDbContext db, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    return Results.Ok(await db.Users.Where(u => u.Id != me).OrderBy(u => u.Username)
        .Select(u => new { id = u.Id, username = u.Username }).ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/general-messages", async (ChatDbContext db) =>
    Results.Ok(await db.GeneralMessages.OrderBy(m => m.SentAt)
        .Join(db.Users, message => message.SenderId, user => user.Id,
            (message, user) => new { message.Id, message.SenderId, senderUsername = user.Username, message.Text, message.SentAt })
        .ToListAsync())).RequireAuthorization();

app.MapPost("/api/general-messages", async (GeneralMessageRequest request, ChatDbContext db, ClaimsPrincipal user, IHubContext<ChatHub> hub) =>
{
    var text = request.Text?.Trim();
    if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
        return Results.BadRequest(new { error = "Введіть повідомлення до 4000 символів." });
    var message = new GeneralMessage { SenderId = UserId(user), Text = text };
    db.GeneralMessages.Add(message);
    await db.SaveChangesAsync();
    var result = new { message.Id, message.SenderId, senderUsername = user.Identity!.Name, message.Text, message.SentAt };
    await hub.Clients.All.SendAsync("GeneralMessage", result);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/messages/{otherUserId:int}", async (int otherUserId, ChatDbContext db, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    if (!await db.Users.AnyAsync(u => u.Id == otherUserId)) return Results.NotFound();
    var messages = await db.Messages.Where(m =>
            (m.SenderId == me && m.RecipientId == otherUserId) || (m.SenderId == otherUserId && m.RecipientId == me))
        .OrderBy(m => m.SentAt).Select(m => new { m.Id, m.SenderId, m.RecipientId, m.Text, m.SentAt })
        .ToListAsync();
    return Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/api/messages", async (SendMessageRequest request, ChatDbContext db, ClaimsPrincipal user, IHubContext<ChatHub> hub) =>
{
    var me = UserId(user);
    var text = request.Text?.Trim();
    if (request.RecipientId == me || string.IsNullOrWhiteSpace(text) || text.Length > 4000)
        return Results.BadRequest(new { error = "Вкажіть іншого адресата й повідомлення до 4000 символів." });
    var recipient = await db.Users.FindAsync(request.RecipientId);
    if (recipient is null) return Results.NotFound(new { error = "Користувача не знайдено." });
    var message = new PrivateMessage { SenderId = me, RecipientId = recipient.Id, Text = text };
    db.Messages.Add(message);
    await db.SaveChangesAsync();
    var result = new { message.Id, message.SenderId, message.RecipientId, message.Text, message.SentAt };
    await hub.Clients.Group(ChatHub.UserGroup(me)).SendAsync("PrivateMessage", result);
    await hub.Clients.Group(ChatHub.UserGroup(recipient.Id)).SendAsync("PrivateMessage", result);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapDelete("/api/messages/{id:int}", async (int id, ChatDbContext db, ClaimsPrincipal user, IHubContext<ChatHub> hub) =>
{
    var message = await db.Messages.FindAsync(id);
    if (message is null) return Results.NotFound();
    var me = UserId(user);
    if (message.SenderId != me) return Results.Forbid();
    var recipientId = message.RecipientId;
    db.Messages.Remove(message);
    await db.SaveChangesAsync();
    await hub.Clients.Groups(new[] { ChatHub.UserGroup(me), ChatHub.UserGroup(recipientId) })
        .SendAsync("MessageDeleted", id);
    return Results.NoContent();
}).RequireAuthorization();

app.MapHub<ChatHub>("/chatHub").RequireAuthorization();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

static int UserId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
static Task SignIn(HttpContext http, ChatUser user) => http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    }, CookieAuthenticationDefaults.AuthenticationScheme)));

record RegisterRequest(string? Username, string? Password);
record LoginRequest(string? Username, string? Password);
record SendMessageRequest(int RecipientId, string? Text);
record GeneralMessageRequest(string? Text);
