using System;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_cs;

namespace GreatKingdom;

public enum AppScreen { Menu, Game, NetLobby } // Added NetLobby
public enum GameMode { Hotseat, VsAI, Network }

class Program
{
    const int CellSize = 80;
    const int GridSize = 9;
    const int Padding = 40;
    const int ScreenWidth = (GridSize * CellSize) + (Padding * 2);
    const int ScreenHeight = ScreenWidth + 80;

    static AppScreen _currentScreen = AppScreen.Menu;
    static GameMode _currentMode = GameMode.Hotseat;
    static GameState _gameState;
    static MCTS _ai;
    static NetworkManager _net; // <--- ADDED

    // UI State
    static bool _isAiThinking = false;
    static string _statusMessage = "";
    static string _targetIp = "127.0.0.1"; // Default to localhost

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Great Kingdom");
        Raylib.SetTargetFPS(60);

        _ai = new MCTS();
        _net = new NetworkManager(); // <--- Init Network

        while (!Raylib.WindowShouldClose())
        {
            switch (_currentScreen)
            {
                case AppScreen.Menu:
                    UpdateMenu();
                    DrawMenu();
                    break;
                case AppScreen.NetLobby: // <--- New Screen
                    UpdateLobby();
                    DrawLobby();
                    break;
                case AppScreen.Game:
                    UpdateGame();
                    DrawGame();
                    break;
            }
        }
        Raylib.CloseWindow();
    }

    // ================= MENU =================
    static void UpdateMenu()
    {
        // Simple buttons
        if(BtnClick(250, "Play Hotseat")) StartGame(GameMode.Hotseat);
        if(BtnClick(330, "Play vs AI"))   StartGame(GameMode.VsAI);
        
        // Go to Lobby instead of starting immediately
        if(BtnClick(410, "Network Multiplayer")) _currentScreen = AppScreen.NetLobby;
    }

    static void DrawMenu()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(30, 30, 35, 255));
        Raylib.DrawText("GREAT KINGDOM", ScreenWidth/2 - 180, 100, 50, Color.RayWhite);
        
        DrawBtn(250, "Play Hotseat");
        DrawBtn(330, "Play vs AI");
        DrawBtn(410, "Network Multiplayer");
        Raylib.EndDrawing();
    }

    // ================= NETWORK LOBBY =================
    static void UpdateLobby()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _currentScreen = AppScreen.Menu;

        // HOST BUTTON
        if (BtnClick(200, "HOST GAME (Blue)"))
        {
            _statusMessage = "Waiting for player...";
            Task.Run(async () => 
            {
                if (await _net.HostGame()) StartGame(GameMode.Network);
            });
        }

        // JOIN BUTTON
        if (BtnClick(300, $"JOIN {_targetIp} (Orange)"))
        {
            _statusMessage = "Connecting...";
            Task.Run(async () => 
            {
                if (await _net.JoinGame(_targetIp)) StartGame(GameMode.Network);
            });
        }
    }

    static void DrawLobby()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 40, 50, 255));
        Raylib.DrawText("NETWORK LOBBY", 250, 50, 30, Color.White);
        
        DrawBtn(200, "HOST GAME (Blue)");
        DrawBtn(300, $"JOIN {_targetIp} (Orange)");
        
        Raylib.DrawText(_statusMessage, 50, 500, 20, Color.Yellow);
        Raylib.DrawText("Note: To change IP, edit source or assume Localhost for testing", 50, 550, 10, Color.Gray);
        Raylib.EndDrawing();
    }

    // ================= GAME LOGIC =================
    static void StartGame(GameMode mode)
    {
        _currentMode = mode;
        _gameState = new GameState();
        _currentScreen = AppScreen.Game;
        _isAiThinking = false;
        _statusMessage = mode == GameMode.Network 
            ? (_net.IsHost ? "You are BLUE" : "You are ORANGE") 
            : "Blue Start!";
    }

    static void UpdateGame()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.R) && _currentMode != GameMode.Network)
        {
            _currentScreen = AppScreen.Menu;
            return;
        }

        if (_gameState.Winner != Player.None)
        {
            _statusMessage = $"{_gameState.Winner} WINS!";
            return;
        }

        // --- 1. NETWORK RECEIVE ---
        if (_currentMode == GameMode.Network)
        {
            // Process all moves sitting in the queue
            while (_net.IncomingMoves.TryDequeue(out int moveIdx))
            {
                _gameState.ApplyMove(moveIdx);
                _statusMessage = "Opponent Moved. Your Turn.";
            }
        }

        // --- 2. AI THINKING ---
        if (_currentMode == GameMode.VsAI && _gameState.CurrentTurn == Player.Orange && !_isAiThinking)
        {
            _isAiThinking = true;
            _statusMessage = "AI Thinking...";
            Task.Run(() => {
                int best = _ai.GetBestMove(_gameState);
                _gameState.ApplyMove(best);
                _isAiThinking = false;
                _statusMessage = "Your Turn";
            });
            return; 
        }

        // --- 3. PLAYER INPUT ---
        // Verify it is actually our turn!
        bool isMyTurn = true;
        
        if (_currentMode == GameMode.VsAI && _gameState.CurrentTurn == Player.Orange) isMyTurn = false;
        if (_currentMode == GameMode.Network)
        {
            // Host plays Blue, Client plays Orange
            if (_net.IsHost && _gameState.CurrentTurn != Player.Blue) isMyTurn = false;
            if (!_net.IsHost && _gameState.CurrentTurn != Player.Orange) isMyTurn = false;
        }

        if (isMyTurn && !_isAiThinking)
        {
            // Pass (Space)
            if (Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                _gameState.ApplyMove(-1);
                if (_currentMode == GameMode.Network) _net.SendMove(-1); // Send Pass
            }

            // Click
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Vector2 m = Raylib.GetMousePosition();
                int gx = ((int)m.X - Padding) / CellSize;
                int gy = ((int)m.Y - Padding) / CellSize;

                if (gx >= 0 && gx < 9 && gy >= 0 && gy < 9)
                {
                    int idx = _gameState.Idx(gx, gy);
                    if (_gameState.Board[idx] == (byte)Player.None)
                    {
                        _gameState.ApplyMove(idx);
                        if (_currentMode == GameMode.Network) _net.SendMove(idx); // Send Move
                    }
                }
            }
        }
    }

    static void DrawGame()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(30, 30, 30, 255));
        
        // Grid
        Raylib.DrawRectangleLines(Padding, Padding, GridSize*CellSize, GridSize*CellSize, Color.Gray);
        for(int i=1; i<9; i++) {
            Raylib.DrawLine(Padding+i*CellSize, Padding, Padding+i*CellSize, Padding+GridSize*CellSize, Color.DarkGray);
            Raylib.DrawLine(Padding, Padding+i*CellSize, Padding+GridSize*CellSize, Padding+i*CellSize, Color.DarkGray);
        }

        // Pieces
        for(int i=0; i<81; i++) {
            if(_gameState.Board[i] == (byte)Player.None) continue;
            int cx = Padding + (i%9*CellSize) + CellSize/2;
            int cy = Padding + (i/9*CellSize) + CellSize/2;
            Color c = _gameState.Board[i] == (byte)Player.Blue ? Color.Blue : 
                      _gameState.Board[i] == (byte)Player.Orange ? Color.Orange : Color.LightGray;
            Raylib.DrawCircle(cx, cy, 30, c);
        }

        Raylib.DrawText(_statusMessage, Padding, ScreenHeight - 60, 24, Color.White);
        Raylib.EndDrawing();
    }

    // UI Helpers
    static bool BtnClick(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), new Rectangle(x,y,w,h));
        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) return true;
        return false;
    }
    static void DrawBtn(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        Raylib.DrawRectangleRec(new Rectangle(x,y,w,h), new Color(50,50,60,255));
        Raylib.DrawRectangleLines((int)x, (int)y, (int)w, (int)h, Color.Gray);
        Raylib.DrawText(text, (int)x+20, (int)y+20, 20, Color.White);
    }
}
