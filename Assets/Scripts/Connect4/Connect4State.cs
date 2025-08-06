using UnityEngine;
using System.Collections.Generic;
using ConnectFour;

public class Connect4State
{
	public enum Piece
	{
		Empty = 0,
		Yellow = 1,
		Red = 2
	}

	public enum Result
	{
		Undecided = 0,
		YellowWin = 1,
		RedWin = 2,
		Draw = 3
	}

	// Convert a match result into a numerical score.
	// Yellow win = 0, red win = 1, draw = 0.5.
	public static float ResultToFloat(Result r)
    {
		if (r == Result.YellowWin)
		{
			return 0.0f;
		}
		else if (r == Result.RedWin)
		{
			return 1.0f;
		}
		else
		{
			return 0.5f;
		}
	}

	// Using bytes and bools to reduce the memory footprint of each state for MCTS.
	private byte[,] field;
	private bool isYellowsTurn;

	public Connect4State()
    {
		// Randomise who moves first.
		isYellowsTurn = (Random.value > 0.5f);

		field = new byte[GameController.numColumns, GameController.numRows];
		for (int x = 0; x < GameController.numColumns; x++)
		{
			for (int y = 0; y < GameController.numRows; y++)
			{
				field[x, y] = (int)Piece.Empty;
			}
		}
	}

	// Extra constructor for cloning.
	public Connect4State(byte[,] _field, bool _isYellowsTurn)
	{
		isYellowsTurn = _isYellowsTurn;
		field = (byte[,])_field.Clone();
	}

	// Return a copy of the state (useful for simulation).
	public Connect4State Clone()
    {
		return new Connect4State(field, isYellowsTurn);
	}

	// MakeMove updates the board and returns the row index where the piece was placed.
	public int MakeMove(int column)
    {
		int rowPlaced = -1;

		for (int i = GameController.numRows - 1; i >= 0; i--)
		{
			if (field[column, i] == 0)
			{
				rowPlaced = i;
				field[column, i] = isYellowsTurn ? (byte)Piece.Yellow : (byte)Piece.Red;
				break;
			}
		}

		if (rowPlaced >= 0)
		{
			isYellowsTurn = !isYellowsTurn;
		}
        else
        {
			Debug.Log("Warning: Unable to move in column " + (column + 1) + ".");
		}

		return rowPlaced;
	}

	public int GetPlayerTurn()
    {
		return isYellowsTurn ? 0 : 1;
	}

	public List<int> GetPossibleMoves()
	{
		List<int> possibleMoves = new List<int>();

		for (int x = 0; x < GameController.numColumns; x++)
		{
			for (int y = GameController.numRows - 1; y >= 0; y--)
			{
				if (field[x, y] == (int)Piece.Empty)
				{
					possibleMoves.Add(x);
					break;
				}
			}
		}

		return possibleMoves;
	}

	// Check if the game is won, drawn or undecided.
	// Note: This method could probably be made more efficient, but it does its job.
	public Result GetResult()
	{
		// Assuming no invald moves have been made, we only need to check if
		// the player who *previously* moved has won.
		byte p  = isYellowsTurn ? (byte)Piece.Red : (byte)Piece.Yellow;

		// Check verticals
		for (int x = 0; x < GameController.numColumns; x++)
		{
			int consecutivePieces = 0;

			for (int y = GameController.numRows - 1; y >= 0; y--)
			{
				if (field[x, y] == (byte)Piece.Empty)
                {
					break;
                }
				else if (field[x, y] != p)
                {
					consecutivePieces = 0;
				}
				else
                {
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
                    {
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}
			}
		}

		// Check horizontals
		for (int y = GameController.numRows - 1; y >= 0; y--)
		{
			int consecutivePieces = 0;

			for (int x = 0; x < GameController.numColumns; x++)
			{
				if (field[x, y] != p)
				{
					consecutivePieces = 0;
				}
				else
				{
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
					{
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}
			}
		}

		// Check diagonals (part 1)
		for (int x = 0; x < (GameController.numColumns - GameController.numPiecesToWin + 1); x++)
		{
			int consecutivePieces = 0;
			int x_tmp = x;
			int y_tmp = GameController.numRows - 1;

			while (x_tmp < GameController.numColumns && y_tmp >= 0)
            {
				if (field[x_tmp, y_tmp] != p)
				{
					consecutivePieces = 0;
				}
				else
				{
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
					{
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}

				x_tmp++;
				y_tmp--;
            }
		}

		// Check diagonals (part 2)
		for (int y = GameController.numRows - 2; y >= (GameController.numPiecesToWin - 1); y--)
		{
			int consecutivePieces = 0;
			int x_tmp = 0;
			int y_tmp = y;

			while (x_tmp < GameController.numColumns && y_tmp >= 0)
			{
				if (field[x_tmp, y_tmp] != p)
				{
					consecutivePieces = 0;
				}
				else
				{
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
					{
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}

				x_tmp++;
				y_tmp--;
			}
		}

		// Check diagonals (part 3)
		for (int x = GameController.numColumns - 1; x >= (GameController.numPiecesToWin - 1); x--)
		{
			int consecutivePieces = 0;
			int x_tmp = x;
			int y_tmp = GameController.numRows - 1;

			while (x_tmp >= 0 && y_tmp >= 0)
			{
				if (field[x_tmp, y_tmp] != p)
				{
					consecutivePieces = 0;
				}
				else
				{
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
					{
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}

				x_tmp--;
				y_tmp--;
			}
		}

		// Check diagonals (part 4)
		for (int y = GameController.numRows - 2; y >= GameController.numPiecesToWin - 1; y--)
		{
			int consecutivePieces = 0;
			int x_tmp = GameController.numColumns - 1;
			int y_tmp = y;

			while (x_tmp >= 0 && y_tmp >= 0)
			{
				if (field[x_tmp, y_tmp] != p)
				{
					consecutivePieces = 0;
				}
				else
				{
					consecutivePieces++;
					if (consecutivePieces == GameController.numPiecesToWin)
					{
						return isYellowsTurn ? Result.RedWin : Result.YellowWin;
					}
				}

				x_tmp--;
				y_tmp--;
			}
		}

		if (FieldContainsEmptyCell())
        {
			return Result.Undecided;
		}
		else
        {
			return Result.Draw;
		}
	}

	// Check if it's still possible to make a move.
	bool FieldContainsEmptyCell()
	{
		for (int x = 0; x < GameController.numColumns; x++)
		{
			for (int y = 0; y < GameController.numRows; y++)
			{
				if (field[x, y] == (int)Piece.Empty)
                {
					return true;
				}
			}
		}
		return false;
	}

	public byte[,] GetBoard()
	{
		return (byte[,])field.Clone();
	}
}
