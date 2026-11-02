using GridWise.Catalog.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Catalog"))
        .UseSnakeCaseNamingConvention());

var app = builder.Build();

app.Run();
