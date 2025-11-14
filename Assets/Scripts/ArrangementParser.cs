using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using UnityEngine;

public static class ArrangementParser
{
    /// <summary>
    /// Parses a Rocksmith arrangement XML file and extracts notes and bar timings.
    /// </summary>
    /// <param name="arrangementXmlPath">The absolute path to the arrangement XML file.</param>
    /// <returns>An ArrangementData object containing notes and bar times.</returns>
    public static ArrangementData ParseArrangement(string arrangementXmlPath)
    {
        ArrangementData data = new ArrangementData();

        if (!System.IO.File.Exists(arrangementXmlPath))
        {
            Debug.LogError($"Arrangement XML file not found: {arrangementXmlPath}");
            return data;
        }

        try
        {
            // XDocument.Load is used to parse the XML file
            XDocument doc = XDocument.Load(arrangementXmlPath);

            // 1. Parse Notes
            // Collect all notes across every level, merging and deduplicating them.
            var allLevels = doc.Descendants("level")
                .Where(l => l.Attribute("difficulty") != null);

            if (!allLevels.Any())
            {
                Debug.LogWarning("No difficulty levels found in the arrangement XML.");
                // Continue to parse bar times even if no notes are found
            }

            Dictionary<string, NoteData> noteDict = new Dictionary<string, NoteData>();

            foreach (var level in allLevels)
            {
                // Find all <note> and <chordNote> elements within the current difficulty level
                var noteElements = level.Descendants("note").Concat(level.Descendants("chordNote"));

                foreach (var noteElement in noteElements)
                {
                    // Extract attributes, converting them to the correct type
                    float time = 0f;
                    int fret = 0;
                    int stringIndex = 0;

                    // Attempt to parse all required attributes
                    var timeAttr = noteElement.Attribute("time");
                    var fretAttr = noteElement.Attribute("fret");
                    var stringAttr = noteElement.Attribute("string");

                    if (timeAttr != null && float.TryParse(timeAttr.Value, out time) &&
                        fretAttr != null && int.TryParse(fretAttr.Value, out fret) &&
                        stringAttr != null && int.TryParse(stringAttr.Value, out stringIndex))
                    {
                        // Create a unique key based on time, string, fret for deduplication
                        string key = $"{time}-{stringIndex}-{fret}";
                        if (noteDict.ContainsKey(key))
                        {
                            // Skip if already added
                            continue;
                        }

                        var sustainAttr = noteElement.Attribute("sustain");
                        var palmMuteAttr = noteElement.Attribute("palmMute");
                        var muteAttr = noteElement.Attribute("mute");
                        var accentAttr = noteElement.Attribute("accent");
                        var slideToAttr = noteElement.Attribute("slideTo");
                        var hammerOnAttr = noteElement.Attribute("hammerOn");
                        var pullOffAttr = noteElement.Attribute("pullOff");

                        float sustain = 0f;
                        if (sustainAttr != null) float.TryParse(sustainAttr.Value, out sustain);

                        int palmMute = 0;
                        if (palmMuteAttr != null) int.TryParse(palmMuteAttr.Value, out palmMute);

                        int mute = 0;
                        if (muteAttr != null) int.TryParse(muteAttr.Value, out mute);

                        int accent = 0;
                        if (accentAttr != null) int.TryParse(accentAttr.Value, out accent);

                        int slideTo = -1;
                        if (slideToAttr != null) int.TryParse(slideToAttr.Value, out slideTo);

                        int hammerOn = 0;
                        if (hammerOnAttr != null) int.TryParse(hammerOnAttr.Value, out hammerOn);

                        int pullOff = 0;
                        if (pullOffAttr != null) int.TryParse(pullOffAttr.Value, out pullOff);

                        noteDict.Add(key, new NoteData
                        {
                            time = time,
                            fretNumber = fret,
                            stringNumber = stringIndex,
                            sustain = sustain,
                            palmMute = palmMute == 1,
                            mute = mute == 1,
                            accent = accent == 1,
                            slideTo = slideTo,
                            hammerOn = hammerOn == 1,
                            pullOff = pullOff == 1
                        });
                    }
                    else
                    {
                        // Log a warning if a note element is malformed, but continue parsing
                        Debug.LogWarning($"Skipping malformed note element in XML: {noteElement}");
                    }
                }
            }

            data.Notes = noteDict.Values.ToList();
            Debug.Log($"Successfully parsed {data.Notes.Count} unique notes from all difficulty levels in arrangement XML.");

            // 2. Parse Bar Times from <ebeats>
            var ebeats = doc.Descendants("ebeat");
            foreach (var ebeat in ebeats)
            {
                var timeAttr = ebeat.Attribute("time");
                var measureAttr = ebeat.Attribute("measure");

                if (timeAttr != null && float.TryParse(timeAttr.Value, out float time) &&
                    measureAttr != null && int.TryParse(measureAttr.Value, out int measure))
                {
                    // Only add beats that mark the start of a new measure (measure > 0)
                    // In Rocksmith XML, measure=1 is the first bar, measure=-1 is an intermediate beat.
                    if (measure > 0)
                    {
                        data.BarTimes.Add(time);
                    }
                }
            }
            
            Debug.Log($"Successfully parsed {data.BarTimes.Count} bar times from arrangement XML.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing arrangement XML at {arrangementXmlPath}: {e.Message}");
        }

        return data;
    }
}
