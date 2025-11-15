using System.Collections.Generic;

public class ArrangementData
{
    public List<NoteData> Notes { get; set; }
    public List<float> BarTimes { get; set; }

    public ArrangementData()
    {
        Notes = new List<NoteData>();
        BarTimes = new List<float>();
    }
}
