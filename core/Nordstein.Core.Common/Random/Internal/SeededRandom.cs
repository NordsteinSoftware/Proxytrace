namespace Nordstein.Core.Common.Random.Internal;

internal class SeededRandom : IRandom
{
    public delegate SeededRandom Factory(int seed);

    private static readonly IReadOnlyCollection<string> Words = ["apple", "banana", "cherry", "date", "elderberry", "fig", "grape", "honeydew", "kiwi", "lemon", "mango", "nectarine", "orange", "papaya", "quince", "raspberry", "strawberry", "tangerine", "ugli fruit", "watermelon", "xigua", "yellow passion fruit", "zucchini"];
    private static readonly IReadOnlyCollection<string> EmailDomains = ["example.com", "test.io", "mail.dev", "sample.net"];

    private readonly System.Random random;
    private readonly Lock lockObject = new();
    
    public SeededRandom(int seed)
    {
        random = new System.Random(seed);
    }

    public bool Bool()
    {
        lock (lockObject)
        {
            return random.Next(0, 2) == 1;
        }
    }

    public Guid Guid()
    {
        lock (lockObject)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes);
        }
    }

    public string String() 
        => Enumerable.Range(0, Int(min: 1, max: 5))
            .Select(_ => Any(Words))
            .Aggregate((a, b) => $"{a} {b}");

    public string UniqueString()
        => Guid().ToString();

    public string Email()
        => $"{Any(Words).Replace(" ", "")}{Int(1, 999)}@{Any(EmailDomains)}";

    public Uri Uri()
        => new($"https://{UniqueString()[..8]}.example.com");

    public int Int(int? min = null, int? max = null)
    {
        lock (lockObject)
        {
            min ??= 0;
            max ??= int.MaxValue;
            return random.Next(min.Value, max.Value);
        }
    }

    public long Long(long? min = null, long? max = null)
    {
        lock (lockObject)
        {
            min ??= 0;
            max ??= long.MaxValue;
            return random.NextInt64(min.Value, max.Value);
        }
    }

    public double Double(double? min = null, double? max = null)
    {
        lock (lockObject)
        {
            min ??= 0;
            max ??= 1;
            return min.Value + (random.NextDouble() * (max.Value - min.Value));
        }
    }

    public decimal Decimal(decimal? min = null, decimal? max = null)
    {
        lock (lockObject)
        {
            min ??= 0;
            max ??= decimal.MaxValue;
            var nextDecimal = (decimal)random.NextDouble();
            return min.Value + (nextDecimal * (max.Value - min.Value));
        }
    }

    public T Any<T>(IReadOnlyCollection<T> options)
        => options.ElementAt(Int(min: 0, max: options.Count));

    public T Enum<T>() where T : struct, Enum
        => Any(System.Enum.GetValues<T>());

    public TimeSpan TimeSpan(TimeSpan? min = null, TimeSpan? max = null)
    {
        min ??= System.TimeSpan.FromMilliseconds(500);
        max ??= System.TimeSpan.FromMilliseconds(5000);
        return System.TimeSpan.FromMilliseconds(Long(min: (long)min.Value.TotalMilliseconds, max: (long)max.Value.TotalMilliseconds));
    }

    public DateTimeOffset DateTimeOffset(DateTimeOffset? min = null, DateTimeOffset? max = null)
    {
        var now = System.DateTimeOffset.UtcNow;
        min ??= now.AddYears(-1);
        max ??= now;
        lock (lockObject)
        {
            var ticks = min.Value.Ticks + (long)(random.NextDouble() * (max.Value.Ticks - min.Value.Ticks));
            return new DateTimeOffset(ticks, System.TimeSpan.Zero);
        }
    }
}