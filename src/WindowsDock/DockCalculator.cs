using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace WindowsDock;

internal sealed record DockCalculation(string Input, string Result, string Detail);

internal static class DockCalculator
{
    private const string Number = @"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?";
    private const RegexOptions Pattern = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private sealed record Unit(string Dimension, string Label, decimal Scale, decimal Offset);
    private static readonly Dictionary<string, Unit> Units = BuildUnits();

    internal static bool IsExplicit(string input) =>
        input.StartsWith('=') || input.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("calc ", StringComparison.OrdinalIgnoreCase);

    internal static bool TryEvaluate(string input, out DockCalculation? calculation, out string? error, DateOnly? today = null)
    {
        input = input.Trim();
        calculation = null;
        error = null;
        var explicitCalculation = IsExplicit(input);
        var expression = input.StartsWith('=') ? input[1..].Trim() : explicitCalculation ? input[4..].Trim() : input.Trim();
        if (expression.Length == 0 || expression.Length > 400)
        {
            if (explicitCalculation)
                error = expression.Length == 0 ? "Enter a calculation. Try 20% of 150." : "Use an expression under 400 characters.";
            return false;
        }
        if (!explicitCalculation && !char.IsDigit(expression[0]) &&
            !"+-.(".Contains(expression[0]) && !Regex.IsMatch(expression, @"^(solve\b|increase\b|decrease\b|today\b|days between\b|what percentage\b|[a-z][a-z0-9]*\s*\()", Pattern))
            return false;
        try
        {
            var (result, detail) = Evaluate(expression, today ?? DateOnly.FromDateTime(DateTime.Today));
            calculation = new(expression, result, detail);
            return true;
        }
        catch (Exception failure) when (failure is FormatException or ArithmeticException or ArgumentOutOfRangeException)
        {
            if (explicitCalculation)
                error = failure switch
                {
                    DivideByZeroException => "Cannot divide by zero.",
                    OverflowException => "The result is outside the supported range.",
                    ArgumentOutOfRangeException => "That value is outside the supported range.",
                    _ => failure.Message
                };
            return false;
        }
    }

    private static (string Result, string Detail) Evaluate(string input, DateOnly today)
    {
        var conversion = Regex.Match(input, $@"^({Number})\s*([a-z°][a-z° ]*)\s+(?:to|in)\s+([a-z°][a-z° ]*)$", Pattern);
        if (conversion.Success)
        {
            var fromName = conversion.Groups[2].Value.Trim();
            var toName = conversion.Groups[3].Value.Trim();
            if (!Units.TryGetValue(fromName, out var from) || !Units.TryGetValue(toName, out var to))
                throw new FormatException("Choose supported units, such as km and mi, °F and °C, or hours and minutes.");
            if (from.Dimension != to.Dimension)
                throw new FormatException("These units measure different quantities.");
            var amount = Decimal(conversion.Groups[1].Value);
            var value = (amount - from.Offset) * from.Scale / to.Scale + to.Offset;
            return ($"{Display(value, 12)} {to.Label}", $"{from.Label} to {to.Label}");
        }

        var comparison = Regex.Match(input, $@"^(?:what percentage is )?({Number})\s+(?:as a percentage of|of)\s+({Number})\??$", Pattern);
        if (comparison.Success)
            return ($"{Display(Decimal(comparison.Groups[1].Value) / Decimal(comparison.Groups[2].Value) * 100, 12)}%", "Part divided by total × 100");
        var adjust = Regex.Match(input, $@"^(increase|decrease) ({Number}) by ({Number})%$", Pattern);
        if (adjust.Success)
        {
            var amount = Decimal(adjust.Groups[2].Value);
            var rate = Decimal(adjust.Groups[3].Value) / 100;
            return (Display(amount * (1 + (adjust.Groups[1].Value.Equals("decrease", StringComparison.OrdinalIgnoreCase) ? -rate : rate))),
                "Amount adjusted by its percentage");
        }
        var ratio = Regex.Match(input, $@"^({Number}):({Number})\s+of\s+({Number})$", Pattern);
        if (ratio.Success)
        {
            var left = Decimal(ratio.Groups[1].Value);
            var right = Decimal(ratio.Groups[2].Value);
            var total = Decimal(ratio.Groups[3].Value);
            if (left < 0 || right < 0 || left + right == 0)
                throw new FormatException("Use positive ratio parts.");
            var first = total * (left / (left + right));
            return ($"{Display(first, 12)} : {Display(total - first, 12)}", "Total split in the given ratio");
        }
        var choose = Regex.Match(input, @"^(\d{1,5}) choose (\d{1,5})$", Pattern);
        if (choose.Success)
        {
            var n = int.Parse(choose.Groups[1].Value, Invariant);
            var k = int.Parse(choose.Groups[2].Value, Invariant);
            if (k > n || Math.Min(k, n - k) > 500)
                throw new FormatException("Use a selection within the population and up to 500 on the shorter side.");
            BigInteger result = 1;
            for (var i = 1; i <= Math.Min(k, n - k); i++)
                result = result * (n - i + 1) / i;
            return (result.ToString(Invariant), "Selections without order or replacement");
        }
        var date = Regex.Match(input, @"^(today|\d{4}-\d{2}-\d{2})\s*([+-])\s*(\d{1,6})\s*(days?|weeks?|months?|years?)$", Pattern);
        if (date.Success)
        {
            var start = ReadDate(date.Groups[1].Value, today);
            var count = int.Parse(date.Groups[3].Value, Invariant) * (date.Groups[2].Value == "-" ? -1 : 1);
            var result = char.ToLowerInvariant(date.Groups[4].Value[0]) switch
            {
                'd' => start.AddDays(count), 'w' => start.AddDays(count * 7), 'm' => start.AddMonths(count), _ => start.AddYears(count)
            };
            return (result.ToString("yyyy-MM-dd", Invariant), result.ToString("dddd", Invariant));
        }
        var between = Regex.Match(input, @"^days between (today|\d{4}-\d{2}-\d{2}) and (today|\d{4}-\d{2}-\d{2})$", Pattern);
        if (between.Success)
            return ($"{ReadDate(between.Groups[2].Value, today).DayNumber - ReadDate(between.Groups[1].Value, today).DayNumber} days",
                "End date minus start date");

        if (input.StartsWith("solve ", StringComparison.OrdinalIgnoreCase))
            input = input[6..];
        var equals = input.IndexOf('=');
        if (equals >= 0)
        {
            var lhs = new Parser(input[..equals]).Read();
            var rhs = new Parser(input[(equals + 1)..]).Read();
            var rise = lhs.X - rhs.X;
            var difference = rhs.Value - lhs.Value;
            if (rise == 0)
                return (difference == 0 ? "Infinitely many solutions" : "No solution", "Linear equation in x");
            return ($"x = {Display(difference / rise, 16)}", "Linear equation in x");
        }
        var valueOnly = new Parser(input).Read();
        if (valueOnly.X != 0)
            throw new FormatException("Use an equals sign to solve for x.");
        return (Display(valueOnly.Value), "Calculation");
    }

    private static DateOnly ReadDate(string input, DateOnly today) =>
        input.Equals("today", StringComparison.OrdinalIgnoreCase) ? today :
        DateOnly.TryParseExact(input, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)
            ? date : throw new FormatException("Enter dates as YYYY-MM-DD.");

    private static decimal Decimal(string input) => decimal.Parse(input, NumberStyles.Float, Invariant);
    private static string Display(decimal value, int digits = 28) => decimal.Round(value, digits).ToString("G29", Invariant);

    private readonly record struct Term(decimal Value, decimal X = 0, bool Percent = false)
    {
        internal Term Multiply(Term other)
        {
            if (X != 0 && other.X != 0)
                throw new FormatException("Only linear equations in x are supported.");
            return new(Value * other.Value, X * other.Value + other.X * Value);
        }
    }

    private sealed class Parser(string input)
    {
        private int position;
        private int depth;

        internal Term Read()
        {
            var value = Sum();
            SkipSpace();
            if (position < input.Length)
                throw new FormatException("Check the operators or units in that expression.");
            return value;
        }

        private Term Sum()
        {
            var result = Product();
            while (true)
            {
                var sign = Take('+') ? 1 : Take('-') || Take('−') ? -1 : 0;
                if (sign == 0)
                    return result;
                var other = Product();
                if (other.Percent && !result.Percent)
                    other = result.Multiply(other);
                result = new(result.Value + sign * other.Value, result.X + sign * other.X, result.Percent && other.Percent);
            }
        }

        private Term Product()
        {
            var result = Unary();
            while (true)
            {
                if (Take('*') || Take('×') || TakeWord("of") || Peek('(') || PeekWord("x"))
                    result = result.Multiply(Unary());
                else if (Take('/') || Take('÷'))
                {
                    var divisor = Unary();
                    if (divisor.X != 0)
                        throw new FormatException("The denominator must be a constant.");
                    result = new(result.Value / divisor.Value, result.X / divisor.Value);
                }
                else
                    return result;
            }
        }

        private Term Unary()
        {
            if (++depth > 48)
                throw new FormatException("That expression is nested too deeply.");
            try
            {
                if (Take('+'))
                    return Unary();
                if (Take('-') || Take('−'))
                {
                    var value = Unary();
                    return new(-value.Value, -value.X, value.Percent);
                }
                var result = Atom();
                if (Take('%'))
                    result = new(result.Value / 100, result.X / 100, true);
                if (!Take('^'))
                    return result;
                var exponent = Constant(Unary());
                if (result.X != 0)
                    return exponent switch { 0 => new(1), 1 => result, _ => throw new FormatException("Only linear equations in x are supported.") };
                return new(Finite(Math.Pow((double)result.Value, (double)exponent)));
            }
            finally { depth--; }
        }

        private Term Atom()
        {
            if (Take('('))
            {
                var result = Sum();
                Require(')');
                return result;
            }
            SkipSpace();
            var start = position;
            while (position < input.Length && char.IsAsciiLetter(input[position]))
                position++;
            if (position > start)
            {
                while (position < input.Length && char.IsAsciiDigit(input[position]))
                    position++;
                var name = input[start..position].ToLowerInvariant();
                if (name == "x") return new(0, 1);
                if (name == "pi") return new((decimal)Math.PI);
                if (name == "e") return new((decimal)Math.E);
                Require('(');
                var values = new List<decimal> { Constant(Sum()) };
                var degree = Take('°') || TakeWord("degrees") || TakeWord("degree") || TakeWord("deg");
                while (Take(','))
                    values.Add(Constant(Sum()));
                Require(')');
                return new(Function(name, values, degree));
            }
            if (position + 1 < input.Length && input[position] == '0' && input[position + 1] is 'b' or 'B' or 'x' or 'X')
            {
                var radix = char.ToLowerInvariant(input[position + 1]) == 'b' ? 2 : 16;
                position += 2;
                start = position;
                var number = 0m;
                while (position < input.Length && Uri.IsHexDigit(input[position]))
                {
                    var digit = Uri.FromHex(input[position++]);
                    if (digit >= radix)
                        throw new FormatException("A digit is invalid for that base.");
                    number = number * radix + digit;
                }
                if (position == start)
                    throw new FormatException("Enter digits after 0b or 0x.");
                return new(number);
            }
            while (position < input.Length && (char.IsAsciiDigit(input[position]) || input[position] == '.'))
                position++;
            if (position == start)
                throw new FormatException("Enter a number, constant, or function.");
            if (position < input.Length && input[position] is 'e' or 'E')
            {
                position++;
                if (position < input.Length && input[position] is '+' or '-')
                    position++;
                while (position < input.Length && char.IsAsciiDigit(input[position]))
                    position++;
            }
            return new(Decimal(input[start..position]));
        }

        private static decimal Constant(Term value) => value.X == 0 ? value.Value
            : throw new FormatException("This operation needs a constant number.");
        private bool Peek(char c) { SkipSpace(); return position < input.Length && input[position] == c; }
        private bool Take(char c) { if (!Peek(c)) return false; position++; return true; }
        private void Require(char c) { if (!Take(c)) throw new FormatException($"Expected '{c}'."); }
        private void SkipSpace() { while (position < input.Length && char.IsWhiteSpace(input[position])) position++; }
        private bool PeekWord(string word)
        {
            SkipSpace();
            return input.AsSpan(position).StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
                (position + word.Length == input.Length || !char.IsAsciiLetter(input[position + word.Length]));
        }
        private bool TakeWord(string word) { if (!PeekWord(word)) return false; position += word.Length; return true; }
    }

    private static decimal Function(string name, List<decimal> values, bool degrees)
    {
        var value = values[0];
        var angle = (double)value * (degrees ? Math.PI / 180 : 1);
        if (degrees && name is not ("sin" or "cos" or "tan"))
            throw new FormatException("Degrees are supported for sin, cos, and tan.");
        return (name, values.Count) switch
        {
            ("sqrt", 1) => Finite(Math.Sqrt((double)value)), ("ln", 1) => Finite(Math.Log((double)value)),
            ("log" or "log10", 1) => Finite(Math.Log10((double)value)), ("sin", 1) => Finite(Math.Sin(angle)),
            ("cos", 1) => Finite(Math.Cos(angle)), ("tan", 1) => Finite(Math.Tan(angle)),
            ("abs", 1) => Math.Abs(value), ("round", 1) => decimal.Round(value),
            ("floor", 1) => decimal.Floor(value), ("ceil", 1) => decimal.Ceiling(value),
            ("mean" or "avg", _) => values.Average(), ("median", _) => Median(values),
            ("min", _) => values.Min(), ("max", _) => values.Max(), ("sum", _) => values.Sum(),
            ("stdev", _) => Deviation(values), ("payment", 3) => Payment(value, values[1], values[2]),
            _ => throw new FormatException("Unknown function or wrong number of arguments.")
        };
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? values[middle - 1] / 2 + values[middle] / 2 : values[middle];
    }

    private static decimal Deviation(List<decimal> values)
    {
        var mean = values.Average();
        return Finite(Math.Sqrt(values.Average(value => Math.Pow((double)(value - mean), 2))));
    }

    private static decimal Payment(decimal principal, decimal annualRate, decimal months)
    {
        if (principal < 0 || annualRate < 0 || months != decimal.Truncate(months) || months is < 1 or > 1200)
            throw new FormatException("Use payment(principal, annual percentage rate, whole months).");
        if (annualRate == 0)
            return decimal.Round(principal / months, 2);
        var rate = (double)(annualRate / 1200);
        return decimal.Round(Finite((double)principal * rate / (1 - Math.Pow(1 + rate, -(double)months))), 2);
    }

    private static decimal Finite(double value) => double.IsFinite(value)
        ? Decimal(value.ToString("G15", Invariant)) : throw new FormatException("The result is undefined for these inputs.");

    private static Dictionary<string, Unit> BuildUnits()
    {
        var units = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        Add("length", "mm", .001m, 0, "millimeter millimeters");
        Add("length", "cm", .01m, 0, "centimeter centimeters");
        Add("length", "m", 1, 0, "meter meters metre metres");
        Add("length", "km", 1000, 0, "kilometer kilometers kilometre kilometres");
        Add("length", "in", .0254m, 0, "inch inches");
        Add("length", "ft", .3048m, 0, "foot feet");
        Add("length", "yd", .9144m, 0, "yard yards");
        Add("length", "mi", 1609.344m, 0, "mile miles");
        Add("volume", "mL", .001m, 0, "milliliter milliliters");
        Add("volume", "L", 1, 0, "liter liters litre litres");
        Add("volume", "US cups", .2365882365m, 0, "cup cups");
        Add("volume", "US gal", 3.785411784m, 0, "gal gallon gallons");
        Add("mass", "g", 1, 0, "gram grams");
        Add("mass", "kg", 1000, 0, "kilogram kilograms");
        Add("mass", "lb", 453.59237m, 0, "lbs pound pounds");
        Add("mass", "oz", 28.349523125m, 0, "ounce ounces");
        Add("time", "s", 1, 0, "sec second seconds");
        Add("time", "min", 60, 0, "minute minutes");
        Add("time", "h", 3600, 0, "hr hrs hour hours");
        Add("time", "days", 86400, 0, "d day");
        Add("temperature", "°C", 1, 0, "c celsius");
        Add("temperature", "°F", 5m / 9, 32, "f fahrenheit");
        Add("temperature", "K", 1, 273.15m, "kelvin");
        return units;

        void Add(string dimension, string label, decimal scale, decimal offset, string aliases)
        {
            var unit = new Unit(dimension, label, scale, offset);
            units[label] = unit;
            foreach (var alias in aliases.Split(' '))
                units[alias] = unit;
        }
    }
}
