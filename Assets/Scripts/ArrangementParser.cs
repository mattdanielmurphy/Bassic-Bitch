using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using UnityEngine;
using System.Globalization;

public static class ArrangementParser
{
    /// <summary>
    /// Parses a Rocksmith arrangement XML file using a difficulty-override strategy to get the final note chart.
    /// </summary>
    /// <param name="arrangementXmlPath">The absolute path to the arrangement XML file.</param>
    /// <returns>An ArrangementData object containing the final list of notes and bar times.</returns>
    public static ArrangementData ParseArrangement(string arrangementXmlPath)
    {
        var data = new ArrangementData();
        if (!System.IO.File.Exists(arrangementXmlPath))
        {
            Debug.LogError($"Arrangement XML file not found: {arrangementXmlPath}");
            return data;
        }

        try
        {
            XDocument doc = XDocument.Load(arrangementXmlPath);

            // --- 1. Pre-parse Chord Templates ---
            var chordTemplates = doc.Descendants("chordTemplate")
                .Select(ct => new
                {
                    fret0 = int.Parse(ct.Attribute("fret0").Value),
                    fret1 = int.Parse(ct.Attribute("fret1").Value),
                    fret2 = int.Parse(ct.Attribute("fret2").Value),
                    fret3 = int.Parse(ct.Attribute("fret3").Value),
                    fret4 = int.Parse(ct.Attribute("fret4").Value),
                    fret5 = int.Parse(ct.Attribute("fret5").Value),
                }).ToList();
            
            Debug.Log($"Parsed {chordTemplates.Count} chord templates.");

            // --- 2. Collect All Events and Apply Override Logic ---
            var eventsByTime = new Dictionary<float, (int difficulty, XElement element)>();
            
            var levels = doc.Descendants("level")
                .Where(l => l.Attribute("difficulty") != null)
                .OrderBy(l => int.Parse(l.Attribute("difficulty").Value));

            if (!levels.Any())
            {
                Debug.LogWarning("No difficulty levels found in the arrangement XML.");
            }

            foreach (var level in levels)
            {
                int difficulty = int.Parse(level.Attribute("difficulty").Value);
                
                foreach (var noteElement in level.Descendants("note"))
                {
                    float time = float.Parse(noteElement.Attribute("time").Value, CultureInfo.InvariantCulture);
                    eventsByTime[time] = (difficulty, noteElement);
                }

                foreach (var chordElement in level.Descendants("chord"))
                {
                    float time = float.Parse(chordElement.Attribute("time").Value, CultureInfo.InvariantCulture);
                    eventsByTime[time] = (difficulty, chordElement);
                }
            }

            Debug.Log($"Collected {eventsByTime.Count} unique timed events after difficulty override.");
            
            // --- 3. Process the Final "Winning" Events into NoteData ---
            foreach (var kvp in eventsByTime)
            {
                var element = kvp.Value.element;

                if (element.Name == "note")
                {
                    data.Notes.Add(ParseNoteData(element));
                }
                else if (element.Name == "chord")
                {
                    int chordId = int.Parse(element.Attribute("chordId").Value);
                    if (chordId < chordTemplates.Count)
                    {
                        var template = chordTemplates[chordId];
                        var frets = new[] { template.fret0, template.fret1, template.fret2, template.fret3, template.fret4, template.fret5 };

                        for (int i = 0; i < frets.Length; i++)
                        {
                            if (frets[i] != -1)
                            {
                                data.Notes.Add(ParseNoteData(element, i, frets[i]));
                            }
                        }
                    }
                }
            }

            // --- 4. Parse Bar Times (ebeats) ---
            var ebeats = doc.Descendants("ebeat");
            foreach (var ebeat in ebeats)
            {
                var timeAttr = ebeat.Attribute("time");
                var measureAttr = ebeat.Attribute("measure");

                // CORRECTED LINE: The TryParse overload needs NumberStyles specified.
                if (timeAttr != null && float.TryParse(timeAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float time) &&
                    measureAttr != null && int.TryParse(measureAttr.Value, out int measure))
                {
                    if (measure > 0)
                    {
                        data.BarTimes.Add(time);
                    }
                    
                    // Add all ebeats as beat times
                    data.BeatTimes.Add(time);
                }
            }
            
            data.Notes = data.Notes.OrderBy(n => n.time).ToList();

            Debug.Log($"Successfully parsed {data.Notes.Count} final notes (including chord notes).");
            Debug.Log($"Successfully parsed {data.BarTimes.Count} bar times.");
            Debug.Log($"Successfully parsed {data.BeatTimes.Count} beat times.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing arrangement XML at {arrangementXmlPath}: {e.Message}");
        }

        return data;
    }
    
    private static NoteData ParseNoteData(XElement noteElement)
    {
        return new NoteData
        {
            time = float.Parse(noteElement.Attribute("time").Value, CultureInfo.InvariantCulture),
            fretNumber = int.Parse(noteElement.Attribute("fret").Value),
            stringNumber = int.Parse(noteElement.Attribute("string").Value),
            sustain = float.Parse(noteElement.Attribute("sustain")?.Value ?? "0", CultureInfo.InvariantCulture),
            palmMute = (int.Parse(noteElement.Attribute("palmMute")?.Value ?? "0")) == 1,
            mute = (int.Parse(noteElement.Attribute("mute")?.Value ?? "0")) == 1,
            accent = (int.Parse(noteElement.Attribute("accent")?.Value ?? "0")) == 1,
            slideTo = int.Parse(noteElement.Attribute("slideTo")?.Value ?? "-1"),
            hammerOn = (int.Parse(noteElement.Attribute("hammerOn")?.Value ?? "0")) == 1,
            pullOff = (int.Parse(noteElement.Attribute("pullOff")?.Value ?? "0")) == 1
        };
    }
    
    private static NoteData ParseNoteData(XElement chordElement, int stringIndex, int fret)
    {
        return new NoteData
        {
            time = float.Parse(chordElement.Attribute("time").Value, CultureInfo.InvariantCulture),
            fretNumber = fret,
            stringNumber = stringIndex,
            sustain = float.Parse(chordElement.Attribute("sustain")?.Value ?? "0", CultureInfo.InvariantCulture),
            palmMute = (int.Parse(chordElement.Attribute("palmMute")?.Value ?? "0")) == 1,
            mute = (int.Parse(chordElement.Attribute("mute")?.Value ?? "0")) == 1,
            accent = (int.Parse(chordElement.Attribute("accent")?.Value ?? "0")) == 1,
            slideTo = -1,
            hammerOn = (int.Parse(chordElement.Attribute("hopo")?.Value ?? "0")) == 1,
            pullOff = (int.Parse(chordElement.Attribute("hopo")?.Value ?? "0")) == 1
        };
    }
}