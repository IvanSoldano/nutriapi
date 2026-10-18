using test.Endpoints;
using test.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Register dependencies
builder.Services.AddOpenApi();
builder.Services.AddScoped<RegistrosRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Map route groups
app.MapRegistrosEndpoints();

app.Run();
