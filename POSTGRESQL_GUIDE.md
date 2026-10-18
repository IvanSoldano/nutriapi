# Guía Práctica: ASP.NET Core + PostgreSQL (Prisma Cloud)

Esta guía resume todo el flujo de trabajo para conectar tu API de ASP.NET Core con una base de datos PostgreSQL alojada en Prisma (o cualquier otro proveedor como Supabase, Neon o Railway), cómo estructurar el código por capas y cómo agregar nuevos endpoints con consultas SQL.

---

## 1. Paquetes y Comandos a Ejecutar

### Paquete Necesario (Driver Oficial de PostgreSQL)
En la raíz del proyecto ejecuta:

```bash
# Driver de PostgreSQL oficial, rápido y de bajo nivel
dotnet add package Npgsql
```

*(Opcional recomendado para escribir menos código al mapear SQL):*
```bash
# Dapper simplifica el mapeo de SQL a objetos en una sola línea
dotnet add package Dapper
```

### Comandos habituales de ejecución
```bash
# Compilar el proyecto para verificar errores
dotnet build

# Iniciar la API con el perfil HTTP (puerto configurado en launchSettings.json, ej: 5241)
dotnet run --launch-profile http
```

---

## 2. Configuración: Dónde guardar la Conexión

En ASP.NET Core, las conexiones no se leen con scripts sueltos, sino mediante la configuración centralizada de la aplicación.

### En `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=db.prisma.io;Port=5432;Database=postgres;Username=TU_USUARIO;Password=TU_PASSWORD;SSL Mode=Require;Trust Server Certificate=true;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

> **Parámetros clave de Prisma PostgreSQL:**
> - `SSL Mode=Require`: Prisma exige conexión cifrada por SSL.
> - `Trust Server Certificate=true`: Evita errores de validación de certificados intermedios.

---

## 3. Arquitectura y Capas que Debes Tocar

Para mantener el código limpio, escalable y mantenible, dividimos el flujo en 3 capas simples:

```
[Cliente / HTTP Request]
          │
          ▼
┌────────────────────────────────────────┐
│  1. Capa de Presentación (Endpoints)   │ -> Program.cs o Controllers/
└───────────────────┬────────────────────┘
                    │ llama a métodos
                    ▼
┌────────────────────────────────────────┐
│  2. Capa de Datos (Repository)         │ -> Repositories/
└───────────────────┬────────────────────┘
                    │ ejecuta SQL
                    ▼
┌────────────────────────────────────────┐
│  3. Modelos / DTOs                     │ -> Models/ o DTOs (Records)
└────────────────────────────────────────┘
```

---

## 4. Guía Paso a Paso: Cómo Agregar un Nuevo Endpoint con SQL

Supongamos que deseas agregar una consulta SQL como la de nutrientes consumidos.

### Paso 1: Definir el Modelo de Respuesta (DTO / Record)
Crea una estructura ligera que represente las columnas que devuelve tu consulta.

*Ubicación sugerida:* En la misma clase del repositorio o en una carpeta `DTOs/`:
```csharp
public record NutrienteConsumo(string Nutriente, string Unidad, decimal TotalConsumido);
```

---

### Paso 2: Escribir el Método en el Repositorio (`Repositories/`)
En tu repositorio (`Repositories/NutrientesRepository.cs` o `Repositories/WeatherForecastRepository.cs`):

1. Inyecta `IConfiguration` para obtener la cadena de conexión.
2. Usa `NpgsqlDataSource` para manejar el pool de conexiones de manera eficiente.
3. **Usa parámetros con `@`** para evitar inyección SQL (SQL Injection).

```csharp
using Npgsql;

namespace test.Repositories;

public class NutrientesRepository
{
    private readonly string _connectionString;

    public NutrientesRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Cadena 'DefaultConnection' no encontrada.");
    }

    public async Task<List<NutrienteConsumo>> ObtenerConsumoPorFechaAsync(string fecha)
    {
        const string sql = """
            SELECT
              n.nombre AS nutriente,
              n.unidad,
              SUM(an.cantidad * (ra.cantidad / 100.0)) AS total_consumido
            FROM registro_alimentario ra
            JOIN alimento_nutriente an ON ra.alimento_id = an.alimento_id
            JOIN nutriente n ON an.nutriente_id = n.nutriente_id
            WHERE ra.fecha = @fecha::date
            AND n.nutriente_id IN (1, 2, 3)
            GROUP BY n.nombre, n.unidad;
        """;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        
        // Parámetro seguro
        command.Parameters.AddWithValue("fecha", fecha);

        var resultados = new List<NutrienteConsumo>();
        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            resultados.Add(new NutrienteConsumo(
                Nutriente: reader.GetString(reader.GetOrdinal("nutriente")),
                Unidad: reader.GetString(reader.GetOrdinal("unidad")),
                TotalConsumido: Convert.ToDecimal(reader["total_consumido"])
            ));
        }

        return resultados;
    }
}
```

---

### Paso 3: Registrar el Repositorio en Inyección de Dependencias (`Program.cs`)
En `Program.cs`, antes de `builder.Build()`:

```csharp
builder.Services.AddScoped<NutrientesRepository>();
```

---

### Paso 4: Crear el Endpoint HTTP en `Program.cs` o Controller

#### Opción A: Minimal API (rápido y directo en `Program.cs`)
```csharp
app.MapGet("/api/nutrientes", async (NutrientesRepository repo, string? fecha) =>
{
    try
    {
        var fechaConsulta = string.IsNullOrWhiteSpace(fecha) ? "2026-10-15" : fecha;
        var datos = await repo.ObtenerConsumoPorFechaAsync(fechaConsulta);
        return Results.Ok(new { success = true, fecha = fechaConsulta, datos });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Error al consultar la base de datos");
    }
});
```

#### Opción B: En un Controlador (`Controllers/NutrientesController.cs`)
```csharp
[ApiController]
[Route("api/[controller]")]
public class NutrientesController : ControllerBase
{
    private readonly NutrientesRepository _repo;

    public NutrientesController(NutrientesRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? fecha)
    {
        var fechaConsulta = string.IsNullOrWhiteSpace(fecha) ? "2026-10-15" : fecha;
        var datos = await _repo.ObtenerConsumoPorFechaAsync(fechaConsulta);
        return Ok(datos);
    }
}
```

---

### Paso 5: Probar el Endpoint con `test.http`

Agrega la petición en `test.http`:

```http
### Consultar consumo de nutrientes
GET http://localhost:5241/api/nutrientes?fecha=2026-10-15
Accept: application/json
```

---

## 5. Buenas Prácticas al Escribir SQL en PostgreSQL + .NET

| Práctica | Explicación |
| :--- | :--- |
| **`100.0` en divisiones** | En PostgreSQL, si `cantidad` es `int`, `cantidad / 100` hace división entera (elimina los decimales). Usa `100.0` o `100::numeric` para conservar la precisión decimal. |
| **Parámetros (`@param`)** | **Nunca** concatenes cadenas (`$"WHERE fecha = '{fecha}'"`). Siempre usa `command.Parameters.AddWithValue("fecha", fecha)` para prevenir inyecciones SQL. |
| **`await using`** | En C#, `await using` asegura que las conexiones (`NpgsqlDataSource`, `NpgsqlDataReader`, `NpgsqlCommand`) se liberen y regresen al pool de inmediato al terminar la consulta. |
| **`Convert.ToDecimal(reader["col"])`** | Los resultados agregados como `SUM(...)` en PostgreSQL se mapean a `numeric`. Usar `Convert.ToDecimal` evita errores de casteo si el tipo subyacente varía ligeramente. |

---

## 6. Alternativa con Dapper (Para escribir menos código)

Si instalas `Dapper` (`dotnet add package Dapper`), el Paso 2 se reduce a esto:

```csharp
using Dapper;
using Npgsql;

public async Task<IEnumerable<NutrienteConsumo>> ObtenerConsumoDapperAsync(string fecha)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    
    const string sql = """
        SELECT
          n.nombre AS Nutriente,
          n.unidad AS Unidad,
          SUM(an.cantidad * (ra.cantidad / 100.0)) AS TotalConsumido
        FROM registro_alimentario ra
        JOIN alimento_nutriente an ON ra.alimento_id = an.alimento_id
        JOIN nutriente n ON an.nutriente_id = n.nutriente_id
        WHERE ra.fecha = @Fecha::date
        AND n.nutriente_id IN (1, 2, 3)
        GROUP BY n.nombre, n.unidad;
    """;

    // Dapper mapea automáticamente las columnas SQL a las propiedades del Record
    return await connection.QueryAsync<NutrienteConsumo>(sql, new { Fecha = fecha });
}
```
