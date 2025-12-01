using System.Collections.Generic;

namespace GreatKingdom;

public enum Player { None = 0, Blue = 1, Orange = 2, Neutral = 3 }

// A lightweight, copyable snapshot of the game
public struct GameState
{
    public byte[] Board; // Flattened 9x9 array (0..80) for speed
    public Player CurrentTurn;
    public int ConsecutivePasses;
    public Player Winner; // Player.None if playing
    
    // Constants
    public const int Size = 9;
    
    public GameState()
    {
        Board = new byte[Size * Size];
        CurrentTurn = Player.Blue;
        ConsecutivePasses = 0;
        Winner = Player.None;
        
        // Place Neutral in Center (4,4) -> Index 40
        Board[40] = (byte)Player.Neutral;
    }

    // Helper to get array index
    public int Idx(int x, int y) => y * Size + x;

    public GameState DeepCopy()
    {
        var clone = new GameState();
        clone.Board = (byte[])this.Board.Clone();
        clone.CurrentTurn = this.CurrentTurn;
        clone.ConsecutivePasses = this.ConsecutivePasses;
        clone.Winner = this.Winner;
        return clone;
    }

    public List<int> GetLegalMoves()
    {
        var moves = new List<int>();
        for (int i = 0; i < Board.Length; i++)
        {
            if (Board[i] == (byte)Player.None) moves.Add(i);
        }
        return moves; // Note: We allow all empty spots, capture rules filter bad outcomes
    }

    public void ApplyMove(int index)
    {
        // 1. Handle Pass (Index -1)
        if (index == -1)
        {
            ConsecutivePasses++;
            if (ConsecutivePasses >= 2) CalculateScoreWinner();
            CurrentTurn = (CurrentTurn == Player.Blue) ? Player.Orange : Player.Blue;
            return;
        }

        ConsecutivePasses = 0;
        byte me = (byte)CurrentTurn;
        byte opp = (CurrentTurn == Player.Blue) ? (byte)Player.Orange : (byte)Player.Blue;

        Board[index] = me;

        // 2. Check Sudden Death Capture (Enemy)
        if (CheckCapture(opp))
        {
            Winner = CurrentTurn;
            return;
        }

        // 3. Check Suicide (Self)
        if (CheckCapture(me))
        {
            Winner = (CurrentTurn == Player.Blue) ? Player.Orange : Player.Blue;
            return;
        }

        // 4. Next Turn
        CurrentTurn = (CurrentTurn == Player.Blue) ? Player.Orange : Player.Blue;
    }

    private bool CheckCapture(byte targetColor)
    {
        bool[] visited = new bool[81];
        for (int i = 0; i < 81; i++)
        {
            if (Board[i] == targetColor && !visited[i])
            {
                if (!HasLiberties(i, targetColor, visited)) return true;
            }
        }
        return false;
    }

    private bool HasLiberties(int startIdx, byte color, bool[] globalVisited)
    {
        Stack<int> stack = new Stack<int>();
        stack.Push(startIdx);
        HashSet<int> group = new HashSet<int>();
        bool foundLiberty = false;

        while (stack.Count > 0)
        {
            int curr = stack.Pop();
            if (group.Contains(curr)) continue;
            group.Add(curr);
            globalVisited[curr] = true;

            int cx = curr % Size;
            int cy = curr / Size;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k];
                int ny = cy + dy[k];

                if (nx < 0 || nx >= Size || ny < 0 || ny >= Size) continue;
                
                int nIdx = ny * Size + nx;
                byte neighbor = Board[nIdx];

                if (neighbor == (byte)Player.None) foundLiberty = true;
                else if (neighbor == color && !group.Contains(nIdx)) stack.Push(nIdx);
            }
        }
        return foundLiberty;
    }

    private void CalculateScoreWinner()
    {
        // Simple flood fill scoring (Simplified for MCTS speed)
        // In MCTS random playout, we just want a result.
        // For accurate AI, this needs the full logic, but for now:
        int blue = 0, orange = 0;
        
        // Count stones on board as heuristic proxy for territory in fast simulation
        // (Real MCTS should use the full territory code, but it's slow)
        for(int i=0; i<81; i++) {
            if(Board[i] == (byte)Player.Blue) blue++;
            if(Board[i] == (byte)Player.Orange) orange++;
        }

        // Handicap +3
        if (blue >= orange + 3) Winner = Player.Blue;
        else Winner = Player.Orange;
    }
}
