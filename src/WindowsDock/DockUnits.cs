using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsDock;

internal static class DockUnits
{
    private const string Number = @"[+\-−]?(?:[0-9]{1,3}(?:,[0-9]{3})+(?:\.[0-9]*)?|[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?";
    private const RegexOptions Insensitive = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Regex Separators = new(@"(?<=\s)(?:to|into|in)(?=\s)|->|→", Insensitive);
    private static readonly Regex LeadingNumber = new("^" + Number);
    private static readonly Regex Fraction = new(@"^\s*/\s*(" + Number + ")");
    private static readonly Regex ExtraAmount = new(@"(?<=\s)" + Number + @"(?=\s*[\p{L}°µμ])");
    private static readonly Dimension Length = new(Length: 1);
    private static readonly Dimension Mass = new(Mass: 1);
    private static readonly Dimension Time = new(Time: 1);
    private static readonly Dimension Temperature = new(Temperature: 1);
    private static readonly Dimension Data = new(Data: 1);
    private static readonly Dimension Angle = new(Angle: 1);
    private static readonly Dimension Current = new(Current: 1);
    private static readonly (Dictionary<string, Unit> Symbols, Dictionary<string, Unit> Names) Units = BuildUnits();

    private readonly record struct Dimension(int Length = 0, int Mass = 0, int Time = 0, int Temperature = 0,
        int Data = 0, int Angle = 0, int Current = 0)
    {
        public static Dimension operator +(Dimension a, Dimension b) => new(a.Length + b.Length,
            a.Mass + b.Mass, a.Time + b.Time, a.Temperature + b.Temperature, a.Data + b.Data, a.Angle + b.Angle,
            a.Current + b.Current);
        public static Dimension operator *(Dimension a, int exponent) => new(a.Length * exponent,
            a.Mass * exponent, a.Time * exponent, a.Temperature * exponent, a.Data * exponent, a.Angle * exponent,
            a.Current * exponent);
    }
    private sealed record Unit(string Label, Dimension Dimension, decimal Scale, decimal Zero = 0,
        bool Absolute = false, bool FuelEconomy = false);

    internal static (string Result, string Detail)? Convert(string input, Func<string, decimal> constant)
    {
        if (input.StartsWith("convert ", StringComparison.OrdinalIgnoreCase))
            input = input[8..].TrimStart();
        var separators = Separators.Matches(input);
        if (separators.Count == 0)
            return null;
        FormatException? invalid = null;
        for (var index = separators.Count - 1; index >= 0; index--)
        {
            var mark = separators[index];
            var left = input[..mark.Index].Trim();
            var right = input[(mark.Index + mark.Length)..].Trim().TrimEnd('?').TrimEnd();
            if (left.Length == 0 || right.Length == 0 ||
                (!LeadingNumber.IsMatch(left) && left[0] != '('))
                continue;
            try
            {
                var (amount, from) = ReadQuantity(left, constant);
                var to = ReadUnit(right);
                decimal value;
                if (from.Dimension == to.Dimension && from.Absolute == to.Absolute)
                    value = (amount - from.Zero) * from.Scale / to.Scale + to.Zero;
                else if (from.FuelEconomy && to.FuelEconomy && from.Dimension == to.Dimension * -1)
                {
                    if (amount <= 0)
                        throw new FormatException("Use a positive fuel economy.");
                    value = 1 / (amount * from.Scale) / to.Scale;
                }
                else
                    throw new FormatException("These units measure different quantities.");
                var display = value != 0 && Math.Abs(value) < .000000001m
                    ? value.ToString("G12", Invariant) : decimal.Round(value, 12).ToString("G29", Invariant);
                return ($"{display} {to.Label}", $"{from.Label} to {to.Label}");
            }
            catch (FormatException error) { invalid ??= error; }
        }
        if (invalid != null)
            throw invalid;
        return null;
    }

    private static (decimal Amount, Unit Unit) ReadQuantity(string expression, Func<string, decimal> constant)
    {
        var feet = Regex.Match(expression, @"^(" + Number + @")\s*['′](?:\s*(" + Number + @")\s*[""″])?$");
        if (feet.Success)
        {
            var whole = Parse(feet.Groups[1].Value);
            var inches = feet.Groups[2].Success ? Parse(feet.Groups[2].Value) : 0;
            return (whole + (feet.Groups[1].Value.StartsWith('-') || feet.Groups[1].Value.StartsWith('−') ? -inches : inches) / 12,
                ReadUnit("ft"));
        }
        if (expression.StartsWith('('))
        {
            var level = 0;
            for (var index = 0; index < expression.Length; index++)
            {
                if (expression[index] == '(')
                    level++;
                else if (expression[index] == ')' && --level == 0)
                    return (constant(expression[..(index + 1)]), ReadUnit(expression[(index + 1)..]));
            }
            throw new FormatException("Close the parentheses around the amount.");
        }
        var lead = LeadingNumber.Match(expression);
        if (!lead.Success)
            throw new FormatException("Enter an amount before the units.");
        var value = Parse(lead.Value);
        var rest = expression[lead.Length..].TrimStart();
        var fraction = Fraction.Match(rest);
        if (fraction.Success)
        {
            value /= Parse(fraction.Groups[1].Value);
            rest = rest[fraction.Length..].TrimStart();
        }
        try { return (value, ReadUnit(rest)); }
        catch (FormatException) when (ExtraAmount.IsMatch(rest))
        {
            var amounts = ExtraAmount.Matches(rest);
            var first = ReadUnit(rest[..amounts[0].Index]);
            if (first.Absolute || first.FuelEconomy)
                throw new FormatException("Use one amount for temperature or fuel economy.");
            var total = value * first.Scale;
            var labels = new List<string> { first.Label };
            for (var index = 0; index < amounts.Count; index++)
            {
                var match = amounts[index];
                var trailing = index + 1 < amounts.Count ? amounts[index + 1].Index : rest.Length;
                var unitText = rest[(match.Index + match.Length)..trailing].Trim();
                var extra = ReadUnit(first.Dimension == Time && unitText == "m" ? "min" : unitText);
                if (extra.Dimension != first.Dimension || extra.Absolute || extra.FuelEconomy)
                    throw new FormatException("Mixed amounts must use units that measure the same quantity.");
                var part = Parse(match.Value);
                if (value < 0 && match.Value[0] is not ('+' or '-' or '−'))
                    part = -part;
                total += part * extra.Scale;
                labels.Add(extra.Label);
            }
            return (total, new Unit(string.Join(" + ", labels), first.Dimension, 1));
        }
    }

    private static decimal Parse(string value) => decimal.Parse(
        value.Replace(",", "").Replace('−', '-'), NumberStyles.Float, Invariant);

    private static Unit ReadUnit(string value)
    {
        var label = Regex.Replace(Regex.Replace(value.Trim(), @"\s+", " "), @"\s*/\s*", "/");
        if (Lookup(label) is { } leaf)
            return leaf;
        if (label.Length is < 1 or > 130)
            throw new FormatException("Enter units on both sides, such as KB to MB.");
        var normalized = Regex.Replace(label, @"\s+per\s+", "/", Insensitive).Replace('·', '*').Replace('×', '*').Replace('−', '-');
        const string superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻";
        normalized = Regex.Replace(normalized, "[" + superscripts + "]+", match =>
            "^" + string.Concat(match.Value.Select(ch => "0123456789+-"[superscripts.IndexOf(ch)])));
        normalized = Regex.Replace(normalized, @"(?<=[\p{L}])([23])\b", "^$1");
        var parsed = new UnitParser(normalized).Read();
        if (parsed.Absolute)
            return parsed;
        return parsed with { Label = label };
    }

    private static Unit? Lookup(string key)
    {
        if (Units.Symbols.TryGetValue(key, out var value))
            return value;
        return Units.Names.GetValueOrDefault(key);
    }

    private sealed class UnitParser(string input)
    {
        private int position;

        internal Unit Read()
        {
            var result = Group(0);
            if (position != input.Length)
                throw new FormatException("Check the grouping in the unit expression.");
            return result;
        }

        private Unit Group(int depth)
        {
            if (depth > 8)
                throw new FormatException("Simplify the nested unit expression.");
            var left = Factor(depth);
            while (true)
            {
                SkipSpace();
                if (position >= input.Length || input[position] == ')')
                    return left;
                var divide = Take('/');
                if (!divide)
                    Take('*');
                var right = Factor(depth);
                if (left.Absolute || right.Absolute)
                    throw new FormatException("Use Δ°C or Δ°F for temperature differences in compound units.");
                var scale = divide ? left.Scale / right.Scale : left.Scale * right.Scale;
                if (scale == 0)
                    throw new OverflowException();
                left = new Unit("", left.Dimension + right.Dimension * (divide ? -1 : 1), scale);
            }
        }

        private Unit Factor(int depth)
        {
            SkipSpace();
            var exponent = Consume("square") || Consume("sq") ? 2 : Consume("cubic") ? 3 : 1;
            Unit unit;
            if (Take('('))
            {
                unit = Group(depth + 1);
                if (!Take(')'))
                    throw new FormatException("Close the parentheses around the units.");
            }
            else
                unit = Atom();
            SkipSpace();
            if (Take('^'))
            {
                SkipSpace();
                var start = position;
                if (position < input.Length && input[position] is '+' or '-')
                    position++;
                while (position < input.Length && char.IsAsciiDigit(input[position]))
                    position++;
                if (!int.TryParse(input.AsSpan(start, position - start), out var power) || power is < -12 or > 12)
                    throw new FormatException("Use a unit exponent from -12 to 12.");
                exponent *= power;
            }
            else if (Consume("squared"))
                exponent *= 2;
            else if (Consume("cubed"))
                exponent *= 3;
            if (exponent == 1)
                return unit;
            if (unit.Absolute)
                throw new FormatException("Use a temperature difference in a powered unit.");
            var scale = 1m;
            for (var count = 0; count < Math.Abs(exponent); count++)
                scale = exponent < 0 ? scale / unit.Scale : scale * unit.Scale;
            if (scale == 0)
                throw new OverflowException();
            return new Unit("", unit.Dimension * exponent, scale);
        }

        private Unit Atom()
        {
            var end = position;
            while (end < input.Length && input[end] is not ('(' or ')' or '/' or '*' or '^'))
                end++;
            var tokenEnd = end;
            while (end > position)
            {
                while (end > position && char.IsWhiteSpace(input[end - 1]))
                    end--;
                if (Lookup(input[position..end]) is { } unit)
                {
                    position = end;
                    return unit;
                }
                while (end > position && !char.IsWhiteSpace(input[end - 1]))
                    end--;
            }
            var label = input[position..tokenEnd].Trim();
            throw new FormatException(label.Length == 0 ? "Check the operators between the units." : $"Unknown unit: {label}.");
        }

        private void SkipSpace()
        {
            while (position < input.Length && char.IsWhiteSpace(input[position]))
                position++;
        }

        private bool Take(char value)
        {
            SkipSpace();
            if (position == input.Length || input[position] != value)
                return false;
            position++;
            return true;
        }

        private bool Consume(string word)
        {
            SkipSpace();
            var end = position + word.Length;
            if (!input.AsSpan(position).StartsWith(word, StringComparison.OrdinalIgnoreCase) ||
                (end < input.Length && (char.IsLetter(input[end]) || input[end] == '°')))
                return false;
            position = end;
            return true;
        }
    }

    private static (Dictionary<string, Unit>, Dictionary<string, Unit>) BuildUnits()
    {
        var symbols = new Dictionary<string, Unit>(StringComparer.Ordinal);
        var names = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        var volume = Length * 3;
        var velocity = Length + Time * -1;
        var force = Mass + Length + Time * -2;
        var pressure = force + Length * -2;
        var energy = force + Length;
        var power = energy + Time * -1;
        var frequency = Time * -1;
        var voltage = power + Current * -1;
        var charge = Current + Time;

        SI(Length, "m", 1, "meter", "metre");
        SI(Time, "s", 1, "second");
        SI(Mass, "g", .001m, "gram");
        SI(volume, "L", .001m, "liter", "litre");
        SI(frequency, "Hz", 1, "hertz");
        SI(force, "N", 1, "newton");
        SI(pressure, "Pa", 1, "pascal");
        SI(energy, "J", 1, "joule");
        SI(power, "W", 1, "watt");
        SI(Current, "A", 1, "ampere", "amp");
        SI(voltage, "V", 1, "volt");
        SI(voltage + Current * -1, "Ω", 1, "ohm");
        SI(charge + voltage * -1, "F", 1, "farad");
        SI(energy, "Wh", 3600, "watt-hour");
        SI(charge, "Ah", 3600, "amp-hour");
        SI(Temperature, "ΔK", 1, "delta kelvin");

        Add(Length, "in", .0254m, "inch|inches|\"|″");
        Add(Length, "ft", .3048m, "foot|feet|'|′");
        Add(Length, "yd", .9144m, "yard|yards");
        Add(Length, "mi", 1609.344m, "mile|miles");
        Add(Length, "nmi", 1852, "nautical mile|nautical miles");
        Add(Length, "au", 149597870700, "astronomical unit|astronomical units");
        Add(Length, "ly", 9460730472580800, "light year|light years|light-year|light-years");
        Add(Length * 2, "acre", 4046.8564224m, "ac|acres");
        Add(Length * 2, "ha", 10000, "hectare|hectares");
        Add(Mass, "lb", .45359237m, "lbs|pound|pounds");
        Add(Mass, "oz", .028349523125m, "ounce|ounces");
        Add(Mass, "st", 6.35029318m, "stone|stones");
        Add(Mass, "t", 1000, "tonne|tonnes|metric ton|metric tons");
        Add(Mass, "US ton", 907.18474m, "ton|tons|short ton|short tons");
        Add(volume, "US cups", .0002365882365m, "cup|cups|US cup");
        Add(volume, "US fl oz", .0000295735295625m, "fl oz|fl. oz.|fluid ounce|fluid ounces");
        Add(volume, "US tbsp", .00001478676478125m, "tbsp|tablespoon|tablespoons");
        Add(volume, "US tsp", .00000492892159375m, "tsp|teaspoon|teaspoons");
        Add(volume, "US pt", .000473176473m, "pt|pint|pints");
        Add(volume, "US qt", .000946352946m, "qt|quart|quarts");
        Add(volume, "US gal", .003785411784m, "gal|gallon|gallons|US gallon|US gallons");
        Add(volume, "UK gal", .00454609m, "UK gallon|UK gallons|imperial gallon|imperial gallons");
        Add(volume, "UK pt", .00056826125m, "UK pint|UK pints|imperial pint|imperial pints");
        Add(volume, "metric cup", .00025m, "metric cups");
        Add(Time, "min", 60, "mins|minute|minutes");
        Add(Time, "h", 3600, "hr|hrs|hour|hours");
        Add(Time, "days", 86400, "d|day");
        Add(Time, "wk", 604800, "week|weeks|wks");
        Add(Time, "Julian yr", 31557600, "yr|yrs|year|years|Julian year|Julian years");
        Add(velocity, "mph", 1609.344m / 3600, "mi/h|miles per hour");
        Add(velocity, "km/h", 1000m / 3600, "kph|kmh|kilometers per hour|kilometres per hour");
        Add(velocity, "kn", 1852m / 3600, "kt|kts|knot|knots");
        Add(frequency, "rpm", 1m / 60, "revolutions per minute");
        Add(Angle, "rad", 1, "radian|radians");
        Add(Angle, "°", (decimal)Math.PI / 180, "degree|degrees|deg");
        Add(Angle, "turn", (decimal)Math.Tau, "turns|revolution|revolutions");
        Add(pressure, "bar", 100000, "bars");
        Add(pressure, "mbar", 100, "millibar|millibars");
        Add(pressure, "atm", 101325, "atmosphere|atmospheres");
        Add(pressure, "psi", .45359237m * 9.80665m / (.0254m * .0254m), "pounds per square inch");
        Add(pressure, "Torr", 101325m / 760, "torr");
        Add(pressure, "mmHg", 133.322387415m, "millimeters of mercury|millimetres of mercury");
        Add(energy, "cal", 4.184m, "calorie|calories");
        Add(energy, "kcal", 4184, "kilocalorie|kilocalories|food calories");
        Add(energy, "BTU", 1055.05585262m, "btu|british thermal unit|british thermal units");
        Add(energy, "eV", .0000000000000000001602176634m, "electronvolt|electronvolts");
        Add(power, "hp", .45359237m * 9.80665m * .3048m * 550, "horsepower");
        Add(charge, "coulomb", 1, "coulombs");
        Add(default, "%", .01m, "percent|percentage");
        Add(default, "ppm", .000001m, "parts per million");
        Add(default, "ppb", .000000001m, "parts per billion");

        Add(Temperature, "°C", 1, "C|Celsius|degrees Celsius|degC", absolute: true);
        Add(Temperature, "°F", 5m / 9, "F|Fahrenheit|degrees Fahrenheit|degF", zero: 32, absolute: true);
        symbols["F"] = symbols["°F"];
        Add(Temperature, "K", 1, "kelvin|kelvins", zero: 273.15m, absolute: true);
        Add(Temperature, "°R", 5m / 9, "rankine|degrees Rankine", zero: 491.67m, absolute: true);
        Add(Temperature, "Δ°C", 1, "delta C|delta °C|delta Celsius|ΔC");
        Add(Temperature, "Δ°F", 5m / 9, "delta F|delta °F|delta Fahrenheit|ΔF");
        Add(Length * -2, "US mpg", 1609.344m / .003785411784m, "mpg|miles per gallon|US miles per gallon", fuel: true);
        Add(Length * -2, "UK mpg", 1609.344m / .00454609m, "imperial mpg|UK miles per gallon", fuel: true);
        Add(Length * 2, "L/100 km", .00000001m,
            "L/100km|liters per 100 km|litres per 100 km|liters per 100 kilometers|litres per 100 kilometres", fuel: true);
        Add(Length * -2, "km/L", 1000000,
            "km/l|kilometers per liter|kilometres per litre", fuel: true);

        Add(Data, "bit", 1, "bits");
        Add(Data, "B", 8, "byte|bytes", exact: true);
        symbols["b"] = symbols["bit"];
        names["byte"] = names["bytes"] = symbols["B"];
        Add(Data + frequency, "bit/s", 1, "bps|bits per second");
        Add(Data + frequency, "B/s", 8, "Bps|bytes per second", exact: true);
        decimal si = 1, binary = 1;
        var prefixes = new[] { ("k", "kilo", "Ki", "kibi"), ("M", "mega", "Mi", "mebi"), ("G", "giga", "Gi", "gibi"),
            ("T", "tera", "Ti", "tebi"), ("P", "peta", "Pi", "pebi"), ("E", "exa", "Ei", "exbi") };
        foreach (var (symbol, name, base1024, binaryName) in prefixes)
        {
            si *= 1000;
            binary *= 1024;
            DataUnits(symbol, name, si);
            DataUnits(base1024, binaryName, binary);
        }
        return (symbols, names);

        void Add(Dimension dimension, string label, decimal scale, string aliases, decimal zero = 0,
            bool absolute = false, bool fuel = false, bool exact = false)
        {
            var unit = new Unit(label, dimension, scale, zero, absolute, fuel);
            symbols[label] = unit;
            if (!exact)
                names[label] = unit;
            foreach (var alias in aliases.Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (exact && !alias.Contains(' ') && alias.Length <= 5)
                    symbols[alias] = unit;
                else
                    names[alias] = unit;
        }

        void SI(Dimension dimension, string symbol, decimal scale, params string[] words)
        {
            foreach (var (prefix, spelled, factor) in new[] { ("p", "pico", .000000000001m), ("n", "nano", .000000001m),
                ("µ", "micro", .000001m), ("m", "milli", .001m), ("c", "centi", .01m), ("d", "deci", .1m),
                ("", "", 1m), ("k", "kilo", 1000m), ("M", "mega", 1000000m), ("G", "giga", 1000000000m),
                ("T", "tera", 1000000000000m) })
            {
                var label = prefix + symbol;
                var unit = new Unit(label, dimension, factor * scale);
                symbols[label] = unit;
                if (prefix == "µ")
                    symbols["u" + symbol] = symbols["μ" + symbol] = unit;
                foreach (var word in words)
                {
                    names[spelled + word] = unit;
                    names[spelled + word + "s"] = unit;
                }
                names.TryAdd(label, unit);
            }
        }

        void DataUnits(string prefix, string longPrefix, decimal units)
        {
            var bits = new Unit(prefix + "b", Data, units);
            var bytes = new Unit(prefix + "B", Data, units * 8);
            var bitsRate = new Unit(prefix + "b/s", Data + frequency, units);
            var bytesRate = new Unit(prefix + "B/s", Data + frequency, units * 8);
            foreach (var spelling in new[] { prefix, prefix.ToLowerInvariant(), prefix.ToUpperInvariant() }.Distinct())
            {
                symbols[spelling + "b"] = symbols[spelling + "bit"] = bits;
                symbols[spelling + "B"] = bytes;
                symbols[spelling + "bps"] = symbols[spelling + "b/s"] = bitsRate;
                symbols[spelling + "Bps"] = symbols[spelling + "B/s"] = bytesRate;
            }
            names[longPrefix + "bit"] = names[longPrefix + "bits"] = bits;
            names[longPrefix + "byte"] = names[longPrefix + "bytes"] = bytes;
        }
    }
}
