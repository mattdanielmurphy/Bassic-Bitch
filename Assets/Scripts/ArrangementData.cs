using System.Collections.Generic;

public class ArrangementData
{
    public List<NoteData> Notes { get; set; }
    public List<float> BarTimes { get; set; }
    public List<float> BeatTimes { get; set; }
    public float LastPlayedSpeed { get; set; }

    public ArrangementData()
    {
        Notes = new List<NoteData>();
        BarTimes = new List<float>();
        BeatTimes = new List<float>();
        LastPlayedSpeed = 100f; // Default to 100% speed
    }
}
