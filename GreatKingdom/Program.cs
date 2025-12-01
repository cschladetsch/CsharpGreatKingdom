using System;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Raylib_cs;

namespace GreatKingdom;

public enum AppScreen { Loading, Menu, Game, NetLobby, Training, BrainSelect, BrainVsBrain }
public enum GameMode { Hotseat, VsAI, VsNeuralNet, Network }

class Program
{
    // --- Configuration (Global) ---
    static ConfigData _config = null!;

    // --- Layout Constants ---
    const int CellSize = 80;
    const int Padding = 40;
    const int ScreenWidth = (9 * CellSize) + (Padding * 2);
    const int ScreenHeight = ScreenWidth + 80;

    // --- Engines & Managers ---
    static Renderer _renderer = null!;
    static MCTS _mcts = null!;
    static DQNAgent _neuralNet = null!;
    static NetworkManager _net = null!;
    static Brain _brainManager = null!;



    static void Main()
    {
        // 1. Load Configuration
        try
        {
            string jsonString = File.ReadAllText("config.json");
            _config = JsonSerializer.Deserialize<ConfigData>(jsonString) ?? new ConfigData();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: Failed to load config.json. Using defaults. Error: {ex.Message}");
            _config = new ConfigData();
        }

        // 2. Init Raylib
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Great Kingdom: Neural Edition");
        Raylib.SetTargetFPS(60);

        // 3. Init Lightweight Engines
        _renderer = new Renderer();
        _mcts = new MCTS();
        _net = new NetworkManager();

        // 4. Init Neural Network and Brain Manager
        _neuralNet = new DQNAgent(_config);
        _brainManager = new Brain(_config);

        // 5. Init Controller (The orchestrator handles async loading and game state)
        var controller = new GameController(_config, _renderer, _mcts, _neuralNet, _net, _brainManager);

        // 6. Main Loop
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            // PHASE A: LOGIC & INPUT
            controller.Update(dt);

            // PHASE B: RENDERING
            Raylib.BeginDrawing();

            int currentFPS = Raylib.GetFPS();

            controller.Draw(currentFPS);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }
    
    // NOTE: All logic and state variables were successfully moved to GameController.cs.
}
