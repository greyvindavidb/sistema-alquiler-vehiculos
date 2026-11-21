
using System.Security.Claims;
using System.Threading.RateLimiting;
using AlquilerVehiculos.Services;

using AlquilerVehiculos.Components;
using AlquilerVehiculos.Data;

using AlquilerVehiculos.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDbContext<AlquilerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AlquilerDb")));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/acceso-denegado";
        options.Cookie.Name = "AlquilerVehiculos.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idText, out var id))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AlquilerDbContext>();
            var usuario = await db.Usuario.AsNoTracking()
                .Include(u => u.IdRolNavigation)
                .FirstOrDefaultAsync(u => u.IdUsuario == id);
            if (usuario is null || !usuario.Activo ||
                !string.Equals(usuario.IdRolNavigation.Nombre,
                    context.Principal?.FindFirstValue(ClaimTypes.Role),
                    StringComparison.OrdinalIgnoreCase))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddDbContextFactory<AlquilerDbContext>(
    options => options.UseSqlServer(
        builder.Configuration.GetConnectionString("AlquilerDb")),
    ServiceLifetime.Scoped);


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<AlquilerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AlquilerDb")));

builder.Services.AddScoped<MultaService>();

var app = builder.Build();

// Solo desarrollo: primera cuenta administradora, mediante User Secrets.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AlquilerDbContext>();
    var usuarioInicial = app.Configuration["BootstrapAdmin:Usuario"];
    var claveInicial = app.Configuration["BootstrapAdmin:Contrasena"];
    if (!string.IsNullOrWhiteSpace(usuarioInicial) &&
        !string.IsNullOrWhiteSpace(claveInicial) &&
        !await db.Usuario.AnyAsync())
    {
        if (claveInicial.Length < 12)
            throw new InvalidOperationException("BootstrapAdmin:Contrasena necesita al menos 12 caracteres.");

        var rol = await db.Rol.FirstOrDefaultAsync(r => r.Nombre == "Administrador");
        if (rol is null)
        {
            rol = new Rol { Nombre = "Administrador" };
            db.Rol.Add(rol);
            await db.SaveChangesAsync();
        }
        var admin = new Usuario
        {
            NombreUsuario = usuarioInicial.Trim(),
            NombreCompleto = "Administrador inicial",
            IdRol = rol.IdRol,
            Activo = true,
            FechaCreacion = DateTime.Now
        };
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        admin.ContrasenaHash = hasher.HashPassword(admin, claveInicial);
        db.Usuario.Add(admin);
        await db.SaveChangesAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapPost("/auth/login", async (
    HttpContext context, IAntiforgery antiforgery, AlquilerDbContext db,
    IPasswordHasher<Usuario> hasher) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud inválida."); }

    var form = await context.Request.ReadFormAsync();
    var nombre = form["usuario"].ToString().Trim();
    var clave = form["contrasena"].ToString();
    var retorno = form["returnUrl"].ToString();
    if (nombre.Length == 0 || clave.Length == 0)
        return Results.LocalRedirect("/login?error=1");

    var usuario = await db.Usuario.Include(u => u.IdRolNavigation)
        .FirstOrDefaultAsync(u => u.NombreUsuario == nombre && u.Activo);
    var valido = usuario is not null &&
        hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, clave)
            != PasswordVerificationResult.Failed;

    if (!valido)
        return Results.LocalRedirect("/login?error=1");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, usuario!.IdUsuario.ToString()),
        new(ClaimTypes.Name, usuario.NombreCompleto),
        new(ClaimTypes.Role, usuario.IdRolNavigation.Nombre)
    };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims,
            CookieAuthenticationDefaults.AuthenticationScheme)));

    var destino = retorno.StartsWith('/') && !retorno.StartsWith("//") &&
                  !retorno.StartsWith("/\\") ? retorno : "/";
    return Results.LocalRedirect(destino);
}).AllowAnonymous().RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud inválida."); }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();
app.Run();
