using UnityEngine;
using System.Collections.Generic;

public class MCTSAgent : Agent
{
    public int totalSims = 2500;
    public float c = Mathf.Sqrt(2.0f);

    // Node class for the MCTS tree
    private class MCTSNode
    {
        public Connect4State state;
        public int move; // The move that led to this state
        public MCTSNode parent;
        public List<MCTSNode> children;
        public int visits;
        public float value;
        public int playerIdx; // Which player's turn it is (0 for Yellow, 1 for Red)

        public MCTSNode(Connect4State state, int move = -1, MCTSNode parent = null)
        {
            this.state = state;
            this.move = move;
            this.parent = parent;
            this.children = new List<MCTSNode>();
            this.visits = 0;
            this.value = 0;
            this.playerIdx = state.GetPlayerTurn();
        }

        // Returns true if all possible moves from this state have been expanded
        public bool IsFullyExpanded()
        {
            List<int> possibleMoves = state.GetPossibleMoves();
            return children.Count == possibleMoves.Count;
        }

        // Returns true if this is a terminal state (game over)
        public bool IsTerminal()
        {
            return state.GetResult() != Connect4State.Result.Undecided;
        }

        // Get the moves that haven't been tried yet
        public List<int> GetUntriedMoves()
        {
            List<int> allMoves = state.GetPossibleMoves();
            List<int> triedMoves = new List<int>();

            foreach (MCTSNode child in children)
            {
                triedMoves.Add(child.move);
            }

            List<int> untriedMoves = new List<int>();
            foreach (int move in allMoves)
            {
                if (!triedMoves.Contains(move))
                    untriedMoves.Add(move);
            }

            return untriedMoves;
        }

        // UCB1 formula for selection - higher value is always better for the current player
        public float UCB1(float c)
        {
            if (visits == 0)
                return float.MaxValue; // Prioritize unexplored nodes

            // Exploitation term - win rate from current player's perspective
            float exploitationTerm = value / visits;

            // Exploration term
            float explorationTerm = c * Mathf.Sqrt(Mathf.Log(parent.visits) / visits);

            // UCB1 formula: exploitation + exploration
            return exploitationTerm + explorationTerm;
        }
    }

    public override int GetMove(Connect4State state)
    {
        // Create root node with the current state
        MCTSNode root = new MCTSNode(state.Clone());

        // Run MCTS for the specified number of simulations
        for (int i = 0; i < totalSims; i++)
        {
            // 1. Selection phase - find the most promising leaf node
            MCTSNode selectedNode = Selection(root);

            // 2. Expansion phase - if not terminal and not fully expanded
            if (!selectedNode.IsTerminal() && !selectedNode.IsFullyExpanded())
            {
                selectedNode = Expansion(selectedNode);
            }

            // 3. Simulation phase - simulate a random playout
            float simulationResult = Simulation(selectedNode);

            // 4. Backpropagation phase - update the statistics
            Backpropagation(selectedNode, simulationResult);
        }

        // Choose the best move based on visit count (most robust option)
        return BestMove(root);
    }

    // Selection phase: Using UCB1 to select the most promising node
    private MCTSNode Selection(MCTSNode node)
    {
        while (!node.IsTerminal() && node.IsFullyExpanded())
        {
            // Find the child with the highest UCB1 value
            MCTSNode bestChild = null;
            float bestScore = float.MinValue;

            foreach (MCTSNode child in node.children)
            {
                float score = child.UCB1(c);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestChild = child;
                }
            }

            if (bestChild == null)
                break;

            node = bestChild;
        }

        return node;
    }

    // Expansion phase: Add a new child node for an untried move
    private MCTSNode Expansion(MCTSNode node)
    {
        List<int> untriedMoves = node.GetUntriedMoves();

        if (untriedMoves.Count == 0)
            return node;

        // Choose a random untried move
        int move = untriedMoves[Random.Range(0, untriedMoves.Count)];

        // Create a new state by applying the move
        Connect4State newState = node.state.Clone();
        newState.MakeMove(move);

        // Create a new child node
        MCTSNode newNode = new MCTSNode(newState, move, node);
        node.children.Add(newNode);

        return newNode;
    }

    // Simulation phase: Play random moves until game ends
    private float Simulation(MCTSNode node)
    {
        Connect4State state = node.state.Clone();

        // Continue until terminal state
        while (state.GetResult() == Connect4State.Result.Undecided)
        {
            List<int> moves = state.GetPossibleMoves();
            if (moves.Count == 0)
                break;

            // Choose random move
            int randomMove = moves[Random.Range(0, moves.Count)];
            state.MakeMove(randomMove);
        }

        // Get result and convert to score from perspective of node's player
        Connect4State.Result result = state.GetResult();
        float rawScore = Connect4State.ResultToFloat(result);

        // Convert score to node player's perspective
        // Yellow player (0) wants score closer to 0 (yellow win)
        // Red player (1) wants score closer to 1 (red win)
        if (node.playerIdx == 0) // Yellow's perspective
            return 1.0f - rawScore;
        else // Red's perspective
            return rawScore;
    }

    // Backpropagation: Update statistics for all nodes in the path
    private void Backpropagation(MCTSNode node, float result)
    {
        while (node != null)
        {
            node.visits++;

            // If this node represents a different player than the simulation node,
            // invert the result to represent this player's perspective
            if (node.parent != null && node.playerIdx != node.parent.playerIdx)
                result = 1.0f - result;

            node.value += result;
            node = node.parent;
        }
    }

    // Choose the best move based on most visits
    private int BestMove(MCTSNode root)
    {
        int bestMove = -1;
        int mostVisits = -1;

        foreach (MCTSNode child in root.children)
        {
            if (child.visits > mostVisits)
            {
                mostVisits = child.visits;
                bestMove = child.move;
            }
        }

        // Fallback if no moves explored (shouldn't happen with enough simulations)
        if (bestMove == -1)
        {
            List<int> possibleMoves = root.state.GetPossibleMoves();
            bestMove = possibleMoves[Random.Range(0, possibleMoves.Count)];
        }

        return bestMove;
    }
}