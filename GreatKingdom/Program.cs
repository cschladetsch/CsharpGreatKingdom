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
    static ConfigData _config = null!;

    const int CellSize = 80;
    const int Padding = 40;
    const int ScreenWidth = (9 * CellSize) + (Padding * 2);
    const int ScreenHeight = ScreenWidth + 80;

    static Renderer _renderer = null!;
    static MCTS _mcts = null!;
    static DQNAgent _neuralNet = null!;
    static NetworkManager _net = null!;
    static Brain _brainManager = null!;

    static void Main()
    {
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

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Great Kingdom: Neural Edition");
        Raylib.SetTargetFPS(60);

        _renderer = new Renderer();
        _mcts = new MCTS();
        _net = new NetworkManager();

        _neuralNet = new DQNAgent(_config);
        _brainManager = new Brain(_config);

        var controller = new GameController(_config, _renderer, _mcts, _neuralNet, _net, _brainManager);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            controller.Update(dt);

            Raylib.BeginDrawing();

            int currentFPS = Raylib.GetFPS();

            controller.Draw(currentFPS);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }
    
    // NOTE: All logic and state variables were successfully moved to GameController.cs.
}
