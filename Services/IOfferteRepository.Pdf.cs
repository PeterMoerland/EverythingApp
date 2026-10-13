using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

// Fase 4 van Offertes: gegevens voor de PDF, de mailinstellingen en "aangeboden" (Access: cmdPrint_Click).

public partial interface IOfferteRepository
{
    /// <summary>Kop, regels, klantadres, contactpersoon, BV en de ondertekenaar (de gebruiker die verstuurt).</summary>
    Task<OffertePdfGegevens?> PdfGegevensAsync(int offerteId, int? ondertekenaarId);

    Task<OfferteMailInstellingen> MailInstellingenAsync();

    /// <summary>
    /// GemaildOp/GemaildDoor zetten en de status op Aangeboden, behalve als hij al op Afgewezen of Opdracht staat
    /// (Access zette die terug op Aangeboden bij opnieuw versturen).
    /// </summary>
    Task MarkeerVerstuurdAsync(int offerteId, int? door);
}

public partial class GeenOfferteRepository
{
    public Task<OffertePdfGegevens?> PdfGegevensAsync(int offerteId, int? ondertekenaarId) => Task.FromResult<OffertePdfGegevens?>(null);
    public Task<OfferteMailInstellingen> MailInstellingenAsync() => Task.FromResult(new OfferteMailInstellingen("", "", "", ""));
    public Task MarkeerVerstuurdAsync(int offerteId, int? door) => Task.CompletedTask;
}

public partial class MySqlOfferteRepository
{
    public async Task<OffertePdfGegevens?> PdfGegevensAsync(int offerteId, int? ondertekenaarId)
    {
        var o = await GetAsync(offerteId);
        if (o is null) return null;
        var g = new OffertePdfGegevens { Offerte = o, Regels = await RegelsAsync(offerteId) };

        await using var cn = await OpenAsync();

        // klant (Access: Relations.name, address, PO_box, town, Country als Landnummer <> 31) en contactpersoon
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(r.`name`, ''), COALESCE(r.`address`, ''), COALESCE(r.`PO_box`, ''), COALESCE(r.`town`, ''), " +
            "       COALESCE(c.`Country`, ''), COALESCE(c.`Landnummer`, 31), COALESCE(r.`VAT number`, ''), " +
            "       COALESCE(k.`Naam`, ''), COALESCE(k.`mail`, '') " +
            "  FROM `Relations` r LEFT JOIN `Country` c ON c.`Country-id` = r.`country-id` " +
            "  LEFT JOIN `Contacts` k ON k.`ID_contact` = @contact " +
            " WHERE r.`id_supplier` = @klant", cn))
        {
            cmd.Parameters.AddWithValue("@klant", (object?)o.KlantId ?? -1);
            cmd.Parameters.AddWithValue("@contact", (object?)o.ContactId ?? -1);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                g.Klant = new FactuurAdres
                {
                    Naam = S(r, 0), Adres = S(r, 1), Postcode = S(r, 2), Plaats = S(r, 3), Land = S(r, 4),
                    Landnummer = Convert.ToInt32(r.GetValue(5)), BtwNummer = S(r, 6), Tav = S(r, 7),
                };
                g.ContactMail = S(r, 8).Trim();
            }
        }

        // de BV van het project, zoals bij de factuur
        if (o.BvId is int bv)
        {
            await using var cmd = new MySqlCommand(
                "SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, ''), COALESCE(`address`, ''), COALESCE(`PO_box`, ''), " +
                "       COALESCE(`town`, ''), COALESCE(`email`, ''), COALESCE(`VAT number`, ''), COALESCE(`KvK`, ''), " +
                "       COALESCE(`IBAN`, ''), COALESCE(`BIC`, '') FROM `Relations` WHERE `id_supplier` = @bv", cn);
            cmd.Parameters.AddWithValue("@bv", bv);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                g.Afzender = new FactuurAfzender
                {
                    Id = r.GetInt32(0), Afkorting = S(r, 1), Naam = S(r, 2), Adres = S(r, 3), Postcode = S(r, 4),
                    Plaats = S(r, 5), Email = S(r, 6), Btw = S(r, 7), Kvk = S(r, 8), Iban = S(r, 9), Bic = S(r, 10),
                };
        }

        // ondertekenaar (Access GetSignOff: naam, functie, mail, mobiel van de gebruiker)
        if (ondertekenaarId is int w)
        {
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(w.`NaamVoluit`, ''), COALESCE(f.`Functie`, ''), COALESCE(w.`Mail`, ''), COALESCE(w.`Mobiel`, '') " +
                "  FROM `Werknemers` w LEFT JOIN `Werknemers_Functie` f ON f.`id_functie` = w.`id_functie` " +
                " WHERE w.`Werknemer-id` = @w", cn);
            cmd.Parameters.AddWithValue("@w", w);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                g.Ondertekenaar = new OfferteOndertekenaar { Id = w, Naam = S(r, 0), Functie = S(r, 1), Mail = S(r, 2), Mobiel = S(r, 3) };
        }
        return g;
    }

    public async Task<OfferteMailInstellingen> MailInstellingenAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`eOfferteMailTekst_NL`, ''), COALESCE(`eOfferteMailTekst_EN`, ''), " +
            "       COALESCE(`FileMetaalunieNL`, ''), COALESCE(`FileMetaalUnieEN`, '') FROM `General_settings` LIMIT 1", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new OfferteMailInstellingen(S(r, 0), S(r, 1), S(r, 2), S(r, 3))
            : new OfferteMailInstellingen("", "", "", "");
    }

    public async Task MarkeerVerstuurdAsync(int offerteId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Offertes` SET `GemaildOp` = NOW(), `GemaildDoor` = @door, " +
            "       `Status` = IF(COALESCE(`Status`, 1) IN (3, 4), `Status`, 2) WHERE `OfferteID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", offerteId);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
