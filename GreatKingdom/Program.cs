using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace GreatKingdom;

// --- Data Structures ---
public enum Cell { Empty, Blue, Orange, Neutral }
public enum GameState { Playing, BlueWin_Capture, OrangeWin_Capture, BlueWin_Score, OrangeWin_Score }

public class GreatKingdomGame
{
    // --- Configuration ---
    private const int GridSize = 9;
    private const int CellPixelSize = 80;
    private const int Padding = 40; 
    private const int UIHeight = 60;
    private const int ScreenWidth = (GridSize * CellPixelSize) + (Padding * 2);
    private const int ScreenHeight = ScreenWidth + UIHeight;

    // --- State ---
    // FIX 1: Use "null!" to tell the compiler we promise to initialize it in the constructor/InitializeGame
    private Cell[,] _board = null!; 
    private bool _isBlueTurn;
    private GameState _state;
    private int _consecutivePasses;

    // --- Colors ---
    private Color ColorBg = new Color(35, 35, 40, 255);       
    private Color ColorGrid = new Color(70, 70, 80, 255);     
    private Color ColorBlue = new Color(60, 120, 230, 255);   
    private Color ColorOrange = new Color(230, 100, 40, 255); 
    private Color ColorNeutral = new Color(140, 140, 140, 255);
    private Color ColorShadow = new Color(0, 0, 0, 100);

    public GreatKingdomGame()
    {
        InitializeGame();
    }

    public void Run()
    {
        // FIX 2: Changed "FlagMsaa4xHint" to "Msaa4xHint" (Raylib-cs 6.0+ syntax)
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Great Kingdom");
        Raylib.SetTargetFPS(60);

        while (!Raylib.WindowShouldClose())
        {
            Update();
            Draw();
        }

        Raylib.CloseWindow();
    }

    private void InitializeGame()
    {
        _board = new Cell[GridSize, GridSize];
        _isBlueTurn = true;
        _state = GameState.Playing;
        _consecutivePasses = 0;

        // Place Neutral Castle at Center
        _board[GridSize / 2, GridSize / 2] = Cell.Neutral;
    }

    // --- Logic ---
    private void Update()
    {
        if (_state != GameState.Playing)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.R)) InitializeGame();
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            _consecutivePasses++;
            CheckEndGameConditions();
            _isBlueTurn = !_isBlueTurn;
            return;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            int x = ((int)mousePos.X - Padding) / CellPixelSize;
            int y = ((int)mousePos.Y - Padding) / CellPixelSize;

            if (IsValidMove(x, y))
            {
                PlaceStone(x, y);
            }
        }
    }

    private bool IsValidMove(int x, int y)
    {
        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) return false;
        if (_board[x, y] != Cell.Empty) return false;
        return true;
    }

    private void PlaceStone(int x, int y)
    {
        _consecutivePasses = 0;
        Cell current = _isBlueTurn ? Cell.Blue : Cell.Orange;
        Cell opponent = _isBlueTurn ? Cell.Orange : Cell.Blue;

        _board[x, y] = current;

        // Instant Win Rule (Capture Enemy)
        if (CheckCapture(opponent))
        {
            _state = _isBlueTurn ? GameState.BlueWin_Capture : GameState.OrangeWin_Capture;
            return;
        }

        // Suicide Rule
        if (CheckCapture(current))
        {
            _state = _isBlueTurn ? GameState.OrangeWin_Capture : GameState.BlueWin_Capture;
            return;
        }

        _isBlueTurn = !_isBlueTurn;
    }

    private bool CheckCapture(Cell targetColor)
    {
        bool[,] visited = new bool[GridSize, GridSize];
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (_board[x, y] == targetColor && !visited[x, y])
                {
                    if (!HasLiberties(x, y, targetColor, visited)) return true;
                }
            }
        }
        return false;
    }

    private bool HasLiberties(int startX, int startY, Cell color, bool[,] globalVisited)
    {
        Stack<(int, int)> stack = new Stack<(int, int)>();
        stack.Push((startX, startY));
        HashSet<(int, int)> group = new HashSet<(int, int)>(); 
        bool foundLiberty = false;

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (group.Contains((cx, cy))) continue;
            group.Add((cx, cy));
            globalVisited[cx, cy] = true;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];
                if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;

                Cell neighbor = _board[nx, ny];
                if (neighbor == Cell.Empty) foundLiberty = true;
                else if (neighbor == color && !group.Contains((nx, ny))) stack.Push((nx, ny));
            }
        }
        return foundLiberty;
    }

    private void CheckEndGameConditions()
    {
        if (_consecutivePasses >= 2) CalculateTerritoryScore();
    }

    private void CalculateTerritoryScore()
    {
        int blueScore = 0;
        int orangeScore = 0;
        bool[,] visited = new bool[GridSize, GridSize];

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (_board[x, y] == Cell.Empty && !visited[x, y])
                {
                    AnalyzeTerritory(x, y, visited, out int size, out Cell owner);
                    if (owner == Cell.Blue) blueScore += size;
                    else if (owner == Cell.Orange) orangeScore += size;
                }
            }
        }

        if (blueScore >= orangeScore + 3) _state = GameState.BlueWin_Score;
        else _state = GameState.OrangeWin_Score;
    }

    private void AnalyzeTerritory(int startX, int startY, bool[,] visited, out int size, out Cell owner)
    {
        size = 0;
        owner = Cell.Empty;
        Stack<(int, int)> stack = new Stack<(int, int)>();
        stack.Push((startX, startY));
        HashSet<Cell> borderingColors = new HashSet<Cell>();
        bool touchesTop = false, touchesBottom = false, touchesLeft = false, touchesRight = false;
        int currentRegionSize = 0;

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (visited[cx, cy]) continue;
            visited[cx, cy] = true;
            currentRegionSize++;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];

                if (ny < 0) touchesTop = true;
                if (ny >= GridSize) touchesBottom = true;
                if (nx < 0) touchesLeft = true;
                if (nx >= GridSize) touchesRight = true;

                if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;

                Cell neighbor = _board[nx, ny];
                if (neighbor == Cell.Empty)
                {
                    if(!visited[nx, ny]) stack.Push((nx, ny));
                }
                else borderingColors.Add(neighbor);
            }
        }

        if (touchesTop && touchesBottom && touchesLeft && touchesRight) return; // 4-Edge Rule

        bool touchesBlue = borderingColors.Contains(Cell.Blue);
        bool touchesOrange = borderingColors.Contains(Cell.Orange);
        
        if (touchesBlue && !touchesOrange) { owner = Cell.Blue; size = currentRegionSize; }
        else if (touchesOrange && !touchesBlue) { owner = Cell.Orange; size = currentRegionSize; }
    }

    // --- Rendering ---
    private void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColorBg);

        // 1. Draw Board Grid
        Raylib.DrawRectangleLinesEx(new Rectangle(Padding, Padding, GridSize*CellPixelSize, GridSize*CellPixelSize), 2, ColorGrid);
        
        for (int i = 1; i < GridSize; i++)
        {
            float pos = Padding + (i * CellPixelSize);
            Raylib.DrawLineEx(new Vector2(pos, Padding), new Vector2(pos, Padding + GridSize * CellPixelSize), 1, ColorGrid);
            Raylib.DrawLineEx(new Vector2(Padding, pos), new Vector2(Padding + GridSize * CellPixelSize, pos), 1, ColorGrid);
        }

        // 2. Draw Stones
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (_board[x, y] == Cell.Empty) continue;

                int cx = Padding + (x * CellPixelSize) + (CellPixelSize / 2);
                int cy = Padding + (y * CellPixelSize) + (CellPixelSize / 2);
                int radius = (CellPixelSize / 2) - 8;

                // Shadow
                Raylib.DrawCircle(cx + 4, cy + 4, radius, ColorShadow);

                // Body
                Color c = Color.White;
                if (_board[x, y] == Cell.Blue) c = ColorBlue;
                else if (_board[x, y] == Cell.Orange) c = ColorOrange;
                else if (_board[x, y] == Cell.Neutral) c = ColorNeutral;
                
                Raylib.DrawCircle(cx, cy, radius, c);
                
                // Rim
                Raylib.DrawCircleLines(cx, cy, radius, new Color(255,255,255,50));
            }
        }
        
        // 3. Hover Effect
        if (_state == GameState.Playing)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            int hx = ((int)mousePos.X - Padding) / CellPixelSize;
            int hy = ((int)mousePos.Y - Padding) / CellPixelSize;
            
            if (IsValidMove(hx, hy))
            {
                int cx = Padding + (hx * CellPixelSize) + (CellPixelSize / 2);
                int cy = Padding + (hy * CellPixelSize) + (CellPixelSize / 2);
                Color ghost = _isBlueTurn ? ColorBlue : ColorOrange;
                ghost.A = 100;
                Raylib.DrawCircle(cx, cy, (CellPixelSize/2) - 12, ghost);
            }
        }

        // 4. UI Text
        DrawUI();

        Raylib.EndDrawing();
    }

    private void DrawUI()
    {
        string text = "";
        Color c = Color.RayWhite;

        switch (_state)
        {
            case GameState.Playing:
                string turn = _isBlueTurn ? "BLUE Turn" : "ORANGE Turn";
                text = $"{turn} (SPACE to Pass)";
                c = _isBlueTurn ? ColorBlue : ColorOrange;
                break;
            case GameState.BlueWin_Capture: text = "BLUE WINS! (Capture) [Press R]"; c = ColorBlue; break;
            case GameState.OrangeWin_Capture: text = "ORANGE WINS! (Capture) [Press R]"; c = ColorOrange; break;
            case GameState.BlueWin_Score: text = "BLUE WINS! (Territory) [Press R]"; c = ColorBlue; break;
            case GameState.OrangeWin_Score: text = "ORANGE WINS! (Territory) [Press R]"; c = ColorOrange; break;
        }

        int textWidth = Raylib.MeasureText(text, 24);
        Raylib.DrawText(text, (ScreenWidth/2) - (textWidth/2), ScreenHeight - 45, 24, c);
    }
}

class Program
{
    public static void Main()
    {
        var game = new GreatKingdomGame();
        game.Run();
    }
}
