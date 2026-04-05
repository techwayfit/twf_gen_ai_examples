namespace _003_Automated_NewsLetter.Models;

/// <summary>Subscriber interest profile — persisted as JSON and used to score clusters.</summary>
public class SubscriberProfile
{
    public string                     DisplayName           { get; set; } = "Reader";
    public Dictionary<string, int>    InterestWeights       { get; set; } = new()
    {
        ["AI & Machine Learning"]      = 9,
        ["Software Development"]       = 8,
        ["Wearables & Health Tech"]    = 7,
        ["Climate & Sustainability"]   = 6,
        ["Business & Finance"]         = 4
    };
    public int    MinRelevanceScore      { get; set; } = 4;
    public int    PreferredSectionCount  { get; set; } = 5;
    public string Tone                   { get; set; } = "informative";
}
