using AgileConfig.Client;
using CertsServer;
using LettuceEncrypt.Extensions.AliDns;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("CERTS_");
builder.Configuration.AddAgileConfig((ConfigClientOptions options) => builder.Configuration.GetSection("AgileConfig")?.Bind(options));

builder.Services.AddOpenApiDocument().AddEndpointsApiExplorer().AddCors();

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddStore();
builder.Services.AddLettuceEncrypt(options =>
{
    options.AllowedChallengeTypes = LettuceEncrypt.Acme.ChallengeType.Dns01;
}).AddAliDns();

builder.Services.AddDbContext<CertsServerDbContext>(options =>
{
    switch (builder.Configuration.GetSection("Db").Value)
    {
        case "postgres":
            options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
            break;
        default:
            options.UseSqlite(builder.Configuration.GetConnectionString("Default"));
            break;
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

app.UseCors();
app.UseStaticFiles();
app.MapEndpoints().ProducesProblem(500).ProducesValidationProblem();

app.Run();
