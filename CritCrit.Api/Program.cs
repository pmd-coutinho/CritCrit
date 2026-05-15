using CritCrit.Api.Configuration;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);

builder.AddCritCritApiServices();

var app = builder.Build();

app.UseCritCritApiPipeline();

return await app.RunJasperFxCommands(args);
