using UnityEngine;
using System.Collections.Generic;

public class RandomAgent : Agent
{
    public override int GetMove(Connect4State state)
    {
        List<int> moves = state.GetPossibleMoves();
        return moves[Random.Range(0, moves.Count)];
    }
}
