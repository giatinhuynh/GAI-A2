// This agent was based on the following articles: 
// https://blog.gamesolver.org/solving-connect-four/03-minmax/
// https://blog.gamesolver.org/solving-connect-four/04-alphabeta/
// https://blog.gamesolver.org/solving-connect-four/08-iterative-deepening/


using UnityEngine;
using System.Collections.Generic;
using ConnectFour;
using System;

// Represents an AI agent using the Minimax algorithm with several optimizations.
public class MinimaxAgent : Agent
{
    // Maximum search depth for the Minimax algorithm. Deeper searches are more thorough but slower.
    public int maxDepth = 12; // Practical search depth limit
    // Maximum time allowed for the agent to decide on a move.
    public float timeLimit = 1.0f; // Time limit in seconds for the search

    // Transposition table: Caches evaluations of previously encountered board states (positions).
    // Uses Zobrist hashing for keys. Improves performance by avoiding redundant computations.
    private Dictionary<long, TranspositionEntry> transpositionTable = new Dictionary<long, TranspositionEntry>();

    // Stores an evaluated position's value and the depth it was evaluated at in the transposition table.
    private class TranspositionEntry
    {
        public float value; // The evaluated score of the position
        public int depth;   // The remaining search depth when this entry was stored

        public TranspositionEntry(float value, int depth)
        {
            this.value = value;
            this.depth = depth;
        }
    }

    // --- Zobrist Hashing ---
    // Unique random numbers for each possible piece at each position on the board.
    private long[,,] zobristTable; // [column, row, pieceType]
    // Unique random number to XOR when it's the second player's (Red's) turn.
    private long zobristPlayerTurn;

    // Initializes the Zobrist hashing keys with pseudo-random numbers.
    private void InitZobristHashing()
    {
        // Use a fixed seed for the random number generator to ensure deterministic hash key generation.
        System.Random rand = new System.Random(42); // Fixed seed for deterministic behavior

        // Initialize the Zobrist table for piece positions: columns x rows x 2 player pieces.
        zobristTable = new long[GameController.numColumns, GameController.numRows, 2]; // 2 piece types (Yellow=0, Red=1)

        // Assign a unique random 64-bit number to each possible piece placement.
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows; y++)
            {
                // One random number for Yellow (index 0), one for Red (index 1).
                for (int p = 0; p < 2; p++)
                {
                    zobristTable[x, y, p] = NextRandom(rand);
                }
            }
        }

        // Assign a unique random number associated with the player turn (used to differentiate identical boards with different current players).
        zobristPlayerTurn = NextRandom(rand);
    }

    // Helper function to generate a random 64-bit integer (long).
    private long NextRandom(System.Random rand)
    {
        byte[] bytes = new byte[8]; // 64 bits = 8 bytes
        rand.NextBytes(bytes);      // Fill the byte array with random values
        return BitConverter.ToInt64(bytes, 0); // Convert bytes to a long
    }

    // Computes the Zobrist hash for the given Connect4 game state.
    // The hash is calculated by XORing the Zobrist keys corresponding to the pieces on the board and the current player's turn.
    private long ComputeZobristHash(Connect4State state)
    {
        long hash = 0; // Start with an empty hash
        byte[,] board = state.GetBoard(); // Get the current board configuration

        // Iterate through each cell on the board.
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows; y++)
            {
                // If a Yellow piece is present, XOR its corresponding Zobrist key into the hash.
                if (board[x, y] == (byte)Connect4State.Piece.Yellow)
                {
                    hash ^= zobristTable[x, y, 0]; // XOR with Yellow piece key at [x, y]
                }
                // If a Red piece is present, XOR its corresponding Zobrist key into the hash.
                else if (board[x, y] == (byte)Connect4State.Piece.Red)
                {
                    hash ^= zobristTable[x, y, 1]; // XOR with Red piece key at [x, y]
                }
                // Empty cells don't affect the hash.
            }
        }

        // XOR the turn-specific key if it's Red's (player 1) turn.
        // This ensures states with the same pieces but different active players have different hashes.
        if (state.GetPlayerTurn() == 1) // Red's turn
        {
            hash ^= zobristPlayerTurn;
        }

        return hash; // Return the computed hash for the state
    }

    // Called by the game controller to request the agent's next move.
    public override int GetMove(Connect4State state)
    {
        // Lazily initialize Zobrist hashing tables the first time GetMove is called.
        if (zobristTable == null)
        {
            InitZobristHashing();
        }

        // Clear the transposition table at the start of each move calculation.
        // Entries are specific to the search starting from the current root state.
        transpositionTable.Clear();

        // Retrieve the list of valid moves (columns where a piece can be dropped).
        List<int> possibleMoves = state.GetPossibleMoves();

        // If there's only one possible move, no need for search, return it immediately.
        if (possibleMoves.Count == 1)
            return possibleMoves[0];

        // Check for immediate wins or necessary blocks before starting the deeper search.
        int immediateMove = CheckForImmediateMoves(state);
        if (immediateMove != -1) // If a winning or blocking move is found
            return immediateMove; // Return it immediately

        // --- Iterative Deepening Depth-First Search (IDDFS) ---
        // Start with a default move (center column preferred). This is a fallback if time runs out early.
        int bestMove = possibleMoves[possibleMoves.Count / 2];
        float startTime = Time.realtimeSinceStartup; // Record the start time for time limiting

        // Gradually increase the search depth (iterative deepening).
        for (int currentDepth = 1; currentDepth <= maxDepth; currentDepth++)
        {
            int iterationBestMove = -1; // Best move found in *this* specific depth iteration
            float bestValue;            // Best evaluation score found so far

            // Initialize the best score based on whether this agent is maximizing (Red) or minimizing (Yellow).
            // Scores are evaluated from Red's perspective (positive = good for Red, negative = good for Yellow).
            if (playerIdx == 1) // We are Red (player 1) - maximizing player
                bestValue = float.MinValue; // Initialize to lowest possible value
            else // We are Yellow (player 0) - minimizing player
                bestValue = float.MaxValue; // Initialize to highest possible value

            // Initialize alpha and beta for alpha-beta pruning at the root.
            float alpha = float.MinValue; // Lower bound for maximizing player
            float beta = float.MaxValue;  // Upper bound for minimizing player

            // --- Move Ordering ---
            // For depths > 1, order moves based on results from the previous, shallower search.
            // This significantly improves alpha-beta pruning effectiveness.
            if (currentDepth > 1)
            {
                OrderMoves(ref possibleMoves, state); // Reorder based on heuristics or previous results
            }
            else // For the first iteration (depth 1)
            {
                // Initial simple ordering: Prioritize center columns, then spread outwards.
                possibleMoves.Sort((a, b) => {
                    int aDist = Mathf.Abs(a - 3); // Distance from center (column 3) for move a
                    int bDist = Mathf.Abs(b - 3); // Distance from center for move b
                    if (aDist != bDist) return aDist - bDist; // Closer to center first
                    return a - b; // If same distance, prefer left columns (arbitrary tie-breaker)
                });
            }

            // Explore each possible move from the current state.
            foreach (int move in possibleMoves)
            {
                // Simulate making the move by creating a copy of the state.
                Connect4State newState = state.Clone();
                newState.MakeMove(move);

                // Check if this move immediately ends the game (win/loss/draw).
                Connect4State.Result result = newState.GetResult();
                if (result != Connect4State.Result.Undecided)
                {
                    float evalValue = EvaluateTerminal(result); // Get score for terminal state
                    // Check if this move is an immediate win for the current player.
                    if ((playerIdx == 1 && evalValue > 0) || // Red wins
                        (playerIdx == 0 && evalValue < 0))   // Yellow wins
                    {
                        // If we found an immediate winning move, take it without further search.
                        return move;
                    }
                    // Note: If it's a loss or draw, we still need to evaluate it via Minimax below.
                }

                // Determine if the *next* player in the simulated state (newState) is maximizing (Red) or minimizing (Yellow).
                bool isMaximizingNext = newState.GetPlayerTurn() == 1; // True if it will be Red's turn

                // Call the Minimax function to evaluate the position resulting from this move.
                // The depth is reduced by 1 because we've made one move.
                float value = Minimax(newState, currentDepth - 1, alpha, beta, isMaximizingNext, startTime);

                // Check if the time limit has been exceeded during the Minimax call.
                if (Time.realtimeSinceStartup - startTime > timeLimit)
                {
                    // Time's up! Return the best move found in the *previous* completed iteration.
                    // `bestMove` holds the result from the last fully completed depth search.
                    return bestMove;
                }

                // --- Update Best Move (Root Level) ---
                // If the current move's evaluation is better than the best found so far *for this agent*:
                if ((playerIdx == 1 && value > bestValue) || // Red (maximizing) found a higher score
                    (playerIdx == 0 && value < bestValue))   // Yellow (minimizing) found a lower score
                {
                    bestValue = value;          // Update the best score
                    iterationBestMove = move;   // Update the best move for *this iteration*

                    // Update alpha or beta for the root node based on which player we are.
                    if (playerIdx == 1) // Red - maximizing player updates alpha (lower bound)
                        alpha = Mathf.Max(alpha, bestValue);
                    else // Yellow - minimizing player updates beta (upper bound)
                        beta = Mathf.Min(beta, bestValue);
                }
                // Note: Alpha-beta pruning at the root level isn't strictly necessary as we explore all root moves,
                // but updating alpha/beta here can potentially prune branches in subsequent move explorations within this loop, though less likely.
            } // End foreach move

            // After checking all moves at the current depth, if a valid move was found, update the overall best move.
            if (iterationBestMove != -1)
            {
                bestMove = iterationBestMove; // Store the best move from the completed iteration
            }

            // Early exit check: If nearing the time limit, stop iterative deepening and use the current best move.
            // Using 80% allows some buffer before hitting the hard limit.
            if (Time.realtimeSinceStartup - startTime > timeLimit * 0.8f)
            {
                break; // Stop iterative deepening
            }

            // Optimization: If a forced win or loss is found (indicated by extreme evaluation scores),
            // searching deeper won't change the outcome, so we can stop early.
            if (bestValue > 900 || bestValue < -900) // Scores near +/- 1000 typically represent win/loss
            {
                break; // Found a certain win/loss path
            }
        } // End iterative deepening loop

        // Return the best move found within the time limit and depth constraints.
        return bestMove;
    }

    // Quickly checks if the current player has an immediate winning move,
    // or if the opponent has an immediate winning move that needs to be blocked.
    // Returns the column index of the move if found, otherwise -1.
    private int CheckForImmediateMoves(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves(); // Get valid moves

        // 1. Check for a winning move for the current agent (us).
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone(); // Simulate the move
            newState.MakeMove(move);

            Connect4State.Result result = newState.GetResult(); // Check game outcome

            // If making this move results in a win for us:
            if ((playerIdx == 1 && result == Connect4State.Result.RedWin) ||    // Red player wins
                (playerIdx == 0 && result == Connect4State.Result.YellowWin)) // Yellow player wins
            {
                return move; // Found a winning move, return it immediately.
            }
        }

        // 2. Check if the opponent has a winning move in the next turn, requiring us to block.
        int opponentIdx = 1 - playerIdx; // Determine opponent's index (0 -> 1, 1 -> 0)

        foreach (int move in possibleMoves) // Consider each column we could potentially block
        {
            // Simulate the opponent making a move in this column *on their turn*.
            Connect4State testState = state.Clone();

            // Temporarily switch player turn to opponent
            while (testState.GetPlayerTurn() != opponentIdx)
            {
                testState = testState.Clone();
                // Making and undoing a move to switch player turn
                int anyMove = testState.GetPossibleMoves()[0];
                testState.MakeMove(anyMove);
            }
            
            // Try opponent move in this column
            testState.MakeMove(move);
            Connect4State.Result result = testState.GetResult();
            
            // Check if opponent would win with this move
            if ((opponentIdx == 1 && result == Connect4State.Result.RedWin) ||
                (opponentIdx == 0 && result == Connect4State.Result.YellowWin))
            {
                return move; // Found a necessary blocking move, return it.
            }
        }
        
        // No immediate winning or blocking moves were found.
        return -1;
    }

    // Reorders the list of possible moves (`moves`) in place to prioritize more promising moves.
    // Uses heuristics and potentially information from the transposition table (though not explicitly shown here).
    // Good move ordering drastically improves alpha-beta pruning efficiency.
    private void OrderMoves(ref List<int> moves, Connect4State state)
    {
        // Use a dictionary to store a heuristic score for each move.
        Dictionary<int, float> moveScores = new Dictionary<int, float>();

        foreach (int move in moves)
        {
            // Simulate the move.
            Connect4State newState = state.Clone();
            newState.MakeMove(move);

            // Check if the move results in an immediate game end.
            Connect4State.Result result = newState.GetResult();
            if (result != Connect4State.Result.Undecided)
            {
                // Assign very high scores to winning moves for the current player.
                if ((playerIdx == 1 && result == Connect4State.Result.RedWin) ||
                    (playerIdx == 0 && result == Connect4State.Result.YellowWin))
                {
                    moveScores[move] = 10000; // High positive score for a win
                    continue; // No need for further evaluation of this move
                }

                // Assign very low scores to moves that result in an immediate loss.
                if ((playerIdx == 1 && result == Connect4State.Result.YellowWin) ||
                    (playerIdx == 0 && result == Connect4State.Result.RedWin))
                {
                    moveScores[move] = -10000; // High negative score for a loss
                    continue; // No need for further evaluation
                }

                // Assign a neutral score for draws.
                moveScores[move] = 0;
                continue;
            }

            // Heuristic 1: Positional Score - Prioritize center columns as they offer more winning opportunities.
            // Score decreases linearly from the center (column 3). Max score 10 (center), min score 0 (edges).
            float positionScore = 10 - Mathf.Abs(move - 3) * 2;

            // Heuristic 2: Quick Evaluation - Use a lightweight evaluation function (not shown in snippet)
            // to get a rough estimate of the board state's quality after the move.
            // Assumes QuickEvaluate exists and returns higher values for Red's advantage, lower for Yellow's.
            float evalScore = QuickEvaluate(newState); // Placeholder for a fast evaluation function

            // Combine scores: Give the quick evaluation more weight than the simple position score.
            moveScores[move] = evalScore + positionScore * 0.1f; // Scale down position score influence
        }

        // Sort the moves list based on the calculated scores.
        moves.Sort((a, b) => {
            // Retrieve scores, defaulting to 0 if a move wasn't scored (shouldn't happen here).
            float scoreA = moveScores.ContainsKey(a) ? moveScores[a] : 0;
            float scoreB = moveScores.ContainsKey(b) ? moveScores[b] : 0;

            // Sort in descending order for Red, ascending for Yellow
            if (playerIdx == 1) // Red wants max (descending)
                return scoreB.CompareTo(scoreA);
            else // Yellow wants min (ascending)
                return scoreA.CompareTo(scoreB);
        });
    }
    
    // Minimax algorithm with alpha-beta pruning
    private float Minimax(Connect4State state, int depth, float alpha, float beta, bool isMaximizingPlayer, float startTime)
    {
        // Check if we've exceeded the time limit
        if (Time.realtimeSinceStartup - startTime > timeLimit)
        {
            // Return a neutral value if we ran out of time
            return 0;
        }
        
        // Check terminal conditions
        Connect4State.Result result = state.GetResult();
        if (result != Connect4State.Result.Undecided)
        {
            return EvaluateTerminal(result);
        }
        
        // If we've reached maximum depth, return a heuristic evaluation
        if (depth == 0)
        {
            return EvaluatePosition(state);
        }
        
        // Generate a hash key for the position
        long hashKey = ComputeZobristHash(state);
        
        // Check if we've already evaluated this position at same or greater depth
        if (transpositionTable.ContainsKey(hashKey) && transpositionTable[hashKey].depth >= depth)
        {
            return transpositionTable[hashKey].value;
        }
        
        List<int> possibleMoves = state.GetPossibleMoves();
        
        // Optimize move ordering - check center columns first
        possibleMoves.Sort((a, b) => {
            int aDist = Mathf.Abs(a - 3);
            int bDist = Mathf.Abs(b - 3);
            if (aDist != bDist) return aDist - bDist;
            return a - b;
        });
        
        float bestValue;
        
        if (isMaximizingPlayer) // Red player (maximizing)
        {
            bestValue = float.MinValue;
            
            foreach (int move in possibleMoves)
            {
                Connect4State newState = state.Clone();
                newState.MakeMove(move);
                
                // Check for immediate win
                result = newState.GetResult();
                if (result != Connect4State.Result.Undecided)
                {
                    float evalValue = EvaluateTerminal(result);
                    transpositionTable[hashKey] = new TranspositionEntry(evalValue, depth);
                    return evalValue;
                }
                
                float eval = Minimax(newState, depth - 1, alpha, beta, !isMaximizingPlayer, startTime);
                bestValue = Mathf.Max(bestValue, eval);
                
                // Alpha-beta pruning
                alpha = Mathf.Max(alpha, eval);
                if (beta <= alpha)
                    break; // Beta cutoff
                
                // Check timeout
                if (Time.realtimeSinceStartup - startTime > timeLimit)
                    break;
            }
        }
        else // Yellow player (minimizing)
        {
            bestValue = float.MaxValue;
            
            foreach (int move in possibleMoves)
            {
                Connect4State newState = state.Clone();
                newState.MakeMove(move);
                
                // Check for immediate win
                result = newState.GetResult();
                if (result != Connect4State.Result.Undecided)
                {
                    float evalValue = EvaluateTerminal(result);
                    transpositionTable[hashKey] = new TranspositionEntry(evalValue, depth);
                    return evalValue;
                }
                
                float eval = Minimax(newState, depth - 1, alpha, beta, !isMaximizingPlayer, startTime);
                bestValue = Mathf.Min(bestValue, eval);
                
                // Alpha-beta pruning
                beta = Mathf.Min(beta, eval);
                if (beta <= alpha)
                    break; // Alpha cutoff
                
                // Check timeout
                if (Time.realtimeSinceStartup - startTime > timeLimit)
                    break;
            }
        }
        
        // Store the evaluation in our transposition table
        transpositionTable[hashKey] = new TranspositionEntry(bestValue, depth);
        return bestValue;
    }
    
    // Only evaluate terminal positions
    private float EvaluateTerminal(Connect4State.Result result)
    {
        if (result == Connect4State.Result.RedWin)
            return 1000;
        else if (result == Connect4State.Result.YellowWin)
            return -1000;
        else // Draw
            return 0;
    }
    
    // Quick evaluation for move ordering
    private float QuickEvaluate(Connect4State state)
    {
        // Simple evaluation that just counts pieces with potential to connect
        byte[,] board = state.GetBoard();
        float score = 0;
        
        // Check horizontal potentials
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                score += EvaluateWindow(board, x, y, 1, 0);
            }
        }
        
        // Check vertical potentials
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                score += EvaluateWindow(board, x, y, 0, 1);
            }
        }
        
        // Check diagonal potentials (rising)
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = GameController.numRows - 4; y < GameController.numRows; y++)
            {
                score += EvaluateWindow(board, x, y, 1, -1);
            }
        }
        
        // Check diagonal potentials (falling)
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                score += EvaluateWindow(board, x, y, 1, 1);
            }
        }
        
        return score;
    }
    
    // For non-terminal positions, use a heuristic evaluation
    private float EvaluatePosition(Connect4State state)
    {
        byte[,] board = state.GetBoard();
        float score = 0;
        
        // Horizontal, vertical, and diagonal checks
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x < GameController.numColumns; x++)
            {
                // Check horizontal window
                if (x <= GameController.numColumns - 4)
                {
                    score += EvaluateWindow(board, x, y, 1, 0);
                }
                
                // Check vertical window
                if (y <= GameController.numRows - 4)
                {
                    score += EvaluateWindow(board, x, y, 0, 1);
                }
                
                // Check diagonal falling window
                if (x <= GameController.numColumns - 4 && y <= GameController.numRows - 4)
                {
                    score += EvaluateWindow(board, x, y, 1, 1);
                }
                
                // Check diagonal rising window
                if (x <= GameController.numColumns - 4 && y >= 3)
                {
                    score += EvaluateWindow(board, x, y, 1, -1);
                }
            }
        }
        
        // Add center control preference
        for (int y = 0; y < GameController.numRows; y++)
        {
            if (board[3, y] == (byte)Connect4State.Piece.Red)
            {
                score += 3; // Bonus for red pieces in center column
            }
            else if (board[3, y] == (byte)Connect4State.Piece.Yellow)
            {
                score -= 3; // Penalty for yellow pieces in center column
            }
        }
        
        return score;
    }
    
    // Evaluate a 4-piece window for potential
    private float EvaluateWindow(byte[,] board, int startX, int startY, int dX, int dY)
    {
        int redCount = 0;
        int yellowCount = 0;
        int emptyCount = 0;
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
            // Check if position is within bounds
            if (x < 0 || x >= GameController.numColumns || y < 0 || y >= GameController.numRows)
            {
                return 0; // Invalid window, return no score
            }
            
            if (board[x, y] == (byte)Connect4State.Piece.Red)
            {
                redCount++;
            }
            else if (board[x, y] == (byte)Connect4State.Piece.Yellow)
            {
                yellowCount++;
            }
            else
            {
                emptyCount++;
            }
        }
        
        // If both red and yellow pieces exist in this window, it's not useful for either player
        if (redCount > 0 && yellowCount > 0)
        {
            return 0;
        }
        
        // Score based on how many pieces of the same color are in the window
        if (redCount > 0)
        {
            switch(redCount)
            {
                case 1: return 1;
                case 2: return 10;
                case 3: return 50;
                case 4: return 1000; // Should be caught earlier as a win
            }
        }
        else if (yellowCount > 0)
        {
            switch(yellowCount)
            {
                case 1: return -1;
                case 2: return -10;
                case 3: return -50;
                case 4: return -1000; // Should be caught earlier as a win
            }
        }
        
        return 0;
    }
}