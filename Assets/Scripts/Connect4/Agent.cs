using UnityEngine;
using System.Linq;

public abstract class Agent : MonoBehaviour
{
    protected int playerIdx;

    public void setPlayerIdx(int _playerIdx)
    {
        playerIdx = _playerIdx;
    }

    public abstract int GetMove(Connect4State state);

    // Helper functions that you may find useful.
    public int argMin(float[] arr)
    {
        return Enumerable.Range(0, arr.Length).Aggregate((a, b) => (arr[a] < arr[b]) ? a : b);
    }

    public int argMax(float[] arr)
    {
        return Enumerable.Range(0, arr.Length).Aggregate((a, b) => (arr[a] > arr[b]) ? a : b);
    }
}
