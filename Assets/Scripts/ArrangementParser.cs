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
                    notes.Add(new NoteData
                    {
                        time = time,
                        fretNumber = fret,
                        stringNumber = stringIndex
                    });
                }
                else
                {
                    // Log a warning if a note element is malformed, but continue parsing
                    Debug.LogWarning($"Skipping malformed note element in XML: {noteElement}");
                }
            }

            Debug.Log($"Successfully parsed {notes.Count} notes from arrangement XML.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing arrangement XML at {arrangementXmlPath}: {e.Message}");
        }

        return notes;
    }
}