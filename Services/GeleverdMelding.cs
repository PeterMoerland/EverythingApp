namespace EverythingApp.Services;

/// <summary>
/// Een bestelling is compleet geleverd: een melding in de bel van de besteller (of NotifyTo), en de mail "PO-… is
/// compleet geleverd" als die erom vroeg (Notify_when_delivered; Access SendMail_Orderdelivered). Gebruikt door
/// Bestellingen ▸ Ontvangst (aftekenen) en door het gereed melden in het bestelscherm.
/// </summary>
public class GeleverdMelding
{
    private readonly IOntvangstRepository _repo;
    private readonly IMeldingRepository _meldingen;
    private readonly MeldingSignaal _signaal;
    private readonly HuidigeGebruiker _ik;
    private readonly MailPostbussen _postbussen;
    private readonly MailService _post;
    private readonly GoogleHerstel _herstel;
    private readonly IWebHostEnvironment _omgeving;

    public GeleverdMelding(IOntvangstRepository repo, IMeldingRepository meldingen, MeldingSignaal signaal, HuidigeGebruiker ik,
                           MailPostbussen postbussen, MailService post, GoogleHerstel herstel, IWebHostEnvironment omgeving)
    {
        _repo = repo; _meldingen = meldingen; _signaal = signaal; _ik = ik;
        _postbussen = postbussen; _post = post; _herstel = herstel; _omgeving = omgeving;
    }

    /// <summary>Meldt het aan de ontvanger uit <paramref name="uit"/>. Geeft tekst voor achter de statusregel (kan leeg zijn).</summary>
    /// <param name="doorNaam">Wie de goederen aftekende of de bestelling gereed meldde (ondertekening van de mail).</param>
    public async Task<string> MeldAsync(int orderId, string leverancier, OntvangstUitkomst uit, string doorNaam)
    {
        if (uit.Ontvanger is not int wie) return "";
        var extra = "";
        try
        {
            await _meldingen.MaakAsync(wie, "geleverd", orderId, $"PO-{orderId} ({leverancier}) is compleet geleverd.");
            _signaal.Geef();
        }
        catch { }
        if (!uit.MailGevraagd) return extra;
        try
        {
            var g = await _repo.GeleverdMailAsync(orderId, wie);
            var postbus = _ik.IsWerkplek ? _postbussen.Werkplek(_ik) : _ik.Email.Length > 0 ? MailService.Eigen(_ik.Email) : null;
            if (g is null || g.MailOntvanger.Length == 0) return " Geen mailadres bekend van de besteller; alleen een melding in de app.";
            if (postbus is null) return " Mail niet verstuurd (geen afzender op deze pc); wel een melding in de app.";
            var logo = await File.ReadAllBytesAsync(Path.Combine(_omgeving.WebRootPath, "logo-mde.png"));
            await _herstel.VoerUitAsync(() => _post.VerstuurAsync(postbus, g.MailOntvanger, BestelMail.GeleverdOnderwerp(g),
                "<html><body>" + BestelMail.GeleverdHtml(g, doorNaam, "cid:" + BestelMail.LogoCid) + "</body></html>",
                null, null, html: true, ingebed: new[] { (BestelMail.LogoCid, "logo-mde.png", logo) }));
            extra = $" {g.NaamOntvanger} krijgt een mail en een melding.";
        }
        catch (Exception ex) { extra = " Mail versturen mislukt: " + ex.Message; }
        return extra;
    }
}
