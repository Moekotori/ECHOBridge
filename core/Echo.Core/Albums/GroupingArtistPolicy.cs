using System.Text.Json;
using System.Text.RegularExpressions;
using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>One rebuild's bounded user rules. Track names are never cached.</summary>
internal sealed class GroupingArtistPolicy
{
    private readonly Dictionary<string, (string Key, string Name)> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _display = new(StringComparer.Ordinal);
    private readonly Regex? _split;
    private readonly Regex? _keep;
    private readonly bool _slash;
    private readonly string _strategy;
    private const int MaximumRules = 5000 * 2 + 1;
    internal static readonly Regex[] Suffixes = [new(@"\s*(?:\+|＋)\s*81$", RegexOptions.IgnoreCase),
        new(@"\s*(?:-|–|—|:|\|)\s*(?:official|topic|vevo)$", RegexOptions.IgnoreCase)];

    public GroupingArtistPolicy(TransactionSql sql, string strategy)
    {
        _strategy = strategy;
        var rules = new List<(string Kind, string Name, string? Target)>();
        using (var command = sql.Command("SELECT kind,name,target FROM artist_credit_rules WHERE suppressed=0 ORDER BY kind,name_key LIMIT " + (MaximumRules + 1)))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) rules.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        if (rules.Count > MaximumRules) throw new InvalidOperationException("Too many artist credit rules.");
        var options = new Dictionary<string, bool> { ["slash"]=true,["semicolon"]=true,["tightComma"]=true,
            ["spacedComma"]=false,["fullwidthComma"]=false,["enumerationComma"]=false,["times"]=true,
            ["ampersand"]=false,["plus"]=false,["featWords"]=true };
        var custom = new List<string>();
        var settings = rules.FirstOrDefault(row => row.Kind == "separators").Target;
        if (settings is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(settings);
                foreach (var key in options.Keys.ToArray())
                    if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        options[key] = value.GetBoolean();
                if (json.RootElement.TryGetProperty("custom", out var literals) && literals.ValueKind == JsonValueKind.Array)
                    custom.AddRange(literals.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()!.Trim()).Where(v => v.Length is >0 and <=12).Distinct().Take(16));
            }
            catch (JsonException) { }
        }
        _slash = options["slash"];
        var symbols = new List<string>(); var words = new List<string>();
        if (_slash) symbols.Add("/");
        if (options["semicolon"]) symbols.AddRange([";","；"]);
        if (options["spacedComma"]) symbols.Add(","); else if(options["tightComma"]) symbols.Add(@",(?!\s)");
        if (options["fullwidthComma"]) symbols.Add("，");
        if (options["enumerationComma"]) symbols.Add("、");
        if (options["times"]) symbols.Add("×");
        if (options["ampersand"]) symbols.AddRange(["&","＆"]);
        if (options["plus"]) symbols.AddRange([@"\+","＋"]);
        if (options["featWords"]) words.AddRange([@"feat\.?",@"ft\.?","featuring","with","x"]);
        foreach (var literal in custom)
            (Regex.IsMatch(literal,@"^[\p{L}\p{N}.]+$") ? words : symbols).Add(Regex.Escape(literal));
        var patterns = new List<string>();
        if (symbols.Count > 0) patterns.Add(@"\s*(?:"+string.Join('|',symbols)+@")\s*");
        if (words.Count > 0) patterns.Add(@"\s+(?:"+string.Join('|',words)+@")\s+");
        if(patterns.Count>0) _split = new(string.Join('|',patterns),RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var atomic = rules.Where(row=>row.Kind=="keep").Select(row=>AlbumIdentity.Normalize(row.Name))
            .Where(name=>name.Length>0 && _split?.IsMatch(name)==true).Distinct().OrderByDescending(name=>name.Length).ThenBy(name=>name,StringComparer.Ordinal).ToArray();
        if(atomic.Length>0) _keep = new(@"(?<![\p{L}\p{N}])(?:"+string.Join('|',atomic.Select(Regex.Escape))+@")(?![\p{L}\p{N}])",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach(var rule in rules.Where(row=>row.Kind=="alias" && row.Target is not null))
        {
            var from=BaseKey(rule.Name); var to=BaseKey(rule.Target!);
            if(from.Length>0 && to.Length>0 && from!=to) _aliases.TryAdd(from,(to,AlbumIdentity.Normalize(rule.Target!)));
        }
        foreach(var from in _aliases.Keys.ToArray())
        {
            var current=_aliases[from]; var seen=new HashSet<string>{from}; var depth=0;
            while(_aliases.TryGetValue(current.Key,out var next))
            {
                if(!seen.Add(current.Key) || ++depth>32) { current=("",""); break; }
                current=next;
            }
            if(current.Key.Length>0) _display.TryAdd(current.Key,current.Name);
        }
    }

    private string BaseKey(string name)
    {
        var display=AlbumIdentity.Normalize(name); var original=display;
        if(_strategy=="standard") foreach(var suffix in Suffixes) display=suffix.Replace(display,"").Trim();
        return AlbumIdentity.ArtistKey(display.Length>0 ? display : original);
    }
    public string Key(string name)
    {
        var key=BaseKey(name); var original=key; var seen=new HashSet<string>{key}; var depth=0;
        while(_aliases.TryGetValue(key,out var next))
        {
            if(!seen.Add(next.Key) || ++depth>32) return original;
            key=next.Key;
        }
        return key;
    }
    public string Display(string key,string name)=>_display.GetValueOrDefault(key,name);
    public IEnumerable<string> Split(string name)
    {
        var masked=new List<string>();
        var value=_keep is null ? name : _keep.Replace(name,match=> { masked.Add(match.Value); return "\u0002"+(masked.Count-1)+"\u0003"; });
        string Restore(string part)=>Regex.Replace(part,@"\u0002(\d+)\u0003",match=>masked[int.Parse(match.Groups[1].Value)]);
        if(_split is null || !_split.IsMatch(value) || (_slash && Regex.IsMatch(AlbumUnicodeNormalization.Normalize(value.Trim(),System.Text.NormalizationForm.FormKC),@"^[a-z0-9]{1,3}/[a-z0-9]{1,3}$",RegexOptions.IgnoreCase))) return value.Trim().Length==0 ? [] : [Restore(value.Trim())];
        if(_slash) value=Regex.Replace(value,@"(\p{N})\s*/\s*(?=\p{N})","$1\u0001");
        return _split.Split(value).Select(part=>Restore(part.Replace('\u0001','/').Trim()))
            .Where(part=>part.Length>0).ToArray();
    }
    public static string Choose(string current,string next)
    {
        current=AlbumIdentity.Normalize(current); next=AlbumIdentity.Normalize(next);
        static int Score(string value)=>(Regex.IsMatch(value,@"[\s._\-~!'""`]+$")?0:4)
            +(Suffixes.Any(suffix=>suffix.IsMatch(value))?0:3)+(Regex.IsMatch(value,"[a-z]") && Regex.IsMatch(value,"[A-Z]")?1:0);
        return current.Length==0 || Score(next)>Score(current) ? next : current;
    }
    public IEnumerable<string> Participants(string credit)
    {
        var seen=new HashSet<string>();
        foreach(var part in Split(credit))
        {
            var name=AlbumIdentity.Display(Regex.Replace(part,@"^[0-9]{1,3}\s*[.)．。]\s*|^[0-9]{1,3}\s*[-_]\s*(?=[^0-9\s(\[（])",""));
            if(name.Length==0) name=part;
            if(name.Length>0 && !Regex.IsMatch(AlbumUnicodeNormalization.Normalize(name,System.Text.NormalizationForm.FormKC),@"^[0-9]{1,2}$|^0+[0-9]*$")
                && seen.Add(AlbumIdentity.ArtistKey(name))) yield return name;
        }
    }
}
