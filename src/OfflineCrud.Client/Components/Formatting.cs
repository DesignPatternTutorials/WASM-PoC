using System.Globalization;

namespace OfflineCrud.Client.Components;

/// <summary>Display formatting shared by the pages.</summary>
public static class Formatting
{
    // LEARN: In WebAssembly, CultureInfo.CurrentCulture comes from the browser's language setting, and .NET loads
    // matching ICU globalisation data at start-up. DummyJSON prices are in US dollars, so prices use a fixed
    // culture and don't change currency symbol with the user's browser language.
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Formats an amount in US dollars, e.g. <c>$1,234.50</c>.</summary>
    public static string Price(decimal amount) => amount.ToString("C", PriceCulture);

    /// <summary>Formats a percentage with up to two decimal places, e.g. <c>12.5%</c>.</summary>
    public static string Percent(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture) + "%";

    /// <summary>Describes how long ago <paramref name="time"/> was, e.g. <c>just now</c> or <c>5 minutes ago</c>.</summary>
    public static string TimeAgo(DateTimeOffset time, DateTimeOffset now)
    {
        TimeSpan elapsed = now - time;
        return elapsed.TotalSeconds switch
        {
            < 45 => "just now",
            < 90 => "a minute ago",
            < 3600 => $"{(int)Math.Round(elapsed.TotalMinutes)} minutes ago",
            < 5400 => "an hour ago",
            < 86_400 => $"{(int)Math.Round(elapsed.TotalHours)} hours ago",
            _ => time.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>Turns a category slug such as <c>home-decoration</c> into <c>Home decoration</c>.</summary>
    public static string Category(string slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return slug;
        }

        string words = slug.Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
