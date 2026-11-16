using PKHeX.Core;

namespace HgssSave.PKHeX;

/// <summary>English display names for species and items. Ids stay canonical; names are presentation only.</summary>
internal static class HgssNames
{
    private static GameStrings? _en;

    private static GameStrings English => _en ??= GameInfo.GetStrings("en");

    public static string? ItemName(int id)
    {
        if (id <= 0)
            return null;
        var list = English.itemlist;
        if ((uint)id >= (uint)list.Length)
            return null;
        var name = list[id];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static string ForSpecies(ushort species)
    {
        try
        {
            return SpeciesName.GetSpeciesName(species, (int)LanguageID.English);
        }
        catch (System.Exception)
        {
            return $"Species {species}";
        }
    }
}
