using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GreatKingdom;

public class MCTS
{
    private Random _rng = new Random();

    // The main function the Game calls
    public int GetBestMove(GameState rootState, int iterations = 3000)
    {
        var legalMoves = rootState.GetLegalMoves();
        if (legalMoves.Count == 0) return -1; // Pass

        int[] scores = new int[legalMoves.Count];
        int[] visits = new int[legalMoves.Count];

        Parallel.For(0, iterations, (i) =>
        {
            int moveIdx = _rng.Next(legalMoves.Count);
            int move = legalMoves[moveIdx];

            GameState simState = rootState.DeepCopy();
            simState.ApplyMove(move);

            Player winner = SimulateRandomGame(simState);

            if (winner == rootState.CurrentTurn) 
                System.Threading.Interlocked.Increment(ref scores[moveIdx]);
            System.Threading.Interlocked.Increment(ref visits[moveIdx]);
        });

        int bestMoveIdx = -1;
        double bestRate = -1.0;

        for (int i = 0; i < legalMoves.Count; i++)
        {
            if (visits[i] == 0) continue;
            double rate = (double)scores[i] / visits[i];
            if (rate > bestRate)
            {
                bestRate = rate;
                bestMoveIdx = i;
            }
        }

        return legalMoves[bestMoveIdx];
    }

    private Player SimulateRandomGame(GameState state)
    {
        int moves = 0;
        while (state.Winner == Player.None && moves < 100)
        {
            var legal = state.GetLegalMoves();
            if (legal.Count == 0) 
            {
                state.ApplyMove(-1); // Force Pass
            }
            else
            {
                int r = _rng.Next(legal.Count);
                state.ApplyMove(legal[r]);
            }
            moves++;
        }
        if (state.Winner == Player.None) return Player.Orange; // Draw goes to Orange (Komi)
        
        return state.Winner;
    }
}
