// This agent was based on the following articles: 
// https://disi.unitn.it/~montreso/asd/docs/connect4.pdf
// https://www.semanticscholar.org/paper/A-Knowledge-Based-Approach-of-Connect-Four-Allis/f3e05731f4f4315159f2d3ef7cb5d94f1f9b5c14
// 

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using ConnectFour; // Assuming this namespace provides Connect4State, GameController, etc.

/// <summary>
/// Implements a Connect Four agent based on Victor Allis's expert-level strategy,
/// incorporating threat detection, Zugzwang analysis, and search with transposition tables.
/// </summary>
public class AllisAgent : Agent
{
    // --- Configuration Parameters ---

    /// <summary>
    /// Maximum time allocated for the agent to compute its move in seconds.
    /// </summary>
    public float timeLimit = 1.0f;

    /// <summary>
    /// The deepest level the search algorithm (e.g., minimax variant) will explore.
    /// </summary>
    public int maxDepth = 12;

    // --- State Variables ---

    /// <summary>
    /// Tracks if the current player has control over the Zugzwang state (forcing the opponent into unfavorable moves).
    /// </summary>
    private bool playerControlsZugzwang = false;

    /// <summary>
    /// Stores columns where the current player has odd threats, relevant for Zugzwang analysis.
    /// </summary>
    private List<int> oddThreatColumns = new List<int>();

    /// <summary>
    /// Stores results of previously computed game states (positions) to avoid redundant calculations.
    /// Key: Zobrist hash of the board state. Value: TranspositionEntry with evaluation data.
    /// </summary>
    private Dictionary<long, TranspositionEntry> transpositionTable = new Dictionary<long, TranspositionEntry>();

    /// <summary>
    /// Holds the list of threat sequences identified in the current board state during analysis.
    /// </summary>
    private List<ThreatSequence> threatSequences = new List<ThreatSequence>();

    // --- Zobrist Hashing Components ---

    /// <summary>
    /// Table of random numbers used for Zobrist hashing. Indexed by [column, row, player_piece_type].
    /// Used to create a unique hash for each board configuration.
    /// </summary>
    private long[,,] zobristTable;

    /// <summary>
    /// A random number XORed into the Zobrist hash to account for whose turn it is.
    /// </summary>
    private long zobristPlayerTurn;

    // --- Threat Definitions ---

    /// <summary>
    /// Defines different types of threats according to Allis's analysis.
    /// </summary>
    private enum ThreatType
    {
        FourInARow,    // An immediate winning line.
        ThreeInARow,   // A line of three pieces with an adjacent empty space, threatening a win on the next move.
        BaseThreat,    // A foundational position from which multiple threats can be built.
        EvenThreat,    // A threat that can be forced in an even number of moves (usually advantageous for the first player).
        OddThreat      // A threat that can be forced in an odd number of moves (usually advantageous for the second player).
    }

    // --- Helper Classes ---

    /// <summary>
    /// Represents a sequence of moves that constitutes a threat.
    /// </summary>
    private class ThreatSequence
    {
        public ThreatType type; // The category of the threat.
        public int column; // The primary column involved in initiating or resolving the threat.
        public int length; // The number of moves required to execute the threat sequence.
        public List<int> sequence; // The specific sequence of column moves to execute the threat.

        public ThreatSequence(ThreatType type, int column)
        {
            this.type = type;
            this.column = column;
            this.sequence = new List<int>(); // Initialize the move sequence list.
        }
    }

    /// <summary>
    /// Stores information about a previously evaluated game state in the transposition table.
    /// </summary>
    private class TranspositionEntry
    {
        public float value;  // The evaluated score of the position.
        public int depth;    // The search depth at which this position was evaluated.
        public int bestMove; // The best move found from this position (-1 if not applicable/terminal).

        public TranspositionEntry(float value, int depth, int bestMove = -1)
        {
            this.value = value;
            this.depth = depth;
            this.bestMove = bestMove;
        }
    }

    // --- Initialization ---

    /// <summary>
    /// Initializes the Zobrist hashing table with random 64-bit numbers.
    /// Called once before the first move calculation.
    /// </summary>
    private void InitZobristHashing()
    {
        // Use a fixed seed for reproducibility of hashes across runs.
        System.Random rand = new System.Random(42);

        // Initialize the table for each possible piece (player 0, player 1) at each board location.
        zobristTable = new long[GameController.numColumns, GameController.numRows, 2];

        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows; y++)
            {
                // Assign a unique random number for each player's piece at each position.
                for (int p = 0; p < 2; p++) // 0 for Yellow (player 0), 1 for Red (player 1)
                {
                    zobristTable[x, y, p] = NextRandom(rand);
                }
            }
        }

        // Generate a unique random number to represent whose turn it is.
        zobristPlayerTurn = NextRandom(rand);
    }

    /// <summary>
    /// Generates a pseudo-random 64-bit integer using the provided Random instance.
    /// </summary>
    /// <param name="rand">The random number generator.</param>
    /// <returns>A random long value.</returns>
    private long NextRandom(System.Random rand)
    {
        byte[] bytes = new byte[8]; // 64 bits = 8 bytes
        rand.NextBytes(bytes); // Fill the byte array with random values.
        return System.BitConverter.ToInt64(bytes, 0); // Convert bytes to a long.
    }

    // --- Hashing ---

    /// <summary>
    /// Calculates the Zobrist hash for the given game state.
    /// The hash is position-dependent and turn-dependent.
    /// </summary>
    /// <param name="state">The current Connect4State.</param>
    /// <returns>A 64-bit hash value representing the state.</returns>
    private long ComputeZobristHash(Connect4State state)
    {
        long hash = 0; // Start with an empty hash.
        byte[,] board = state.GetBoard(); // Get the current board layout.

        // Iterate through each cell of the board.
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows; y++)
            {
                // If a piece is present, XOR its corresponding random number into the hash.
                if (board[x, y] == (byte)Connect4State.Piece.Yellow) // Player 0's piece
                {
                    hash ^= zobristTable[x, y, 0]; // XOR with Yellow piece hash at (x, y).
                }
                else if (board[x, y] == (byte)Connect4State.Piece.Red) // Player 1's piece
                {
                    hash ^= zobristTable[x, y, 1]; // XOR with Red piece hash at (x, y).
                }
                // Empty cells do not contribute to the hash directly.
            }
        }

        // XOR the turn-specific hash value if it's player 1's (Red's) turn.
        // This ensures that the same board position results in different hashes depending on whose turn it is.
        if (state.GetPlayerTurn() == 1) // Red's turn
        {
            hash ^= zobristPlayerTurn;
        }

        return hash; // Return the final computed hash.
    }

    // --- Core Logic ---

    /// <summary>
    /// Calculates the best move for the agent given the current game state.
    /// This is the main entry point called by the game controller.
    /// </summary>
    /// <param name="state">The current state of the Connect Four game.</param>
    /// <returns>The column number (0-indexed) for the agent's chosen move.</returns>
    public override int GetMove(Connect4State state)
    {
        // Perform one-time initialization of Zobrist hashing if it hasn't been done yet.
        if (zobristTable == null)
        {
            InitZobristHashing();
        }

        // Reset data structures for the new move calculation.
        transpositionTable.Clear(); // Clear cached evaluations from previous turns.
        threatSequences.Clear(); // Clear threats detected in the previous turn.

        // Determine Zugzwang control based on the current state and agent's player index.
        // Assumes ZugzwangControl class provides this static method.
        playerControlsZugzwang = ZugzwangControl.DetermineZugzwangControl(state, playerIdx);

        // Find and store columns containing odd threats for the current player.
        oddThreatColumns.Clear();
        // Assumes ZugzwangControl class provides OddThreat struct/class and this static method.
        List<OddThreat> oddThreats = ZugzwangControl.FindOddThreats(state, playerIdx);
        foreach (OddThreat threat in oddThreats)
        {
            oddThreatColumns.Add(threat.Column); // Store the column index of each odd threat.
        }

        // Analyze the board to find all relevant threat sequences for the current player.
        threatSequences = DetectComplexThreats(state, playerIdx);

        // Apply the core Allis strategy rules to decide the best move.
        // This method likely uses the detected threats, Zugzwang info, and potentially search.
        return ApplyAllisRules(state); // Assumes ApplyAllisRules method implements the decision logic.
    }

    // --- Threat Detection ---

    /// <summary>
    /// Checks if the current player has a winning threat sequence available.
    /// This looks for immediate wins (three-in-a-row) or double threats.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="player">The player index (0 or 1) to check for.</param>
    /// <returns>True if a winning threat sequence exists, false otherwise.</returns>
    private bool DetectWinningThreatSequence(Connect4State state, int player)
    {
        // Get board state and player's piece type.
        byte[,] board = state.GetBoard();
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;

        // Check for horizontal three-in-a-row threats (e.g., O O O _ or _ O O O).
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++) // Iterate through possible start columns for a 4-length sequence.
            {
                // Assumes CheckThreeWithSpace helper function exists.
                if (CheckThreeWithSpace(board, x, y, 1, 0, playerPiece)) // dx=1, dy=0 for horizontal
                {
                    return true; // Found an immediate winning threat.
                }
            }
        }

        // Check for vertical three-in-a-row threats.
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++) // Iterate through possible start rows.
            {
                 // Assumes CheckThreeWithSpace helper function exists.
                if (CheckThreeWithSpace(board, x, y, 0, 1, playerPiece)) // dx=0, dy=1 for vertical
                {
                    return true;
                }
            }
        }

        // Check for diagonal (bottom-left to top-right) three-in-a-row threats.
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++) // Start from row 3 to allow space for diagonal up.
            {
                 // Assumes CheckThreeWithSpace helper function exists.
                if (CheckThreeWithSpace(board, x, y, 1, -1, playerPiece)) // dx=1, dy=-1 for rising diagonal
                {
                    return true;
                }
            }
        }

        // Check for diagonal (top-left to bottom-right) three-in-a-row threats.
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                 // Assumes CheckThreeWithSpace helper function exists.
                if (CheckThreeWithSpace(board, x, y, 1, 1, playerPiece)) // dx=1, dy=1 for falling diagonal
                {
                    return true;
                }
            }
        }

        // If no immediate three-in-a-row threats are found, check for double threats.
        // A double threat occurs when playing in one column creates two separate winning opportunities.
        // Assumes HasDoubleThreat helper function exists.
        return HasDoubleThreat(state, player);
    }


    /// <summary>
    /// Detects various types of threats (immediate, even, odd, rule-based) for the given player.
    /// Populates the `threatSequences` list.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="player">The player index (0 or 1) whose threats are being detected.</param>
    /// <returns>A list of detected ThreatSequence objects.</returns>
    private List<ThreatSequence> DetectComplexThreats(Connect4State state, int player)
    {
        List<ThreatSequence> threats = new List<ThreatSequence>(); // Initialize list for this detection run.
        byte[,] board = state.GetBoard();
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;

        // Step 1: Find all immediate threats (three-in-a-row with an adjacent empty space).
        DetectImmediateThreats(board, player, threats);

        // Step 2: Detect even threats (threats requiring an even number of moves to force a win).
        // Assumes DetectEvenThreats helper function exists.
        DetectEvenThreats(state, player, threats);

        // Step 3: Detect odd threats (threats requiring an odd number of moves to force a win).
        // Assumes DetectOddThreats helper function exists.
        DetectOddThreats(state, player, threats);

        // Step 4: Detect more complex threats based on specific strategic rules or patterns.
        // Assumes DetectRuleBasedThreats helper function exists.
        DetectRuleBasedThreats(state, player, threats);

        return threats; // Return the consolidated list of detected threats.
    }

    /// <summary>
    /// Finds immediate threats (three pieces in a row with a playable empty space)
    /// and adds them to the provided list.
    /// </summary>
    /// <param name="board">The current board state.</param>
    /// <param name="player">The player index (0 or 1) to check for.</param>
    /// <param name="threats">The list to add detected threats to.</param>
    private void DetectImmediateThreats(byte[,] board, int player, List<ThreatSequence> threats)
    {
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;

        // Check Horizontal immediate threats (e.g., O O O _ or _ O O O)
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                // Assumes FindThreeWithSpace helper function exists and returns the relative index (0-3) of the empty space, or -1 if no threat.
                int emptyPos = FindThreeWithSpace(board, x, y, 1, 0, playerPiece); // dx=1, dy=0
                if (emptyPos != -1) // If a threat is found
                {
                    ThreatSequence threat = new ThreatSequence(ThreatType.ThreeInARow, x + emptyPos);
                    threat.sequence.Add(x + emptyPos);
                    threats.Add(threat);
                }
            }
        }

        // Check Vertical immediate threats (only possible pattern is _ O O O at the top of a column)
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                 // Assumes FindThreeWithSpace helper function exists.
                int emptyPos = FindThreeWithSpace(board, x, y, 0, 1, playerPiece); // dx=0, dy=1
                if (emptyPos != -1) // emptyPos should correspond to the top empty slot if threat exists
                {
                    ThreatSequence threat = new ThreatSequence(ThreatType.ThreeInARow, x);
                    threat.sequence.Add(x);
                    threats.Add(threat);
                }
            }
        }

        // Check Diagonal rising immediate threats (bottom-left to top-right)
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++) // Start high enough to have space below
            {
                 // Assumes FindThreeWithSpace helper function exists.
                int emptyPos = FindThreeWithSpace(board, x, y, 1, -1, playerPiece); // dx=1, dy=-1
                if (emptyPos != -1)
                {
                    ThreatSequence threat = new ThreatSequence(ThreatType.ThreeInARow, x + emptyPos);
                    threat.sequence.Add(x + emptyPos);
                    threats.Add(threat);
                }
            }
        }

        // Check Diagonal falling immediate threats (top-left to bottom-right)
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++) // Start low enough to have space above
            {
                 // Assumes FindThreeWithSpace helper function exists.
                int emptyPos = FindThreeWithSpace(board, x, y, 1, 1, playerPiece); // dx=1, dy=1
                if (emptyPos != -1)
                {
                    ThreatSequence threat = new ThreatSequence(ThreatType.ThreeInARow, x + emptyPos);
                    threat.sequence.Add(x + emptyPos);
                    threats.Add(threat);
                }
            }
        }
    }
    private int FindThreeWithSpace(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
    {
        int playerCount = 0;
        int emptyIndex = -1;
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
            if (board[x, y] == playerPiece)
            {
                playerCount++;
            }
            else if (board[x, y] == 0) // Empty
            {
                // Check if this empty position is accessible (either at bottom row or has piece below it)
                if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                {
                    emptyIndex = i;
                }
            }
            else
            {
                // Found opponent piece, invalid threat
                return -1;
            }
        }
        
        // Return position of the empty space if this is a valid threat
        if (playerCount == 3 && emptyIndex != -1)
        {
            return emptyIndex;
        }
        
        return -1;
    }
    
    // Detect even threats (require even number of moves)
    private void DetectEvenThreats(Connect4State state, int player, List<ThreatSequence> threats)
    {
        byte[,] board = state.GetBoard();
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
        
        // Type 1: Two separate threats that force opponent to defend both (Allis's thesis section 4.3)
        List<int> potentialThreatColumns = new List<int>();
        
        // Check horizontal patterns that could lead to even threats
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                int[] emptyColumns = FindTwoWithTwoSpaces(board, x, y, 1, 0, playerPiece);
                if (emptyColumns != null)
                {
                    foreach (int col in emptyColumns)
                    {
                        if (!potentialThreatColumns.Contains(col))
                            potentialThreatColumns.Add(col);
                    }
                }
            }
        }
        
        // Check diagonal rising patterns
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++)
            {
                int[] emptyColumns = FindTwoWithTwoSpaces(board, x, y, 1, -1, playerPiece);
                if (emptyColumns != null)
                {
                    foreach (int col in emptyColumns)
                    {
                        if (!potentialThreatColumns.Contains(col))
                            potentialThreatColumns.Add(col);
                    }
                }
            }
        }
        
        // Check diagonal falling patterns
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                int[] emptyColumns = FindTwoWithTwoSpaces(board, x, y, 1, 1, playerPiece);
                if (emptyColumns != null)
                {
                    foreach (int col in emptyColumns)
                    {
                        if (!potentialThreatColumns.Contains(col))
                            potentialThreatColumns.Add(col);
                    }
                }
            }
        }
        
        // If there are at least two threat columns, create an even threat
        if (potentialThreatColumns.Count >= 2)
        {
            for (int i = 0; i < potentialThreatColumns.Count - 1; i++)
            {
                for (int j = i + 1; j < potentialThreatColumns.Count; j++)
                {
                    ThreatSequence threat = new ThreatSequence(ThreatType.EvenThreat, potentialThreatColumns[i]);
                    threat.sequence.Add(potentialThreatColumns[i]);
                    threat.sequence.Add(potentialThreatColumns[j]);
                    threats.Add(threat);
                }
            }
        }
        
        // Type 2: Threats that use Claimeven (section 6.2.1 of Allis's thesis)
        // This requires checking for Claimeven patterns
        List<ThreatGroup> allThreats = FindAllThreats(state, 1 - player);
        List<Claimeven> claimevens = FindClaimevens(state, player);
        
        foreach (Claimeven claimeven in claimevens)
        {
            if (claimeven.CanApply(state, player))
            {
                // If this Claimeven can be applied, it creates an even threat
                ThreatSequence threat = new ThreatSequence(ThreatType.EvenThreat, claimeven.GetUsedSquares().ElementAt(0).x);
                foreach (Vector2Int square in claimeven.GetUsedSquares())
                {
                    if (!threat.sequence.Contains(square.x))
                        threat.sequence.Add(square.x);
                }
                threats.Add(threat);
            }
        }
    }
    
    // Find columns with pattern: two pieces and two accessible empty spaces
    private int[] FindTwoWithTwoSpaces(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
    {
        int playerCount = 0;
        int emptyCount = 0;
        int[] emptyColumns = new int[2];
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
            if (board[x, y] == playerPiece)
            {
                playerCount++;
            }
            else if (board[x, y] == 0) // Empty
            {
                // Check if this empty position is accessible (either at bottom row or has piece below it)
                if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                {
                    if (emptyCount < 2)
                        emptyColumns[emptyCount] = x;
                    emptyCount++;
                }
            }
            else
            {
                // Found opponent piece, invalid threat
                return null;
            }
        }
        
        // Return empty columns if this pattern has exactly two pieces and two accessible empty spaces
        if (playerCount == 2 && emptyCount == 2)
        {
            return emptyColumns;
        }
        
        return null;
    }
    
    // Detect odd threats (require odd number of moves)
    private void DetectOddThreats(Connect4State state, int player, List<ThreatSequence> threats)
    {
        List<OddThreat> oddThreats = ZugzwangControl.FindOddThreats(state, player);
        
        foreach (OddThreat oddThreat in oddThreats)
        {
            ThreatSequence threat = new ThreatSequence(ThreatType.OddThreat, oddThreat.Column);
            threat.sequence.Add(oddThreat.Column);
            threats.Add(threat);
        }
    }
    
    // Detect threats based on the strategic rule system
    private void DetectRuleBasedThreats(Connect4State state, int player, List<ThreatSequence> threats)
    {
        // Find all applicable strategic rules
        List<StrategicRule> applicableRules = FindApplicableRules(state, player);
        
        // Use the rule interaction system to find valid rule combinations
        List<ThreatGroup> allThreats = FindAllThreats(state, 1 - player);
        List<StrategicRule> validRuleSet = RuleInteractionSystem.FindValidRuleSet(applicableRules, allThreats);
        
        if (validRuleSet != null && validRuleSet.Count > 0)
        {
            // Create threat sequences for each rule in the valid set
            foreach (StrategicRule rule in validRuleSet)
            {
                RuleType ruleType = rule.GetRuleType();
                HashSet<Vector2Int> usedSquares = rule.GetUsedSquares();
                
                // Create a base threat type based on the rule
                ThreatType threatType = ThreatType.BaseThreat;
                if (ruleType == RuleType.Claimeven || ruleType == RuleType.Before || 
                    ruleType == RuleType.Aftereven || ruleType == RuleType.Baseclaim)
                {
                    threatType = ThreatType.EvenThreat;
                }
                
                // Select a primary column for this threat
                int primaryColumn = usedSquares.ElementAt(0).x;
                ThreatSequence threat = new ThreatSequence(threatType, primaryColumn);
                
                // Add all columns used by this rule to the sequence
                foreach (Vector2Int square in usedSquares)
                {
                    if (!threat.sequence.Contains(square.x))
                        threat.sequence.Add(square.x);
                }
                
                threats.Add(threat);
            }
        }
    }
    
    // Find all Claimeven instances that can be applied
    private List<Claimeven> FindClaimevens(Connect4State state, int playerIdx)
    {
        List<Claimeven> claimevens = new List<Claimeven>();
        byte[,] board = state.GetBoard();
        
        // Find all possible Claimeven patterns
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows - 1; y++)
            {
                // Check for Claimeven pattern: odd row with even row above
                if ((y + 1) % 2 == 1 && board[x, y] == 0 && board[x, y+1] == 0)
                {
                    Claimeven rule = new Claimeven(x, y, y+1);
                    if (rule.CanApply(state, playerIdx))
                        claimevens.Add(rule);
                }
            }
        }
        
        return claimevens;
    }
    
    // Check for three pieces in a row with an empty space at either end
    private bool CheckThreeWithSpace(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
    {
        // Count pattern: empty-player-player-player-empty
        // Or player-player-player-empty
        // Or empty-player-player-player
        
        int playerCount = 0;
        int emptyCount = 0;
        int[] emptyX = new int[2];
        int[] emptyY = new int[2];
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
            if (board[x, y] == playerPiece)
            {
                playerCount++;
            }
            else if (board[x, y] == 0) // Empty
            {
                if (emptyCount < 2)
                {
                    emptyX[emptyCount] = x;
                    emptyY[emptyCount] = y;
                }
                emptyCount++;
            }
        }
        
        // Check if we have a valid threat
        if (playerCount == 3 && emptyCount == 1)
        {
            // Verify the empty cell is accessible (either at bottom row or has piece below it)
            for (int i = 0; i < emptyCount; i++)
            {
                int x = emptyX[i];
                int y = emptyY[i];
                
                if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                {
                    // This is a valid threat
                    return true;
                }
            }
        }
        
        return false;
    }
    
    // Check if player has a double threat (can win in two different columns)
    private bool HasDoubleThreat(Connect4State state, int player)
    {
        List<int> winningColumns = new List<int>();
        List<int> possibleMoves = state.GetPossibleMoves();
        
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            Connect4State.Result result = newState.GetResult();
            if ((player == 0 && result == Connect4State.Result.YellowWin) ||
                (player == 1 && result == Connect4State.Result.RedWin))
            {
                winningColumns.Add(move);
                
                if (winningColumns.Count > 1)
                {
                    return true; // Found at least two winning moves
                }
            }
        }
        
        return false;
    }
    
    // Evaluate position using Allis's knowledge-based heuristics
    private float EvaluateKnowledgeBased(Connect4State state)
    {
        byte[,] board = state.GetBoard();
        float score = 0;
        
        // In Allis's thesis, position evaluation is based on strategic patterns
        // and different threat types rather than just counting pieces
        
        // 1. Control of center columns is critical
        int[] columnWeights = { 0, 0, 1, 3, 1, 0, 0 }; // Column importance weights
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows; y++)
            {
                if (board[x, y] == (byte)Connect4State.Piece.Red)
                {
                    score += columnWeights[x];
                }
                else if (board[x, y] == (byte)Connect4State.Piece.Yellow)
                {
                    score -= columnWeights[x];
                }
            }
        }
        
        // 2. Check for specific threats (these are highly weighted)
        score += EvaluateThreats(board);
        
        // 3. Check winning patterns
        score += EvaluateConnectPatterns(board);
        
        // 4. Evaluate strategic positions
        score += EvaluateStrategicPositions(board);
        
        return score;
    }
    
    // Evaluate threats based on Allis's threat categories
    private float EvaluateThreats(byte[,] board)
    {
        float score = 0;
        
        // Check for type-1 threats (immediate win opportunities)
        score += CountThreats(board, 3, 1, (byte)Connect4State.Piece.Red) * 100;
        score -= CountThreats(board, 3, 1, (byte)Connect4State.Piece.Yellow) * 100;
        
        // Check for type-2 threats (double threats)
        score += CountThreats(board, 2, 2, (byte)Connect4State.Piece.Red) * 50;
        score -= CountThreats(board, 2, 2, (byte)Connect4State.Piece.Yellow) * 50;
        
        // Check for type-3 threats (single threats with potential)
        score += CountThreats(board, 2, 1, (byte)Connect4State.Piece.Red) * 20;
        score -= CountThreats(board, 2, 1, (byte)Connect4State.Piece.Yellow) * 20;
        
        return score;
    }
    
    // Count threats of specific type
    private int CountThreats(byte[,] board, int pieceCount, int emptyCount, byte playerPiece)
    {
        int threatCount = 0;
        
        // Horizontal threats
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                if (CountThreatInWindow(board, x, y, 1, 0, pieceCount, emptyCount, playerPiece))
                {
                    threatCount++;
                }
            }
        }
        
        // Vertical threats
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                if (CountThreatInWindow(board, x, y, 0, 1, pieceCount, emptyCount, playerPiece))
                {
                    threatCount++;
                }
            }
        }
        
        // Diagonal rising threats
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++)
            {
                if (CountThreatInWindow(board, x, y, 1, -1, pieceCount, emptyCount, playerPiece))
                {
                    threatCount++;
                }
            }
        }
        
        // Diagonal falling threats
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                if (CountThreatInWindow(board, x, y, 1, 1, pieceCount, emptyCount, playerPiece))
                {
                    threatCount++;
                }
            }
        }
        
        return threatCount;
    }
    
    // Check if window has exact number of pieces and empty spaces
    private bool CountThreatInWindow(byte[,] board, int startX, int startY, int dX, int dY, 
                                    int pieceCount, int emptyCount, byte playerPiece)
    {
        int playerCount = 0;
        int emptySpaces = 0;
        int otherCount = 0;
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
            if (board[x, y] == playerPiece)
            {
                playerCount++;
            }
            else if (board[x, y] == 0)
            {
                // Check if space is accessible
                if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                {
                    emptySpaces++;
                }
                else
                {
                    // Empty but not accessible
                    otherCount++;
                }
            }
            else
            {
                otherCount++;
            }
        }
        
        // Return true if window has exactly the required number of pieces and empty spaces
        // and no opponent pieces
        return playerCount == pieceCount && emptySpaces == emptyCount && otherCount == 0;
    }
    
    // Evaluate connect patterns (1, 2, 3 in a row with potential to connect 4)
    private float EvaluateConnectPatterns(byte[,] board)
    {
        float score = 0;
        
        // Horizontal patterns
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                score += EvaluateWindow(board, x, y, 1, 0);
            }
        }
        
        // Vertical patterns
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                score += EvaluateWindow(board, x, y, 0, 1);
            }
        }
        
        // Diagonal rising patterns
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++)
            {
                score += EvaluateWindow(board, x, y, 1, -1);
            }
        }
        
        // Diagonal falling patterns
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                score += EvaluateWindow(board, x, y, 1, 1);
            }
        }
        
        return score;
    }
    
    // Evaluate strategic positions based on Allis's thesis
    private float EvaluateStrategicPositions(byte[,] board)
    {
        float score = 0;
        
        // Allis identified certain key positions as strategically important
        // These are specific patterns that lead to winning positions
        
        // Pattern 1: Diagonal connections in specific positions
        // Check key positions for diagonal setups (examples from thesis)
        int[][] keyDiagonalPositions = new int[][] {
            new int[] {1, 1}, new int[] {2, 2}, new int[] {4, 2}, new int[] {5, 1},
            new int[] {1, 4}, new int[] {2, 3}, new int[] {4, 3}, new int[] {5, 4}
        };
        
        foreach (int[] pos in keyDiagonalPositions)
        {
            int x = pos[0];
            int y = pos[1];
            
            // Check if position is within bounds
            if (x >= 0 && x < GameController.numColumns && y >= 0 && y < GameController.numRows)
            {
                if (board[x, y] == (byte)Connect4State.Piece.Red)
                {
                    score += 2;
                }
                else if (board[x, y] == (byte)Connect4State.Piece.Yellow)
                {
                    score -= 2;
                }
            }
        }
        
        // Pattern 2: "Trap" positions that allow multiple winning moves
        // These are positions where a player controls adjacent columns
        for (int x = 0; x < GameController.numColumns - 1; x++)
        {
            for (int y = 0; y < GameController.numRows - 1; y++)
            {
                // Check 2x2 squares
                int redCount = 0;
                int yellowCount = 0;
                
                for (int dx = 0; dx <= 1; dx++)
                {
                    for (int dy = 0; dy <= 1; dy++)
                    {
                        if (board[x + dx, y + dy] == (byte)Connect4State.Piece.Red)
                        {
                            redCount++;
                        }
                        else if (board[x + dx, y + dy] == (byte)Connect4State.Piece.Yellow)
                        {
                            yellowCount++;
                        }
                    }
                }
                
                // Assign score based on control of 2x2 areas
                if (redCount >= 2 && yellowCount == 0)
                {
                    score += 5;
                }
                else if (yellowCount >= 2 && redCount == 0)
                {
                    score -= 5;
                }
            }
        }
        
        return score;
    }
    
    // Evaluate a 4-cell window according to Allis's heuristics
    private float EvaluateWindow(byte[,] board, int startX, int startY, int dX, int dY)
    {
        int redCount = 0;
        int yellowCount = 0;
        int emptyCount = 0;
        
        for (int i = 0; i < 4; i++)
        {
            int x = startX + i * dX;
            int y = startY + i * dY;
            
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
        
        // Cannot be a threat if both colors present
        if (redCount > 0 && yellowCount > 0)
        {
            return 0;
        }
        
        // Score based on Allis's threat patterns
        if (redCount > 0)
        {
            switch (redCount)
            {
                case 1: return 1;       // Single piece - low threat
                case 2: return 10;      // Two pieces - medium threat
                case 3: return 50;      // Three pieces - high threat
                case 4: return 1000;    // Win
            }
        }
        else if (yellowCount > 0)
        {
            switch (yellowCount)
            {
                case 1: return -1;      // Single piece - low threat
                case 2: return -10;     // Two pieces - medium threat
                case 3: return -50;     // Three pieces - high threat
                case 4: return -1000;   // Win
            }
        }
        
        return 0;
    }
    
    // Additional advanced threat sequence detection methods from Allis's thesis
    
    // Detect a potential "even threat" - requires even number of moves to win
    private bool DetectEvenThreat(Connect4State state, int player)
    {
        byte[,] board = state.GetBoard();
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
        
        // Check for possible threat configurations that require even number of moves
        // Example: two separate threats that force opponent to defend both
        
        // Look for two separate threat formations
        List<int> threatColumns = new List<int>();
        
        // Check horizontal threats
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 3; x++)
            {
                // Check for pattern: empty-player-player-empty where both empties are accessible
                if (CheckTwoWithSpaces(board, x, y, 1, 0, playerPiece))
                {
                    // Add the empty columns to threat list
                    if (IsAccessible(board, x, y) && !threatColumns.Contains(x))
                        threatColumns.Add(x);
                        
                    if (IsAccessible(board, x+3, y) && !threatColumns.Contains(x+3))
                        threatColumns.Add(x+3);
                }
            }
        }
        
        // Check diagonal threats (falling)
        for (int x = 0; x <= GameController.numColumns - 3; x++)
        {
            for (int y = 0; y <= GameController.numRows - 3; y++)
            {
                if (CheckTwoWithSpaces(board, x, y, 1, 1, playerPiece))
                {
                    if (IsAccessible(board, x, y) && !threatColumns.Contains(x))
                        threatColumns.Add(x);
                        
                    if (IsAccessible(board, x+3, y+3) && !threatColumns.Contains(x+3))
                        threatColumns.Add(x+3);
                }
            }
        }
        
        // Check diagonal threats (rising)
        for (int x = 0; x <= GameController.numColumns - 3; x++)
        {
            for (int y = 3; y < GameController.numRows; y++)
            {
                if (CheckTwoWithSpaces(board, x, y, 1, -1, playerPiece))
                {
                    if (IsAccessible(board, x, y) && !threatColumns.Contains(x))
                        threatColumns.Add(x);
                        
                    if (IsAccessible(board, x+3, y-3) && !threatColumns.Contains(x+3))
                        threatColumns.Add(x+3);
                }
            }
        }
        
        // An even threat exists if we found at least two separate threat columns
        return threatColumns.Count >= 2;
    }
    
    // Check for two pieces with empty spaces on both sides
    private bool CheckTwoWithSpaces(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
    {
        // Check if out of bounds
        if (startX < 0 || startX + 3*dX >= GameController.numColumns || 
            startY < 0 || startY + 3*dY >= GameController.numRows || 
            startY + 3*dY < 0) // For rising diagonals
            return false;
            
        // Pattern: empty-player-player-empty
        bool isEmpty0 = board[startX, startY] == 0;
        bool isPlayer1 = board[startX + dX, startY + dY] == playerPiece;
        bool isPlayer2 = board[startX + 2*dX, startY + 2*dY] == playerPiece;
        bool isEmpty3 = board[startX + 3*dX, startY + 3*dY] == 0;
        
        return isEmpty0 && isPlayer1 && isPlayer2 && isEmpty3;
    }
    
    // Check if a position is accessible (can place a piece there)
    private bool IsAccessible(byte[,] board, int x, int y)
    {
        // Bottom row is always accessible
        if (y == GameController.numRows - 1)
            return true;
            
        // Otherwise, need a piece below it
        return board[x, y+1] != 0;
    }
    
    // Detect vertical threats from Allis's thesis
    private bool DetectVerticalThreat(Connect4State state, int player)
    {
        byte[,] board = state.GetBoard();
        byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
        
        // Check vertical columns for potential three-in-a-row threats
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = GameController.numRows - 1; y >= 3; y--)
            {
                // Check for pattern: empty-player-player-player (bottom to top)
                if (board[x, y] == 0 && 
                    board[x, y-1] == playerPiece && 
                    board[x, y-2] == playerPiece && 
                    board[x, y-3] == playerPiece)
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    // Implement the 9-rule solution strategy from Allis's thesis
    private int ApplyAllisRules(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        
        // Rule 1: If can win in one move, do it
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            Connect4State.Result result = newState.GetResult();
            if ((playerIdx == 0 && result == Connect4State.Result.YellowWin) ||
                (playerIdx == 1 && result == Connect4State.Result.RedWin))
            {
                return move;
            }
        }
        
        // Rule 2: If opponent can win in one move, block it
        int opponentIdx = 1 - playerIdx;
        
        foreach (int move in possibleMoves)
        {
            Connect4State testState = state.Clone();
            
            // Switch turn to opponent
            if (testState.GetPlayerTurn() != opponentIdx)
            {
                while (testState.GetPlayerTurn() != opponentIdx)
                {
                    testState = testState.Clone();
                    int anyMove = testState.GetPossibleMoves()[0];
                    testState.MakeMove(anyMove);
                }
            }
            
            testState.MakeMove(move);
            Connect4State.Result result = testState.GetResult();
            
            if ((opponentIdx == 0 && result == Connect4State.Result.YellowWin) ||
                (opponentIdx == 1 && result == Connect4State.Result.RedWin))
            {
                return move;
            }
        }
        
        // Check Zugzwang control and apply formal strategic rules EARLY
        // This is now higher priority than other tactical rules
        bool controlsZugzwang = ZugzwangControl.DetermineZugzwangControl(state, playerIdx);
        if (controlsZugzwang)
        {
            // We control Zugzwang, try to find a move that preserves our control
            // and leads to a better position using formal strategic rules
            int bestMove = ApplyStrategicRules(state);
            if (bestMove != -1)
                return bestMove;
        }
        else
        {
            // We don't control Zugzwang, try to create an odd threat to gain control
            int bestMove = TryToGainZugzwangControl(state);
            if (bestMove != -1)
                return bestMove;
        }
        
        // Rule 3: If can create a double threat, do it
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            if (HasDoubleThreat(newState, playerIdx))
            {
                return move;
            }
        }
        
        // Rule 4: If can create an even threat, do it
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            if (DetectEvenThreat(newState, playerIdx))
            {
                return move;
            }
        }
        
        // Rule 5: Block opponent's threats
        foreach (int move in possibleMoves)
        {
            Connect4State opponentState = state.Clone();
            
            // Switch turns and have opponent make this move
            while (opponentState.GetPlayerTurn() != opponentIdx)
            {
                opponentState.MakeMove(opponentState.GetPossibleMoves()[0]);
            }
            
            opponentState.MakeMove(move);
            
            if (HasDoubleThreat(opponentState, opponentIdx) || 
                DetectEvenThreat(opponentState, opponentIdx))
            {
                return move;
            }
        }
        
        // Rule 6: If center column is available, play there
        if (possibleMoves.Contains(3))
        {
            return 3;
        }
        
        // Rule 7: Preferred columns (adjacent to center)
        if (possibleMoves.Contains(2))
        {
            return 2;
        }
        if (possibleMoves.Contains(4))
        {
            return 4;
        }
        
        // Rule 8: Avoid setting up opponent's win
        foreach (int move in possibleMoves)
        {
            // Try this move
            Connect4State testState = state.Clone();
            testState.MakeMove(move);
            
            // Let opponent make a move in each column
            foreach (int opponentMove in testState.GetPossibleMoves())
            {
                Connect4State opponentState = testState.Clone();
                opponentState.MakeMove(opponentMove);
                
                // Check if opponent has created a winning threat
                if (DetectWinningThreatSequence(opponentState, opponentIdx) || 
                    HasDoubleThreat(opponentState, opponentIdx))
                {
                    // This move is dangerous, try a different one
                    goto NextMove;
                }
            }
            
            // If we get here, this move doesn't set up opponent's win
            return move;
            
            NextMove:
            continue;
        }
        
        // Rule 9: Choose remaining move with best heuristic evaluation
        int bestMoveEval = -1;
        float bestScore = playerIdx == 0 ? float.MinValue : float.MaxValue;
        
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            // Use the new strategic evaluation function based on Allis' thesis
            float score = EvaluateStrategyBased(newState);
            
            if ((playerIdx == 0 && score > bestScore) || // Yellow maximizes (higher is better for us)
                (playerIdx == 1 && score < bestScore))   // Red minimizes (lower is better for us)
            {
                bestScore = score;
                bestMoveEval = move;
            }
        }
        
        // Default to first legal move if no good move found
        if (bestMoveEval == -1 && possibleMoves.Count > 0)
        {
            bestMoveEval = possibleMoves[0];
        }
        
        return bestMoveEval;
    }
    
    // Apply the formal strategic rules to find a good move
    private int ApplyStrategicRules(Connect4State state)
    {
        // Track time to avoid timing out
        float startTime = Time.realtimeSinceStartup;
        float timeThreshold = timeLimit * 0.7f; // Use 70% of our time budget
        
        List<int> possibleMoves = state.GetPossibleMoves();
        List<ThreatGroup> allThreats = FindAllThreats(state, 1 - playerIdx);
        
        // Create all possible instances of strategic rules
        List<StrategicRule> allRules = new List<StrategicRule>();
        
        // For each possible move, check what strategic rules would be applicable after that move
        foreach (int move in possibleMoves)
        {
            // Check time budget
            if (Time.realtimeSinceStartup - startTime > timeThreshold)
            {
                // We're running out of time, fall back to a simpler evaluation
                break;
            }
            
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            List<StrategicRule> rulesAfterMove = FindApplicableRules(newState, playerIdx);
            if (rulesAfterMove.Count > 0)
            {
                // Find a valid set of rules that solve all threats
                List<StrategicRule> validRuleSet = RuleInteractionSystem.FindValidRuleSet(rulesAfterMove, allThreats);
                if (validRuleSet != null && validRuleSet.Count > 0)
                {
                    // This move allows us to solve all threats
                    return move;
                }
            }
        }
        
        return -1; // No strategic move found
    }
    
    // Find all threats for the opponent
    private List<ThreatGroup> FindAllThreats(Connect4State state, int opponentIdx)
    {
        List<ThreatGroup> threats = new List<ThreatGroup>();
        byte[,] board = state.GetBoard();
        byte opponentPiece = opponentIdx == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
        
        // Check horizontal threats
        for (int y = 0; y < GameController.numRows; y++)
        {
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                ThreatGroup group = new ThreatGroup(x, y, 1, 0);
                if (IsValidThreat(board, group.columns, group.rows, opponentPiece))
                    threats.Add(group);
            }
        }
        
        // Check vertical threats
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                ThreatGroup group = new ThreatGroup(x, y, 0, 1);
                if (IsValidThreat(board, group.columns, group.rows, opponentPiece))
                    threats.Add(group);
            }
        }
        
        // Check diagonal rising threats
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 3; y < GameController.numRows; y++)
            {
                ThreatGroup group = new ThreatGroup(x, y, 1, -1);
                if (IsValidThreat(board, group.columns, group.rows, opponentPiece))
                    threats.Add(group);
            }
        }
        
        // Check diagonal falling threats
        for (int x = 0; x <= GameController.numColumns - 4; x++)
        {
            for (int y = 0; y <= GameController.numRows - 4; y++)
            {
                ThreatGroup group = new ThreatGroup(x, y, 1, 1);
                if (IsValidThreat(board, group.columns, group.rows, opponentPiece))
                    threats.Add(group);
            }
        }
        
        return threats;
    }
    
    // Check if a group forms a valid threat for the given player
    private bool IsValidThreat(byte[,] board, int[] columns, int[] rows, byte playerPiece)
    {
        byte otherPiece = playerPiece == (byte)Connect4State.Piece.Yellow ? 
                          (byte)Connect4State.Piece.Red : (byte)Connect4State.Piece.Yellow;
        
        int playerCount = 0;
        int emptyCount = 0;
        
        for (int i = 0; i < 4; i++)
        {
            if (board[columns[i], rows[i]] == playerPiece)
                playerCount++;
            else if (board[columns[i], rows[i]] == 0)
                emptyCount++;
            else
                return false; // Contains opponent piece, not a threat
        }
        
        // A valid threat has at least one player piece and at least one empty space
        return playerCount > 0 && emptyCount > 0;
    }
    
    // Find all applicable strategic rules in a position
    private List<StrategicRule> FindApplicableRules(Connect4State state, int playerIdx)
    {
        List<StrategicRule> rules = new List<StrategicRule>();
        byte[,] board = state.GetBoard();
        
        // Find all Claimeven instances
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows - 1; y++)
            {
                // Check for Claimeven pattern: odd row with even row above
                if ((y + 1) % 2 == 1 && board[x, y] == 0 && board[x, y+1] == 0)
                {
                    Claimeven rule = new Claimeven(x, y, y+1);
                    if (rule.CanApply(state, playerIdx))
                        rules.Add(rule);
                }
            }
        }
        
        // Find all Baseinverse instances
        for (int x1 = 0; x1 < GameController.numColumns; x1++)
        {
            for (int y1 = 0; y1 < GameController.numRows; y1++)
            {
                if (board[x1, y1] != 0)
                    continue;
                    
                // Check if the square is directly playable
                if (y1 < GameController.numRows - 1 && board[x1, y1 + 1] == 0)
                    continue;
                    
                for (int x2 = x1; x2 < GameController.numColumns; x2++)
                {
                    int startY = (x2 == x1) ? y1 + 1 : 0;
                    
                    for (int y2 = startY; y2 < GameController.numRows; y2++)
                    {
                        if (board[x2, y2] != 0)
                            continue;
                            
                        // Check if the second square is directly playable
                        if (y2 < GameController.numRows - 1 && board[x2, y2 + 1] == 0)
                            continue;
                            
                        Baseinverse rule = new Baseinverse(x1, y1, x2, y2);
                        if (rule.CanApply(state, playerIdx))
                            rules.Add(rule);
                    }
                }
            }
        }
        
        // Find all Vertical instances
        for (int x = 0; x < GameController.numColumns; x++)
        {
            for (int y = 0; y < GameController.numRows - 1; y++)
            {
                // Check for Vertical pattern: lower row with odd row above
                if ((y + 1) % 2 == 0 && board[x, y] == 0 && board[x, y+1] == 0)
                {
                    Vertical rule = new Vertical(x, y, y+1);
                    if (rule.CanApply(state, playerIdx))
                        rules.Add(rule);
                }
            }
        }
        
        // Other rule types would be added similarly
        
        return rules;
    }
    
    // Try to gain Zugzwang control by creating an odd threat
    private int TryToGainZugzwangControl(Connect4State state)
    {
        List<int> possibleMoves = state.GetPossibleMoves();
        
        // Try each move and check if it creates an odd threat
        foreach (int move in possibleMoves)
        {
            Connect4State newState = state.Clone();
            newState.MakeMove(move);
            
            List<OddThreat> oddThreats = ZugzwangControl.FindOddThreats(newState, playerIdx);
            if (oddThreats.Count > 0)
            {
                // This move creates an odd threat, gaining Zugzwang control
                return move;
            }
        }
        
        return -1; // No move creates an odd threat
    }

    // Classes for handling strategic rules from Allis' thesis
    private class ZugzwangControl
    {
        // Determine which player controls the Zugzwang in a given position
        // Returns true if our agent controls it, false if opponent controls it
        public static bool DetermineZugzwangControl(Connect4State state, int playerIdx)
        {
            // If we're playing as White (playerIdx == 0)
            if (playerIdx == 0)
            {
                // White controls Zugzwang if they have created an odd threat
                List<OddThreat> ourOddThreats = FindOddThreats(state, playerIdx);
                return ourOddThreats.Count > 0; // White controls if they have odd threats
            }
            else
            {
                // Black controls Zugzwang by default unless White has odd threats
                List<OddThreat> opponentOddThreats = FindOddThreats(state, 1 - playerIdx);
                return opponentOddThreats.Count == 0; // Black controls if White has no threats
            }
        }
        
        // Find all odd threats for a player
        public static List<OddThreat> FindOddThreats(Connect4State state, int player)
        {
            byte[,] board = state.GetBoard();
            List<OddThreat> oddThreats = new List<OddThreat>();
            
            byte playerPiece = player == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
            
            // Check for odd threats horizontally
            for (int y = 0; y < GameController.numRows; y++)
            {
                for (int x = 0; x <= GameController.numColumns - 4; x++)
                {
                    if (IsOddThreat(board, x, y, 1, 0, playerPiece))
                    {
                        oddThreats.Add(new OddThreat(FindThreatSquare(board, x, y, 1, 0, playerPiece)));
                    }
                }
            }
            
            // Check for odd threats diagonally (rising)
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                for (int y = 3; y < GameController.numRows; y++)
                {
                    if (IsOddThreat(board, x, y, 1, -1, playerPiece))
                    {
                        oddThreats.Add(new OddThreat(FindThreatSquare(board, x, y, 1, -1, playerPiece)));
                    }
                }
            }
            
            // Check for odd threats diagonally (falling)
            for (int x = 0; x <= GameController.numColumns - 4; x++)
            {
                for (int y = 0; y <= GameController.numRows - 4; y++)
                {
                    if (IsOddThreat(board, x, y, 1, 1, playerPiece))
                    {
                        oddThreats.Add(new OddThreat(FindThreatSquare(board, x, y, 1, 1, playerPiece)));
                    }
                }
            }
            
            return oddThreats;
        }
        
        // Check if a potential line contains an odd threat
        private static bool IsOddThreat(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
        {
            int playerCount = 0;
            int emptyOddCount = 0;
            int y;
            
            for (int i = 0; i < 4; i++)
            {
                int x = startX + i * dX;
                y = startY + i * dY;
                
                if (board[x, y] == playerPiece)
                {
                    playerCount++;
                }
                else if (board[x, y] == 0) // Empty square
                {
                    // Check if this is an odd square (rows are 0-indexed, so add 1)
                    if ((y + 1) % 2 == 1)
                    {
                        emptyOddCount++;
                        
                        // Check if this square is accessible (at bottom or has piece below)
                        if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                        {
                            // This is a potential odd threat
                            return playerCount == 2 && emptyOddCount == 1;
                        }
                    }
                }
            }
            
            return false;
        }
        
        // Find the square that comprises the odd threat
        private static Vector2Int FindThreatSquare(byte[,] board, int startX, int startY, int dX, int dY, byte playerPiece)
        {
            for (int i = 0; i < 4; i++)
            {
                int x = startX + i * dX;
                int y = startY + i * dY;
                
                if (board[x, y] == 0 && ((y + 1) % 2 == 1))
                {
                    // If accessible
                    if (y == GameController.numRows - 1 || board[x, y + 1] != 0)
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }
            
            return new Vector2Int(-1, -1); // Should not reach here if IsOddThreat returned true
        }
        
        // Check if we should play "follow-up" strategy
        public static bool ShouldPlayFollowUp(Connect4State state, int playerIdx, List<int> oddThreatColumns)
        {
            // Follow-up is only applicable if we control Zugzwang
            if (!DetermineZugzwangControl(state, playerIdx))
                return false;
                
            // We can play follow-up if there's a distinct risk of losing control
            return true;
        }
    }
    
    // Class to represent an odd threat
    private class OddThreat
    {
        public Vector2Int position;
        
        public OddThreat(Vector2Int position)
        {
            this.position = position;
        }
        
        public int Column { get { return position.x; } }
        public int Row { get { return position.y; } }
    }
    
    // Base class for strategic rules from Allis' thesis
    private abstract class StrategicRule
    {
        // Check if this rule can be applied to the given squares
        public abstract bool CanApply(Connect4State state, int playerIdx);
        
        // Get the list of threat groups that this rule solves
        public abstract List<ThreatGroup> GetSolvedThreats();
        
        // Represents a group of four connected positions that could be a threat
        protected bool IsValidThreatGroup(byte[,] board, int[] columns, int[] rows, int playerIdx)
        {
            // A group is a valid threat if the opponent can complete it
            // (none of our pieces block it, and at least one square is empty)
            byte opponentPiece = playerIdx == 0 ? (byte)Connect4State.Piece.Red : (byte)Connect4State.Piece.Yellow;
            byte ownPiece = playerIdx == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
            
            bool hasEmptySquare = false;
            
            for (int i = 0; i < 4; i++)
            {
                // If our piece is in this group, it's not a threat
                if (board[columns[i], rows[i]] == ownPiece)
                    return false;
                
                // Check if the square is empty
                if (board[columns[i], rows[i]] == 0)
                    hasEmptySquare = true;
            }
            
            // It's a valid threat if there's at least one empty square
            return hasEmptySquare;
        }
        
        // Get information about which squares are used by this rule
        public abstract HashSet<Vector2Int> GetUsedSquares();
        
        // Get RuleType for interaction checking
        public abstract RuleType GetRuleType();
    }
    
    // Enum to identify rule types for the interaction system
    private enum RuleType
    {
        Claimeven,
        Baseinverse,
        Vertical,
        Aftereven,
        Lowinverse,
        Highinverse,
        Baseclaim,
        Before,
        Specialbefore
    }
    
    // Represents a group of four connected positions that could be a threat
    private class ThreatGroup
    {
        public int[] columns = new int[4];
        public int[] rows = new int[4];
        
        public ThreatGroup(int startX, int startY, int dX, int dY)
        {
            for (int i = 0; i < 4; i++)
            {
                columns[i] = startX + i * dX;
                rows[i] = startY + i * dY;
            }
        }
        
        // Check if this threat group contains the given square
        public bool ContainsSquare(int x, int y)
        {
            for (int i = 0; i < 4; i++)
            {
                if (columns[i] == x && rows[i] == y)
                    return true;
            }
            return false;
        }
        
        // Check if this threat group contains both given squares
        public bool ContainsBothSquares(int x1, int y1, int x2, int y2)
        {
            return ContainsSquare(x1, y1) && ContainsSquare(x2, y2);
        }
    }
    
    // 1. Claimeven - Controller claims an even-numbered square
    private class Claimeven : StrategicRule
    {
        private int column;
        private int oddRow;  // The odd row (directly playable)
        private int evenRow; // The even row above it
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Claimeven(int column, int oddRow, int evenRow)
        {
            this.column = column;
            this.oddRow = oddRow;
            this.evenRow = evenRow;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // Both squares must be empty
            if (board[column, oddRow] != 0 || board[column, evenRow] != 0)
                return false;
                
            // The odd row must be directly playable
            if (oddRow < GameController.numRows - 1 && board[column, oddRow + 1] == 0)
                return false;
                
            // The even row must be directly above the odd row
            if (evenRow != oddRow - 1)
                return false;
                
            // The row number (0-indexed) must match the even/odd description
            // Even rows have y+1 is even, odd rows have y+1 is odd
            if ((oddRow + 1) % 2 != 1 || (evenRow + 1) % 2 != 0)
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Find all potential threat groups that contain the even square
            // Horizontal threats
            for (int startX = System.Math.Max(0, column - 3); startX <= System.Math.Min(GameController.numColumns - 4, column); startX++)
            {
                ThreatGroup group = new ThreatGroup(startX, evenRow, 1, 0);
                if (IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    solvedThreats.Add(group);
            }
            
            // Vertical threats (can only be one)
            if (evenRow >= 3)
            {
                ThreatGroup group = new ThreatGroup(column, evenRow - 3, 0, 1);
                if (IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    solvedThreats.Add(group);
            }
            
            // Diagonal rising threats
            for (int i = 0; i < 4; i++)
            {
                int startX = column - i;
                int startY = evenRow + i;
                
                if (startX >= 0 && startX <= GameController.numColumns - 4 && 
                    startY >= 3 && startY < GameController.numRows)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                        solvedThreats.Add(group);
                }
            }
            
            // Diagonal falling threats
            for (int i = 0; i < 4; i++)
            {
                int startX = column - i;
                int startY = evenRow - i;
                
                if (startX >= 0 && startX <= GameController.numColumns - 4 && 
                    startY >= 0 && startY <= GameController.numRows - 4)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                        solvedThreats.Add(group);
                }
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column, oddRow));
            squares.Add(new Vector2Int(column, evenRow));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Claimeven;
        }
    }
    
    // 2. Baseinverse - Controller claims one of two directly playable squares
    private class Baseinverse : StrategicRule
    {
        private int column1, row1; // First square
        private int column2, row2; // Second square
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Baseinverse(int column1, int row1, int column2, int row2)
        {
            this.column1 = column1;
            this.row1 = row1;
            this.column2 = column2;
            this.row2 = row2;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // Both squares must be empty
            if (board[column1, row1] != 0 || board[column2, row2] != 0)
                return false;
                
            // Both squares must be directly playable
            if ((row1 < GameController.numRows - 1 && board[column1, row1 + 1] == 0) ||
                (row2 < GameController.numRows - 1 && board[column2, row2 + 1] == 0))
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Find all potential threat groups that contain both squares
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(column1, row1, column2, row2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check vertical threats
            for (int x = 0; x < GameController.numColumns; x++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(x, startY, 0, 1);
                    if (group.ContainsBothSquares(column1, row1, column2, row2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(column1, row1, column2, row2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(column1, row1, column2, row2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column1, row1));
            squares.Add(new Vector2Int(column2, row2));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Baseinverse;
        }
    }
    
    // 3. Vertical - Controller claims one of two consecutive squares in a column
    private class Vertical : StrategicRule
    {
        private int column;
        private int lowerRow;
        private int upperRow;
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Vertical(int column, int lowerRow, int upperRow)
        {
            this.column = column;
            this.lowerRow = lowerRow;
            this.upperRow = upperRow;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // Both squares must be empty
            if (board[column, lowerRow] != 0 || board[column, upperRow] != 0)
                return false;
                
            // The upper row must be directly above the lower row
            if (upperRow != lowerRow - 1)
                return false;
                
            // The upper square must be odd (for Vertical; if even, use Claimeven)
            if ((upperRow + 1) % 2 == 0)
                return false;
                
            // The lower row must be directly playable
            if (lowerRow < GameController.numRows - 1 && board[column, lowerRow + 1] == 0)
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Find all potential threat groups that contain both the lower and upper squares
            // Only vertical threats are possible
            if (lowerRow <= GameController.numRows - 4)
            {
                ThreatGroup group = new ThreatGroup(column, lowerRow - 3, 0, 1);
                if (group.ContainsBothSquares(column, lowerRow, column, upperRow) &&
                    IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                {
                    solvedThreats.Add(group);
                }
            }
            
            // Check horizontal threats
            for (int startX = System.Math.Max(0, column - 3); startX <= System.Math.Min(GameController.numColumns - 4, column); startX++)
            {
                // Check lower row
                ThreatGroup group1 = new ThreatGroup(startX, lowerRow, 1, 0);
                if (group1.ContainsBothSquares(column, lowerRow, column, upperRow) &&
                    IsValidThreatGroup(board, group1.columns, group1.rows, playerIdx))
                {
                    solvedThreats.Add(group1);
                }
                
                // Check upper row
                ThreatGroup group2 = new ThreatGroup(startX, upperRow, 1, 0);
                if (group2.ContainsBothSquares(column, lowerRow, column, upperRow) &&
                    IsValidThreatGroup(board, group2.columns, group2.rows, playerIdx))
                {
                    solvedThreats.Add(group2);
                }
            }
            
            // Check diagonal threats - similar pattern, checking if both squares are in the group
            // (Implementation would follow same pattern as horizontal checks above)
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column, lowerRow));
            squares.Add(new Vector2Int(column, upperRow));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Vertical;
        }
    }
    
    // Rule Interaction System as described in Chapter 7 of Allis' thesis
    private class RuleInteractionSystem
    {
        // Matrix defining which rules can be combined (based on Section 7.4)
        private static bool[,] canCombine = new bool[9, 9] {
            // CL  BI  VE  AE  LI  HI  BC  BE  SB
            {  true, true, true, true, false, false, true, true, true },  // CL - Claimeven
            {  true, true, true, true, true, true, true, true, true },    // BI - Baseinverse
            {  true, true, true, true, true, true, true, true, true },    // VE - Vertical
            {  true, true, true, true, false, false, true, true, true },  // AE - Aftereven
            {  false, true, true, false, true, true, false, false, false }, // LI - Lowinverse
            {  false, true, true, false, true, true, false, false, false }, // HI - Highinverse
            {  true, true, true, true, false, false, true, true, true },  // BC - Baseclaim
            {  true, true, true, true, false, false, true, true, true },  // BE - Before
            {  true, true, true, true, false, false, true, true, true }   // SB - Specialbefore
        };
        
        // Check if two rules can be combined according to the compatibility matrix
        public static bool CanCombineRules(StrategicRule rule1, StrategicRule rule2)
        {
            int idx1 = (int)rule1.GetRuleType();
            int idx2 = (int)rule2.GetRuleType();
            
            // First check the basic compatibility matrix
            if (!canCombine[idx1, idx2])
                return false;
            
            // Then check for additional constraints
            switch (rule1.GetRuleType())
            {
                case RuleType.Claimeven:
                    // Claimeven can be combined with all rules except Lowinverse/Highinverse
                    // if their sets of squares are disjoint
                    return HasDisjointSquares(rule1, rule2);
                
                case RuleType.Baseinverse:
                case RuleType.Vertical:
                    // These can be combined with any rule if squares are disjoint
                    return HasDisjointSquares(rule1, rule2);
                
                case RuleType.Aftereven:
                    // Can be combined with Claimeven, Baseinverse, Vertical if squares are disjoint
                    // Can be combined with Before/Specialbefore if column-wise disjoint or equal
                    if (rule2.GetRuleType() == RuleType.Before || rule2.GetRuleType() == RuleType.Specialbefore)
                        return AreColumnWiseDisjointOrEqual(rule1, rule2);
                    return HasDisjointSquares(rule1, rule2);
                
                case RuleType.Lowinverse:
                case RuleType.Highinverse:
                    // These can be combined with Baseinverse/Vertical if squares are disjoint
                    // They can be combined with each other if columns are disjoint or equal
                    if (rule2.GetRuleType() == RuleType.Lowinverse || rule2.GetRuleType() == RuleType.Highinverse)
                        return AreColumnWiseDisjointOrEqual(rule1, rule2);
                    return HasDisjointSquares(rule1, rule2);
                
                case RuleType.Baseclaim:
                case RuleType.Before:
                case RuleType.Specialbefore:
                    // These can combine with Claimeven/Baseinverse/Vertical if squares are disjoint
                    // They can combine with Aftereven if column-wise disjoint or equal
                    if (rule2.GetRuleType() == RuleType.Aftereven)
                        return AreColumnWiseDisjointOrEqual(rule1, rule2);
                    return HasDisjointSquares(rule1, rule2);
            }
            
            return false;
        }
        
        // Check if two rules have disjoint sets of squares
        private static bool HasDisjointSquares(StrategicRule rule1, StrategicRule rule2)
        {
            HashSet<Vector2Int> squares1 = rule1.GetUsedSquares();
            HashSet<Vector2Int> squares2 = rule2.GetUsedSquares();
            
            foreach (Vector2Int square in squares1)
            {
                if (squares2.Contains(square))
                    return false;
            }
            
            return true;
        }
        
        // Check if two rules are column-wise disjoint or equal
        private static bool AreColumnWiseDisjointOrEqual(StrategicRule rule1, StrategicRule rule2)
        {
            HashSet<Vector2Int> squares1 = rule1.GetUsedSquares();
            HashSet<Vector2Int> squares2 = rule2.GetUsedSquares();
            
            HashSet<int> columns1 = new HashSet<int>();
            HashSet<int> columns2 = new HashSet<int>();
            
            foreach (Vector2Int square in squares1)
                columns1.Add(square.x);
                
            foreach (Vector2Int square in squares2)
                columns2.Add(square.x);
            
            // Check if each column is either in both sets or in neither
            foreach (int column in columns1)
            {
                if (columns2.Contains(column))
                {
                    // Column is in both - check if all squares in this column are the same
                    foreach (Vector2Int square1 in squares1)
                    {
                        if (square1.x == column)
                        {
                            bool found = false;
                            foreach (Vector2Int square2 in squares2)
                            {
                                if (square2.x == column && square2.y == square1.y)
                                {
                                    found = true;
                                    break;
                                }
                            }
                            
                            if (!found)
                                return false;
                        }
                    }
                }
            }
            
            return true;
        }
        
        // Find a valid set of rules that solve all threats
        public static List<StrategicRule> FindValidRuleSet(List<StrategicRule> allRules, List<ThreatGroup> threats)
        {
            // Start with an empty rule set
            List<StrategicRule> selectedRules = new List<StrategicRule>();
            
            // Find rules for each threat
            HashSet<ThreatGroup> solvedThreats = new HashSet<ThreatGroup>();
            
            // Keep adding rules until all threats are solved or no more valid rules
            while (solvedThreats.Count < threats.Count)
            {
                StrategicRule bestRule = null;
                int bestRuleScore = 0;
                
                foreach (StrategicRule rule in allRules)
                {
                    // Check if this rule can be combined with all selected rules
                    bool canCombine = true;
                    foreach (StrategicRule selectedRule in selectedRules)
                    {
                        if (!CanCombineRules(rule, selectedRule))
                        {
                            canCombine = false;
                            break;
                        }
                    }
                    
                    if (!canCombine)
                        continue;
                    
                    // Count how many unsolved threats this rule would solve
                    int score = 0;
                    foreach (ThreatGroup threat in rule.GetSolvedThreats())
                    {
                        if (!solvedThreats.Contains(threat))
                            score++;
                    }
                    
                    if (score > bestRuleScore)
                    {
                        bestRuleScore = score;
                        bestRule = rule;
                    }
                }
                
                // If no rule can solve more threats, we're done (or failed)
                if (bestRuleScore == 0)
                    break;
                
                // Add the best rule and mark its threats as solved
                selectedRules.Add(bestRule);
                foreach (ThreatGroup threat in bestRule.GetSolvedThreats())
                {
                    solvedThreats.Add(threat);
                }
            }
            
            // If all threats are solved, return the selected rules
            if (solvedThreats.Count == threats.Count)
                return selectedRules;
                
            // Otherwise, we couldn't find a valid rule set
            return null;
        }
    }
    
    // The remaining rules (Aftereven, Lowinverse, Highinverse, Baseclaim, Before, Specialbefore)
    // follow the same pattern - they check if they can be applied and find all threats they solve
    
    // 4. Aftereven - Controller claims a group that can be completed using only even squares
    private class Aftereven : StrategicRule
    {
        private List<Vector2Int> emptySquares = new List<Vector2Int>(); // Empty squares of the Aftereven group
        private List<int> afterevenColumns = new List<int>(); // Columns containing empty squares
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        private List<Claimeven> claimevens = new List<Claimeven>(); // Claimevens that are part of this Aftereven
        
        public Aftereven(ThreatGroup group, List<Claimeven> relevantClaimevens, Connect4State state, int playerIdx)
        {
            // Find all empty squares in the group that can be claimed with Claimeven
            byte[,] board = state.GetBoard();
            byte playerPiece = playerIdx == 0 ? (byte)Connect4State.Piece.Yellow : (byte)Connect4State.Piece.Red;
            
            for (int i = 0; i < 4; i++)
            {
                int x = group.columns[i];
                int y = group.rows[i];
                
                // If the square is empty and can be claimed with a Claimeven
                if (board[x, y] == 0)
                {
                    // Check if this square is part of a Claimeven
                    foreach (Claimeven claimeven in relevantClaimevens)
                    {
                        HashSet<Vector2Int> claimedSquares = claimeven.GetUsedSquares();
                        if (claimedSquares.Contains(new Vector2Int(x, y)) && (y + 1) % 2 == 0) // Must be even square
                        {
                            emptySquares.Add(new Vector2Int(x, y));
                            if (!afterevenColumns.Contains(x))
                                afterevenColumns.Add(x);
                            
                            claimevens.Add(claimeven);
                            break;
                        }
                    }
                }
            }
            
            FindSolvedThreats(state, playerIdx);
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            // Can apply if there are empty squares that can be claimed with Claimeven
            // and these would complete a group for the controller
            return emptySquares.Count > 0 && solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // First, add all threats solved by the individual Claimevens
            foreach (Claimeven claimeven in claimevens)
            {
                solvedThreats.AddRange(claimeven.GetSolvedThreats());
            }
            
            // Then add threats that require squares in all Aftereven columns
            // above the empty squares of the Aftereven group
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 0); // Horizontal
                    CheckAftereven(group, board, playerIdx);
                }
            }
            
            for (int startX = 0; startX < GameController.numColumns; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 0, 1); // Vertical
                    CheckAftereven(group, board, playerIdx);
                }
            }
            
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1); // Rising diagonal
                    CheckAftereven(group, board, playerIdx);
                }
            }
            
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1); // Falling diagonal
                    CheckAftereven(group, board, playerIdx);
                }
            }
        }
        
        private void CheckAftereven(ThreatGroup group, byte[,] board, int playerIdx)
        {
            // Check if this group contains squares in all Aftereven columns
            // above the empty squares of the Aftereven group
            bool containsAllColumns = true;
            
            foreach (int column in afterevenColumns)
            {
                bool containsColumn = false;
                
                // Find the highest empty square in this column that's part of the Aftereven
                int maxRow = -1;
                foreach (Vector2Int square in emptySquares)
                {
                    if (square.x == column && square.y > maxRow)
                        maxRow = square.y;
                }
                
                // Check if the group contains a square in this column above maxRow
                for (int i = 0; i < 4; i++)
                {
                    if (group.columns[i] == column && group.rows[i] < maxRow)
                    {
                        containsColumn = true;
                        break;
                    }
                }
                
                if (!containsColumn)
                {
                    containsAllColumns = false;
                    break;
                }
            }
            
            // If the group contains squares in all Aftereven columns above the empty squares,
            // check if it's a valid threat
            if (containsAllColumns && IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
            {
                solvedThreats.Add(group);
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            
            // Add all squares from the component Claimevens
            foreach (Claimeven claimeven in claimevens)
            {
                foreach (Vector2Int square in claimeven.GetUsedSquares())
                {
                    squares.Add(square);
                }
            }
            
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Aftereven;
        }
    }
    
    // 5. Lowinverse - Controller claims one of two odd squares in different columns
    private class Lowinverse : StrategicRule
    {
        private int column1, lowerRow1, upperRow1; // First column
        private int column2, lowerRow2, upperRow2; // Second column
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Lowinverse(int column1, int lowerRow1, int upperRow1, int column2, int lowerRow2, int upperRow2)
        {
            this.column1 = column1;
            this.lowerRow1 = lowerRow1;
            this.upperRow1 = upperRow1;
            this.column2 = column2;
            this.lowerRow2 = lowerRow2;
            this.upperRow2 = upperRow2;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // All four squares must be empty
            if (board[column1, lowerRow1] != 0 || board[column1, upperRow1] != 0 ||
                board[column2, lowerRow2] != 0 || board[column2, upperRow2] != 0)
                return false;
                
            // The upper squares must be directly above the lower squares
            if (upperRow1 != lowerRow1 - 1 || upperRow2 != lowerRow2 - 1)
                return false;
                
            // Both upper squares must be odd
            if ((upperRow1 + 1) % 2 != 1 || (upperRow2 + 1) % 2 != 1)
                return false;
                
            // The columns must be different
            if (column1 == column2)
                return false;
                
            // The lower squares must be directly playable
            if ((lowerRow1 < GameController.numRows - 1 && board[column1, lowerRow1 + 1] == 0) ||
                (lowerRow2 < GameController.numRows - 1 && board[column2, lowerRow2 + 1] == 0))
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Find all potential threat groups that contain both upper squares (main goal of Lowinverse)
            FindThreatsWithBothSquares(board, column1, upperRow1, column2, upperRow2, playerIdx);
            
            // Add vertical threats in first column
            FindVerticalThreats(board, column1, lowerRow1, upperRow1, playerIdx);
            
            // Add vertical threats in second column
            FindVerticalThreats(board, column2, lowerRow2, upperRow2, playerIdx);
        }
        
        private void FindThreatsWithBothSquares(byte[,] board, int x1, int y1, int x2, int y2, int playerIdx)
        {
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        private void FindVerticalThreats(byte[,] board, int column, int lowerRow, int upperRow, int playerIdx)
        {
            // Check vertical threats that contain both squares in this column
            if (lowerRow <= GameController.numRows - 4)
            {
                ThreatGroup group = new ThreatGroup(column, lowerRow - 3, 0, 1);
                if (group.ContainsBothSquares(column, lowerRow, column, upperRow) &&
                    IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                {
                    solvedThreats.Add(group);
                }
            }
            
            // Check horizontal threats for each of the two squares
            // Lower square horizontal threats
            for (int startX = System.Math.Max(0, column - 3); startX <= System.Math.Min(GameController.numColumns - 4, column); startX++)
            {
                ThreatGroup group = new ThreatGroup(startX, lowerRow, 1, 0);
                if (group.ContainsSquare(column, lowerRow) &&
                    group.ContainsSquare(column, upperRow) &&
                    IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                {
                    solvedThreats.Add(group);
                }
            }
            
            // Upper square horizontal threats
            for (int startX = System.Math.Max(0, column - 3); startX <= System.Math.Min(GameController.numColumns - 4, column); startX++)
            {
                ThreatGroup group = new ThreatGroup(startX, upperRow, 1, 0);
                if (group.ContainsSquare(column, lowerRow) &&
                    group.ContainsSquare(column, upperRow) &&
                    IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                {
                    solvedThreats.Add(group);
                }
            }
            
            // Similar checks would be done for diagonal threats containing both squares
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column1, lowerRow1));
            squares.Add(new Vector2Int(column1, upperRow1));
            squares.Add(new Vector2Int(column2, lowerRow2));
            squares.Add(new Vector2Int(column2, upperRow2));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Lowinverse;
        }
    }
    
    // 6. Highinverse - Extension of Lowinverse that includes the even squares above
    private class Highinverse : StrategicRule
    {
        private int column1, lowerRow1, middleRow1, upperRow1; // First column (3 squares)
        private int column2, lowerRow2, middleRow2, upperRow2; // Second column (3 squares)
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Highinverse(int column1, int lowerRow1, int middleRow1, int upperRow1, 
                           int column2, int lowerRow2, int middleRow2, int upperRow2)
        {
            this.column1 = column1;
            this.lowerRow1 = lowerRow1;
            this.middleRow1 = middleRow1;
            this.upperRow1 = upperRow1;
            this.column2 = column2;
            this.lowerRow2 = lowerRow2;
            this.middleRow2 = middleRow2;
            this.upperRow2 = upperRow2;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // All six squares must be empty
            if (board[column1, lowerRow1] != 0 || board[column1, middleRow1] != 0 || board[column1, upperRow1] != 0 ||
                board[column2, lowerRow2] != 0 || board[column2, middleRow2] != 0 || board[column2, upperRow2] != 0)
                return false;
                
            // The middle squares must be directly above the lower squares
            if (middleRow1 != lowerRow1 - 1 || middleRow2 != lowerRow2 - 1)
                return false;
                
            // The upper squares must be directly above the middle squares
            if (upperRow1 != middleRow1 - 1 || upperRow2 != middleRow2 - 1)
                return false;
                
            // Both middle squares must be odd
            if ((middleRow1 + 1) % 2 != 1 || (middleRow2 + 1) % 2 != 1)
                return false;
                
            // Both upper squares must be even
            if ((upperRow1 + 1) % 2 != 0 || (upperRow2 + 1) % 2 != 0)
                return false;
                
            // The columns must be different
            if (column1 == column2)
                return false;
                
            // The lower squares must be directly playable
            if ((lowerRow1 < GameController.numRows - 1 && board[column1, lowerRow1 + 1] == 0) ||
                (lowerRow2 < GameController.numRows - 1 && board[column2, lowerRow2 + 1] == 0))
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Main goals of Highinverse:
            // 1. Groups containing both middle squares
            FindThreatsWithBothSquares(board, column1, middleRow1, column2, middleRow2, playerIdx);
            
            // 2. Groups containing both upper squares
            FindThreatsWithBothSquares(board, column1, upperRow1, column2, upperRow2, playerIdx);
            
            // 3. Vertical threats in first column
            FindVerticalThreats(board, column1, lowerRow1, middleRow1, upperRow1, playerIdx);
            
            // 4. Vertical threats in second column
            FindVerticalThreats(board, column2, lowerRow2, middleRow2, upperRow2, playerIdx);
            
            // 5. Special case: If lower square of first column is directly playable,
            // solve groups with both lower square of first column and upper square of second column
            if (lowerRow1 == GameController.numRows - 1 || board[column1, lowerRow1 + 1] != 0)
            {
                FindThreatsWithBothSquares(board, column1, lowerRow1, column2, upperRow2, playerIdx);
            }
            
            // 6. Special case: If lower square of second column is directly playable,
            // solve groups with both lower square of second column and upper square of first column
            if (lowerRow2 == GameController.numRows - 1 || board[column2, lowerRow2 + 1] != 0)
            {
                FindThreatsWithBothSquares(board, column2, lowerRow2, column1, upperRow1, playerIdx);
            }
        }
        
        private void FindThreatsWithBothSquares(byte[,] board, int x1, int y1, int x2, int y2, int playerIdx)
        {
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        private void FindVerticalThreats(byte[,] board, int column, int lowerRow, int middleRow, int upperRow, int playerIdx)
        {
            // Check vertical threats that contain consecutive squares in this column
            if (lowerRow <= GameController.numRows - 4)
            {
                ThreatGroup group = new ThreatGroup(column, lowerRow - 3, 0, 1);
                // Check if the vertical group contains the relevant pairs
                if ((group.ContainsBothSquares(column, lowerRow, column, middleRow) ||
                     group.ContainsBothSquares(column, middleRow, column, upperRow)) &&
                    IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                {
                    solvedThreats.Add(group);
                }
            }
            
            // Check horizontal and diagonal threats for pairs of squares
            // This is a simplified check - a more thorough implementation would check
            // for all possible combinations of squares in the column
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column1, lowerRow1));
            squares.Add(new Vector2Int(column1, middleRow1));
            squares.Add(new Vector2Int(column1, upperRow1));
            squares.Add(new Vector2Int(column2, lowerRow2));
            squares.Add(new Vector2Int(column2, middleRow2));
            squares.Add(new Vector2Int(column2, upperRow2));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Highinverse;
        }
    }
    
    // 7. Baseclaim - Combination of two Baseinverses and a Claimeven
    private class Baseclaim : StrategicRule
    {
        private int playable1Column, playable1Row; // First playable square
        private int playable2Column, playable2Row; // Second playable square (with non-playable square above)
        private int playable3Column, playable3Row; // Third playable square
        private int nonPlayableColumn, nonPlayableRow; // Non-playable even square above second playable square
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Baseclaim(int playable1Column, int playable1Row, 
                        int playable2Column, int playable2Row, int nonPlayableColumn, int nonPlayableRow,
                        int playable3Column, int playable3Row)
        {
            this.playable1Column = playable1Column;
            this.playable1Row = playable1Row;
            this.playable2Column = playable2Column;
            this.playable2Row = playable2Row;
            this.nonPlayableColumn = nonPlayableColumn;
            this.nonPlayableRow = nonPlayableRow;
            this.playable3Column = playable3Column;
            this.playable3Row = playable3Row;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // All four squares must be empty
            if (board[playable1Column, playable1Row] != 0 || 
                board[playable2Column, playable2Row] != 0 ||
                board[nonPlayableColumn, nonPlayableRow] != 0 ||
                board[playable3Column, playable3Row] != 0)
                return false;
                
            // Three squares must be directly playable
            if ((playable1Row < GameController.numRows - 1 && board[playable1Column, playable1Row + 1] == 0) ||
                (playable2Row < GameController.numRows - 1 && board[playable2Column, playable2Row + 1] == 0) ||
                (playable3Row < GameController.numRows - 1 && board[playable3Column, playable3Row + 1] == 0))
                return false;
                
            // The non-playable square must be directly above the second playable square
            if (nonPlayableColumn != playable2Column || nonPlayableRow != playable2Row - 1)
                return false;
                
            // The non-playable square must be even
            if ((nonPlayableRow + 1) % 2 != 0)
                return false;
                
            // The columns must form the correct pattern for Baseclaim
            if (playable2Column == playable3Column)
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Baseclaim solves two types of threats:
            // 1. Threats containing both the first playable square and the non-playable square
            FindThreatsWithBothSquares(board, playable1Column, playable1Row, nonPlayableColumn, nonPlayableRow, playerIdx);
            
            // 2. Threats containing both the second and third playable squares
            FindThreatsWithBothSquares(board, playable2Column, playable2Row, playable3Column, playable3Row, playerIdx);
        }
        
        private void FindThreatsWithBothSquares(byte[,] board, int x1, int y1, int x2, int y2, int playerIdx)
        {
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check vertical threats
            for (int x = 0; x < GameController.numColumns; x++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(x, startY, 0, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(playable1Column, playable1Row));
            squares.Add(new Vector2Int(playable2Column, playable2Row));
            squares.Add(new Vector2Int(nonPlayableColumn, nonPlayableRow));
            squares.Add(new Vector2Int(playable3Column, playable3Row));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Baseclaim;
        }
    }
    
    // 8. Before - Combination of Claimevens and Verticals organized to handle threats in a specific order
    private class Before : StrategicRule
    {
        private int column1, oddRow1, evenRow1; // First Claimeven or Vertical
        private int column2, oddRow2, evenRow2; // Second Claimeven or Vertical
        private bool firstIsClaimeven; // Whether the first pair is Claimeven or Vertical
        private bool secondIsClaimeven; // Whether the second pair is Claimeven or Vertical
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Before(int column1, int oddRow1, int evenRow1, bool firstIsClaimeven,
                     int column2, int oddRow2, int evenRow2, bool secondIsClaimeven)
        {
            this.column1 = column1;
            this.oddRow1 = oddRow1;
            this.evenRow1 = evenRow1;
            this.firstIsClaimeven = firstIsClaimeven;
            this.column2 = column2;
            this.oddRow2 = oddRow2;
            this.evenRow2 = evenRow2;
            this.secondIsClaimeven = secondIsClaimeven;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // All four squares must be empty
            if (board[column1, oddRow1] != 0 || board[column1, evenRow1] != 0 ||
                board[column2, oddRow2] != 0 || board[column2, evenRow2] != 0)
                return false;
                
            // The rows must be adjacent pairs
            if (evenRow1 != oddRow1 - 1 || evenRow2 != oddRow2 - 1)
                return false;
                
            // Verify row parity for Claimeven and Vertical
            if (firstIsClaimeven)
            {
                // For Claimeven: oddRow must be odd, evenRow must be even
                if ((oddRow1 + 1) % 2 != 1 || (evenRow1 + 1) % 2 != 0)
                    return false;
            }
            else
            {
                // For Vertical: oddRow must be odd, evenRow must be odd
                if ((oddRow1 + 1) % 2 != 1 || (evenRow1 + 1) % 2 != 1)
                    return false;
            }
            
            if (secondIsClaimeven)
            {
                // For Claimeven: oddRow must be odd, evenRow must be even
                if ((oddRow2 + 1) % 2 != 1 || (evenRow2 + 1) % 2 != 0)
                    return false;
            }
            else
            {
                // For Vertical: oddRow must be odd, evenRow must be odd
                if ((oddRow2 + 1) % 2 != 1 || (evenRow2 + 1) % 2 != 1)
                    return false;
            }
            
            // The odd rows must be directly playable
            if ((oddRow1 < GameController.numRows - 1 && board[column1, oddRow1 + 1] == 0) ||
                (oddRow2 < GameController.numRows - 1 && board[column2, oddRow2 + 1] == 0))
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // A Before rule involves two combinations: first and second
            // First, add threats solved by the first combination
            if (firstIsClaimeven)
            {
                Claimeven claimeven = new Claimeven(column1, oddRow1, evenRow1);
                if (claimeven.CanApply(state, playerIdx))
                    solvedThreats.AddRange(claimeven.GetSolvedThreats());
            }
            else
            {
                Vertical vertical = new Vertical(column1, oddRow1, evenRow1);
                if (vertical.CanApply(state, playerIdx))
                    solvedThreats.AddRange(vertical.GetSolvedThreats());
            }
            
            // Next, add threats solved by the second combination
            if (secondIsClaimeven)
            {
                Claimeven claimeven = new Claimeven(column2, oddRow2, evenRow2);
                if (claimeven.CanApply(state, playerIdx))
                    solvedThreats.AddRange(claimeven.GetSolvedThreats());
            }
            else
            {
                Vertical vertical = new Vertical(column2, oddRow2, evenRow2);
                if (vertical.CanApply(state, playerIdx))
                    solvedThreats.AddRange(vertical.GetSolvedThreats());
            }
            
            // Additionally, the Before rule solves threats between specific
            // squares from the first and second combinations, particularly those
            // that form a diagonal, horizontal or vertical aligned threat
            
            // For example, check for threats between the even squares of both combinations
            FindThreatsWithBothSquares(board, column1, evenRow1, column2, evenRow2, playerIdx);
            
            // And check for threats between the odd squares of both combinations
            FindThreatsWithBothSquares(board, column1, oddRow1, column2, oddRow2, playerIdx);
        }
        
        private void FindThreatsWithBothSquares(byte[,] board, int x1, int y1, int x2, int y2, int playerIdx)
        {
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column1, oddRow1));
            squares.Add(new Vector2Int(column1, evenRow1));
            squares.Add(new Vector2Int(column2, oddRow2));
            squares.Add(new Vector2Int(column2, evenRow2));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Before;
        }
    }
    
    // 9. Specialbefore - A variant of Before with a directly playable square that requires specific handling
    private class Specialbefore : StrategicRule
    {
        private int column1, oddRow1, evenRow1; // First Claimeven
        private int column2, directRow2; // Second directly playable square (no square above)
        private List<ThreatGroup> solvedThreats = new List<ThreatGroup>();
        
        public Specialbefore(int column1, int oddRow1, int evenRow1, int column2, int directRow2)
        {
            this.column1 = column1;
            this.oddRow1 = oddRow1;
            this.evenRow1 = evenRow1;
            this.column2 = column2;
            this.directRow2 = directRow2;
        }
        
        public override bool CanApply(Connect4State state, int playerIdx)
        {
            byte[,] board = state.GetBoard();
            
            // All three squares must be empty
            if (board[column1, oddRow1] != 0 || board[column1, evenRow1] != 0 ||
                board[column2, directRow2] != 0)
                return false;
                
            // The rows in the first column must be adjacent
            if (evenRow1 != oddRow1 - 1)
                return false;
                
            // Verify row parity for Claimeven in first column
            if ((oddRow1 + 1) % 2 != 1 || (evenRow1 + 1) % 2 != 0)
                return false;
                
            // The odd row must be directly playable
            if (oddRow1 < GameController.numRows - 1 && board[column1, oddRow1 + 1] == 0)
                return false;
                
            // The direct row must be directly playable
            if (directRow2 < GameController.numRows - 1 && board[column2, directRow2 + 1] == 0)
                return false;
                
            // Special restriction: the direct row must be at the top of its column
            if (directRow2 > 0 && board[column2, directRow2 - 1] != 0)
                return false;
                
            FindSolvedThreats(state, playerIdx);
            return solvedThreats.Count > 0;
        }
        
        private void FindSolvedThreats(Connect4State state, int playerIdx)
        {
            solvedThreats.Clear();
            byte[,] board = state.GetBoard();
            
            // Add threats solved by the Claimeven in first column
            Claimeven claimeven = new Claimeven(column1, oddRow1, evenRow1);
            if (claimeven.CanApply(state, playerIdx))
                solvedThreats.AddRange(claimeven.GetSolvedThreats());
            
            // Add threats involving the direct playable square and the even square
            FindThreatsWithBothSquares(board, column1, evenRow1, column2, directRow2, playerIdx);
            
            // Add threats involving the direct playable square and the odd square
            FindThreatsWithBothSquares(board, column1, oddRow1, column2, directRow2, playerIdx);
        }
        
        private void FindThreatsWithBothSquares(byte[,] board, int x1, int y1, int x2, int y2, int playerIdx)
        {
            // Check horizontal threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int y = 0; y < GameController.numRows; y++)
                {
                    ThreatGroup group = new ThreatGroup(startX, y, 1, 0);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal rising threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 3; startY < GameController.numRows; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, -1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
            
            // Check diagonal falling threats
            for (int startX = 0; startX <= GameController.numColumns - 4; startX++)
            {
                for (int startY = 0; startY <= GameController.numRows - 4; startY++)
                {
                    ThreatGroup group = new ThreatGroup(startX, startY, 1, 1);
                    if (group.ContainsBothSquares(x1, y1, x2, y2) &&
                        IsValidThreatGroup(board, group.columns, group.rows, playerIdx))
                    {
                        solvedThreats.Add(group);
                    }
                }
            }
        }
        
        public override List<ThreatGroup> GetSolvedThreats()
        {
            return solvedThreats;
        }
        
        public override HashSet<Vector2Int> GetUsedSquares()
        {
            HashSet<Vector2Int> squares = new HashSet<Vector2Int>();
            squares.Add(new Vector2Int(column1, oddRow1));
            squares.Add(new Vector2Int(column1, evenRow1));
            squares.Add(new Vector2Int(column2, directRow2));
            return squares;
        }
        
        public override RuleType GetRuleType()
        {
            return RuleType.Specialbefore;
        }
    }
    
    // Evaluate position with the enhanced strategic rule system from Allis's thesis
    private float EvaluateStrategyBased(Connect4State state)
    {
        float score = 0;
        int myPlayer = state.GetPlayerTurn();
        int opponent = 1 - myPlayer;
        
        // 1. First, evaluate Zugzwang control (crucial in Allis's thesis)
        bool iControlZugzwang = ZugzwangControl.DetermineZugzwangControl(state, myPlayer);
        if (iControlZugzwang)
        {
            score += 50; // Controlling Zugzwang is a significant advantage
        }
        else
        {
            score -= 50; // Not controlling Zugzwang is a disadvantage
        }
        
        // 2. Evaluate threats for both players
        List<ThreatSequence> myThreats = DetectComplexThreats(state, myPlayer);
        List<ThreatSequence> opponentThreats = DetectComplexThreats(state, opponent);
        
        // My immediate winning threats
        int myWinningThreats = myThreats.Count(t => t.type == ThreatType.ThreeInARow || t.type == ThreatType.FourInARow);
        if (myWinningThreats > 0)
            score += 1000 * myWinningThreats;
            
        // Opponent's immediate winning threats - these are critically important to block
        int opponentWinningThreats = opponentThreats.Count(t => t.type == ThreatType.ThreeInARow || t.type == ThreatType.FourInARow);
        if (opponentWinningThreats > 0)
            score -= 1000 * opponentWinningThreats;
            
        // My even threats (require even number of moves)
        int myEvenThreats = myThreats.Count(t => t.type == ThreatType.EvenThreat);
        if (myEvenThreats > 0)
            score += 200 * myEvenThreats;
            
        // Opponent's even threats
        int opponentEvenThreats = opponentThreats.Count(t => t.type == ThreatType.EvenThreat);
        if (opponentEvenThreats > 0)
            score -= 200 * opponentEvenThreats;
            
        // My odd threats - important when controlling Zugzwang
        int myOddThreats = myThreats.Count(t => t.type == ThreatType.OddThreat);
        if (myOddThreats > 0 && iControlZugzwang)
            score += 150 * myOddThreats;
            
        // Opponent's odd threats - important when they control Zugzwang
        int opponentOddThreats = opponentThreats.Count(t => t.type == ThreatType.OddThreat);
        if (opponentOddThreats > 0 && !iControlZugzwang)
            score -= 150 * opponentOddThreats;
            
        // Base threats (potential future threats)
        int myBaseThreats = myThreats.Count(t => t.type == ThreatType.BaseThreat);
        if (myBaseThreats > 0)
            score += 50 * myBaseThreats;
            
        int opponentBaseThreats = opponentThreats.Count(t => t.type == ThreatType.BaseThreat);
        if (opponentBaseThreats > 0)
            score -= 50 * opponentBaseThreats;
        
        // 3. Evaluate strategic rules
        List<StrategicRule> myRules = FindApplicableRules(state, myPlayer);
        List<StrategicRule> opponentRules = FindApplicableRules(state, opponent);
        
        // Score based on rule types (Allis's thesis section 7.4 explains the relative importance)
        foreach (StrategicRule rule in myRules)
        {
            switch(rule.GetRuleType())
            {
                case RuleType.Claimeven:
                    score += 100;
                    break;
                case RuleType.Baseinverse:
                    score += 80;
                    break;
                case RuleType.Vertical:
                    score += 80;
                    break;
                case RuleType.Aftereven:
                    score += 120;
                    break;
                case RuleType.Lowinverse:
                    score += 70;
                    break;
                case RuleType.Highinverse:
                    score += 90;
                    break;
                case RuleType.Baseclaim:
                    score += 110;
                    break;
                case RuleType.Before:
                    score += 130;
                    break;
                case RuleType.Specialbefore:
                    score += 140;
                    break;
            }
        }
        
        foreach (StrategicRule rule in opponentRules)
        {
            switch(rule.GetRuleType())
            {
                case RuleType.Claimeven:
                    score -= 100;
                    break;
                case RuleType.Baseinverse:
                    score -= 80;
                    break;
                case RuleType.Vertical:
                    score -= 80;
                    break;
                case RuleType.Aftereven:
                    score -= 120;
                    break;
                case RuleType.Lowinverse:
                    score -= 70;
                    break;
                case RuleType.Highinverse:
                    score -= 90;
                    break;
                case RuleType.Baseclaim:
                    score -= 110;
                    break;
                case RuleType.Before:
                    score -= 130;
                    break;
                case RuleType.Specialbefore:
                    score -= 140;
                    break;
            }
        }
        
        // 4. Incorporate rule interactions - check if valid rule combinations exist (Section 7.3)
        List<ThreatGroup> allThreats = FindAllThreats(state, opponent);
        List<StrategicRule> validRuleSet = RuleInteractionSystem.FindValidRuleSet(myRules, allThreats);
        
        if (validRuleSet != null && validRuleSet.Count > 0)
        {
            // Having a valid rule set that solves all threats is a huge advantage
            score += 500;
            
            // More comprehensive rule sets get higher scores
            score += 50 * validRuleSet.Count;
        }
        
        // Opponent's valid rule set
        List<ThreatGroup> myThreatsAsGroups = FindAllThreats(state, myPlayer);
        List<StrategicRule> opponentValidRuleSet = RuleInteractionSystem.FindValidRuleSet(opponentRules, myThreatsAsGroups);
        
        if (opponentValidRuleSet != null && opponentValidRuleSet.Count > 0)
        {
            // Opponent having a valid rule set is a disadvantage
            score -= 500;
            
            // More comprehensive rule sets get higher negative scores
            score -= 50 * opponentValidRuleSet.Count;
        }
        
        return score;
    }
} 