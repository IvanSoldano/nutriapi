# Recipe Sheet: ASP.NET Core Minimal API from Scratch

A practical, zero-fluff cheat sheet for building and scaling clean Minimal APIs in .NET.

---

## Part 1: Starting from Scratch (Empty Folder)

Open your terminal in an empty directory and run:

```bash
# 1. Create a clean Minimal API (no template clutter or WeatherForecast boilerplate)
dotnet new web

# 2. Add PostgreSQL driver (official, high-performance)
dotnet add package Npgsql

# 3. (Optional) Add OpenAPI documentation support
dotnet add package Microsoft.AspNetCore.OpenApi

# 4. Verify compilation
dotnet build
```

> **Why `dotnet new web`?**  
> `dotnet new web` generates only `Program.cs` and `appsettings.json` without any obsolete controllers or mock weather forecast files.

---

## Part 2: Recommended Folder Structure

Organize by **Data** and **Endpoints**:

```
MyProject/
├── Endpoints/              # HTTP routes grouped by domain feature
│   └── RegistrosEndpoints.cs
├── Repositories/           # Raw SQL queries & DTO records
│   └── RegistrosRepository.cs
├── appsettings.json        # Connection strings and config
├── Program.cs              # Lean entry point (DI + route mapping)
└── requests.http           # Quick testing inside your IDE
```

---

## Part 3: Step-by-Step Recipe to Add New Endpoints & Features

Follow this consistent 4-step loop every time you add a query or an endpoint:

```
[1. DTO / Model] ──► [2. Repository SQL] ──► [3. Endpoint Route] ──► [4. Wire in Program.cs (if new)]
```

### Step 1: Define the DTO (Return Type)
In `Repositories/` (or a `Models/` file), create a C# `record` representing the columns your SQL query will produce:

```csharp
public record AlimentoItem(int Id, string Nombre);
```

---

### Step 2: Add the SQL Query in your Repository
In `Repositories/YourRepository.cs`, add the database method:

```csharp
public async Task<List<AlimentoItem>> ObtenerTodosAsync()
{
    const string sql = "SELECT alimento_id, nombre FROM alimento ORDER BY nombre ASC;";

    await using var dataSource = NpgsqlDataSource.Create(_connectionString);
    await using var cmd = dataSource.CreateCommand(sql);

    var list = new List<AlimentoItem>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        list.Add(new AlimentoItem(
            reader.GetInt32(0),
            reader.GetString(1)
        ));
    }
    return list;
}
```

---

### Step 3: Expose the Route in your `Endpoints/` File
In `Endpoints/YourEndpoints.cs`, map the HTTP verb and URL directly:

```csharp
public static class AlimentosEndpoints
{
    public static void MapAlimentosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alimentos");

        // GET /api/alimentos
        group.MapGet("/", async (AlimentosRepository repo) =>
        {
            var data = await repo.ObtenerTodosAsync();
            return Results.Ok(data);
        });

        // POST /api/alimentos (receiving a JSON body)
        group.MapPost("/", async (AlimentosRepository repo, NuevoAlimentoDto dto) =>
        {
            var id = await repo.CrearAsync(dto);
            return Results.Created($"/api/alimentos/{id}", new { id });
        });
    }
}
```

---

### Step 4: Register in `Program.cs` (Only when creating a new Repository/Group)

If you created a new Repository or a new Endpoint group, add one line for each:

```csharp
// 1. Register repository for Dependency Injection
builder.Services.AddScoped<AlimentosRepository>();

// 2. Map the routes before app.Run()
app.MapAlimentosEndpoints();
```

---

## Part 4: Cheat Sheet for Endpoint Types

| What you want to do | Minimal API Syntax |
| :--- | :--- |
| **Get list or single item** | `group.MapGet("/", async (Repo r) => ...)` |
| **Query Parameter** (`?fecha=...`) | `group.MapGet("/", async (Repo r, string? fecha) => ...)` |
| **Route Parameter** (`/api/items/5`) | `group.MapGet("/{id:int}", async (Repo r, int id) => ...)` |
| **Create (POST with JSON body)** | `group.MapPost("/", async (Repo r, CreateDto body) => ...)` |
| **Delete** | `group.MapDelete("/{id:int}", async (Repo r, int id) => ...)` |
| **Return HTTP 200 OK** | `return Results.Ok(data);` |
| **Return HTTP 201 Created** | `return Results.Created($"/api/.../{id}", data);` |
| **Return HTTP 404 Not Found** | `return Results.NotFound(new { message = "Item not found" });` |
| **Return HTTP 400 / 500 Error** | `return Results.Problem(detail: ex.Message, statusCode: 500);` |

---

## Part 5: Useful CLI Commands

```bash
# Build to check for errors
dotnet build

# Run the API locally
dotnet run

# Run with hot reload (auto-reloads on file save)
dotnet watch
```
