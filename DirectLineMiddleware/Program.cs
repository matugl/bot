using DirectLineMiddleware.Interfaces;
using DirectLineMiddleware.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Configuración de HttpClient para el bot externo
builder.Services.AddHttpClient<IExternalBotClient, ExternalBotClient>();

// Bot Framework: Adapter y el bot
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, CloudAdapter>();
builder.Services.AddSingleton<IBot, ProxyBot>();
builder.Services.AddHttpClient();
// Servicios propios
builder.Services.AddSingleton<IOmnichannelService, OmnichannelService>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRouting();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "Middleware Agents funcionando.");

app.Run();
