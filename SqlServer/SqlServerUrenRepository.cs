// ============================================================================
// LIVE SQL SERVER REPOSITORY — not compiled by default.
//
// Activate:
//   1. dotnet add package Microsoft.Data.SqlClient
//   2. Remove the <Compile Remove="SqlServer\**" /> lines from the .csproj
//   3. Put your connection string in appsettings.json under "MDE"
//   4. In Program.cs replace the JsonDemoRepository registration with:
//        builder.Services.AddSingleton<IUrenRepository>(sp =>
//            new SqlServerUrenRepository(builder.Configuration.GetConnectionString("MDE")!));
//
// Column names follow the linked tables in the Access front-end
// (DSN "MDE-Automation"). VERIFY names/types against the real database
// before going live — especially [Werknemers] (name column) and [Workorders].
// ============================================================================
using Microsoft.Data.SqlClient;
using EverythingApp.Models;
using EverythingApp.Services;

namespace EverythingApp.SqlServer;

public class SqlServerUrenRepository : IUrenRepository
{
    private readonly string _cs;
    public SqlServerUrenRepository(string connectionString) => _cs = connectionString;

    private SqlConnection Open() => new(_cs);

    public async Task<List<Werknemer>> GetWerknemersAsync()
    {
        var list = new List<Werknemer>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT [Werknemer-id], [Naam], [Directie], [Hoofdactiviteit],
                     [woonwerkkilometers], [TextWoonWerk], [TextWerkWoon]
              FROM Werknemers", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new Werknemer
            {
                WerknemerId = rd.GetInt32(0),
                Naam = rd.IsDBNull(1) ? "" : rd.GetString(1),
                Directie = !rd.IsDBNull(2) && rd.GetBoolean(2),
                Hoofdactiviteit = rd.IsDBNull(3) ? null : rd.GetInt32(3),
                WoonWerkKilometers = rd.IsDBNull(4) ? 0 : rd.GetDecimal(4),
                TextWoonWerk = rd.IsDBNull(5) ? "Woon-werk" : rd.GetString(5),
                TextWerkWoon = rd.IsDBNull(6) ? "Werk-woon" : rd.GetString(6),
            });
        return list;
    }

    public async Task<Werknemer?> GetWerknemerAsync(int id) =>
        (await GetWerknemersAsync()).FirstOrDefault(w => w.WerknemerId == id);

    public Task<int?> VindWerknemerOpEmailAsync(string email) => throw new NotImplementedException();
    public Task<List<Workorder>> ZoekWerkordersAsync(string zoek, bool alleenOpen, int max) => throw new NotImplementedException();
    public Task<List<Workorder>> RecentGebruikteWerkordersAsync(int werknemerId, int dagen, int max) => throw new NotImplementedException();
    public Task<List<Workorder>> PersoonlijkeWerkordersAsync(int werknemerId) => throw new NotImplementedException();
    public Task ZetPersoonlijkeWerkorderAsync(int werknemerId, int werkbonId, bool toevoegen) => throw new NotImplementedException();
    public Task<Dictionary<string, object?>> GetWerknemerVeldenAsync(int werknemerId) => throw new NotImplementedException();
    public Task<List<Werknemer>> ZoekWerknemersAsync(bool inclusiefIngeleend, bool inclusiefUitDienst, bool alleenPersonen, string? zoek) => throw new NotImplementedException();
    public Task UpdateWerknemerVeldAsync(int werknemerId, string kolom, object? waarde) => throw new NotImplementedException();
    public Task<HashSet<string>> WerknemerKolommenAsync() => throw new NotImplementedException();

    public async Task<List<Workorder>> GetOpenWorkordersAsync()
    {
        var list = new List<Workorder>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT [Werkbon-id], [Omschrijving], [WorkorderStatus]
              FROM Workorders WHERE [WorkorderStatus] <> 0", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new Workorder
            {
                WerkbonId = rd.GetInt32(0),
                Omschrijving = rd.IsDBNull(1) ? "" : rd.GetString(1),
                WorkorderStatus = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
            });
        return list;
    }

    public async Task<Workorder?> GetWorkorderAsync(int id)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT [Werkbon-id], [Omschrijving], [WorkorderStatus]
              FROM Workorders WHERE [Werkbon-id] = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new Workorder
        {
            WerkbonId = rd.GetInt32(0),
            Omschrijving = rd.IsDBNull(1) ? "" : rd.GetString(1),
            WorkorderStatus = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
        };
    }

    // TODO in fase 2: Tariefsoort, UrenActiviteit, GeneralSettingsSpecialOrders,
    // General_settings — zelfde patroon als hierboven.
    public Task<List<Tariefsoort>> GetTariefsoortenAsync() => throw new NotImplementedException();
    public Task<List<UrenActiviteit>> GetActiviteitenAsync() => throw new NotImplementedException();
    public Task<List<SpecialOrder>> GetSpecialOrdersAsync() => throw new NotImplementedException();
    public Task<GeneralSettings> GetGeneralSettingsAsync() => throw new NotImplementedException();

    // TODO planning-module: nieuwe tabel [ProjectPlanning] aanmaken op SQL Server, bijv.:
    //   CREATE TABLE ProjectPlanning (
    //       Id INT IDENTITY PRIMARY KEY,
    //       [Werknemer-id] INT NOT NULL,
    //       Werkorder INT NOT NULL,
    //       Startdatum DATE NOT NULL,
    //       Einddatum DATE NOT NULL,
    //       GeplandeUren DECIMAL(6,2) NOT NULL DEFAULT 0,
    //       Notitie NVARCHAR(255) NOT NULL DEFAULT '')
    public Task<List<PlanningItem>> GetPlanningAsync() => throw new NotImplementedException();
    public Task<PlanningItem> AddPlanningAsync(PlanningItem item) => throw new NotImplementedException();
    public Task UpdatePlanningAsync(PlanningItem item) => throw new NotImplementedException();
    public Task DeletePlanningAsync(int id) => throw new NotImplementedException();
    public Task<List<UrenEntry>> GetAlleUrenAsync(DateOnly van, DateOnly tot) => throw new NotImplementedException();

    public async Task<List<UrenEntry>> GetUrenAsync(int werknemerId, DateOnly van, DateOnly tot)
    {
        var list = new List<UrenEntry>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT [Uren-id], [StartDatum], [StartTijd], [EindDatum], [Eindtijd],
                     [Werknemer-id], [Uren], [werkorder], [Kilometers], [PriveKMs],
                     [Omschrijving], [Tariefsoort-id], [Fixed_Amount], [FixedType-ID],
                     [UrenActiviteitID], [GeneratedByRule]
              FROM Uren
              WHERE [Werknemer-id] = @wn AND [StartDatum] >= @van AND [StartDatum] <= @tot
              ORDER BY [StartDatum], [StartTijd]", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.ToDateTime(TimeOnly.MinValue));
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new UrenEntry
            {
                UrenId = rd.GetInt32(0),
                StartDatum = DateOnly.FromDateTime(rd.GetDateTime(1)),
                StartTijd = rd.IsDBNull(2) ? null : TimeOnly.FromDateTime(rd.GetDateTime(2)),
                EindDatum = rd.IsDBNull(3) ? null : DateOnly.FromDateTime(rd.GetDateTime(3)),
                EindTijd = rd.IsDBNull(4) ? null : TimeOnly.FromDateTime(rd.GetDateTime(4)),
                WerknemerId = rd.GetInt32(5),
                Uren = rd.IsDBNull(6) ? 0 : rd.GetDecimal(6),
                Werkorder = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                Kilometers = rd.IsDBNull(8) ? 0 : rd.GetDecimal(8),
                PriveKMs = !rd.IsDBNull(9) && rd.GetBoolean(9),
                Omschrijving = rd.IsDBNull(10) ? "" : rd.GetString(10),
                TariefsoortId = rd.IsDBNull(11) ? 0 : rd.GetInt32(11),
                FixedAmount = rd.IsDBNull(12) ? 0 : rd.GetDecimal(12),
                FixedTypeId = rd.IsDBNull(13) ? 0 : rd.GetInt32(13),
                UrenActiviteitId = rd.IsDBNull(14) ? null : rd.GetInt32(14),
                GeneratedByRule = !rd.IsDBNull(15) && rd.GetBoolean(15),
            });
        return list;
    }

    public Task<List<UrenEntry>> GetUrenVanDagAsync(int werknemerId, DateOnly dag) =>
        GetUrenAsync(werknemerId, dag, dag);

    public async Task<UrenEntry> AddAsync(UrenEntry e)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"INSERT INTO Uren ([StartDatum],[StartTijd],[EindDatum],[Eindtijd],[Werknemer-id],
                                [Uren],[werkorder],[Kilometers],[PriveKMs],[Omschrijving],
                                [Tariefsoort-id],[Fixed_Amount],[FixedType-ID],[UrenActiviteitID],[GeneratedByRule])
              OUTPUT INSERTED.[Uren-id]
              VALUES (@sd,@st,@ed,@et,@wn,@u,@wo,@km,@pkm,@oms,@ts,@fa,@ft,@act,@gbr)", cn);
        cmd.Parameters.AddWithValue("@sd", e.StartDatum.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@st", (object?)e.StartTijd?.ToTimeSpan() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)e.EindDatum?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@et", (object?)e.EindTijd?.ToTimeSpan() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wn", e.WerknemerId);
        cmd.Parameters.AddWithValue("@u", e.Uren);
        cmd.Parameters.AddWithValue("@wo", (object?)e.Werkorder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@km", e.Kilometers);
        cmd.Parameters.AddWithValue("@pkm", e.PriveKMs);
        cmd.Parameters.AddWithValue("@oms", e.Omschrijving);
        cmd.Parameters.AddWithValue("@ts", e.TariefsoortId);
        cmd.Parameters.AddWithValue("@fa", e.FixedAmount);
        cmd.Parameters.AddWithValue("@ft", e.FixedTypeId);
        cmd.Parameters.AddWithValue("@act", (object?)e.UrenActiviteitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gbr", e.GeneratedByRule);
        e.UrenId = (int)(await cmd.ExecuteScalarAsync())!;
        return e;
    }

    public async Task UpdateAsync(UrenEntry e)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"UPDATE Uren SET [StartDatum]=@sd,[StartTijd]=@st,[EindDatum]=@ed,[Eindtijd]=@et,
                              [Uren]=@u,[werkorder]=@wo,[Kilometers]=@km,[PriveKMs]=@pkm,
                              [Omschrijving]=@oms,[Tariefsoort-id]=@ts,[Fixed_Amount]=@fa,
                              [FixedType-ID]=@ft,[UrenActiviteitID]=@act
              WHERE [Uren-id]=@id", cn);
        cmd.Parameters.AddWithValue("@id", e.UrenId);
        cmd.Parameters.AddWithValue("@sd", e.StartDatum.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@st", (object?)e.StartTijd?.ToTimeSpan() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)e.EindDatum?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@et", (object?)e.EindTijd?.ToTimeSpan() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", e.Uren);
        cmd.Parameters.AddWithValue("@wo", (object?)e.Werkorder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@km", e.Kilometers);
        cmd.Parameters.AddWithValue("@pkm", e.PriveKMs);
        cmd.Parameters.AddWithValue("@oms", e.Omschrijving);
        cmd.Parameters.AddWithValue("@ts", e.TariefsoortId);
        cmd.Parameters.AddWithValue("@fa", e.FixedAmount);
        cmd.Parameters.AddWithValue("@ft", e.FixedTypeId);
        cmd.Parameters.AddWithValue("@act", (object?)e.UrenActiviteitId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int urenId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM Uren WHERE [Uren-id]=@id", cn);
        cmd.Parameters.AddWithValue("@id", urenId);
        await cmd.ExecuteNonQueryAsync();
    }
}
