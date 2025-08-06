using UnityEngine;
using System.Collections.Generic;
using ConnectFour;

public class RuleBasedAgent : Agent
{
    public override int GetMove(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        
        // If only one move possible, take it
        if (possibleMoves.Count == 1)
            return possibleMoves[0];

        // 1. Check for immediate winning moves
        int winningMove = FindWinningMove(state, playerIdx);
        if (winningMove != -1)
            return winningMove;

        // 2. Check for opponent's winning moves to block
        int blockingMove = FindWinningMove(state, 1 - playerIdx);
        if (blockingMove != -1)
            return blockingMove;

        // 3. Check for fork creation
        int forkMove = FindForkMove(state, playerIdx);
        if (forkMove != -1)
            return forkMove;

        // 4. Check for opponent's fork attempts to block
        int blockForkMove = FindForkMove(state, 1 - playerIdx);
        if (blockForkMove != -1)
            return blockForkMove;

        // 5 & 6. Create or block sequences
        int sequenceMove = FindBestSequenceMove(state);
        if (sequenceMove != -1)
            return sequenceMove;

        // 7. Prefer center column if available
        if (possibleMoves.Contains(3))
            return 3;

        // 8. Choose based on column preference (center outward)
        int[] columnPreference = { 3, 2, 4, 1, 5, 0, 6 };
        foreach (int col in columnPreference)
        {
            if (possibleMoves.Contains(col))
                return col;
        }

        // Fallback to first available move
        return possibleMoves[0];
    }

    // Find immediate winning move for specified player
    private int FindWinningMove(Connect4State state, int player)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            // Switch turn if needed
            while (newState.GetPlayerTurn() != player)
            {
                int tempMove = newState.GetPossibleMoves()[0];
                newState.MakeMove(tempMove);
            }
            newState.MakeMove(move);
            
            Connect4State.Result result = newState.GetResult();
            if ((player == 1 && result == Connect4State.Result.RedWin) ||
                (player == 0 && result == Connect4State.Result.YellowWin))
            {
                return move;
            }
        }
        return -1;
    }

    // Find moves that create forks (multiple winning possibilities)
    private int FindForkMove(Connect4State state, int player)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            // Ensure correct player turn
            while (newState.GetPlayerTurn() != player)
            {
                int tempMove = newState.GetPossibleMoves()[0];
                newState.MakeMove(tempMove);
            }
            newState.MakeMove(move);
            
            // Count winning possibilities after this move
            int winningPossibilities = CountWinningPossibilities(newState, player);
            if (winningPossibilities >= 2)
                return move;
        }
        return -1;
    }

    // Count number of winning possibilities in a position
    private int CountWinningPossibilities(Connect4State state, int player)
    {
        int count = 0;
        List<int> moves = state.GetPossibleMoves();
        
        foreach (int move in moves)
        {
            Connect4State testState = state.Clone();
            // Ensure correct player turn
            while (testState.GetPlayerTurn() != player)
            {
                int tempMove = testState.GetPossibleMoves()[0];
                testState.MakeMove(tempMove);
            }
            testState.MakeMove(move);
            
            Connect4State.Result result = testState.GetResult();
            if ((player == 1 && result == Connect4State.Result.RedWin) ||
                (player == 0 && result == Connect4State.Result.YellowWin))
            {
                count++;
            }
        }
        return count;
    }

    // Find best move for creating or blocking sequences
    private int FindBestSequenceMove(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        int bestScore = int.MinValue;
        int bestMove = -1;
        byte[,] board = state.GetBoard();
        
        foreach (int move in possibleMoves)
        {
            int score = EvaluateSequenceMove(board, move, state.GetPlayerTurn());
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }
        
        return bestMove;
    }

    // Evaluate a move's potential for creating/blocking sequences
    private int EvaluateSequenceMove(byte[,] board, int column, int currentPlayer)
    {
        int row = FindNextRow(board, column);
        if (row == -1) return int.MinValue; // Column is full

        // Heavily penalize moves in nearly full columns with no winning potential
        if (!HasWinningSpace(board, column, row))
        {
            return -1000;
        }

        // Start with position-based score (reduced weight)
        int score = GetPositionScore(column);
        
        // Penalize moves in columns that are getting too full
        score -= (GameController.numRows - row) * 2;  // Higher penalty for higher placement
        
        // Temporarily place piece
        board[column, row] = (byte)(currentPlayer == 1 ? Connect4State.Piece.Red : Connect4State.Piece.Yellow);
        
        // Check all directions for sequences
        int[,] directions = { {0,1}, {1,0}, {1,1}, {1,-1} }; // Vertical, horizontal, diagonal
        int maxSequence = 0;  // Track longest sequence for this move
        
        for (int d = 0; d < 4; d++)
        {
            int dx = directions[d,0];
            int dy = directions[d,1];
            
            // Evaluate sequence in both directions
            (int length1, bool blocked1) = EvaluateDirection(board, column, row, dx, dy);
            (int length2, bool blocked2) = EvaluateDirection(board, column, row, -dx, -dy);
            
            // Calculate total sequence length and if it's blocked on both ends
            int totalLength = length1 + length2 + 1; // +1 for the current piece
            bool fullyBlocked = blocked1 && blocked2;
            
            // Track longest sequence
            maxSequence = Mathf.Max(maxSequence, totalLength);
            
            // Add weighted score for this sequence
            score += CalculateSequenceScore(totalLength, !fullyBlocked);
        }
        
        // Consider future potential
        score += EvaluateFuturePotential(board, column, row, currentPlayer);
        
        // Bonus for moves that create long sequences
        if (maxSequence >= GameController.numPiecesToWin - 1)
        {
            score += 200;  // Significant bonus for moves that create threats
        }
        
        // Remove temporary piece
        board[column, row] = 0;
        
        return score;
    }

    // Get base score for column position (favors center control)
    private int GetPositionScore(int column)
    {
        int center = GameController.numColumns / 2;
        int distanceFromCenter = Mathf.Abs(column - center);
        return 5 - distanceFromCenter; // Center = 5, decreases by 1 for each column away
    }

    // Check if there's enough space above for a winning sequence
    private bool HasWinningSpace(byte[,] board, int column, int row)
    {
        int spaceAbove = 0;
        for (int r = row; r < GameController.numRows && board[column, r] == 0; r++)
        {
            spaceAbove++;
        }
        return spaceAbove >= GameController.numPiecesToWin;
    }

    // Calculate score for a sequence based on length and whether it's blocked
    private int CalculateSequenceScore(int length, bool isOpen)
    {
        // Base score increases exponentially with length
        int baseScore = length * length * 10;
        
        // Bonus for open sequences (not blocked)
        if (isOpen) baseScore *= 2;
        
        // Extra bonus for sequences close to winning
        if (length >= GameController.numPiecesToWin - 1) baseScore *= 3;
        
        return baseScore;
    }

    // Evaluate sequence in a direction and return length and blocked status
    private (int length, bool blocked) EvaluateDirection(byte[,] board, int startX, int startY, int dx, int dy)
    {
        int length = 0;
        byte piece = board[startX, startY];
        int x = startX + dx;
        int y = startY + dy;
        
        // Count consecutive pieces
        while (x >= 0 && x < GameController.numColumns &&
               y >= 0 && y < GameController.numRows &&
               board[x,y] == piece)
        {
            length++;
            x += dx;
            y += dy;
        }

        // Check if sequence is blocked
        bool blocked = false;
        if (x < 0 || x >= GameController.numColumns ||
            y < 0 || y >= GameController.numRows ||
            (board[x,y] != 0 && board[x,y] != piece))
        {
            blocked = true;
        }

        return (length, blocked);
    }

    // Evaluate potential for future moves
    private int EvaluateFuturePotential(byte[,] board, int column, int row, int currentPlayer)
    {
        int score = 0;
        
        // Prefer moves that don't immediately enable opponent wins
        if (row < GameController.numRows - 1) // If not top row
        {
            board[column, row + 1] = (byte)(currentPlayer == 1 ? Connect4State.Piece.Yellow : Connect4State.Piece.Red);
            if (WouldWin(board, column, row + 1))
            {
                score -= 100; // Heavily penalize moves that give opponent immediate win
            }
            board[column, row + 1] = 0;
        }
        
        // Bonus for creating multiple threats
        int threats = CountPotentialThreats(board, column, row, currentPlayer);
        score += threats * 50;
        
        return score;
    }

    // Check if a position would result in a win
    private bool WouldWin(byte[,] board, int column, int row)
    {
        byte piece = board[column, row];
        int[,] directions = { {0,1}, {1,0}, {1,1}, {1,-1} };
        
        for (int d = 0; d < 4; d++)
        {
            int dx = directions[d,0];
            int dy = directions[d,1];
            
            (int length1, _) = EvaluateDirection(board, column, row, dx, dy);
            (int length2, _) = EvaluateDirection(board, column, row, -dx, -dy);
            
            if (length1 + length2 + 1 >= GameController.numPiecesToWin)
                return true;
        }
        
        return false;
    }

    // Count number of potential winning threats from a position
    private int CountPotentialThreats(byte[,] board, int column, int row, int currentPlayer)
    {
        int threats = 0;
        int[,] directions = { {0,1}, {1,0}, {1,1}, {1,-1} };
        
        for (int d = 0; d < 4; d++)
        {
            int dx = directions[d,0];
            int dy = directions[d,1];
            
            (int length1, bool blocked1) = EvaluateDirection(board, column, row, dx, dy);
            (int length2, bool blocked2) = EvaluateDirection(board, column, row, -dx, -dy);
            
            // Consider it a threat if the total sequence is long enough and not fully blocked
            if (length1 + length2 + 1 >= GameController.numPiecesToWin - 1 && !(blocked1 && blocked2))
            {
                threats++;
            }
        }
        
        return threats;
    }

    // Find next available row in a column
    private int FindNextRow(byte[,] board, int column)
    {
        for (int row = 0; row < GameController.numRows; row++)
        {
            if (board[column, row] == 0)
                return row;
        }
        return -1;
    }

}