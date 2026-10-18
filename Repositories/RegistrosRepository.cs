using Npgsql;

namespace test.Repositories;

public record NutrienteConsumo(string Nutriente, string Unidad, decimal TotalConsumido);

public record AlimentoConsumido(
    int RegistroAlimentarioId,
    string Alimento,
    TimeOnly Hora,
    decimal CantidadGramos
);

public record GuardarRegistroDto(
    int AlimentoId,
    DateOnly Fecha,
    TimeOnly Hora,
    decimal CantidadGramos
);

public record PaginatedList<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
);

public class RegistrosRepository
{
    private readonly string _connectionString;

    public RegistrosRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");
    }

    /// <summary>
    /// Executes a simple query to verify connection to PostgreSQL.
    /// </summary>
    public async Task<string> TestConnectionAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand("SELECT version();");
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? "Connected, but no version string returned.";
    }

    /// <summary>
    /// Gets the list of foods consumed on a specific date.
    /// </summary>
    public async Task<List<AlimentoConsumido>> GetAlimentosDelDiaAsync(string fecha = "2026-10-15")
    {
        const string sql = """
            SELECT
              ra.registro_alimentario_id,
              a.nombre AS alimento,
              ra.hora,
              ra.cantidad AS cantidad_gramos
            FROM registro_alimentario ra
            JOIN alimento a ON ra.alimento_id = a.alimento_id
            WHERE ra.fecha = @fecha::date
            ORDER BY ra.hora ASC;
        """;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("fecha", fecha);

        var list = new List<AlimentoConsumido>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new AlimentoConsumido(
                RegistroAlimentarioId: reader.GetInt32(reader.GetOrdinal("registro_alimentario_id")),
                Alimento: reader.GetString(reader.GetOrdinal("alimento")),
                Hora: reader.GetFieldValue<TimeOnly>(reader.GetOrdinal("hora")),
                CantidadGramos: Convert.ToDecimal(reader["cantidad_gramos"])
            ));
        }

        return list;
    }

    /// <summary>
    /// Calculates the sum of nutrients consumed on a specific date based on registered food quantities.
    /// </summary>
    public async Task<List<NutrienteConsumo>> GetNutrientesDelDiaAsync(string fecha = "2026-10-15")
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
            GROUP BY n.nombre, n.unidad;
        """;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("fecha", fecha);

        var list = new List<NutrienteConsumo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new NutrienteConsumo(
                Nutriente: reader.GetString(reader.GetOrdinal("nutriente")),
                Unidad: reader.GetString(reader.GetOrdinal("unidad")),
                TotalConsumido: Convert.ToDecimal(reader["total_consumido"])
            ));
        }

        return list;
    }

    /// <summary>
    /// Inserts a new food log and returns the generated ID.
    /// </summary>
    public async Task<int> CrearRegistroAsync(GuardarRegistroDto dto)
    {
        const string sql = """
            INSERT INTO registro_alimentario (alimento_id, fecha, hora, cantidad)
            VALUES (@alimento_id, @fecha, @hora, @cantidad)
            RETURNING registro_alimentario_id;
        """;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("alimento_id", dto.AlimentoId);
        command.Parameters.AddWithValue("fecha", dto.Fecha);
        command.Parameters.AddWithValue("hora", dto.Hora);
        command.Parameters.AddWithValue("cantidad", dto.CantidadGramos);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Updates an existing food log. Returns true if the record was found and updated.
    /// </summary>
    public async Task<bool> ActualizarRegistroAsync(int id, GuardarRegistroDto dto)
    {
        const string sql = """
            UPDATE registro_alimentario
            SET alimento_id = @alimento_id,
                fecha = @fecha,
                hora = @hora,
                cantidad = @cantidad
            WHERE registro_alimentario_id = @id;
        """;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("alimento_id", dto.AlimentoId);
        command.Parameters.AddWithValue("fecha", dto.Fecha);
        command.Parameters.AddWithValue("hora", dto.Hora);
        command.Parameters.AddWithValue("cantidad", dto.CantidadGramos);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    /// <summary>
    /// Deletes a food log by ID. Returns true if the record was found and deleted.
    /// </summary>
    public async Task<bool> EliminarRegistroAsync(int id)
    {
        const string sql = "DELETE FROM registro_alimentario WHERE registro_alimentario_id = @id;";

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    /// <summary>
    /// Returns a paginated list of food records, optionally filtered by date.
    /// </summary>
    public async Task<PaginatedList<AlimentoConsumido>> GetRegistrosPaginadosAsync(
        string? fecha,
        int page = 1,
        int pageSize = 10)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var dataSource = NpgsqlDataSource.Create(_connectionString);

        // 1. Total count
        const string countSql = """
            SELECT COUNT(*)
            FROM registro_alimentario
            WHERE (@fecha IS NULL OR fecha = @fecha::date);
        """;
        await using var countCmd = dataSource.CreateCommand(countSql);
        countCmd.Parameters.AddWithValue("fecha", (object?)fecha ?? DBNull.Value);
        var totalItems = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 2. Fetch page items with LIMIT and OFFSET
        const string dataSql = """
            SELECT
              ra.registro_alimentario_id,
              a.nombre AS alimento,
              ra.hora,
              ra.cantidad AS cantidad_gramos
            FROM registro_alimentario ra
            JOIN alimento a ON ra.alimento_id = a.alimento_id
            WHERE (@fecha IS NULL OR ra.fecha = @fecha::date)
            ORDER BY ra.fecha DESC, ra.hora DESC
            LIMIT @limit OFFSET @offset;
        """;
        await using var dataCmd = dataSource.CreateCommand(dataSql);
        dataCmd.Parameters.AddWithValue("fecha", (object?)fecha ?? DBNull.Value);
        dataCmd.Parameters.AddWithValue("limit", pageSize);
        dataCmd.Parameters.AddWithValue("offset", offset);

        var list = new List<AlimentoConsumido>();
        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new AlimentoConsumido(
                RegistroAlimentarioId: reader.GetInt32(reader.GetOrdinal("registro_alimentario_id")),
                Alimento: reader.GetString(reader.GetOrdinal("alimento")),
                Hora: reader.GetFieldValue<TimeOnly>(reader.GetOrdinal("hora")),
                CantidadGramos: Convert.ToDecimal(reader["cantidad_gramos"])
            ));
        }

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PaginatedList<AlimentoConsumido>(list, page, pageSize, totalItems, totalPages);
    }

    /// <summary>
    /// PLACEHOLDER: Calculates total nutrients within a date range (e.g. from 2026-10-01 to 2026-10-15).
    /// </summary>
    public async Task<List<NutrienteConsumo>> GetNutrientesPorRangoFechaAsync(string fechaInicio, string fechaFin)
    {
        // =========================================================================
        // TODO (FOR YOU TO IMPLEMENT):
        //
        // 1. Write the SQL query:
        //    - Use JOIN between registro_alimentario, alimento_nutriente, and nutriente.
        //    - Filter ra.fecha using a range:
        //      e.g. `ra.fecha BETWEEN @fechaInicio::date AND @fechaFin::date`
        //      or `ra.fecha >= @fechaInicio::date AND ra.fecha <= @fechaFin::date`
        //    - GROUP BY n.nombre, n.unidad
        //
        // 2. Pass @fechaInicio and @fechaFin parameters to NpgsqlCommand.
        // 3. Read the results and return the List<NutrienteConsumo>.
        // =========================================================================

        await Task.CompletedTask; // Keeps async signature happy until implemented
        throw new NotImplementedException("TODO: Implement date range calculation here!");
    }
}
