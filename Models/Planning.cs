namespace EverythingApp.Models;

/// <summary>Een soort planningsbalk (verlof, werk, sluiting…), met de kleur
/// waarin hij getekend wordt. Uit de tabel PlanningSoorten; een nieuwe soort
/// is daar een regel erbij, zonder programmeerwerk.</summary>
public record PlanningSoort(int Soort, string Naam, string Kleur, int Volgorde);

/// <summary>
/// Eén balk op de planning. Waar hij bij hoort bepalen twee losse verwijzingen
/// die elk leeg mogen zijn: geen van beide = geldt voor iedereen; alleen een
/// werknemer = iets persoonlijks (verlof); alleen een werkorder = gepland werk
/// zonder toewijzing; allebei = deze persoon op dat werk.
/// </summary>
public class PlanningBalk
{
    public int Id { get; set; }
    public int Soort { get; set; }
    public int? WerknemerId { get; set; }
    public int? WerkbonId { get; set; }

    /// <summary>Hele dagen; Einde is de laatste dag die meetelt (t/m).</summary>
    public DateOnly Start { get; set; }
    public DateOnly Einde { get; set; }

    public string Omschrijving { get; set; } = "";

    /// <summary>Geplande uren voor de hele balk; leeg waar dat niet past (verlof).</summary>
    public decimal? GeplandeUren { get; set; }
}

/// <summary>Een pijl in de Gantt: van het einde van de ene balk naar het begin
/// van de andere. Speling in dagen (drogen, transport); mag negatief.</summary>
public record PlanningPijl(int VanId, int NaarId, int Speling);
