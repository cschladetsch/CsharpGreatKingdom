using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net.Http;
using Raylib_cs;

namespace GreatKingdom;

// --- Enums ---
public enum AppScreen { Menu, Game, NetLobby }
public enum GameMode { Hotseat, VsAI, Network }

class Program
{
    // --- Config ---
    const int CellSize = 80;
    const int GridSize = 9;
    const int Padding = 40;
    const int ScreenWidth = (GridSize * CellSize) + (Padding * 2);
    const int ScreenHeight = ScreenWidth + 80;

    // --- State ---
    static AppScreen _currentScreen = AppScreen.Menu;
    static GameMode _currentMode = GameMode.Hotseat;
    static GameState _gameState;
    static MCTS _ai;
    static NetworkManager _net;

    // --- UI/Animation State ---
    static bool _isAiThinking = false;
    static string _statusMessage = "";
    static string _targetIp = "127.0.0.1";
    static string _publicIp = "Fetching...";
    static bool _fetchedIp = false;
    
    // Animation timers
    static float _time = 0.0f; 

    // --- Colors ---
    static Color ColBlue = new Color(60, 120, 230, 255);
    static Color ColOrange = new Color(230, 100, 40, 255);
    static Color ColNeutral = new Color(120, 120, 130, 255);
    static Color ColDarkBg = new Color(30, 30, 35, 255);

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Great Kingdom: Castle Edition");
        Raylib.SetTargetFPS(60);

        _ai = new MCTS();
        _net = new NetworkManager();

        while (!Raylib.WindowShouldClose())
        {
            _time += Raylib.GetFrameTime(); // Global animation timer

            switch (_currentScreen)
            {
                case AppScreen.Menu:
                    UpdateMenu();
                    DrawMenu();
                    break;
                case AppScreen.NetLobby:
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

    // ==========================================
    //              MENU LOGIC
    // ==========================================

    static void UpdateMenu()
    {
        if(BtnClick(250, "Play Hotseat")) StartGame(GameMode.Hotseat);
        if(BtnClick(330, "Play vs AI"))   StartGame(GameMode.VsAI);
        if(BtnClick(410, "Network Multiplayer")) _currentScreen = AppScreen.NetLobby;
    }

    static void DrawMenu()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColDarkBg);
        
        // Draw a decorative castle logo
        DrawCastle(ScreenWidth/2, 120, 60, ColBlue, false);

        Raylib.DrawText("GREAT KINGDOM", ScreenWidth/2 - Raylib.MeasureText("GREAT KINGDOM", 40)/2, 180, 40, Color.RayWhite);
        
        DrawBtn(250, "Play Hotseat");
        DrawBtn(330, "Play vs AI");
        DrawBtn(410, "Network Multiplayer");
        Raylib.EndDrawing();
    }

    // ==========================================
    //              LOBBY LOGIC
    // ==========================================

    static void UpdateLobby()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _currentScreen = AppScreen.Menu;

        if (!_fetchedIp)
        {
            _fetchedIp = true;
            Task.Run(async () => {
                try { using var c = new HttpClient(); _publicIp = await c.GetStringAsync("https://api.ipify.org"); }
                catch { _publicIp = "Unknown"; }
            });
        }

        if (BtnClick(200, "HOST GAME (Blue)"))
        {
            _statusMessage = "Starting Server...";
            Task.Run(async () => {
                if(await _net.HostGame()) StartGame(GameMode.Network);
                else _statusMessage = "Error: Port 7777 busy!";
            });
        }

        if (BtnClick(300, $"JOIN {_targetIp} (Orange)"))
        {
            _statusMessage = "Connecting...";
            Task.Run(async () => {
                if(await _net.JoinGame(_targetIp)) StartGame(GameMode.Network);
                else _statusMessage = "Connection Failed.";
            });
        }
    }

    static void DrawLobby()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 40, 50, 255));
        
        // Title with Pulse
        int titleY = 50 + (int)(Math.Sin(_time * 2) * 5);
        Raylib.DrawText("NETWORK LOBBY", ScreenWidth/2 - Raylib.MeasureText("NETWORK LOBBY", 30)/2, titleY, 30, Color.White);
        
        DrawBtn(200, "HOST GAME (Blue)");
        DrawBtn(300, $"JOIN {_targetIp} (Orange)");
        
        Raylib.DrawText("Your Public IP:", 50, 400, 20, Color.Gray);
        Raylib.DrawText(_publicIp, 50, 430, 30, Color.Green);

        Raylib.DrawText(_statusMessage, 50, 500, 20, Color.Yellow);
        Raylib.EndDrawing();
    }

    // ==========================================
    //              GAME LOGIC
    // ==========================================

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
        // R to Restart (Only if Game Over or Hotseat/AI)
        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            if (_gameState.Winner != Player.None || _currentMode != GameMode.Network)
            {
                _currentScreen = AppScreen.Menu;
                return;
            }
        }

        // --- GAME OVER STATE ---
        if (_gameState.Winner != Player.None) return; // Stop processing moves

        // --- NETWORK RECEIVE ---
        if (_currentMode == GameMode.Network)
        {
            while (_net.IncomingMoves.TryDequeue(out int moveIdx))
            {
                _gameState.ApplyMove(moveIdx);
                _statusMessage = "Opponent Moved.";
            }
        }

        // --- AI LOGIC ---
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

        // --- PLAYER INPUT ---
        bool isMyTurn = true;
        if (_currentMode == GameMode.VsAI && _gameState.CurrentTurn == Player.Orange) isMyTurn = false;
        if (_currentMode == GameMode.Network)
        {
            if (_net.IsHost && _gameState.CurrentTurn != Player.Blue) isMyTurn = false;
            if (!_net.IsHost && _gameState.CurrentTurn != Player.Orange) isMyTurn = false;
        }

        if (isMyTurn && !_isAiThinking)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                _gameState.ApplyMove(-1);
                if (_currentMode == GameMode.Network) _net.SendMove(-1);
            }

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
                        if (_currentMode == GameMode.Network) _net.SendMove(idx);
                    }
                }
            }
        }
    }

    static void DrawGame()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColDarkBg);
        
        // 1. Draw Grid
        Raylib.DrawRectangleLines(Padding, Padding, GridSize*CellSize, GridSize*CellSize, Color.Gray);
        for(int i=1; i<9; i++) {
            Raylib.DrawLine(Padding+i*CellSize, Padding, Padding+i*CellSize, Padding+GridSize*CellSize, new Color(50,50,60,255));
            Raylib.DrawLine(Padding, Padding+i*CellSize, Padding+GridSize*CellSize, Padding+i*CellSize, new Color(50,50,60,255));
        }

        // 2. Draw Castles
        for(int i=0; i<81; i++) {
            if(_gameState.Board[i] == (byte)Player.None) continue;
            
            int cx = Padding + (i%GridSize*CellSize) + CellSize/2;
            int cy = Padding + (i/GridSize*CellSize) + CellSize/2;
            
            Color c = ColNeutral;
            if(_gameState.Board[i] == (byte)Player.Blue) c = ColBlue;
            else if(_gameState.Board[i] == (byte)Player.Orange) c = ColOrange;

            DrawCastle(cx, cy, 35, c, false);
        }
        
        // 3. Hover Ghost
        if (_gameState.Winner == Player.None && !_isAiThinking)
        {
            Vector2 m = Raylib.GetMousePosition();
            int hx = ((int)m.X - Padding) / CellSize;
            int hy = ((int)m.Y - Padding) / CellSize;
            if (hx >= 0 && hx < 9 && hy >=0 && hy < 9) {
                 int cx = Padding + hx * CellSize + CellSize/2;
                 int cy = Padding + hy * CellSize + CellSize/2;
                 Color ghost = _gameState.CurrentTurn == Player.Blue ? ColBlue : ColOrange;
                 DrawCastle(cx, cy, 35, ghost, true); // True = Ghost mode
            }
        }

        // 4. UI Status
        Raylib.DrawText(_statusMessage, Padding, ScreenHeight - 60, 24, Color.White);

        // ==========================================
        //           FANCY GAME OVER SCREEN
        // ==========================================
        if (_gameState.Winner != Player.None)
        {
            // A. Full Screen Pulse Overlay
            Color winColor = _gameState.Winner == Player.Blue ? ColBlue : ColOrange;
            float alpha = 0.2f + (float)Math.Sin(_time * 3) * 0.1f; // Pulse between 0.1 and 0.3
            Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, Raylib.ColorAlpha(winColor, alpha));

            // B. Bouncing Text
            string winText = $"{_gameState.Winner} WINS!";
            int fontSize = 60;
            int txtW = Raylib.MeasureText(winText, fontSize);
            float bounceY = ScreenHeight/2 - 50 + (float)Math.Sin(_time * 5) * 20;

            // Draw Shadow then Text
            Raylib.DrawText(winText, ScreenWidth/2 - txtW/2 + 4, (int)bounceY + 4, fontSize, new Color(0,0,0,128));
            Raylib.DrawText(winText, ScreenWidth/2 - txtW/2, (int)bounceY, fontSize, Color.White);

            // C. Restart Prompt (Blinking)
            if ((int)(_time * 2) % 2 == 0)
            {
                string restart = "PRESS [ R ] TO RETURN TO MENU";
                int rW = Raylib.MeasureText(restart, 20);
                Raylib.DrawText(restart, ScreenWidth/2 - rW/2, (int)bounceY + 80, 20, Color.RayWhite);
            }
        }

        Raylib.EndDrawing();
    }

    // --- PROCEDURAL CASTLE DRAWING ---
    static void DrawCastle(int cx, int cy, int size, Color c, bool isGhost)
    {
        if (isGhost) c = Raylib.ColorAlpha(c, 0.4f);

        // Dimensions relative to size
        int w = size;      // Width of tower body
        int h = size * 4/3;// Height of tower
        int baseW = (int)(w * 1.4f); // Base width
        
        // 1. Draw Shadow (if not ghost)
        if (!isGhost)
        {
            Raylib.DrawEllipse(cx, cy + h/2 - 2, baseW/1.5f, 10, new Color(0,0,0,100));
        }

        // 2. Base (Foundation)
        Raylib.DrawRectangle(cx - baseW/2, cy + h/2 - 10, baseW, 10, c);
        
        // 3. Tower Body
        Raylib.DrawRectangle(cx - w/2, cy - h/2 + 10, w, h - 10, c);
        
        // 4. Battlements (The "Teeth" on top)
        int topY = cy - h/2;
        int toothW = w / 3;
        
        // Draw wider top platform
        Raylib.DrawRectangle(cx - baseW/2, topY, baseW, 10, c);
        
        // Draw 3 Teeth
        Raylib.DrawRectangle(cx - baseW/2, topY - 8, toothW, 8, c); // Left
        Raylib.DrawRectangle(cx - toothW/2, topY - 8, toothW, 8, c); // Center
        Raylib.DrawRectangle(cx + baseW/2 - toothW, topY - 8, toothW, 8, c); // Right

        // 5. Detail: Window Slit (Black or Darker)
        Color detail = new Color(0,0,0,60);
        Raylib.DrawRectangle(cx - 3, cy - 5, 6, 15, detail);
        Raylib.DrawCircle(cx, cy - 8, 3, detail); // Rounded top of window
        
        // 6. Highlight (Rim Light)
        Raylib.DrawRectangleLines(cx - w/2, cy - h/2 + 10, w, h - 10, Raylib.ColorAlpha(Color.White, 0.2f));
    }

    // --- UI Helpers ---
    static bool BtnClick(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), new Rectangle(x,y,w,h));
        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) return true;
        return false;
    }
    static void DrawBtn(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), new Rectangle(x,y,w,h));
        
        // Button Shadow
        Raylib.DrawRectangle((int)x+4, (int)y+4, (int)w, (int)h, new Color(0,0,0,80));
        
        // Button Body
        Color col = hover ? ColBlue : new Color(50,50,60,255);
        Raylib.DrawRectangleRec(new Rectangle(x,y,w,h), col);
        
        // Border
        Color border = hover ? Color.White : Color.Gray;
        Raylib.DrawRectangleLinesEx(new Rectangle(x,y,w,h), 2, border);
        
        // Text
        int txtSize = 20;
        int txtW = Raylib.MeasureText(text, txtSize);
        Raylib.DrawText(text, (int)(x + w/2 - txtW/2), (int)(y + h/2 - txtSize/2), txtSize, Color.White);
    }
}
