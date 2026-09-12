namespace Looxdex.Api.Models;

public class WeatherInfo
{
    public double TempCelsius { get; set; }
    public string City { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public class DailyLook
{
    public string Headline { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public WeatherInfo Weather { get; set; } = new();
    public List<ClosetItem> Items { get; set; } = new();
}
