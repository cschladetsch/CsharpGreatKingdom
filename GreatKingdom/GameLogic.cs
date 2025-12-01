using System.Collections.Generic;

namespace GreatKingdom;

public enum Player { None = 0, Blue = 1, Orange = 2, Neutral = 3 }

public struct GameState
{
    public byte[] Board; 
    public Player CurrentTurn;
    public int ConsecutivePasses;
    public Player Winner; 
    public const int Size = 9;
    
    public GameState()
    {
        Board = new byte[Size * Size];
        CurrentTurn = Player.Blue;
        ConsecutivePasses = 0;
        Winner = Player.None;
        Board[40] = (byte)Player.Neutral; // Center (Index 40)
    }

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
            if (Board[i] == (byte)Player.None) moves.Add(i);
        return moves;
    }

    public void ApplyMove(int index)
    {
        if (index == -1) // Pass
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

        if (CheckCapture(opp)) { Winner = CurrentTurn; return; }
        if (CheckCapture(me)) { Winner = (CurrentTurn == Player.Blue) ? Player.Orange : Player.Blue; return; }

        CurrentTurn = (CurrentTurn == Player.Blue) ? Player.Orange : Player.Blue;
    }

    private bool CheckCapture(byte targetColor)
    {
        bool[] visited = new bool[81];
        for (int i = 0; i < 81; i++)
            if (Board[i] == targetColor && !visited[i])
                if (!HasLiberties(i, targetColor, visited)) return true;
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
        int blue = 0, orange = 0;
        for(int i=0; i<81; i++) {
            if(Board[i] == (byte)Player.Blue) blue++;
            if(Board[i] == (byte)Player.Orange) orange++;
        }
        if (blue >= orange + 3) Winner = Player.Blue;
        else Winner = Player.Orange;
    }
}
