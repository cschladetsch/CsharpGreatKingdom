using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

namespace GreatKingdom;

public class Brain
{
    private const string BrainDirectory = "brains";
    private const int MaxBrainsToKeep = 5;
    public const string LatestFileAlias = "latest.bin";

    public Brain(ConfigData config)
    {
        // Ensure the directory exists
        Directory.CreateDirectory(BrainDirectory);
    }
    
    // --- FILENAME GENERATION ---
    private string GenerateFilename(float loss, int gamesPlayed)
    {
        // Format loss to 6 digits for 3 decimal places precision
        // Example: 0.005843 -> L005843
        string lossStr = (loss * 1000000).ToString("000000", CultureInfo.InvariantCulture);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // Final format: brain_L005843_G12345_20251202_100000.bin
        return $"brain_L{lossStr}_G{gamesPlayed}_{timestamp}.bin";
    }
    
    // --- MAIN SAVE METHOD (Synchronized) ---
    // Takes the DQNAgent instance to read its current state (loss, games) and save the model.
    public void SaveCurrentBrain(DQNAgent agent)
    {
        // 1. Generate new filename based on current performance
        string newFilename = GenerateFilename(agent.CurrentLoss, agent.GamesPlayed);
        string newPath = Path.Combine(BrainDirectory, newFilename);

        // 2. Save the model using the agent's internal save logic
        agent.SaveModel(newPath);

        // 3. Update the 'latest' alias
        string latestPath = Path.Combine(BrainDirectory, LatestFileAlias);
        
        // Delete old alias file if it exists, then create a new symbolic link or copy (simple copy for cross-platform safety)
        if (File.Exists(latestPath)) File.Delete(latestPath);
        File.Copy(newPath, latestPath);

        // 4. Clean up old brains
        CleanupBrains();
    }
    
    // --- CLEANUP LOGIC ---
    private void CleanupBrains()
    {
        var brainFiles = Directory.GetFiles(BrainDirectory, "brain_L*.bin")
                                  .Where(f => !f.EndsWith(LatestFileAlias, StringComparison.OrdinalIgnoreCase))
                                  .ToList();

        if (brainFiles.Count <= MaxBrainsToKeep) return;

        // Structure to hold filename and parsed loss
        var sortedBrains = new List<(float Loss, string Path)>();

        // Parse loss from filename (Loss is defined by L[6 digits])
        foreach (var file in brainFiles)
        {
            string filename = Path.GetFileName(file);

            int start = filename.IndexOf("L") + 1;
            int end = start + 6;

            if (start > 0 && end <= filename.Length)
            {
                string lossDigits = filename.Substring(start, 6);
                // Convert L005843 back to 0.005843
                if (float.TryParse(lossDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out float lossValue))
                {
                    sortedBrains.Add((lossValue / 1000000f, file));
                }
            }
        }

        // Sort by Loss (ascending - lowest loss is best)
        var toDelete = sortedBrains.OrderBy(b => b.Loss)
                                   .Skip(MaxBrainsToKeep)
                                   .ToList();

        // Delete the excess files
        foreach (var item in toDelete)
        {
            File.Delete(item.Path);
        }
    }

    // --- LISTING LOGIC ---
    public string[] ListAvailableBrains()
    {
        // 1. Get all saved brain files (excluding the 'latest.bin' alias)
        var brainPaths = Directory.GetFiles(BrainDirectory, "brain_L*.bin")
                                  .Where(f => !f.EndsWith(LatestFileAlias, StringComparison.OrdinalIgnoreCase));
        
        var brains = new List<(float Loss, string DisplayName, string Path)>();

        // 2. Parse details for display and sorting
        foreach (var path in brainPaths)
        {
            string filename = Path.GetFileName(path);
            float loss = 0;
            
            // Extract loss value and display name
            int lossStart = filename.IndexOf("L") + 1;
            int lossEnd = lossStart + 6;

            if (lossStart > 0 && lossEnd <= filename.Length)
            {
                string lossDigits = filename.Substring(lossStart, 6);
                if (float.TryParse(lossDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out float lossValue))
                {
                    loss = lossValue / 1000000f;
                }
            }

            // Create display name (e.g., [Loss: 0.005843] brain_G12345_20251202_100000.bin)
            string displayName = $"[Loss: {loss:F6}] {filename.Replace($"_L{loss*1000000:000000}", "")}";
            brains.Add((loss, displayName, filename));
        }

        // 3. Sort by Loss (lowest first)
        return brains.OrderBy(b => b.Loss)
                     .Select(b => b.Path) // Return the actual filename for loading
                     .ToArray();
    }

    // --- LOADING LOGIC ---
    public bool LoadBrain(string filename)
    {
        string path = Path.Combine(BrainDirectory, filename);
        if (!File.Exists(path)) return false;
        
        // This relies on GameController's AsyncLoadWorker to perform the actual load
        // by replacing the agent instance. For simplicity, we just return true here
        // as the loading logic usually lives inside the DQNAgent (neuralNet.LoadModel)
        
        // We will assume the GameController handles the final loading based on the filename passed.
        // If this were a real file, you would put the loading logic here, but since 
        // DQNAgent handles it, we only pass the reference.
        
        return true; 
    }
}
