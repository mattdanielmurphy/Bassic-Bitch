using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using UnityEngine;

public static class ArrangementParser
{
    /// <summary>
    /// Parses a Rocksmith arrangement XML file and extracts a list of notes.
    /// </summary>
    /// <param name="arrangementXmlPath">The absolute path to the arrangement XML file.</param>
    /// <returns>A list of NoteData objects.</returns>
    public static List<NoteData> ParseArrangement(string arrangementXmlPath)
    {
        List<NoteData> notes = new List<NoteData>();
        // Use a HashSet to track unique notes based on time, string, and fret to filter out duplicates
        HashSet<string> uniqueNoteKeys = new HashSet<string>();

        if (!System.IO.File.Exists(arrangementXmlPath))
        {
            Debug.LogError($"Arrangement XML file not found: {arrangementXmlPath}");
            return notes;
        }

        try
        {
            // XDocument.Load is used to parse the XML file
            XDocument doc = XDocument.Load(arrangementXmlPath);
            
            // Find all <note> elements in the XML
            var noteElements = doc.Descendants("note");

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
                    // For uniqueness, base only on time/string/fret:
                    string noteKey = $"{time:0.0000}{stringIndex}{fret}";

                    if (uniqueNoteKeys.Add(noteKey))
                    {
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

                        notes.Add(new NoteData
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
                        // Log a warning if a duplicate note is found and skipped
                        Debug.LogWarning($"Skipping duplicate note: {noteKey}");
                    }
                }
                else
                {
                    // Log a warning if a note element is malformed, but continue parsing
                    Debug.LogWarning($"Skipping malformed note element in XML: {noteElement}");
                }
            }

            Debug.Log($"Successfully parsed {notes.Count} unique notes from arrangement XML.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing arrangement XML at {arrangementXmlPath}: {e.Message}");
        }

        return notes;
    }
}