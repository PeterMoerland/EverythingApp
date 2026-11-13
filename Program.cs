using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using EverythingApp.Components;
using EverythingApp.Services;

// Nederlandse notatie voor getallen en datums: '3500,50' wordt zo correct gelezen,
// ongeacht de taalinstelling van de server.
var nl = new CultureInfo("nl-NL");
CultureInfo.DefaultThreadCurrentCulture = nl;
CultureInfo.DefaultThreadCurrentUICulture = nl;

// ---------------------------------------------------------------------------
// Crashlogboek.
//
// Als de app als service omvalt, verdwijnt de foutmelding in het Windows-
// logboek tussen alles wat daar staat. Deze regel schrijft elke onafgevangen
// fout naar logboek\fouten.txt naast de app, met tijdstip en stapel. Dat is
// het eerste waar je kijkt als iemand meldt dat het scherm 'opnieuw verbinden'
// laat zien.
// ---------------------------------------------------------------------------
// Gegevens die een uitrol moeten overleven horen niet in de publicatiemap: die
// wordt bij elke publicatie leeggemaakt. C:\ProgramData\MDE is de gebruikelijke
// plek voor een dienst, en het account waaronder de app draait maakt de map zelf
// aan en is daarmee eigenaar.
var gegevensmap = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MDE");

var logmap = Path.Combine(gegevensmap, "logboek");
void Noteer(string wat, Exception? fout)
{
    try
    {
        Directory.CreateDirectory(logmap);
        File.AppendAllText(Path.Combine(logmap, "fouten.txt"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {wat}{Environment.NewLine}"
          + (fout is null ? "" : fout + Environment.NewLine)
          + new string('-', 70) + Environment.NewLine);
    }
    catch { /* als zelfs het logboek niet lukt, moet de app gewoon doordraaien */ }
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Noteer("Onafgevangen fout — de app stopt hierna.", e.ExceptionObject as Exception);

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Noteer("Fout in een achtergrondtaak.", e.Exception);
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

// Laat de app als Windows-service draaien (geen effect bij 'dotnet run').
builder.Host.UseWindowsService();

// ---------------------------------------------------------------------------
// Sleutels voor versleuteling op een vaste plek.
//
// ASP.NET Core versleutelt hiermee onder meer de Blazor-verbinding en de
// inlogcookie. Zonder eigen plek bewaart het die sleutels in het profiel van de
// gebruiker waaronder de app draait — en een serviceaccount heeft dat profiel
// vaak niet. De sleutels zijn dan alleen in het geheugen: bij elke herstart
// vervallen alle verbindingen, en zonder profiel kan het aanmaken zelfs
// mislukken. Vandaar een eigen map naast de app.
// ---------------------------------------------------------------------------
var sleutelmap = new DirectoryInfo(Path.Combine(gegevensmap, "sleutels"));
try
{
    sleutelmap.Create();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(sleutelmap)
        .SetApplicationName("MDE");
}
catch
{
    // Lukt zelfs dat niet, dan draait de app door met sleutels in het geheugen.
    // Iedereen moet dan na een herstart opnieuw inloggen, maar hij start wel.
}

builder.Services.AddRazorComponents()
    // De toestand van een weggevallen scherm (bv. een half ingevulde factuur)
    // tien minuten vasthouden in plaats van drie: wie na een storing
    // terugkomt, vindt zijn werk dan gewoon terug.
    .AddInteractiveServerComponents(o =>
    {
        o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    })
    // Verdraagzamer bij netwerkhikjes (VPN, wifi): de server wacht een minuut
    // op een stille client voordat hij de verbinding opgeeft, in plaats van de
    // standaard 30 seconden. De hartslag elke 15 seconden houdt onderweg ook
    // proxies (Caddy) ervan overtuigd dat de verbinding leeft.
    .AddHubOptions(o =>
    {
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
        o.HandshakeTimeout = TimeSpan.FromSeconds(30);
        // Antwoorden van de werkplek-agent komen via JS-interop de server binnen (browser →
        // server). Standaard mag zo'n bericht maar 32 kB zijn; een objectdictionary-scan of
        // een lange servo-registerlijst is groter en dan blijft de aanroep stil hangen.
        o.MaximumReceiveMessageSize = 8 * 1024 * 1024;
    });

// Nodig zodat App.razor (de <base href> tag) weet met welk pad-voorvoegsel de
// app wordt aangeboden — zie ook "PathBase" verderop en in appsettings.json.
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Databron: als er een echte connection string is ingevuld in appsettings.json
// -> live MySQL. Anders -> demo-data (JsonDemoRepository).
// ---------------------------------------------------------------------------
var cs = builder.Configuration.GetConnectionString("MDE");
var liveDb = !string.IsNullOrWhiteSpace(cs) && !cs!.Contains("REPLACE_");

if (liveDb)
{
    builder.Services.AddSingleton<IUrenRepository>(_ => new MySqlUrenRepository(cs!));
    builder.Services.AddSingleton<IWerknemerRepository>(_ => new MySqlWerknemerRepository(cs!));
    builder.Services.AddSingleton<IRelatieRepository>(_ => new MySqlRelatieRepository(cs!));
    builder.Services.AddSingleton<IProjectRepository>(_ => new MySqlProjectRepository(cs!));
    builder.Services.AddSingleton<INdaRepository>(_ => new MySqlNdaRepository(cs!));
    builder.Services.AddSingleton<IProductRepository>(_ => new MySqlProductRepository(cs!,
        builder.Configuration.GetValue<int?>("Productie:StandaardKlantId")));
    builder.Services.AddSingleton<IBestelRepository>(_ => new MySqlBestelRepository(cs!));
    // BV-structuur (Financiën > BV-structuur); lokaal testen zonder het script: BvStructuur__Geheugen=true (leest de BV's echt)
    if (builder.Configuration.GetValue<bool>("BvStructuur:Geheugen"))
        builder.Services.AddSingleton<IBvStructuurRepository>(_ => new GeheugenBvStructuurRepository(new MySqlBvStructuurRepository(cs!)));
    else
        builder.Services.AddSingleton<IBvStructuurRepository>(_ => new MySqlBvStructuurRepository(cs!));
    // Verzekeringen (Financiën > Verzekeringen); lokaal testen zonder de tabel: Verzekeringen__Geheugen=true
    if (builder.Configuration.GetValue<bool>("Verzekeringen:Geheugen"))
        builder.Services.AddSingleton<IVerzekeringRepository>(_ => new GeheugenVerzekeringRepository(new MySqlVerzekeringRepository(cs!)));
    else
        builder.Services.AddSingleton<IVerzekeringRepository>(_ => new MySqlVerzekeringRepository(cs!));
    builder.Services.AddSingleton<IMeldingRepository>(_ => new MySqlMeldingRepository(cs!));
    builder.Services.AddSingleton<IProductTodoRepository>(_ => new MySqlProductTodoRepository(cs!));
    builder.Services.AddSingleton<IProjectFotoRepository>(_ => new MySqlProjectFotoRepository(cs!));   // projectfoto's vanaf de telefoon
    builder.Services.AddSingleton<IInkoopBevestigingRepository>(_ => new MySqlInkoopBevestigingRepository(cs!));   // orderbevestigingen uit inkoop@
    builder.Services.AddSingleton<IAiVraagRepository>(_ => new MySqlAiVraagRepository(cs!));
    builder.Services.AddSingleton<INacalculatieRepository>(_ => new MySqlNacalculatieRepository(cs!));
    builder.Services.AddSingleton<IOntvangstRepository>(_ => new MySqlOntvangstRepository(cs!));
    builder.Services.AddSingleton<IBankRepository>(_ => new MySqlBankRepository(cs!));
    builder.Services.AddSingleton<IServiceRepository>(_ => new MySqlServiceRepository(cs!));
    builder.Services.AddSingleton<IArtikelRepository>(_ => new MySqlArtikelRepository(cs!,
        builder.Configuration.GetValue<int?>("Productie:StandaardArtikelEenheidId") ?? 2));
    builder.Services.AddSingleton<IOrderBatchRepository>(_ => new MySqlOrderBatchRepository(cs!));
    builder.Services.AddSingleton<IFactuurRepository>(_ => new MySqlFactuurRepository(cs!));
    builder.Services.AddSingleton<IVerkoopFactuurRepository>(_ => new MySqlVerkoopFactuurRepository(cs!));
    // Betalingen per factuur (deelbetalingen); lokaal testen zonder te schrijven: FactuurBetalingen__Geheugen=true
    if (builder.Configuration.GetValue<bool>("FactuurBetalingen:Geheugen"))
        builder.Services.AddSingleton<IFactuurBetalingRepository>(sp => new GeheugenFactuurBetalingRepository(
            new MySqlFactuurBetalingRepository(cs!), sp.GetRequiredService<IVerkoopFactuurRepository>()));
    else
        builder.Services.AddSingleton<IFactuurBetalingRepository>(_ => new MySqlFactuurBetalingRepository(cs!));
    builder.Services.AddSingleton<IUrenImportRepository>(_ => new MySqlUrenImportRepository(cs!));
    // lokaal testen zonder in VasteLasten te schrijven: VasteLasten__Geheugen=true (leest echt)
    if (builder.Configuration.GetValue<bool>("VasteLasten:Geheugen"))
        builder.Services.AddSingleton<IVasteLastenRepository>(_ => new GeheugenVasteLastenRepository(new MySqlVasteLastenRepository(cs!)));
    else
        builder.Services.AddSingleton<IVasteLastenRepository>(_ => new MySqlVasteLastenRepository(cs!));
    builder.Services.AddSingleton<ILiquiditeitRepository>(_ => new MySqlLiquiditeitRepository(cs!));
    builder.Services.AddSingleton<IBetalingRepository>(_ => new MySqlBetalingRepository(cs!));
    builder.Services.AddSingleton<IPOKlantRepository>(_ => new MySqlPOKlantRepository(cs!));
    builder.Services.AddSingleton<IBezoekRepository>(_ => new MySqlBezoekRepository(cs!));
    builder.Services.AddSingleton<IOfferteRepository>(_ => new MySqlOfferteRepository(cs!));
    // lokaal testen zonder in Calculaties te schrijven: Calculaties__Geheugen=true (offertes en activiteiten echt)
    if (builder.Configuration.GetValue<bool>("Calculaties:Geheugen"))
        builder.Services.AddSingleton<ICalculatieRepository>(sp => new GeheugenCalculatieRepository(
            new MySqlCalculatieRepository(cs!), sp.GetRequiredService<IOfferteRepository>()));
    else
        builder.Services.AddSingleton<ICalculatieRepository>(_ => new MySqlCalculatieRepository(cs!));
    builder.Services.AddSingleton<ISamenstellingRepository>(_ => new MySqlSamenstellingRepository(cs!));
    // uren boeken op de delen van een werkorder; lokaal testen zonder de kolom en zonder echte delen: UrenDelen__Geheugen=true
    if (builder.Configuration.GetValue<bool>("UrenDelen:Geheugen"))
        builder.Services.AddSingleton<IWerkorderDelenRepository, GeheugenWerkorderDelenRepository>();
    else
        builder.Services.AddSingleton<IWerkorderDelenRepository>(sp => new MySqlWerkorderDelenRepository(cs!, sp.GetRequiredService<IUrenRepository>()));
    // grootboekrekeningen (Financiën ▸ Grootboekrekeningen); lokaal testen zonder de tabel: Grootboek__Geheugen=true
    if (builder.Configuration.GetValue<bool>("Grootboek:Geheugen"))
        builder.Services.AddSingleton<IGrootboekRepository, GeheugenGrootboekRepository>();
    else
        builder.Services.AddSingleton<IGrootboekRepository>(_ => new MySqlGrootboekRepository(cs!));
    // lokaal testen zonder in Locations te schrijven: Lokaties__Geheugen=true (leest echt)
    if (builder.Configuration.GetValue<bool>("Lokaties:Geheugen"))
        builder.Services.AddSingleton<ILokatieRepository>(_ => new GeheugenLokatieRepository(new MySqlLokatieRepository(cs!)));
    else
        builder.Services.AddSingleton<ILokatieRepository>(_ => new MySqlLokatieRepository(cs!));
    builder.Services.AddSingleton<IVoorraadRepository>(_ => new MySqlVoorraadRepository(cs!));
    builder.Services.AddSingleton<IInstellingRepository>(_ => new MySqlInstellingRepository(cs!));
    builder.Services.AddSingleton<IPlanningRepository>(_ => new MySqlPlanningRepository(cs!));
    builder.Services.AddSingleton<IComputerRepository>(_ => new MySqlComputerRepository(cs!));
    builder.Services.AddSingleton<IBaseconeRepository>(_ => new MySqlBaseconeRepository(cs!));
    builder.Services.AddSingleton<IEcComponentRepository>(_ => new MySqlEcComponentRepository(cs!));
    builder.Services.AddSingleton<IJlcComponentRepository>(_ => new MySqlJlcComponentRepository(cs!));
    builder.Services.AddSingleton<IDriveRepository>(_ => new MySqlDriveRepository(cs!));
    builder.Services.AddSingleton<IOpbouwRepository>(_ => new MySqlOpbouwRepository(cs!));
    builder.Services.AddSingleton<IActivityBoardRepository>(_ => new MySqlActivityBoardRepository(cs!));
    builder.Services.AddSingleton<ISnelkoppelingRepository>(_ => new MySqlSnelkoppelingRepository(cs!));
    builder.Services.AddSingleton<IWerkplekRepository>(_ => new MySqlWerkplekRepository(cs!));
    builder.Services.AddSingleton<IFactuurInboxRepository>(_ => new MySqlFactuurInboxRepository(cs!));
    builder.Services.AddSingleton<IWelkomRepository>(_ => new MySqlWelkomRepository(cs!));
    builder.Services.AddSingleton<IVerbeterRepository>(_ => new MySqlVerbeterRepository(cs!));
    builder.Services.AddSingleton<IVerkoopOrderRepository>(sp => new MySqlVerkoopOrderRepository(cs!, sp.GetRequiredService<IProjectRepository>()));
    builder.Services.AddSingleton<IBroodjesRepository>(_ => new MySqlBroodjesRepository(cs!));
    builder.Services.AddSingleton<IProjectBudgetRepository>(_ => new MySqlProjectBudgetRepository(cs!));
    // Projectteam; lokaal testen zonder in ProjectTeam te schrijven: ProjectTeam__Geheugen=true (rollen en namen wel echt)
    if (builder.Configuration.GetValue<bool>("ProjectTeam:Geheugen"))
        builder.Services.AddSingleton<IProjectTeamRepository>(sp => new GeheugenProjectTeamRepository(new MySqlProjectTeamRepository(cs!), sp.GetRequiredService<IProjectRepository>()));
    else
        builder.Services.AddSingleton<IProjectTeamRepository>(_ => new MySqlProjectTeamRepository(cs!));
    // Pakbonnen; lokaal testen zonder in de pakbontabellen te schrijven: Pakbon__Geheugen=true (leest wel echt)
    if (builder.Configuration.GetValue<bool>("Pakbon:Geheugen"))
        builder.Services.AddSingleton<IPakbonRepository>(_ => new GeheugenPakbonRepository(new MySqlPakbonRepository(cs!)));
    else
        builder.Services.AddSingleton<IPakbonRepository>(_ => new MySqlPakbonRepository(cs!));
}
else
{
    builder.Services.AddSingleton<IUrenRepository, JsonDemoRepository>();
    builder.Services.AddSingleton<IWerknemerRepository, GeenWerknemerRepository>();
    builder.Services.AddSingleton<IRelatieRepository, GeenRelatieRepository>();
    builder.Services.AddSingleton<IProjectRepository, GeenProjectRepository>();
    builder.Services.AddSingleton<INdaRepository, GeenNdaRepository>();
    builder.Services.AddSingleton<IProductRepository, GeenProductRepository>();
    builder.Services.AddSingleton<IBestelRepository, GeenBestelRepository>();
    builder.Services.AddSingleton<IBvStructuurRepository, GeenBvStructuurRepository>();
    builder.Services.AddSingleton<IVerzekeringRepository, GeenVerzekeringRepository>();
    builder.Services.AddSingleton<IMeldingRepository, GeenMeldingRepository>();
    builder.Services.AddSingleton<IProductTodoRepository, GeenProductTodoRepository>();
    builder.Services.AddSingleton<IProjectFotoRepository, GeenProjectFotoRepository>();
    builder.Services.AddSingleton<IInkoopBevestigingRepository, GeenInkoopBevestigingRepository>();
    builder.Services.AddSingleton<IAiVraagRepository, GeenAiVraagRepository>();
    builder.Services.AddSingleton<INacalculatieRepository, GeenNacalculatieRepository>();
    builder.Services.AddSingleton<IOntvangstRepository, GeenOntvangstRepository>();
    builder.Services.AddSingleton<IBankRepository, GeenBankRepository>();
    builder.Services.AddSingleton<IServiceRepository, GeenServiceRepository>();
    builder.Services.AddSingleton<IArtikelRepository, GeenArtikelRepository>();
    builder.Services.AddSingleton<IInstellingRepository, GeenInstellingRepository>();
    builder.Services.AddSingleton<IOrderBatchRepository, GeenOrderBatchRepository>();
    builder.Services.AddSingleton<IFactuurRepository, GeenFactuurRepository>();
    builder.Services.AddSingleton<IVerkoopFactuurRepository, GeenVerkoopFactuurRepository>();
    builder.Services.AddSingleton<IFactuurBetalingRepository, GeenFactuurBetalingRepository>();
    builder.Services.AddSingleton<IUrenImportRepository, GeenUrenImportRepository>();
    builder.Services.AddSingleton<IVasteLastenRepository, GeenVasteLastenRepository>();
    builder.Services.AddSingleton<ILiquiditeitRepository, GeenLiquiditeitRepository>();
    builder.Services.AddSingleton<IBetalingRepository, GeenBetalingRepository>();
    builder.Services.AddSingleton<IPOKlantRepository, GeenPOKlantRepository>();
    builder.Services.AddSingleton<IBezoekRepository, GeenBezoekRepository>();
    builder.Services.AddSingleton<IActivityBoardRepository, GeenActivityBoardRepository>();
    builder.Services.AddSingleton<ISnelkoppelingRepository, GeenSnelkoppelingRepository>();
    builder.Services.AddSingleton<IWerkplekRepository, GeenWerkplekRepository>();
    builder.Services.AddSingleton<IFactuurInboxRepository, GeenFactuurInboxRepository>();
    builder.Services.AddSingleton<IWelkomRepository, GeenWelkomRepository>();
    builder.Services.AddSingleton<IVerbeterRepository, GeenVerbeterRepository>();
    builder.Services.AddSingleton<IVerkoopOrderRepository, GeenVerkoopOrderRepository>();
    builder.Services.AddSingleton<IBroodjesRepository, GeenBroodjesRepository>();
    builder.Services.AddSingleton<IProjectBudgetRepository, GeenProjectBudgetRepository>();
    builder.Services.AddSingleton<IProjectTeamRepository, GeenProjectTeamRepository>();
    builder.Services.AddSingleton<IPakbonRepository, GeenPakbonRepository>();
    builder.Services.AddSingleton<IOfferteRepository, GeenOfferteRepository>();
    builder.Services.AddSingleton<ICalculatieRepository, GeenCalculatieRepository>();
    builder.Services.AddSingleton<ISamenstellingRepository, GeenSamenstellingRepository>();
    builder.Services.AddSingleton<IWerkorderDelenRepository, GeenWerkorderDelenRepository>();
    builder.Services.AddSingleton<IGrootboekRepository, GeenGrootboekRepository>();
    builder.Services.AddSingleton<ILokatieRepository, GeenLokatieRepository>();
    builder.Services.AddSingleton<IVoorraadRepository, GeenVoorraadRepository>();
    builder.Services.AddSingleton<IPlanningRepository, GeenPlanningRepository>();
    builder.Services.AddSingleton<IComputerRepository, GeenComputerRepository>();
    builder.Services.AddSingleton<IBaseconeRepository, GeenBaseconeRepository>();
    builder.Services.AddSingleton<IEcComponentRepository, GeenEcComponentRepository>();
    builder.Services.AddSingleton<IJlcComponentRepository, GeenJlcComponentRepository>();
    builder.Services.AddSingleton<IDriveRepository, NullDriveRepository>();
    builder.Services.AddSingleton<IOpbouwRepository, NullOpbouwRepository>();
}

builder.Services.AddSingleton<CameraService>();
builder.Services.AddSingleton<BatchBel>();
builder.Services.AddSingleton<ActivityBoardLive>();   // Activity board: wijzigingen direct bij collega's
// Alleen om het Activity board lokaal te testen zonder database: ActivityBoard__Geheugen=true
if (builder.Configuration.GetValue<bool>("ActivityBoard:Geheugen"))
    builder.Services.AddSingleton<IActivityBoardRepository, GeheugenActivityBoardRepository>();
builder.Services.AddSingleton<SnelIconen>();   // snelbalk: Lucide-iconen uit wwwroot/iconen
// Alleen om de todo-schermen lokaal te bekijken zonder de tabel ProductTodo: ProductTodo__Geheugen=true
// (met twee voorbeeldpunten; toevoegen/afvinken schrijft wel een regel in de echte ProductHistorie, dus niet klikken)
if (builder.Configuration.GetValue<bool>("ProductTodo:Geheugen"))
    builder.Services.AddSingleton<IProductTodoRepository, GeheugenProductTodoRepository>();
// Alleen om het overzicht van orderbevestigingen lokaal te bekijken zonder de tabel: InkoopBevestigingen__Geheugen=true
if (builder.Configuration.GetValue<bool>("InkoopBevestigingen:Geheugen"))
    builder.Services.AddSingleton<IInkoopBevestigingRepository, GeheugenInkoopBevestigingRepository>();
// Alleen om de projectfoto's lokaal te proberen zonder de tabel ProjectFotos: ProjectFotos__Geheugen=true
// (werkorders en projecten zoeken gaat echt, alleen lezen; de foto's blijven in het geheugen)
if (builder.Configuration.GetValue<bool>("ProjectFotos:Geheugen"))
    builder.Services.AddSingleton<IProjectFotoRepository>(_ => new GeheugenProjectFotoRepository(
        new MySqlProjectFotoRepository(builder.Configuration.GetConnectionString("MDE") ?? "")));
// Alleen om Vragen aan AI lokaal te testen zonder de tabel AiVragen: AiVragen__Geheugen=true
if (builder.Configuration.GetValue<bool>("AiVragen:Geheugen"))
    builder.Services.AddSingleton<IAiVraagRepository, GeheugenAiVraagRepository>();
// Alleen om de snelbalk lokaal te testen zonder database: Snelbalk__Geheugen=true
if (builder.Configuration.GetValue<bool>("Snelbalk:Geheugen"))
    builder.Services.AddSingleton<ISnelkoppelingRepository, GeheugenSnelkoppelingRepository>();
// Alleen om werkplekken lokaal te testen zonder database: Werkplekken__Geheugen=true
if (builder.Configuration.GetValue<bool>("Werkplekken:Geheugen"))
    builder.Services.AddSingleton<IWerkplekRepository, GeheugenWerkplekRepository>();
// Alleen om de factuurinbox en het bonnetje lokaal te testen zonder de tabel FactuurInbox: FactuurInbox__Geheugen=true
if (builder.Configuration.GetValue<bool>("FactuurInbox:Geheugen"))
    builder.Services.AddSingleton<IFactuurInboxRepository, GeheugenFactuurInboxRepository>();
// Alleen om het welkomstscherm lokaal te testen zonder de tabel Welkomstbord: Welkom__Geheugen=true
if (builder.Configuration.GetValue<bool>("Welkom:Geheugen"))
    builder.Services.AddSingleton<IWelkomRepository, GeheugenWelkomRepository>();
builder.Services.AddSingleton<WelkomSchermService>();
// Verbeterverzoeken (lampje in de bovenbalk) lokaal testen zonder de tabel: Verbeter__Geheugen=true
if (builder.Configuration.GetValue<bool>("Verbeter:Geheugen"))
    builder.Services.AddSingleton<IVerbeterRepository, GeheugenVerbeterRepository>();
// Verkooporders lokaal testen zonder de tabellen: Verkoop__Geheugen=true (geen project/werkorder; adres en BV wel gelezen)
if (builder.Configuration.GetValue<bool>("Verkoop:Geheugen"))
    builder.Services.AddSingleton<IVerkoopOrderRepository>(sp => new GeheugenVerkoopOrderRepository(
        string.IsNullOrWhiteSpace(cs) ? null : new MySqlVerkoopOrderRepository(cs, sp.GetRequiredService<IProjectRepository>())));
builder.Services.AddSingleton<OrderbevestigingPdfService>();
builder.Services.AddSingleton<VerkoopBestellen>();
builder.Services.AddSingleton<VerkoopAfhandeling>();
// Broodjes (Kaeswienkeltje, /broodjes) lokaal testen zonder de tabellen: Broodjes__Geheugen=true
if (builder.Configuration.GetValue<bool>("Broodjes:Geheugen"))
    builder.Services.AddSingleton<IBroodjesRepository, GeheugenBroodjesRepository>();
builder.Services.AddSingleton<BroodjesAssortimentService>();
builder.Services.AddSingleton<AlarmVerkenner>();
builder.Services.AddSingleton<UNiiKoppeling>();

// De Vault-backup-waker: kijkt elk half uur, ruimt op tot het ingestelde
// aantal en zet een waarschuwing klaar voor de directiebalk. Als singleton
// geregistreerd zodat de balk dezelfde stand ziet als de achtergrondtaak.
builder.Services.AddSingleton<VaultBackupWaker>();
// Nieuwe versie aankondigen: balk met aftelklok bij iedereen (service-herstarten.bat of Beheer ▸ Nieuwe versie aankondigen)
builder.Services.AddSingleton<OnderhoudMelding>();
builder.Services.AddSingleton<Changelog>();      // CHANGELOG.md voor Wat is er nieuw (/nieuw)
builder.Services.AddHostedService(sp => sp.GetRequiredService<VaultBackupWaker>());
// herinnering in de bel vóór de opzegdatum van een verzekering (Financiën > Verzekeringen)
builder.Services.AddHostedService<VerzekeringWaker>();
// één keer per werkdag een melding in de bel van Financial en Directie over te laat betaalde facturen (Financiën > Te laat betaald)
builder.Services.AddHostedService<TeLaatWaker>();
builder.Services.AddSingleton<VerzekeringBron>();
builder.Services.AddSingleton<FactuurLezer>();

// Vragen aan AI (Financiën): Amazon Bedrock (sectie "Bedrock" in appsettings.json), opvragingen in
// FinancieleAiTools, wachtrij voor 'Mag wachten' (daluren, Flex-tarief) in AiAnalyseService.
builder.Services.AddSingleton<BedrockClient>();
builder.Services.AddSingleton<FinancieleAiTools>();
builder.Services.AddSingleton<AlgemeneAiTools>();
builder.Services.AddSingleton<BestelregelLezer>();
builder.Services.AddSingleton<RelatieLezer>();
builder.Services.AddSingleton<AiSignaal>();
builder.Services.AddSingleton<AiAnalyseService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AiAnalyseService>());

// Factuurinbox: de postbus invoice@ op de achtergrond lezen (sectie "FactuurInbox"); bonnetjes komen via MDE mobiel.
builder.Services.AddSingleton<FactuurInboxOphaler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FactuurInboxOphaler>());
// Orderbevestigingen: de inkooppostbus op de achtergrond lezen en PDF's met een PO-nummer bij de bestelling zetten
builder.Services.AddSingleton<InkoopBevestigingOphaler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<InkoopBevestigingOphaler>());

// Rabobank Business Account Insight: elk uur de bankafschriften ophalen (sectie
// "Rabobank" in appsettings.json). In de sandbox wordt er nooit iets weggeschreven.
builder.Services.AddSingleton<RabobankKoppeling>();
builder.Services.AddSingleton<RabobankOphaler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabobankOphaler>());

builder.Services.AddScoped<UrenService>();
builder.Services.AddSingleton<RecentPagesService>();
builder.Services.AddSingleton<FilestoreService>();
builder.Services.AddSingleton<FactuurPdfService>();
builder.Services.AddSingleton<PakbonPdfService>();
builder.Services.AddSingleton<OffertePdfService>();
builder.Services.AddSingleton<DuurtestService>();
builder.Services.AddSingleton<ArtikelFotoService>();   // artikelfoto uit klembord/bestand in de fotomap
builder.Services.AddSingleton<AgentUitgave>();
builder.Services.AddSingleton<InventorAddInUitgave>();   // Inventor-add-in als zip uit de share (Tools > Inventor > Add-in installeren)
builder.Services.AddSingleton<FusionAddInUitgave>();     // Fusion-add-in als zip uit de share (Tools > Fusion > Add-in installeren)          // nieuwste agentversie op de share (publiceer-z.bat)
builder.Services.AddScoped<WerkplekAgent>();
builder.Services.AddScoped<ExemplaarHerkenning>();   // welk exemplaar hangt er aan de EtherCAT-adapter (auto-detect)           // per browser: is er een MDE Werkplek-agent op deze pc?   // duurtestlog van de testrig-PLC ophalen (FTP)

// NDA: document maken uit het Word-sjabloon, opslaan in de filestore, en de
// eenmalige overname van de oude Python-app.
builder.Services.AddSingleton<NdaDocumentService>();
// Typeplaatje voor de laser (SVG + PDF)
builder.Services.AddSingleton<LabelService>();
builder.Services.AddSingleton<SonosService>();
builder.Services.AddSingleton<MeldingSignaal>();
builder.Services.AddSingleton<BelCentrale>();        // videobellen op het intranet (Components/Layout/Videobellen.razor)
builder.Services.AddSingleton<FotoBrug>();           // de telefoon als camera voor een scherm op de pc (Components/FotoVak.razor, /mobiel/foto)
builder.Services.AddScoped<BelVerzoek>();           // "bel deze collega" vanuit de berichtenbox naar het belvenster
builder.Services.AddSingleton<AltecPrinter>();
builder.Services.AddSingleton<FusionPakketten>();
builder.Services.AddSingleton<FirmwarePakketten>();
builder.Services.AddSingleton<ComponentZoeker>();
builder.Services.AddSingleton<JlcKoppeling>();
builder.Services.AddSingleton<JlcOnderdelen>();
// Word/Excel in beeld brengen door ze naar PDF om te zetten
builder.Services.AddSingleton<VoorbeeldService>();

// Gebouwbeheer: verlichting, zonwering en compressor via de PLC.
// Met Plc:Adres gaat het echt naar de Omron NJ over EtherNet/IP; zonder dat
// adres draait alles op de demoversie en gebeurt er niets in het gebouw.
var plcAdres = builder.Configuration["Plc:Adres"];
if (!string.IsNullOrWhiteSpace(plcAdres) && !plcAdres.Contains("REPLACE_"))
    builder.Services.AddSingleton<IPlc, OmronPlc>();
else
    builder.Services.AddSingleton<IPlc, DemoPlc>();
builder.Services.AddScoped<NdaService>();
builder.Services.AddScoped<NdaImportService>();

// Google Agenda: tokens onthouden + de API aanspreken
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AgendaTokens>();
builder.Services.AddScoped<AgendaService>();

// Gmail: eigen postbus (token van de gebruiker) en gedeelde postbussen
// (serviceaccount met domeinbrede delegatie).
builder.Services.AddSingleton<GoogleServiceAccount>();
builder.Services.AddSingleton<MailPostbussen>();
builder.Services.AddScoped<MailService>();

// ---------------------------------------------------------------------------
// Inloggen met Google (Google Workspace).
// Zolang Google:ClientId nog niet is ingevuld, blijft de app open en werkt
// alles zoals voorheen — zo kan een onvolledige configuratie niets breken.
// ---------------------------------------------------------------------------
var googleId = builder.Configuration["Google:ClientId"];
var googleSecret = builder.Configuration["Google:ClientSecret"];
var googleDomein = builder.Configuration["Google:ToegestaanDomein"];
var loginAan = !string.IsNullOrWhiteSpace(googleId)
            && !googleId!.Contains("REPLACE_")
            && !string.IsNullOrWhiteSpace(googleSecret)
            && !googleSecret!.Contains("REPLACE_");

var authBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "MDEApp";
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // ook bruikbaar via http (test)
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/geen-toegang";
        // Werkplek (gekoppelde pc): bij elk verzoek kijken of het apparaat nog gekoppeld is (WerkplekLogin).
        o.Events.OnValidatePrincipal = WerkplekLogin.ControleerAsync;
    });

if (loginAan)
{
    authBuilder.AddGoogle(o =>
    {
        o.ClientId = googleId!;
        o.ClientSecret = googleSecret!;
        o.CallbackPath = "/signin-google";
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Toegang tot de agenda van de ingelogde gebruiker (lezen én afspraken maken).
        o.Scope.Add("https://www.googleapis.com/auth/calendar.events");

        // Gmail: meelezen bij een relatie en mail versturen namens de gebruiker.
        // Let op: na het toevoegen van deze machtigingen moet iedereen één keer
        // uitloggen en opnieuw inloggen, anders geeft Gmail 403 terug.
        o.Scope.Add("https://www.googleapis.com/auth/gmail.readonly");
        o.Scope.Add("https://www.googleapis.com/auth/gmail.send");
        // modify = lezen en opruimen (prullenbak, archiveren, gelezen markeren).
        // Definitief verwijderen kan de app bewust níet.
        o.Scope.Add("https://www.googleapis.com/auth/gmail.modify");
        // Geen tokens in het inlogcookie: OnCreatingTicket hieronder zet ze al in AgendaTokens
        // (Blazor kan ze uit het cookie toch niet lezen). Met tokens erin werd het cookie zo
        // groot dat het in stukken ging, en oude stukken bleven op iPhones hangen.
        o.SaveTokens = false;
        o.AccessType = "offline";        // levert een refresh-token, zodat het na een uur blijft werken

        // Alleen accounts van het eigen Workspace-domein toelaten,
        // en de tokens opvangen voor de agenda-koppeling.
        o.Events.OnCreatingTicket = ctx =>
        {
            var email = ctx.Identity?.FindFirst(ClaimTypes.Email)?.Value ?? "";

            if (!string.IsNullOrWhiteSpace(googleDomein)
                && !email.EndsWith("@" + googleDomein, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Fail($"Alleen accounts van @{googleDomein} hebben toegang (aangemeld met: {email}).");
                return Task.CompletedTask;
            }

            var tokens = ctx.HttpContext.RequestServices.GetRequiredService<AgendaTokens>();
            tokens.Bewaar(email, ctx.AccessToken, ctx.RefreshToken,
                          ctx.ExpiresIn is { } t ? (int)t.TotalSeconds : null);

            // Google geeft het refresh-token alleen bij (nieuwe) toestemming. Is er nog geen (ook niet bewaard), dan werkt
            // mail en agenda maar een uur: meteen nog één keer langs Google met prompt=consent (11-11-2026). Daarna wordt
            // het bewaard (AgendaTokens) en is dit niet meer nodig, ook niet na een update.
            if (!tokens.HeeftRefresh(email) && !ctx.Properties.Items.ContainsKey("toestemming"))
            {
                var terug = string.IsNullOrWhiteSpace(ctx.Properties.RedirectUri) ? ctx.Request.PathBase + "/" : ctx.Properties.RedirectUri;
                ctx.Properties.RedirectUri = ctx.Request.PathBase + "/login?toestemming=1&terug=" + Uri.EscapeDataString(terug);
            }

            return Task.CompletedTask;
        };

        // Bij inloggen altijd de accountkeuze tonen (zodat je van gebruiker kunt wisselen
        // en 'uitloggen' ook echt merkbaar is), en meteen het juiste domein voorstellen.
        o.Events.OnRedirectToAuthorizationEndpoint = ctx =>
        {
            // toestemming=1 (zie OnCreatingTicket): ook om toestemming vragen, dan stuurt Google een refresh-token
            var url = ctx.RedirectUri + "&prompt=" + Uri.EscapeDataString(
                ctx.Properties.Items.ContainsKey("toestemming") ? "select_account consent" : "select_account");
            if (!string.IsNullOrWhiteSpace(googleDomein))
                url += "&hd=" + Uri.EscapeDataString(googleDomein!);
            ctx.Response.Redirect(url);
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Wie is ingelogd + welke rechten (uit de Werknemers-tabel).
builder.Services.AddScoped<HuidigeGebruiker>();
// Opnieuw aanmelden bij Google midden in een actie (GoogleHerstelVenster in de layouts).
builder.Services.AddScoped<GoogleHerstel>();
// melding (en mail) "PO-… is compleet geleverd": Ontvangst en het gereed melden in Bestellingen
builder.Services.AddScoped<GeleverdMelding>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Reverse-proxy ondersteuning: deze app kan zowel standalone draaien (eigen
// poort, PathBase leeg) als achter Caddy op een pad-voorvoegsel zoals /mde/.
// ForwardedHeaders zorgt dat de app het juiste (https-)protocol en de juiste
// host herkent van het request dat Caddy doorstuurt (nodig o.a. voor een
// correcte Google-redirect-URL).
//
// PathBase: Caddy's "handle_path" strip het voorvoegsel (bv. "/mde") al vóór
// het doorsturen, dus deze app ziet in Request.Path zelf nooit "/mde" meer
// staan — de ingebouwde app.UsePathBase("/mde") zou daarom niets vinden om te
// strippen. In plaats daarvan stuurt Caddy het gestripte voorvoegsel mee als
// header X-Forwarded-Prefix (zie Caddyfile), en zetten we Request.PathBase
// hier handmatig op basis van die header. App.razor (<base href>) en alle
// url-generatie in Blazor gebruiken vervolgens automatisch deze PathBase.
// ---------------------------------------------------------------------------
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.Use((context, next) =>
{
    var forwardedPrefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();
    if (!string.IsNullOrEmpty(forwardedPrefix))
    {
        context.Request.PathBase = forwardedPrefix;
    }
    return next();
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// HuidigeGebruiker leest de ingelogde gebruiker via de AuthenticationStateProvider. Binnen Blazor zet Blazor die zelf;
// bij de losse adressen hieronder (calculaties/api, bestand, artikelfoto, Fusion-download) niet, waardoor iedereen daar
// "niet ingelogd" was: de calculatieboom stond op alleen lezen (gevonden 10-11-2026). Daarom per verzoek zetten.
app.Use((ctx, next) =>
{
    if (ctx.RequestServices.GetService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>()
        is Microsoft.AspNetCore.Components.Authorization.IHostEnvironmentAuthenticationStateProvider host)
        host.SetAuthenticationState(Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(ctx.User)));
    return next();
});
app.UseAntiforgery();

// --- inlog- en uitlogadressen ---
// Let op: "/" en "/login" etc. hieronder zijn root-absolute paden. Onder een
// reverse-proxy pad-voorvoegsel (PathBase, bv. "/mde") moeten die expliciet
// met PathBase geprefixt worden — anders navigeert de browser per ongeluk
// naar de root van het domein in plaats van terug naar "/mde/...".
// popup=1: opnieuw aanmelden in een klein venster (GoogleHerstelVenster) en daarna terug naar /google-klaar, dat het venster
// sluit; het scherm eronder blijft staan en de onderbroken actie (een mail) gaat verder. toestemming=1: ook om toestemming
// vragen (zie OnCreatingTicket).
app.MapGet("/login", async (HttpContext ctx, string? terug, string? popup, string? toestemming) =>
{
    var thuis = ctx.Request.PathBase + "/";
    if (!loginAan) { ctx.Response.Redirect(thuis); return; }
    // alleen paden binnen de app als terugadres (geen open redirect naar een andere site)
    if (!string.IsNullOrWhiteSpace(terug) && (!terug.StartsWith('/') || terug.StartsWith("//"))) terug = null;
    var eigenschappen = new AuthenticationProperties
    {
        RedirectUri = popup == "1" ? ctx.Request.PathBase + "/google-klaar" : string.IsNullOrWhiteSpace(terug) ? thuis : terug,
        // Zonder dit is het inlogkoekje een sessiekoekje en gooit de browser
        // het weg bij het afsluiten — dan moet iedereen elke ochtend opnieuw
        // inloggen. Zo blijft het de ingestelde 14 dagen (glijdend) geldig.
        IsPersistent = true,
    };
    if (toestemming == "1") eigenschappen.Items["toestemming"] = "1";
    await ctx.ChallengeAsync(GoogleDefaults.AuthenticationScheme, eigenschappen);
});

// Einde van het aanmelden in een pop-up: venster dicht (het scherm in het hoofdvenster merkt dat en gaat verder).
app.MapGet("/google-klaar", (HttpContext ctx) => Results.Content(Pagina(
    "Aangemeld",
    "<p>Je bent weer aangemeld bij Google. Dit venster sluit vanzelf.</p>" +
    $"<script>if (window.opener) window.close(); else location.href = '{ctx.Request.PathBase}/';</script>"), "text/html"));

// Uitloggen: cookie wissen en naar een pagina die géén login vereist. Zou je naar "/"
// terugsturen, dan volgt meteen een nieuwe login en lijkt uitloggen niet te werken.
app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect(ctx.Request.PathBase + "/uitgelogd");
});

app.MapGet("/uitgelogd", (HttpContext ctx) => Results.Content(Pagina(
    "Je bent uitgelogd",
    "<p>Je bent afgemeld bij MDE.</p>" +
    $"<p><a class=\"knop\" href=\"{ctx.Request.PathBase}/login\">Opnieuw inloggen</a></p>" +
    "<p class=\"klein\">Wil je ook je Google-account afmelden op deze computer, " +
    "doe dat dan via je Google-profiel in de browser.</p>"), "text/html"));

app.MapGet("/geen-toegang", (HttpContext ctx) => Results.Content(Pagina(
    "Geen toegang",
    "<p>Dit Google-account hoort niet bij MDE Automation, of is niet gekoppeld aan een werknemer.</p>" +
    $"<p><a class=\"knop\" href=\"{ctx.Request.PathBase}/logout\">Uitloggen en opnieuw proberen</a></p>"), "text/html"));

// Kleine, eigen opmaak voor de pagina's buiten de app (geen login nodig).
// Let op: geen string-interpolatie hier, omdat de CSS accolades bevat.
static string Pagina(string titel, string inhoud)
{
    const string sjabloon = @"<!DOCTYPE html><html lang=""nl""><head><meta charset=""utf-8"">
<title>[TITEL] — MDE</title><style>
body{font-family:""Segoe UI"",system-ui,sans-serif;background:#fafafa;color:#222;
display:flex;justify-content:center;padding-top:12vh;margin:0}
.kaart{background:#fff;border:1px solid #ddd;border-radius:8px;padding:1.5rem 2rem;max-width:32rem}
h1{color:#1a5fb4;font-size:1.4rem;margin:0 0 .8rem}
a.knop{display:inline-block;background:#1a5fb4;color:#fff;text-decoration:none;
padding:.5rem 1rem;border-radius:5px}
a.knop:hover{background:#14498a}
.klein{color:#777;font-size:.9em}
</style></head><body><div class=""kaart""><h1>[TITEL]</h1>[INHOUD]</div></body></html>";

    return sjabloon.Replace("[TITEL]", titel).Replace("[INHOUD]", inhoud);
}

// --- werkplek koppelen: een pc met een code uit Beheer ▸ Werkplekken als werkplek laten inloggen (geen Google) ---
static string KoppelFormulier(string pathBase, string? fout) =>
    (fout is null ? "" : $"<p style=\"color:#a01200\"><b>{System.Net.WebUtility.HtmlEncode(fout)}</b></p>") +
    "<p>Vul de koppelcode in die Beheer bij de werkplek heeft gemaakt (Beheer ▸ Werkplekken). Deze pc blijft daarna " +
    "ingelogd als die werkplek, tot hij in het beheerscherm wordt ontkoppeld.</p>" +
    $"<form method=\"post\" action=\"{pathBase}/werkplek/koppelen\">" +
    "<input name=\"code\" autocomplete=\"off\" autofocus maxlength=\"12\" placeholder=\"bv. K7MQ2P\" " +
    "style=\"font-size:1.4rem;letter-spacing:.2em;text-transform:uppercase;padding:.4rem .6rem;width:10rem;border:1px solid #bbb;border-radius:5px\"> " +
    "<button type=\"submit\" style=\"font-size:1rem;padding:.55rem 1rem;background:#1a5fb4;color:#fff;border:none;border-radius:5px;cursor:pointer\">Koppelen</button>" +
    "</form><p class=\"klein\">Geen code? Vraag iemand met het recht Beheer of Directie.</p>";

// --- nieuwe versie aankondigen (service-herstarten.bat, vóór het stoppen): alleen vanaf de server zelf ---
// Het script roept http://127.0.0.1:5010 rechtstreeks aan. Een verzoek via Caddy draagt X-Forwarded-For (en krijgt van
// UseForwardedHeaders het echte adres), dus dat telt nooit als "van de server".
static bool VanDezeServer(HttpContext ctx)
    => ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)
       && !ctx.Request.Headers.ContainsKey("X-Forwarded-For") && !ctx.Request.Headers.ContainsKey("X-Original-For");
app.MapPost("/onderhoud/aankondigen", (HttpContext ctx, OnderhoudMelding melding, double? minuten, string? tekst) =>
{
    if (!VanDezeServer(ctx)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var a = melding.Kondig(minuten ?? 3, tekst, "publiceerscript");
    return Results.Text($"Aangekondigd: de app stopt om {a.Om:HH:mm:ss}; {melding.OpenSchermen} scherm(en) open gewaarschuwd.");
}).DisableAntiforgery();
app.MapPost("/onderhoud/intrekken", (HttpContext ctx, OnderhoudMelding melding) =>
{
    if (!VanDezeServer(ctx)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    melding.TrekIn();
    return Results.Text("Aankondiging ingetrokken.");
}).DisableAntiforgery();

app.MapGet("/werkplek/koppelen", (HttpContext ctx) =>
{
    var nu = WerkplekLogin.WerkplekId(ctx.User) is not null
        ? $"<p>Deze pc is nu gekoppeld als werkplek <b>{System.Net.WebUtility.HtmlEncode(ctx.User.Identity?.Name ?? "")}</b>. " +
          "Een nieuwe code vervangt dat.</p>"
        : "";
    return Results.Content(Pagina("Werkplek koppelen", nu + KoppelFormulier(ctx.Request.PathBase, null)), "text/html");
});

app.MapPost("/werkplek/koppelen", async (HttpContext ctx, IWerkplekRepository repo) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var omschrijving = $"{ctx.Request.Headers.UserAgent} · {ctx.Connection.RemoteIpAddress}";
    (Werkplek Werkplek, int ApparaatId)? gekoppeld = null;
    try { gekoppeld = await repo.KoppelAsync(form["code"].ToString(), omschrijving); }
    catch { }
    if (gekoppeld is not { } g)
    {
        await Task.Delay(600);                                   // raden van codes ontmoedigen
        return Results.Content(Pagina("Werkplek koppelen",
            KoppelFormulier(ctx.Request.PathBase, "Deze code klopt niet of is verlopen (een code is 30 minuten geldig).")), "text/html");
    }
    WerkplekLogin.Vergeet(g.ApparaatId);                         // geen oude uitkomst van de controle laten hangen
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        WerkplekLogin.Maak(g.Werkplek, g.ApparaatId), WerkplekLogin.Eigenschappen());
    var start = g.Werkplek.Startpagina.Trim().TrimStart('/');
    return Results.Redirect(ctx.Request.PathBase + "/" + (start.Contains(':') ? "" : start));
}).DisableAntiforgery();

// --- bestand uit de filestore ophalen ---
// /bestand/12          -> in de browser bekijken (pdf's, afbeeldingen)
// /bestand/12/download -> opslaan
// Vertrouwelijke soorten (personeelsdossier: HRM/Directie; BV-documenten: Financial/Directie) en bijlagen van berichten.
// Geldt ook voor /voorbeeld/{id} (daar zat tot 9-11-2026 geen controle op).
async Task<bool> MagBestand(int id, int storeType, HuidigeGebruiker ik, IMeldingRepository meldingen)
{
    if (storeType is 300 or 301 or FilestoreService.StoreTypeBvDocument or FilestoreService.StoreTypeVerzekering)
    {
        await ik.ZorgGeladenAsync();
        if (!FilestoreService.MagSoortZien(storeType, ik)) return false;
    }
    // plaatje bij een bericht aan een collega: alleen voor de ontvanger en de afzender (berichten zijn privé)
    if (storeType == BerichtBijlage.StoreType)
    {
        await ik.ZorgGeladenAsync();
        if (ik.WerknemerId is not int wn || !await meldingen.MagBijlageZienAsync(id, wn)) return false;
    }
    return true;
}

async Task<IResult> BestandUitgeven(int id, bool alsDownload,
                                    FilestoreService store, HuidigeGebruiker ik, IMeldingRepository meldingen)
{
    var gevonden = await store.ZoekAsync(id);
    if (gevonden is null) return Results.NotFound("Bestand niet gevonden.");

    var (pad, naam, storeType) = gevonden.Value;
    if (!await MagBestand(id, storeType, ik, meldingen)) return Results.Forbid();

    var mime = FilestoreService.MimeVoor(naam);
    return alsDownload
        ? Results.File(pad, mime, naam)   // met bestandsnaam = opslaan
        : Results.File(pad, mime);        // zonder = in de browser tonen
}

app.MapGet("/bestand/{id:int}",
    (int id, FilestoreService store, HuidigeGebruiker ik, IMeldingRepository meldingen) => BestandUitgeven(id, false, store, ik, meldingen));

app.MapGet("/bestand/{id:int}/download",
    (int id, FilestoreService store, HuidigeGebruiker ik, IMeldingRepository meldingen) => BestandUitgeven(id, true, store, ik, meldingen));

// --- klein voorbeeld van een projectfoto (Projecten ▸ project ▸ tab Foto's); de foto zelf gaat via /bestand/{id} ---
var projectFotoKlein = app.MapGet("/projectfoto/{id:int}/klein", async (int id, IProjectFotoRepository fotos, HttpContext http) =>
{
    var klein = await fotos.MiniatuurAsync(id);
    if (klein is null || klein.Length == 0) return Results.NotFound();
    http.Response.Headers.CacheControl = "private, max-age=86400";
    return Results.File(klein, "image/jpeg");
});
if (loginAan) projectFotoKlein.RequireAuthorization();

// --- installer van de MDE Werkplek-agent: de nieuwste van de share (AgentUitgave), voor de knop in de melding
// "nieuwere agent" (Components/AgentVersieMelding.razor). Een link naar een netwerkmap blokkeert de browser. ---
var agentInstaller = app.MapGet("/agent/installer", (AgentUitgave uitgave) =>
    uitgave.NieuwstePad() is { } pad && File.Exists(pad)
        ? Results.File(pad, "application/octet-stream", Path.GetFileName(pad))
        : Results.NotFound("Geen installer van de MDE Agent gevonden op de share."));
if (loginAan) agentInstaller.RequireAuthorization();

// --- de hele MDE Agent-installatie als zip (Tools ▸ MDE Agent): installeer.bat, LEESMIJ, certificaat en de installer.
// Geen apart recht (net als Test PCB), wel aangemeld; het token zit er niet in. ---
var agentZip = app.MapGet("/tools/mde-agent/download", async (AgentUitgave uitgave) =>
    await uitgave.ZipAsync() is { } pad
        ? Results.File(pad, "application/zip", Path.GetFileName(pad))
        : Results.NotFound("De MDE Agent staat niet (volledig) op de share (MdeAgent\\publiceer-z.bat)."));
if (loginAan) agentZip.RequireAuthorization();

// --- Calculaties (Projecten ▸ Offertes en calculaties): de boompagina wwwroot/calculatieboom.html haalt en bewaart hier
// zijn gegevens. Lezen: aangemeld; wijzigen en bevriezen: een gekoppelde werknemer (zoals bij offertes) of Directie;
// ontdooien: alleen Directie. Bevroren mogen alleen de notities nog veranderen (CalculatieData.Kern). ---
static bool MagCalculatie(HuidigeGebruiker ik) => ik.Gekoppeld || ik.Mag("Directie");
var calcJson = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var calcApi = app.MapGroup("/calculaties/api");
if (loginAan) calcApi.RequireAuthorization();
calcApi.MapGet("/{offerteId:int}", async (int offerteId, ICalculatieRepository repo, IOfferteRepository offertes, HuidigeGebruiker ik,
                                          IWerkorderDelenRepository delen) =>
{
    await ik.ZorgGeladenAsync();
    var o = await offertes.GetAsync(offerteId);
    var c = o is null ? null : await repo.GetAsync(offerteId);
    if (o is null || c is null) return Results.Json(new { fout = "Deze offerte heeft (nog) geen calculatie." }, statusCode: 404);
    var act = (await repo.TarievenAsync()).Select(t => new { id = t.ActiviteitId, naam = t.Naam, tarief = t.Kostprijs ?? 0m });
    var woTabel = await repo.WerkorderTabelAanwezigAsync();
    var werkorders = woTabel ? await repo.WerkordersAsync(offerteId) : new List<CalculatieWerkorder>();
    var boeken = await delen.BeschikbaarAsync();
    var stand = boeken && werkorders.Count > 0 ? await delen.StandAsync(werkorders.Select(w => w.WerkbonId).ToList()) : new List<DeelStand>();
    var wanneer = c.Bevroren && c.BevrorenOp is DateTime b
        ? $"Bevroren {b:dd-MM-yyyy HH:mm}{(c.BevrorenDoor.Length > 0 ? " door " + c.BevrorenDoor : "")}"
        : c.GewijzigdOp is DateTime g ? $"Opgeslagen {g:dd-MM-yyyy HH:mm}{(c.GewijzigdDoor.Length > 0 ? " door " + c.GewijzigdDoor : "")}" : "";
    return Results.Json(new
    {
        info = new { offertenr = o.OfferteNr, offerte = o.Omschrijving, project = $"{o.Projectnummer} · {o.Project}", projectnr = o.Projectnummer, klant = o.Klant, bv = o.Bv,
                     projectId = o.ProjectId, volgendWerkbon = await repo.VolgendWerkbonnummerAsync(o.ProjectId), werkorderTabel = woTabel,
                     boekenOpDelen = boeken },
        stand = stand.Select(s => new { werkbonId = s.WerkbonId, deel = s.DeelId, naam = s.Naam, begroot = s.BegroteUren, geboekt = s.Geboekt }),
        werkorders = werkorders.Select(w => new { groep = w.GroepId, werkbonId = w.WerkbonId, nummer = w.Nummer, omschrijving = w.Omschrijving,
                                                   budget = w.Budget, op = w.AangemaaktOp?.ToString("dd-MM-yyyy HH:mm") ?? "", door = w.AangemaaktDoor }),
        act,
        data = System.Text.Json.Nodes.JsonNode.Parse(c.Data),
        versie = c.Versie,
        status = c.Status,
        mag = MagCalculatie(ik),
        magOntdooien = ik.Mag("Directie"),
        gebruiker = ik.Naam,
        gewijzigd = wanneer,
    });
});
calcApi.MapPut("/{offerteId:int}", async (int offerteId, HttpRequest req, ICalculatieRepository repo, HuidigeGebruiker ik) =>
{
    await ik.ZorgGeladenAsync();
    if (!MagCalculatie(ik)) return Results.Json(new { fout = "Je mag deze calculatie niet wijzigen." }, statusCode: 403);
    System.Text.Json.Nodes.JsonNode? body;
    try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(req.Body); } catch { body = null; }
    if (body?["data"] is not System.Text.Json.Nodes.JsonObject data || body["versie"] is null)
        return Results.Json(new { fout = "Geen geldige calculatie ontvangen." }, statusCode: 400);
    var tekst = data.ToJsonString(calcJson);
    if (CalculatieData.Controleer(tekst) is { } reden) return Results.Json(new { fout = reden }, statusCode: 400);
    var c = await repo.GetAsync(offerteId);
    if (c is null) return Results.Json(new { fout = "Deze calculatie bestaat niet (meer)." }, statusCode: 404);
    if (c.Bevroren && CalculatieData.Kern(tekst) != CalculatieData.Kern(c.Data))
        return Results.Json(new { fout = "De calculatie is bevroren (klant akkoord): alleen notities kunnen nog veranderen. Vernieuw de pagina." }, statusCode: 423);
    decimal Getal(string k) { try { return body["totalen"]?[k]?.GetValue<decimal>() ?? 0; } catch { return 0; } }
    int versie;
    try { versie = body["versie"]!.GetValue<int>(); } catch { return Results.Json(new { fout = "Geen geldige versie ontvangen." }, statusCode: 400); }
    try
    {
        var nieuw = await repo.BewaarAsync(offerteId, versie, tekst, Getal("kost"), Getal("verkoop"), Getal("uren"), ik.WerknemerId);
        return Results.Json(new { versie = nieuw });
    }
    catch (CalculatieConflictException ex) { return Results.Json(new { fout = ex.Message }, statusCode: 409); }
});
// De calculatie als JSON-bestand, in het formaat van de losse calculatieboom (bewaren vóór het wissen, of buiten de app
// verder werken; importeren kan bij een offerte zonder calculatie).
calcApi.MapGet("/{offerteId:int}/export", async (int offerteId, ICalculatieRepository repo, IOfferteRepository offertes) =>
{
    var o = await offertes.GetAsync(offerteId);
    var c = o is null ? null : await repo.GetAsync(offerteId);
    if (o is null || c is null) return Results.Json(new { fout = "Deze offerte heeft (nog) geen calculatie." }, statusCode: 404);
    var json = CalculatieData.NaarExport(c.Data, $"{o.OfferteNr} {o.Omschrijving}".Trim(), o.Klant, o.OfferteNr, c.Bevroren, c.GewijzigdOp);
    var naam = string.Concat($"Calculatie {o.OfferteNr} {o.Omschrijving}".Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    if (naam.Length > 100) naam = naam[..100].Trim();
    return Results.File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", naam + ".json");
});
// Werkorders aanmaken bij klant akkoord (calculaties stap 2): het plan komt uit de boom (werkorderPlan), de app
// controleert het (bevroren, versie, opbouw, budget = verkoop van de calculatie) en maakt alles in één transactie.
calcApi.MapPost("/{offerteId:int}/werkorders", async (int offerteId, HttpRequest req, ICalculatieRepository repo, HuidigeGebruiker ik) =>
{
    await ik.ZorgGeladenAsync();
    if (!MagCalculatie(ik)) return Results.Json(new { fout = "Je mag voor deze calculatie geen werkorders aanmaken." }, statusCode: 403);
    var c = await repo.GetAsync(offerteId);
    if (c is null) return Results.Json(new { fout = "Deze calculatie bestaat niet (meer)." }, statusCode: 404);
    if (!c.Bevroren) return Results.Json(new { fout = "Werkorders aanmaken kan pas als de calculatie bevroren is (klant akkoord)." }, statusCode: 423);
    if (!await repo.WerkorderTabelAanwezigAsync())
        return Results.Json(new { fout = "Draai eerst CalculatieWerkorders_MariaDB.sql op de database." }, statusCode: 503);

    var plan = new List<WerkorderPlanRij>();
    int versie;
    try
    {
        var body = await System.Text.Json.Nodes.JsonNode.ParseAsync(req.Body) ?? throw new FormatException();
        versie = body["versie"]!.GetValue<int>();
        static decimal G(System.Text.Json.Nodes.JsonNode? n) => n is null ? 0 : Math.Round(n.GetValue<decimal>(), 4);
        foreach (var r in body["rijen"]!.AsArray())
        {
            var delen = (r!["delen"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
                .Select(d => new WerkorderPlanDeel((d!["naam"]?.GetValue<string>() ?? "").Trim(), G(d["uren"]), G(d["kosten"]),
                                                   d["act"] is { } a ? a.GetValue<int>() : null))
                .ToList();
            plan.Add(new WerkorderPlanRij(r["groep"]!.GetValue<int>(), r["ouder"] is { } o ? o.GetValue<int>() : null,
                (r["naam"]?.GetValue<string>() ?? "").Trim(), G(r["budget"]), G(r["kostUur"]), G(r["kostMat"]), G(r["uren"]), delen));
        }
    }
    catch { return Results.Json(new { fout = "Geen geldig werkorderplan ontvangen." }, statusCode: 400); }

    // het plan moet kloppen: ouders vóór kinderen, namen ingevuld, delen uniek, en samen precies de verkoop van de calculatie
    if (plan.Count == 0) return Results.Json(new { fout = "Er is geen groep met een werkorder." }, statusCode: 400);
    if (plan.Count > 300) return Results.Json(new { fout = "Meer dan 300 werkorders: dat lijkt niet te kloppen." }, statusCode: 400);
    var gezien = new HashSet<int>();
    foreach (var r in plan)
    {
        if (r.Naam.Length == 0) return Results.Json(new { fout = "Een groep met een werkorder heeft geen naam." }, statusCode: 400);
        if (r.Ouder is int og && !gezien.Contains(og)) return Results.Json(new { fout = "De opbouw van de werkorders klopt niet (ouder na kind)." }, statusCode: 400);
        if (!gezien.Add(r.Groep)) return Results.Json(new { fout = "Een groep staat twee keer in het plan." }, statusCode: 400);
        if (r.Delen.Any(d => d.Naam.Length == 0) || r.Delen.GroupBy(d => d.Naam.ToLowerInvariant()).Any(g => g.Count() > 1))
            return Results.Json(new { fout = $"Werkorder {r.Naam}: een deel zonder naam of twee keer dezelfde naam." }, statusCode: 400);
    }
    var som = plan.Sum(r => r.Budget);
    if (Math.Abs(som - c.Verkoop) > 1m)
        return Results.Json(new { fout = $"De budgetten ({som:N2}) zijn samen niet de verkoopprijs van de calculatie ({c.Verkoop:N2}). Vernieuw de pagina en probeer opnieuw." }, statusCode: 409);

    try
    {
        var gemaakt = await repo.MaakWerkordersAsync(offerteId, versie, plan, ik.WerknemerId);
        return Results.Json(new
        {
            werkorders = gemaakt.Select(w => new { groep = w.GroepId, werkbonId = w.WerkbonId, nummer = w.Nummer, omschrijving = w.Omschrijving,
                                                   budget = w.Budget, op = w.AangemaaktOp?.ToString("dd-MM-yyyy HH:mm") ?? "", door = w.AangemaaktDoor }),
        });
    }
    catch (InvalidOperationException ex) { return Results.Json(new { fout = ex.Message }, statusCode: 409); }
});
calcApi.MapPost("/{offerteId:int}/{actie}", async (int offerteId, string actie, ICalculatieRepository repo, HuidigeGebruiker ik) =>
{
    await ik.ZorgGeladenAsync();
    var c = await repo.GetAsync(offerteId);
    if (c is null) return Results.Json(new { fout = "Deze calculatie bestaat niet (meer)." }, statusCode: 404);
    int nieuw;
    if (actie == "bevries")
    {
        if (!MagCalculatie(ik)) return Results.Json(new { fout = "Je mag deze calculatie niet bevriezen." }, statusCode: 403);
        nieuw = CalculatieStatus.Bevroren;
    }
    else if (actie == "ontdooi")
    {
        if (!ik.Mag("Directie")) return Results.Json(new { fout = "Alleen Directie kan een bevroren calculatie weer bewerkbaar maken." }, statusCode: 403);
        // besluit gebruiker 10-11-2026: na het aanmaken van de werkorders ligt de calculatie vast (wijzigen = meerwerk)
        if ((await repo.WerkordersAsync(offerteId)).Count > 0)
            return Results.Json(new { fout = "De werkorders zijn al gemaakt: de calculatie kan niet meer ontdooid worden. Een wijziging gaat via meerwerk (een nieuwe offerte)." }, statusCode: 423);
        nieuw = CalculatieStatus.Concept;
    }
    else return Results.Json(new { fout = "Onbekende actie." }, statusCode: 404);
    if (c.Status == nieuw) return Results.Json(new { status = nieuw, versie = c.Versie });
    var versie = await repo.ZetStatusAsync(offerteId, nieuw, ik.WerknemerId);
    return Results.Json(new { status = nieuw, versie });
});

// --- de Fusion-add-in als zip (FusionAddInUitgave), alleen met het recht Fusion; voor wie niet bij de share kan ---
var fusionAddin = app.MapGet("/tools/fusion-addin/download", async (FusionAddInUitgave uitgave, HuidigeGebruiker ik) =>
{
    await ik.ZorgGeladenAsync();
    if (!ik.MagFusion) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return uitgave.Zip() is { } zip
        ? Results.File(zip, "application/zip", $"MdeFusionTools-{uitgave.KorteVersie()}.zip".Replace("-.zip", ".zip"))
        : Results.NotFound("De Fusion-add-in staat niet op de share (FusionTools, publish.ps1).");
});
if (loginAan) fusionAddin.RequireAuthorization();

// --- de Inventor-add-in als zip (InventorAddInUitgave); geen apart recht, wel aangemeld (geen geheimen in de uitgave) ---
var inventorAddin = app.MapGet("/tools/inventor-addin/download", (InventorAddInUitgave uitgave) =>
    uitgave.Zip() is { } zip
        ? Results.File(zip, "application/zip", $"MdeInventorTools-{uitgave.Uitgave()}.zip".Replace("-.zip", ".zip"))
        : Results.NotFound("De Inventor-add-in staat niet op de share (InventorTools, publish.ps1)."));
if (loginAan) inventorAddin.RequireAuthorization();

// --- bestand tonen in de browser ---
// PDF's en afbeeldingen gaan er rechtstreeks doorheen; Word en Excel worden eerst
// met LibreOffice naar PDF omgezet en daarna bewaard, zodat de tweede keer snel is.
app.MapGet("/voorbeeld/{id:int}", async (int id, VoorbeeldService voorbeeld, FilestoreService store, HuidigeGebruiker ik,
                                         IMeldingRepository meldingen) =>
{
    if (await store.ZoekAsync(id) is { } bron && !await MagBestand(id, bron.StoreType, ik, meldingen)) return Results.Forbid();
    var gevonden = await voorbeeld.VoorbeeldAsync(id);
    return gevonden is null
        ? Results.NotFound("Dit bestand kan niet worden getoond.")
        : Results.File(gevonden.Value.Pad, gevonden.Value.Mime);
});

// --- foto van een artikel ---
// De oude opzet: een aparte map met per duizend artikelen een submap.
//   Z:\Filestore\Artikelen\00000005\00005200.JPG
// De submap is het artikelnummer gedeeld door duizend, beide met acht cijfers.
// Pad instelbaar met Artikelen:FotoPad; ontbreekt de foto, dan 404.
// Foto van een artikel vervangen/toevoegen: de browser stuurt de afbeelding (uit het
// klembord of een bestand) als body; hij komt in de fotomap als 0000xxxx.jpg/.png en een
// eventuele oude foto met dat nummer gaat weg.
app.MapPost("/api/artikelfoto/{nummer:int}", async (int nummer, HttpRequest req, ArtikelFotoService fotos, HuidigeGebruiker ik) =>
{
    await ik.ZorgGeladenAsync();
    if (!fotos.Beschikbaar) return Results.Problem("Artikelen:FotoPad staat niet in appsettings.json.");
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    try { return Results.Text(await fotos.BewaarAsync(nummer, req.ContentType, ms.ToArray(), ik.Email)); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem("Opslaan mislukt: " + ex.Message); }
});

// Rabobank koppelen: eenmalig toestemming geven in Rabo Internetbankieren Professional.
// /rabobank/terug is de redirect-URI die bij de app op developer.rabobank.nl staat.
string RabobankTerug(HttpContext ctx, RabobankKoppeling bank)
    => !string.IsNullOrWhiteSpace(bank.Instellingen.RedirectUri)
        ? bank.Instellingen.RedirectUri
        : $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}/rabobank/terug";

// Let op: HuidigeGebruiker leunt op Blazor's AuthenticationStateProvider en werkt
// buiten een Blazor-pagina niet. Hier dus rechtstreeks uit het inlogcookie.
async Task<bool> MagRabobank(HttpContext ctx, IUrenRepository repo)
{
    if (!loginAan) return true;
    var email = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    if (string.IsNullOrWhiteSpace(email)) return false;
    try
    {
        var id = await repo.VindWerknemerOpEmailAsync(email);
        if (id is null) return false;
        var velden = await repo.GetWerknemerVeldenAsync(id.Value);
        bool Recht(string kolom)
        {
            if (!velden.TryGetValue(kolom, out var w) || w is null) return false;
            if (w is bool b) return b;
            try { return Convert.ToInt64(w) != 0; } catch { return false; }
        }
        return Recht("Financial") || Recht("Directie");
    }
    catch { return false; }
}

var rabobankKoppel = app.MapGet("/rabobank/koppel", async (HttpContext ctx, RabobankKoppeling bank, IUrenRepository repo) =>
{
    if (!await MagRabobank(ctx, repo)) return Results.Redirect(ctx.Request.PathBase + "/geen-toegang");
    if (!bank.Instellingen.Ingesteld)
        return Results.Content(Pagina("Rabobank", "<p>De sectie Rabobank in appsettings.json is nog niet ingevuld.</p>"), "text/html");
    return Results.Redirect(bank.AutorisatieAdres(RabobankTerug(ctx, bank)));
});

var rabobankTerug = app.MapGet("/rabobank/terug", async (HttpContext ctx, string? code, string? state, string? error,
                                                     RabobankKoppeling bank, RabobankOphaler ophaler, IUrenRepository repo) =>
{
    if (!await MagRabobank(ctx, repo)) return Results.Redirect(ctx.Request.PathBase + "/geen-toegang");
    var fout = !string.IsNullOrEmpty(error)
        ? "De Rabobank meldde: " + error
        : await bank.RondKoppelingAfAsync(code, state, RabobankTerug(ctx, bank));
    if (fout is null) _ = ophaler.HaalOpAsync();      // meteen een eerste keer ophalen
    var terug = ctx.Request.PathBase + "/financien/banktransacties?rabobank=" + (fout is null ? "ok" : "fout");
    return fout is null
        ? Results.Redirect(terug)
        : Results.Content(Pagina("Rabobank", $"<p>{System.Net.WebUtility.HtmlEncode(fout)}</p>" +
                                             $"<p><a class=\"knop\" href=\"{terug}\">Terug</a></p>"), "text/html");
});
if (loginAan) { rabobankKoppel.RequireAuthorization(); rabobankTerug.RequireAuthorization(); }

// Albumhoes van wat een Sonos-speaker speelt (SonosPaneel). Via de server: de speaker geeft een pad op zichzelf.
app.MapGet("/sonos/hoes/{uuid}", async (string uuid, SonosService sonos) =>
{
    try { return await sonos.HoesAsync(uuid) is { } h ? Results.File(h.Data, h.Soort) : Results.NotFound(); }
    catch { return Results.NotFound(); }
});

app.MapGet("/artikelfoto/{nummer:int}", (int nummer, IConfiguration config,
                                         ILoggerFactory logboek, bool? uitleg) =>
{
    var log = logboek.CreateLogger("Artikelfoto");
    var hoofdmap = config["Artikelen:FotoPad"];

    // ?uitleg=true geeft tekst in plaats van een plaatje — handig om te zien
    // waar de app kijkt als er niets verschijnt.
    IResult Klaar(string reden)
    {
        log.LogInformation("Foto {Nummer}: {Reden}", nummer, reden);
        return uitleg == true ? Results.Text(reden) : Results.NotFound(reden);
    }

    if (string.IsNullOrWhiteSpace(hoofdmap))
        return Klaar("Artikelen:FotoPad staat niet in appsettings.json.");

    var map = Path.Combine(hoofdmap, (nummer / 1000).ToString("D8"));
    var basis = nummer.ToString("D8");

    try
    {
        string? gevonden = null;

        if (Directory.Exists(map))
        {
            foreach (var ext in new[] { ".jpg", ".JPG", ".jpeg", ".JPEG", ".png", ".PNG" })
            {
                var pad = Path.Combine(map, basis + ext);
                if (File.Exists(pad)) { gevonden = pad; break; }
            }

            // Hoofdletters of een afwijkende extensie: dan op naam zoeken.
            gevonden ??= Directory.EnumerateFiles(map, basis + ".*").FirstOrDefault();
        }

        // ?uitleg=true geeft altijd het rapport — óók als de foto er wél is.
        // Zou hier de foto zelf teruggaan, dan probeert het scherm die als tekst
        // te tonen en bezwijkt de verbinding onder de omvang.
        if (uitleg == true)
            return Results.Text(FotoRapport(hoofdmap, map, basis, nummer, gevonden));

        if (gevonden is not null) return Results.File(gevonden, FilestoreService.MimeVoor(gevonden));

        return Klaar($"Geen foto voor {basis} in {map}.");
    }
    catch (Exception ex)
    {
        if (uitleg == true)
            return Results.Text($"Fout bij het lezen van {map}:\n{ex.GetType().Name}: {ex.Message}\n\n"
                              + FotoRapport(hoofdmap, map, basis, nummer, null));
        return Klaar($"Fout bij het lezen van {map}: {ex.Message}");
    }
});

/// <summary>
/// Vertelt waar de app naar de foto heeft gekeken en wat daar staat. Zonder dit
/// blijft het gissen tussen "geen rechten", "map heet anders" en "bestand heet anders".
/// </summary>
static string FotoRapport(string hoofdmap, string map, string basis, int nummer, string? gevonden)
{
    var r = new System.Text.StringBuilder();

    void Regel(string tekst = "") => r.AppendLine(tekst);
    void Kop(string tekst) { Regel(); Regel(tekst); Regel(new string('-', tekst.Length)); }

    Regel($"Foto van artikel {nummer}");
    Regel();

    if (gevonden is not null)
    {
        Regel("GEVONDEN — de foto is er en wordt getoond:");
        Regel("   " + gevonden);
        try { Regel($"   {new FileInfo(gevonden).Length / 1024} kB"); } catch { }
        Regel();
    }
    else
    {
        Regel("NIET GEVONDEN. Hieronder waar is gekeken en wat daar staat.");
        Regel();
    }

    Regel($"De app draait als : {Environment.UserDomainName}\\{Environment.UserName}");
    Regel($"Machine           : {Environment.MachineName}");

    // De waarschuwing over het computeraccount alleen tonen als die van toepassing
    // is; anders leest iedereen elke keer een uitleg over een probleem dat er niet is.
    if (Environment.UserName.EndsWith("$") && hoofdmap.StartsWith(@"\\"))
    {
        Regel();
        Regel("LET OP: dit is het computeraccount, en dat is op een andere machine");
        Regel("onbekend. Een netwerkmap weigert de toegang dan — dat is de oorzaak,");
        Regel("niet het pad of de bestandsnaam. Zet de service onder een gewoon");
        Regel("account dat óók op de bestandsserver bestaat, met hetzelfde wachtwoord");
        Regel("(services.msc > Eigenschappen > Aanmelden). Zie ook README.md.");
    }

    Kop("Waar is gekeken");
    Regel($"Hoofdmap uit appsettings : {hoofdmap}");
    Regel($"Verwachte submap         : {map}");
    Regel($"Verwachte bestandsnaam   : {basis}.jpg (of .jpeg / .png)");

    Kop("Wat er werkelijk is");

    bool Bestaat(string pad, string wat)
    {
        try
        {
            var er = Directory.Exists(pad);
            Regel($"{wat}: {(er ? "bestaat" : "BESTAAT NIET of geen toegang")}");
            return er;
        }
        catch (Exception ex)
        {
            Regel($"{wat}: fout — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    void ToonInhoud(string pad, string wat, int hoeveel)
    {
        try
        {
            var mappen = Directory.EnumerateDirectories(pad).Take(hoeveel).ToList();
            var bestanden = Directory.EnumerateFiles(pad).Take(hoeveel).ToList();

            // Alleen de eerste paar, anders is het rapport onleesbaar. Dat het een
            // greep is en niet de hele inhoud, moet er wel bij staan.
            Regel($"  submappen in {wat}: {(mappen.Count == 0 ? "(geen)" : $"(eerste {mappen.Count})")}");
            foreach (var m in mappen) Regel("    " + Path.GetFileName(m));

            Regel($"  bestanden in {wat}: {(bestanden.Count == 0 ? "(geen)" : $"(eerste {bestanden.Count})")}");
            foreach (var b in bestanden) Regel("    " + Path.GetFileName(b));
        }
        catch (Exception ex)
        {
            Regel($"  inhoud van {wat} niet te lezen — {ex.GetType().Name}: {ex.Message}");
        }
    }

    if (!Bestaat(hoofdmap, "Hoofdmap")) return r.ToString();
    ToonInhoud(hoofdmap, "de hoofdmap", 15);

    Regel();
    if (Bestaat(map, "Verwachte submap")) ToonInhoud(map, "de submap", 15);

    // Laatste poging: staat het nummer ergens in een bestandsnaam? Alleen in de
    // hoofdmap en één laag daaronder — de hele filestore doorzoeken zou te lang duren.
    Kop($"Zoeken naar '{nummer}' in de bestandsnamen (hoofdmap en één laag daaronder)");
    try
    {
        var treffers = new List<string>();

        void Zoek(string waar)
        {
            if (treffers.Count >= 10) return;
            try { treffers.AddRange(Directory.EnumerateFiles(waar, "*" + nummer + "*").Take(10)); }
            catch { /* map niet leesbaar; verder zoeken heeft nog wel zin */ }
        }

        Zoek(hoofdmap);
        foreach (var sub in Directory.EnumerateDirectories(hoofdmap).Take(2000))
        {
            if (treffers.Count >= 10) break;
            Zoek(sub);
        }

        if (treffers.Count == 0) Regel("Niets gevonden.");
        foreach (var t in treffers.Take(10)) Regel("  " + t);
    }
    catch (Exception ex)
    {
        Regel($"Zoeken mislukt — {ex.GetType().Name}: {ex.Message}");
    }

    return r.ToString();
}

// --- camerabeeld ---
// De browser vraagt het beeld aan de app, en de app aan de camera. Zo blijft het
// camerawachtwoord op de server en werkt het ook als de app op https draait
// terwijl de camera's zelf op http zitten.
var camerabeeld = app.MapGet("/camera/{nummer:int}/beeld",
    async (int nummer, bool? uitleg, CameraService cameras, HttpContext ctx) =>
{
    // ?uitleg=true vertelt welke adressen zijn geprobeerd en wat eruit kwam.
    if (uitleg == true)
        return Results.Text(await cameras.UitlegAsync(nummer, ctx.RequestAborted));

    var beeld = await cameras.BeeldAsync(nummer, ctx.RequestAborted);
    if (beeld is null) return Results.NotFound();

    // Nooit bewaren: het volgende beeld moet echt vers zijn.
    ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(beeld, "image/jpeg");
});
if (loginAan) camerabeeld.RequireAuthorization();

// --- Dymo-label van een artikel ---
// Het scherm maakt de tekening rechtstreeks via LabelService; dit adres is er om
// een label te kunnen bekijken als er twijfel is over wat er naar de printer gaat.
var dymolabel = app.MapGet("/dymolabel/{artikelId:int}", async (int artikelId, string? soort,
                                                bool? uitleg,
                                                IArtikelRepository repo, LabelService labels) =>
{
    if (!repo.Beschikbaar) return Results.NotFound("Geen live database.");

    // ?uitleg=true vertelt stap voor stap wat er met de foto gebeurt.
    if (uitleg == true) return Results.Text(await labels.FotoUitlegAsync(artikelId));

    var artikel = await repo.GetAsync(artikelId);
    if (artikel is null) return Results.NotFound("Artikel niet gevonden.");

    var groot = string.Equals(soort, "groot", StringComparison.OrdinalIgnoreCase);
    var xml = await labels.ArtikelLabelAsync(artikelId, artikel.Code, artikel.OmschrijvingNl, groot);
    return Results.Text(xml, "application/xml");
});
if (loginAan) dymolabel.RequireAuthorization();

// --- typeplaatje van een product ---
// /label/{id}      toont het label in de browser (SVG, dus scherp op elke zoom)
// /label/{id}/pdf  levert dezelfde tekening als PDF, het formaat voor de laser
async Task<IResult> LabelMaken(int productId, IProductRepository repo, LabelService labels, bool alsPdf)
{
    if (!repo.Beschikbaar) return Results.NotFound("Geen live database.");

    var gegevens = await repo.GetLabelGegevensAsync(productId);
    if (gegevens is null) return Results.NotFound("Product niet gevonden.");

    var svg = labels.MaakSvg(gegevens.Value.Type, gegevens.Value.Serienummer,
                             gegevens.Value.Bouwjaar, LabelService.CodeVoor(productId));

    if (!alsPdf) return Results.Content(svg, "image/svg+xml");

    var pdf = await labels.MaakPdfAsync(svg);
    if (pdf is null) return Results.Problem(
        "De PDF kon niet gemaakt worden. Staat LibreOffice op de server?");

    // zonder bestandsnaam: de browser toont hem, in plaats van te downloaden
    return Results.File(pdf, "application/pdf");
}

app.MapGet("/label/{productId:int}",
    (int productId, IProductRepository repo, LabelService labels) => LabelMaken(productId, repo, labels, false));

app.MapGet("/label/{productId:int}/pdf",
    (int productId, IProductRepository repo, LabelService labels) => LabelMaken(productId, repo, labels, true));

// --- welkomstscherm in de hal (Relaties ▸ Welkomstscherm) ---
// De tv opent /hal/<sleutel> zonder inlog; de sleutel (Welkomstscherm:Sleutel, minstens 12 tekens) houdt de rest
// van de wereld buiten. Verkeerde sleutel of geen sleutel ingesteld = 404, zodat er niets te raden valt.
app.MapGet("/hal/{sleutel}", (string sleutel, int? voorbeeld, HttpContext ctx, WelkomSchermService scherm) =>
    scherm.SleutelKlopt(sleutel)
        ? Results.Content(scherm.Html(ctx.Request.PathBase, voorbeeld), "text/html; charset=utf-8")
        : Results.NotFound());

app.MapGet("/hal/{sleutel}/data", async (string sleutel, int? voorbeeld, HttpContext ctx, WelkomSchermService scherm) =>
{
    if (!scherm.SleutelKlopt(sleutel)) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Content(await scherm.DataJsonAsync(ctx.Request.PathBase, voorbeeld), "application/json; charset=utf-8");
});

app.MapGet("/hal/{sleutel}/logo/{relatieId:int}", async (string sleutel, int relatieId, WelkomSchermService scherm) =>
{
    if (!scherm.SleutelKlopt(sleutel)) return Results.NotFound();
    return await scherm.RelatieLogoBestandAsync(relatieId) is { } logo ? Results.File(logo.Pad, logo.Mime) : Results.NotFound();
});

app.MapGet("/hal/{sleutel}/eigen-logo", (string sleutel, WelkomSchermService scherm) =>
{
    if (!scherm.SleutelKlopt(sleutel)) return Results.NotFound();
    return scherm.EigenLogoPad() is { } pad ? Results.File(pad, FilestoreService.MimeVoor(pad)) : Results.NotFound();
});

// --- pasfoto van een werknemer ---
// In de database staat (nog) een pad naar een bestand, bijv. op Z:. De app zoekt
// het bestand op en levert het uit, zodat de browser niet bij die map hoeft.
app.MapGet("/werknemerfoto/{werknemerId:int}", async (int werknemerId,
                                                     FilestoreService store,
                                                     IUrenRepository repo,
                                                     IWerknemerRepository wnRepo) =>
{
    try
    {
        // 1. Foto uit de filestore (StoreType 302): de gekozen hoofdfoto, anders de nieuwste
        if (wnRepo.Beschikbaar)
        {
            var fotos = await wnRepo.GetBestandenAsync(302, werknemerId);
            if (fotos.Count > 0)
            {
                var gekozen = await wnRepo.GetHoofdfotoAsync(werknemerId);
                var foto = fotos.FirstOrDefault(f => f.FileStoreId == gekozen) ?? fotos[0];

                var gevonden = await store.ZoekAsync(foto.FileStoreId);
                if (gevonden is not null)
                    return Results.File(gevonden.Value.Pad,
                                        FilestoreService.MimeVoor(gevonden.Value.Naam));
            }
        }

        // 2. Terugval: de oude kolom in Werknemers — daar staat óf een pad, óf de
        //    afbeelding zelf als bytes (Access-OLE).
        var velden = await repo.GetWerknemerVeldenAsync(werknemerId);
        foreach (var kolom in new[] { "Foto", "foto", "Pasfoto", "pasfoto", "FotoPad", "Afbeelding", "Photo" })
        {
            if (!velden.TryGetValue(kolom, out var v) || v is null) continue;

            if (v is byte[])
            {
                var ontleed = AfbeeldingHulp.Ontleed(v);
                if (ontleed is not null)
                    return Results.File(ontleed.Value.Bytes, ontleed.Value.Mime);
            }
            else if (v is string s && !string.IsNullOrWhiteSpace(s))
            {
                var pad = store.ZoekOpPad(s);
                if (pad is not null) return Results.File(pad, FilestoreService.MimeVoor(pad));
            }
        }

        return Results.NotFound();
    }
    catch
    {
        return Results.NotFound();
    }
});

// ---------------------------------------------------------------------------
// Computers melden zichzelf hier bij het starten van de app (MDE-Starten.bat →
// computer-registreren.ps1) in de tabel Computers. Geen Google-login — het
// script heeft geen browser — maar wel een gedeelde sleutel in de kop, zodat
// niet iedereen zomaar de tabel kan vullen. Sleutel leeg = meldpunt uit.
// /api/laptop blijft bestaan voor pc's met het oude script.
// ---------------------------------------------------------------------------
async Task<IResult> ComputerMelden(HttpContext ctx, IComputerRepository computers, IConfiguration config)
{
    var sleutel = config["Laptops:Sleutel"] ?? "";
    if (sleutel.Length == 0 || !computers.Beschikbaar) return Results.NotFound();
    if (ctx.Request.Headers["X-Sleutel"] != sleutel) return Results.Unauthorized();

    // Nieuw én oud formaat lezen: het oude script stuurt gebruikersnaam/merk/model/geheugenGB.
    System.Text.Json.JsonElement json;
    try { json = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>(); }
    catch { return Results.BadRequest(); }
    if (json.ValueKind != System.Text.Json.JsonValueKind.Object) return Results.BadRequest();

    string T(params string[] namen)
    {
        foreach (var n in namen)
            foreach (var p in json.EnumerateObject())
                if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
        return "";
    }
    int? G(params string[] namen) => int.TryParse(T(namen), out var v) ? v : null;

    var computernaam = T("computernaam");
    if (computernaam.Length == 0) return Results.BadRequest();
    var merkType = T("merkType");
    if (merkType.Length == 0) merkType = (T("merk") + " " + T("model")).Trim();

    await computers.MeldAsync(new ComputerMelding(
        computernaam, T("gebruiker", "gebruikersnaam"), T("gebruikerNaam"), T("serviceTag"), merkType,
        T("processor"), G("geheugenGb", "geheugenGB"), T("schijven"), G("schijvenGb"), T("grafischeKaarten")));
    return Results.Ok();
}
app.MapPost("/api/computer", ComputerMelden);
app.MapPost("/api/laptop", ComputerMelden);

// ---------------------------------------------------------------------------
// Fusion → CAM: het zip-pakket als echte download. De pagina zet het pakket
// in FusionPakketten en toont een link hierheen; het id is een onraadbare
// GUID en het pakket verloopt na een uur.
// ---------------------------------------------------------------------------
app.MapGet("/api/fusion-pakket/{id:guid}", (Guid id, FusionPakketten pakketten) =>
    pakketten.Pak(id) is { } pakket
        ? Results.File(pakket.Inhoud, "application/zip", pakket.Naam)
        : Results.NotFound());

// Firmware voor de werkplek-agent (Productie ▸ Test PCB ▸ Firmware laden):
// de agent op de werkplek-pc haalt het bestand hier op. Zie MdeAgent/README.md.
app.MapGet("/api/firmware/{id:guid}", (Guid id, FirmwarePakketten pakketten) =>
    pakketten.Pak(id) is { } pakket
        ? Results.File(pakket.Inhoud, "application/octet-stream", pakket.Naam)
        : Results.NotFound());

// ---------------------------------------------------------------------------
// Standaardcomponenten voor de Fusion-add-in: GET /api/componenten, optioneel
// gefilterd met ?soort=R|C&grootte=0402&zoek=100nF. Beveiligd met een gedeelde
// sleutel in de kop X-Sleutel (config Componenten:Sleutel; leeg = uit), zoals
// het laptopregister. Zie Fusion-AddIn-Componenten-API.md voor de afspraken.
// ---------------------------------------------------------------------------
app.MapGet("/api/componenten", async (HttpContext ctx, IEcComponentRepository ecComponenten,
                                      IConfiguration config) =>
{
    var sleutel = config["Componenten:Sleutel"] ?? "";
    if (sleutel.Length == 0 || !ecComponenten.Beschikbaar) return Results.NotFound();
    if (ctx.Request.Headers["X-Sleutel"] != sleutel) return Results.Unauthorized();

    string? Q(string naam) => ctx.Request.Query[naam].FirstOrDefault()?.Trim();
    var soort = Q("soort")?.ToUpperInvariant();
    var grootte = Q("grootte");
    var zoek = Q("zoek");

    static string Compact(string s) => s.Replace(" ", "");

    var alles = await ecComponenten.GetAllesAsync();
    var lijst = alles
        .Where(c => string.IsNullOrEmpty(soort) || c.Soort.ToString() == soort)
        .Where(c => string.IsNullOrEmpty(grootte) || c.Grootte == grootte)
        .Where(c => string.IsNullOrEmpty(zoek)
                    || c.Gpn.Contains(zoek, StringComparison.OrdinalIgnoreCase)
                    || Compact(c.WaardeTekst).Contains(Compact(zoek), StringComparison.OrdinalIgnoreCase)
                    || (c.Omschrijving?.Contains(zoek, StringComparison.OrdinalIgnoreCase) ?? false))
        .Select(c => new
        {
            gpn = c.Gpn,
            soort = c.Soort.ToString(),
            grootte = c.Grootte,
            waardeBasis = c.WaardeBasis,
            waardeTekst = c.WaardeTekst,
            tolerantie = c.Tolerantie,
            vermogen = c.Vermogen,
            spanning = c.Spanning,
            dielectricum = c.Dielectricum,
            omschrijving = c.Omschrijving,
        });
    return Results.Ok(lijst);
});

var componenten = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Inloggen alleen verplichten als Google daadwerkelijk is ingesteld.
if (loginAan) componenten.RequireAuthorization();

app.Run();
