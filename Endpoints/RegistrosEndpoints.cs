using System.Security.Cryptography;
using System.Text;
using test.Repositories;

namespace test.Endpoints;

public static class RegistrosEndpoints
{
    public static void MapRegistrosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/registros")
            .AddEndpointFilter(async (context, next) =>
            {
                var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var expectedKey = config["Authentication:ApiKey"];

                if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var extractedKey))
                {
                    return Results.Json(new
                    {
                        success = false,
                        message = "Missing 'X-Api-Key' header."
                    }, statusCode: StatusCodes.Status401Unauthorized);
                }

                var expectedBytes = Encoding.UTF8.GetBytes(expectedKey ?? string.Empty);
                var extractedBytes = Encoding.UTF8.GetBytes(extractedKey.ToString());

                if (expectedBytes.Length == 0 || !CryptographicOperations.FixedTimeEquals(expectedBytes, extractedBytes))
                {
                    return Results.Json(new
                    {
                        success = false,
                        message = "Invalid API Key."
                    }, statusCode: StatusCodes.Status401Unauthorized);
                }

                return await next(context);
            });

        // GET /api/registros/test-db
        group.MapGet("/test-db", async (RegistrosRepository repo) =>
        {
            try
            {
                var dbVersion = await repo.TestConnectionAsync();
                return Results.Ok(new
                {
                    success = true,
                    message = "Successfully connected to PostgreSQL!",
                    databaseVersion = dbVersion
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Database connection failed");
            }
        });

        // GET /api/registros?fecha=2026-10-15&page=1&pageSize=10
        // Returns a paginated list of foods consumed
        group.MapGet("/", async (RegistrosRepository repo, string? fecha, int? page, int? pageSize) =>
        {
            try
            {
                var p = page ?? 1;
                var size = pageSize ?? 10;
                var result = await repo.GetRegistrosPaginadosAsync(fecha, p, size);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error querying food records");
            }
        });

        // GET /api/registros/nutrientes?fecha=2026-10-15
        // Returns the aggregated nutrients consumed that day
        group.MapGet("/nutrientes", async (RegistrosRepository repo, string? fecha) =>
        {
            try
            {
                var targetDate = string.IsNullOrWhiteSpace(fecha) ? "2026-10-15" : fecha;
                var nutrientes = await repo.GetNutrientesDelDiaAsync(targetDate);
                return Results.Ok(new
                {
                    success = true,
                    fecha = targetDate,
                    totalNutrientes = nutrientes.Count,
                    data = nutrientes
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error calculating nutrients");
            }
        });

        // GET /api/registros/nutrientes/rango?fechaInicio=2026-10-01&fechaFin=2026-10-15
        // PLACEHOLDER for you to implement: Aggregates nutrients over a date range
        group.MapGet("/nutrientes/rango", async (RegistrosRepository repo, string? fechaInicio, string? fechaFin) =>
        {
            if (string.IsNullOrWhiteSpace(fechaInicio) || string.IsNullOrWhiteSpace(fechaFin))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "Both 'fechaInicio' and 'fechaFin' query parameters are required (e.g. ?fechaInicio=2026-10-01&fechaFin=2026-10-15)."
                });
            }

            try
            {
                // =========================================================================
                // TODO (FOR YOU TO UNCOMMENT WHEN READY):
                // var data = await repo.GetNutrientesPorRangoFechaAsync(fechaInicio, fechaFin);
                // return Results.Ok(new { success = true, fechaInicio, fechaFin, totalNutrientes = data.Count, data });
                // =========================================================================

                return Results.Json(new
                {
                    message = "Placeholder endpoint. Implement GetNutrientesPorRangoFechaAsync in RegistrosRepository to activate this!"
                }, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error calculating nutrients for date range");
            }
        });

        // POST /api/registros
        // Creates a new food consumption entry
        group.MapPost("/", async (RegistrosRepository repo, GuardarRegistroDto dto) =>
        {
            try
            {
                var newId = await repo.CrearRegistroAsync(dto);
                return Results.Created($"/api/registros/{newId}", new
                {
                    success = true,
                    message = "Registro created successfully.",
                    id = newId
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error creating food record");
            }
        });

        // PUT /api/registros/{id}
        // Updates an existing food consumption entry
        group.MapPut("/{id:int}", async (RegistrosRepository repo, int id, GuardarRegistroDto dto) =>
        {
            try
            {
                var updated = await repo.ActualizarRegistroAsync(id, dto);
                if (!updated)
                {
                    return Results.NotFound(new
                    {
                        success = false,
                        message = $"Registro with ID {id} not found."
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    message = $"Registro {id} updated successfully."
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error updating food record");
            }
        });

        // DELETE /api/registros/{id}
        // Deletes a food consumption entry
        group.MapDelete("/{id:int}", async (RegistrosRepository repo, int id) =>
        {
            try
            {
                var deleted = await repo.EliminarRegistroAsync(id);
                if (!deleted)
                {
                    return Results.NotFound(new
                    {
                        success = false,
                        message = $"Registro with ID {id} not found."
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    message = $"Registro {id} deleted successfully."
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Error deleting food record");
            }
        });
    }
}
