using System;
using System.Numerics;
using Raylib_cs;

namespace GreatKingdom;

public class Renderer
{
    // --- Layout Constants (Must match Program.cs logic) ---
    const int CellSize = 80;
    const int GridSize = 9;
    const int Padding = 40;
    const int ScreenWidth = (GridSize * CellSize) + (Padding * 2);
    const int ScreenHeight = ScreenWidth + 80;

    // --- Colors ---
    static Color ColBlue = new Color(60, 120, 230, 255);
    static Color ColOrange = new Color(230, 100, 40, 255);
    static Color ColNeutral = new Color(120, 120, 130, 255);
    static Color ColDarkBg = new Color(30, 30, 35, 255);

    // --- DRAWING METHODS ---

    public void DrawMenu(bool isBrainReady, float time, int fps)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColDarkBg);
        
        DrawCastle(ScreenWidth/2, 80, 50, ColBlue, false);
        Raylib.DrawText("GREAT KINGDOM", ScreenWidth/2 - 160, 140, 40, Color.RayWhite);

        DrawBtn(200, "Play Hotseat");
        DrawBtn(280, "Play vs MCTS (Hard)");
        
        if (isBrainReady)
        {
            DrawBtn(360, "Play vs NeuralNet");
            DrawBtn(440, "Train Neural Net");
            DrawBtn(600, "Load Specific Brain");
        }
        else
        {
            DrawBtnDisabled(360, $"Loading AI...");
            DrawBtnDisabled(440, "Please Wait");
            DrawBtnDisabled(600, "Load Specific Brain");
        }

        DrawBtn(520, "Network Multiplayer");
        
        DrawFPS(fps);
        Raylib.EndDrawing();
    }

    public void DrawLobby(string ip, string status, float time, int fps)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 40, 50, 255));
        
        int yOffset = (int)(Math.Sin(time * 3) * 5);
        Raylib.DrawText("NETWORK LOBBY", 260, 50 + yOffset, 30, Color.White);
        
        DrawBtn(200, "HOST GAME (Blue)");
        DrawBtn(300, "JOIN 127.0.0.1 (Orange)");
        
        Raylib.DrawText("Your Public IP:", 50, 400, 20, Color.Gray);
        Raylib.DrawText(ip, 50, 430, 30, Color.Green);
        Raylib.DrawText(status, 50, 500, 20, Color.Yellow);
        
        DrawFPS(fps);
        Raylib.EndDrawing();
    }

    public void DrawTraining(int games, float loss, float time, string msg, float msgTimer, int fps)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColDarkBg);
        
        Raylib.DrawText("NEURAL NETWORK GYM", 50, 50, 30, Color.White);
        Raylib.DrawText($"Games: {games}", 50, 150, 30, ColBlue);
        Raylib.DrawText($"Loss:  {loss:F5}", 50, 200, 30, ColOrange);
        
        // Pulse
        float pulse = 10 + (float)Math.Sin(time * 15) * 5;
        Raylib.DrawCircle(600, 200, 40 + pulse, Raylib.ColorAlpha(Color.Magenta, 0.4f));

        DrawBtn(ScreenHeight - 80, "Stop Training");
        DrawBtn(ScreenHeight - 160, "Save New Brain"); 
        DrawBtn(ScreenHeight - 240, "Load Specific Brain"); 
        
        if (msgTimer > 0) Raylib.DrawText(msg, 400, ScreenHeight - 120, 20, Color.Green);

        DrawFPS(fps);
        Raylib.EndDrawing();
    }
    
    public void DrawBrainSelect(string[] availableBrains, int selectedIndex, int scrollOffset)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 25, 40, 255));
        
        Raylib.DrawText("SELECT BRAIN MODEL (ESC to Cancel / ENTER to Load)", 50, 50, 30, Color.White);

        if (availableBrains.Length == 0)
        {
            Raylib.DrawText("No timestamped brains found in /brain_models/", 50, 150, 20, Color.Red);
        }

        // Display list of files
        const int DisplayLimit = 12;
        for (int i = 0; i < Math.Min(DisplayLimit, availableBrains.Length); i++)
        {
            int fileIndex = i + scrollOffset;
            if (fileIndex >= availableBrains.Length) break;

            int yPos = 150 + i * 30;
            string filename = availableBrains[fileIndex];
            
            Color textColor = Color.White;
            
            // Highlight the selected file
            if (fileIndex == selectedIndex)
            {
                Raylib.DrawRectangle(40, yPos - 5, ScreenWidth - 80, 30, Raylib.ColorAlpha(ColOrange, 0.5f));
                textColor = Color.White;
            }

            Raylib.DrawText(filename, 50, yPos, 20, textColor);
        }
        
        if (availableBrains.Length > 0)
        {
            Raylib.DrawText("Press ENTER to Load Selected Brain", 50, ScreenHeight - 50, 20, Color.Green);
        }
        
        DrawFPS(Raylib.GetFPS()); // Draw FPS here directly since it's a one-off call

        Raylib.EndDrawing();
    }


    public void DrawGame(GameState state, bool isAiThinking, string status, float time, int fps)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(ColDarkBg);
        
        // Grid
        Raylib.DrawRectangleLines(Padding, Padding, GridSize*CellSize, GridSize*CellSize, Color.Gray);
        for(int i=1; i<9; i++) {
            Raylib.DrawLine(Padding+i*CellSize, Padding, Padding+i*CellSize, Padding+GridSize*CellSize, new Color(50,50,60,255));
            Raylib.DrawLine(Padding, Padding+i*CellSize, Padding+GridSize*CellSize, Padding+i*CellSize, new Color(50,50,60,255));
        }

        // Castles
        for(int i=0; i<81; i++) {
            if(state.Board[i] == (byte)Player.None) continue;
            int cx = Padding + (i%GridSize*CellSize) + CellSize/2;
            int cy = Padding + (i/GridSize*CellSize) + CellSize/2;
            Color c = (state.Board[i] == (byte)Player.Blue) ? ColBlue : 
                      (state.Board[i] == (byte)Player.Orange) ? ColOrange : ColNeutral;
            DrawCastle(cx, cy, 35, c, false);
        }

        // Hover Ghost
        if (state.Winner == Player.None && !isAiThinking) {
            Vector2 m = Raylib.GetMousePosition();
            int hx = ((int)m.X - Padding) / CellSize;
            int hy = ((int)m.Y - Padding) / CellSize;
            if (hx >= 0 && hx < 9 && hy >=0 && hy < 9) {
                 int cx = Padding + hx * CellSize + CellSize/2;
                 int cy = Padding + hy * CellSize + CellSize/2;
                 Color ghost = state.CurrentTurn == Player.Blue ? ColBlue : ColOrange;
                 DrawCastle(cx, cy, 35, ghost, true);
            }
        }
        
        // UI
        Raylib.DrawText(status, Padding, ScreenHeight - 60, 24, Color.White);

        // Game Over Overlay
        if (state.Winner != Player.None) {
            Color wc = state.Winner == Player.Blue ? ColBlue : ColOrange;
            Raylib.DrawRectangle(0,0,ScreenWidth,ScreenHeight,Raylib.ColorAlpha(wc, 0.3f));
            string wTxt = $"{state.Winner} WINS!";
            float bounce = (float)Math.Sin(time*5)*10;
            Raylib.DrawText(wTxt, ScreenWidth/2 - 100, (int)(ScreenHeight/2 + bounce), 50, Color.White);
            if((int)(time*2)%2==0) Raylib.DrawText("PRESS R TO RESET", ScreenWidth/2 - 90, (int)(ScreenHeight/2 + 60 + bounce), 20, Color.RayWhite);
        }
        
        DrawFPS(fps);
        Raylib.EndDrawing();
    }

    // --- HELPERS ---

    private void DrawCastle(int cx, int cy, int size, Color c, bool isGhost)
    {
        if (isGhost) c = Raylib.ColorAlpha(c, 0.4f);
        int w = size, h = size * 4/3, baseW = (int)(w * 1.4f);
        
        if (!isGhost) Raylib.DrawEllipse(cx, cy + h/2 - 2, baseW/1.5f, 10, new Color(0,0,0,100));
        Raylib.DrawRectangle(cx - baseW/2, cy + h/2 - 10, baseW, 10, c);
        Raylib.DrawRectangle(cx - w/2, cy - h/2 + 10, w, h - 10, c);
        
        int topY = cy - h/2, toothW = w / 3;
        Raylib.DrawRectangle(cx - baseW/2, topY, baseW, 10, c);
        Raylib.DrawRectangle(cx - baseW/2, topY - 8, toothW, 8, c);
        Raylib.DrawRectangle(cx - toothW/2, topY - 8, toothW, 8, c);
        Raylib.DrawRectangle(cx + baseW/2 - toothW, topY - 8, toothW, 8, c);

        Raylib.DrawRectangle(cx - 3, cy - 5, 6, 15, new Color(0,0,0,60));
        Raylib.DrawRectangleLines(cx - w/2, cy - h/2 + 10, w, h - 10, Raylib.ColorAlpha(Color.White, 0.2f));
    }

    public void DrawBtn(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), new Rectangle(x,y,w,h));
        Raylib.DrawRectangle((int)x+4, (int)y+4, (int)w, (int)h, new Color(0,0,0,80));
        Raylib.DrawRectangleRec(new Rectangle(x,y,w,h), hover ? ColBlue : new Color(50,50,60,255));
        Raylib.DrawRectangleLinesEx(new Rectangle(x,y,w,h), 2, hover ? Color.White : Color.Gray);
        int txtSize = 20, txtW = Raylib.MeasureText(text, txtSize);
        Raylib.DrawText(text, (int)(x + w/2 - txtW/2), (int)(y + h/2 - txtSize/2), txtSize, Color.White);
    }

    public void DrawBtnDisabled(float y, string text) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        Raylib.DrawRectangleRec(new Rectangle(x,y,w,h), new Color(30,30,30,255));
        Raylib.DrawRectangleLinesEx(new Rectangle(x,y,w,h), 2, Color.DarkGray);
        int txtSize = 20, txtW = Raylib.MeasureText(text, txtSize);
        Raylib.DrawText(text, (int)(x + w/2 - txtW/2), (int)(y + h/2 - txtSize/2), txtSize, Color.DarkGray);
    }
    
    private void DrawFPS(int fps)
    {
        string fpsText = $"FPS: {fps}";
        int textWidth = Raylib.MeasureText(fpsText, 20);
        int xPos = ScreenWidth - Padding - textWidth;
        int yPos = ScreenHeight - Padding / 2; 

        Raylib.DrawText(fpsText, xPos, yPos, 20, Color.RayWhite);
    }
}
