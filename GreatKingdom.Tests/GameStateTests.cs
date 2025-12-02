using Microsoft.VisualStudio.TestTools.UnitTesting;
using GreatKingdom;
using System.Linq;

namespace GreatKingdom.Tests
{
    [TestClass]
    public class GameStateTests
    {
        // Helper to create a new GameState for each test
        private GameState CreateInitialState()
        {
            return new GameState(); // Constructor initializes a 9x9 board with neutral castle
        }

        [TestMethod]
        public void SimpleMove_PlacesStoneCorrectlyAndChangesTurn()
        {
            var state = CreateInitialState();
            int movePos = 0; // Top-left corner

            state.ApplyMove(movePos);

            Assert.AreEqual((byte)Player.Blue, state.Board[movePos], "Blue stone should be placed.");
            Assert.AreEqual(Player.Orange, state.CurrentTurn, "Turn should switch to Orange.");
        }

        [TestMethod]
        public void CaptureSingleStone_RemovesOpponentStone()
        {
            var state = CreateInitialState();
            
            // Setup: Blue places a stone, Orange places a stone, Blue captures Orange
            state.ApplyMove(0); // Blue
            state.ApplyMove(1); // Orange at (1,0)
            state.ApplyMove(9); // Blue at (0,1)

            // Now, Orange at (1,0) is surrounded by Blue at (0,0) and (0,1), and border
            // Current state before capture: B O . / B . . .
            // Blue moves to (2,0) to capture Orange at (1,0)
            state.ApplyMove(2); // Blue at (2,0) captures Orange at (1,0)

            Assert.AreEqual((byte)Player.None, state.Board[1], "Orange stone at (1,0) should be captured.");
            Assert.AreEqual((byte)Player.Blue, state.Board[0], "Blue stone at (0,0) should remain.");
            Assert.AreEqual((byte)Player.Blue, state.Board[9], "Blue stone at (0,1) should remain.");
            Assert.AreEqual((byte)Player.Blue, state.Board[2], "Blue stone at (2,0) should be placed.");
            Assert.AreEqual(Player.Orange, state.CurrentTurn, "Turn should switch to Orange.");
        }

        [TestMethod]
        public void CaptureGroup_RemovesMultipleOpponentStones()
        {
            var state = CreateInitialState();

            // Board setup to capture an Orange group of 2 stones
            // O O .     B . .
            // B . .  -> B . .
            // B . .     B . .

            state.ApplyMove(18); // Blue (0,2)
            state.ApplyMove(1);  // Orange (1,0)
            state.ApplyMove(27); // Blue (0,3)
            state.ApplyMove(10); // Orange (1,1)
            state.ApplyMove(0);  // Blue (0,0)

            // Blue now captures the Orange group (1,0) and (1,1) by placing at (2,0)
            state.ApplyMove(2); // Blue (2,0) - captures Orange group

            Assert.AreEqual((byte)Player.None, state.Board[1], "Orange stone at (1,0) should be captured.");
            Assert.AreEqual((byte)Player.None, state.Board[10], "Orange stone at (1,1) should be captured.");
            Assert.AreEqual((byte)Player.Blue, state.Board[0], "Blue stone at (0,0) should remain.");
            Assert.AreEqual((byte)Player.Blue, state.Board[18], "Blue stone at (0,2) should remain.");
            Assert.AreEqual((byte)Player.Blue, state.Board[27], "Blue stone at (0,3) should remain.");
            Assert.AreEqual((byte)Player.Blue, state.Board[2], "Blue stone at (2,0) should be placed.");
            Assert.AreEqual(Player.Orange, state.CurrentTurn, "Turn should switch to Orange.");
        }

        [TestMethod]
        public void MoveLeadingToWinByCapture_IdentifiesWinner()
        {
            var state = CreateInitialState();

            // Setup: Blue sets up a capture, Orange is about to be captured
            state.ApplyMove(0); // Blue (0,0)
            state.ApplyMove(1); // Orange (1,0)
            state.ApplyMove(9); // Blue (0,1)
            state.ApplyMove(19); // Orange (1,2)
            state.ApplyMove(18); // Blue (0,2)

            // Blue makes a capturing move at (2,1) that captures Orange at (1,1) and (1,2) effectively winning
            state.ApplyMove(11); // Blue (2,1)

            Assert.AreEqual(Player.Blue, state.Winner, "Blue should be declared winner after capturing a stone.");
            Assert.AreEqual((byte)Player.None, state.Board[1], "Orange stone at (1,0) should be captured.");
            Assert.AreEqual((byte)Player.None, state.Board[10], "Orange stone at (1,1) should be captured.");
        }

        [TestMethod]
        public void TwoConsecutivePasses_EndsGame()
        {
            var state = CreateInitialState();

            state.ApplyMove(0); // Blue makes a move
            Assert.AreEqual(0, state.ConsecutivePasses, "Consecutive passes should be 0.");

            state.ApplyMove(-1); // Blue passes
            Assert.AreEqual(1, state.ConsecutivePasses, "Consecutive passes should be 1 after one pass.");
            Assert.AreEqual(Player.Orange, state.CurrentTurn, "Turn should switch to Orange after pass.");

            state.ApplyMove(-1); // Orange passes
            Assert.AreEqual(2, state.ConsecutivePasses, "Consecutive passes should be 2 after two passes.");
            Assert.AreEqual(Player.None, state.Winner, "Game should end (Winner is None for territory scoring).");
        }

        [TestMethod]
        public void SuicideMove_IsInvalid()
        {
            var state = CreateInitialState();

            // Setup a situation where Blue would commit suicide without capture
            state.ApplyMove(1);  // Blue (1,0)
            state.ApplyMove(10); // Orange (1,1)
            state.ApplyMove(2);  // Blue (2,0)
            state.ApplyMove(11); // Orange (2,1)
            state.ApplyMove(9);  // Blue (0,1)

            // Orange now places a stone at (0,0) (empty) which would surround Blue at (1,0) if valid.
            // This move should be disallowed as a suicide move.
            state.ApplyMove(0); // Orange tries to place at (0,0)

            // Assert that the board position is still empty, and the turn did not change back to Blue
            Assert.AreEqual((byte)Player.None, state.Board[0], "Orange should not be able to commit suicide, position (0,0) should remain empty.");
            Assert.AreEqual(Player.Blue, state.CurrentTurn, "Turn should remain Blue if suicide move was blocked.");
        }
    }
}
