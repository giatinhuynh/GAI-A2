using UnityEngine;
using System.Collections.Generic;

public class MonteCarloAgent : Agent
{
    public int totalSims = 2500;

    public override int GetMove(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        int moveCount = possibleMoves.Count;

        // If there's only one possible move, take it
        if (moveCount == 1)
            return possibleMoves[0];

        // Array to store the score for each move
        float[] moveScores = new float[moveCount];

        // Number of simulations per move
        int simsPerMove = totalSims / moveCount;

        // For each possible move
        for (int i = 0; i < moveCount; i++)
        {
            int column = possibleMoves[i];
            float score = 0;

            // Run simulations for this move
            for (int sim = 0; sim < simsPerMove; sim++)
            {
                // Create a copy of the current state
                Connect4State simState = state.Clone();

                // Make the move
                simState.MakeMove(column);

                // Run a random simulation until the game ends
                float simResult = SimulateRandomGame(simState);

                // Convert the result to our player's perspective
                // For Yellow (0): a Yellow win (0.0) is good, want to minimize result
                // For Red (1): a Red win (1.0) is good, want to maximize result
                if (playerIdx == 0) // Yellow wants to minimize (closer to 0)
                    score += (1.0f - simResult); // Invert so higher is better for yellow
                else // Red wants to maximize (closer to 1)
                    score += simResult;
            }

            // Average score for this move (higher is better for both players now)
            moveScores[i] = score / simsPerMove;
        }

        // Return the move with the highest score
        return possibleMoves[argMax(moveScores)];
    }

    // Runs a random simulation from the given state until the game ends
    // Returns the result as a float (0 for yellow win, 1 for red win, 0.5 for draw)
    private float SimulateRandomGame(Connect4State state)
    {
        // Continue until the game reaches a terminal state
        while (true)
        {
            // Check if the game is over
            Connect4State.Result result = state.GetResult();
            if (result != Connect4State.Result.Undecided)
            {
                // Convert the result to a float (0 for yellow win, 1 for red win, 0.5 for draw)
                return Connect4State.ResultToFloat(result);
            }

            // Get all possible moves
            List<int> moves = state.GetPossibleMoves();

            // If no more moves are possible, it's a draw
            if (moves.Count == 0)
                return 0.5f;

            // Choose a random move
            int randomMove = moves[Random.Range(0, moves.Count)];

            // Make the move
            state.MakeMove(randomMove);
        }
    }
}