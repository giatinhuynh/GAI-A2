using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace ConnectFour
{
	public class GameController : MonoBehaviour 
	{
		public GameObject yellowAgent;
		public GameObject redAgent;

		private Agent[] agents;

		[Range(3, 8)]
		public static int numRows = 6;
		[Range(3, 8)]
		public static int numColumns = 7;

		[Tooltip("How many pieces have to be connected to win.")]
		public static int numPiecesToWin = 4;

		[Tooltip("Allow diagonally connected Pieces?")]
		public static bool allowDiagonally = true;
		
		public float dropTime = 4f;

		// Gameobjects 
		public GameObject pieceRed;
		public GameObject pieceYellow;
		public GameObject pieceField;
		public GameObject gameOverText;
		public GameObject btnPlayAgain;

		bool btnPlayAgainTouching = false;
		Color btnPlayAgainOrigColor;
		Color btnPlayAgainHoverColor = new Color(255, 143, 4);

		Connect4State state;

		GameObject gameObjectField;

		// temporary gameobject, holds the piece at mouse position until the mouse has clicked
		GameObject gameObjectTurn;

		bool isLoading = true;
		bool isDropping = false; 
		bool mouseButtonPressed = false;

		// Use this for initialization
		void Start () 
		{
			GameObject[] players = new GameObject[] { yellowAgent, redAgent };
			agents = new Agent[2];

			for (int i = 0; i < 2; i++)
            {
				GameObject ag = Instantiate(players[i], Vector3.zero, Quaternion.identity);
				ag.name = "Player " + (i + 1);

				agents[i] = ag.GetComponent<Agent>();
				if (agents[i] != null)
                {
					agents[i].setPlayerIdx(i);
				}
			}

			int max = Mathf.Max(numRows, numColumns);

			if(numPiecesToWin > max)
				numPiecesToWin = max;

			CreateField ();

			btnPlayAgainOrigColor = btnPlayAgain.GetComponent<Renderer>().material.color;
		}

		/// <summary>
		/// Creates the field.
		/// </summary>
		void CreateField()
		{
			state = new Connect4State();

			gameOverText.SetActive(false);
			btnPlayAgain.SetActive(false);

			isLoading = true;

			gameObjectField = GameObject.Find ("Field");
			if(gameObjectField != null)
			{
				DestroyImmediate(gameObjectField);
			}
			gameObjectField = new GameObject("Field");

			// instantiate the cells
			for(int x = 0; x < numColumns; x++)
			{
				for(int y = 0; y < numRows; y++)
				{
					GameObject g = Instantiate(pieceField, new Vector3(x, y * -1, -1), Quaternion.identity);
					g.transform.parent = gameObjectField.transform;
				}
			}

			isLoading = false;

			// center camera
			Camera.main.transform.position = new Vector3(
				(numColumns-1) / 2.0f, -((numRows-1) / 2.0f), Camera.main.transform.position.z);

			gameOverText.transform.position = new Vector3(
				(numColumns-1) / 2.0f, -((numRows-1) / 2.0f) + 1, gameOverText.transform.position.z);

			btnPlayAgain.transform.position = new Vector3(
				(numColumns-1) / 2.0f, -((numRows-1) / 2.0f) - 1, btnPlayAgain.transform.position.z);
		}

		/// <summary>
		/// Spawns a piece at mouse position above the first row
		/// </summary>
		/// <returns>The piece.</returns>
		GameObject SpawnPiece()
		{
			Vector3 spawnPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			int playerIdx = state.GetPlayerTurn();

			// If non-human player's turn, get move from agent.
			if (agents[playerIdx] != null)
			{
				List<int> moves = state.GetPossibleMoves();

				if (moves.Count > 0)
				{
					int column = agents[playerIdx].GetMove(state);
					spawnPos = new Vector3(column, 0, 0);
				}
			}

			GameObject g = Instantiate(
					(state.GetPlayerTurn() == 0) ? pieceYellow : pieceRed,
					new Vector3(
					Mathf.Clamp(spawnPos.x, 0, numColumns-1), 
					gameObjectField.transform.position.y + 1, 0), // spawn it above the first row
					Quaternion.identity) as GameObject;

			return g;
		}

		void UpdatePlayAgainButton()
		{
			RaycastHit hit;

			// Ray shooting out of the camera from where the mouse is
			Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			
			if (Physics.Raycast(ray, out hit) && hit.collider.name == btnPlayAgain.name)
			{
				btnPlayAgain.GetComponent<Renderer>().material.color = btnPlayAgainHoverColor;
				
				//check if the left mouse has been pressed down this frame
				if (Input.GetMouseButtonDown(0) || Input.touchCount > 0 && btnPlayAgainTouching == false)
				{
					btnPlayAgainTouching = true;
					SceneManager.LoadScene(SceneManager.GetActiveScene().name);
				}
			}
			else
			{
				btnPlayAgain.GetComponent<Renderer>().material.color = btnPlayAgainOrigColor;
			}
			
			if(Input.touchCount == 0)
			{
				btnPlayAgainTouching = false;
			}
		}

		// Update is called once per frame
		void Update() 
		{
			if (isLoading)
				return;

			// Check if the game is over.
			Connect4State.Result result = state.GetResult();
			if (result != Connect4State.Result.Undecided)
			{
				if (result == Connect4State.Result.YellowWin)
                {
					gameOverText.GetComponent<TextMesh>().text = "Yellow wins!";
				}
				else if (result == Connect4State.Result.RedWin)
				{
					gameOverText.GetComponent<TextMesh>().text = "Red wins!";
				}
				else
				{
					gameOverText.GetComponent<TextMesh>().text = "Match drawn!";
				}

				gameOverText.SetActive(true);
				btnPlayAgain.SetActive(true);

				UpdatePlayAgainButton();

				return;
			}

			// If human player, spawn a piece to place manually.
			if (agents[state.GetPlayerTurn()] == null)
			{
				if (gameObjectTurn == null)
				{
					gameObjectTurn = SpawnPiece();
				}
				else
				{
					// update the object's position
					Vector3 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
					gameObjectTurn.transform.position = new Vector3(
						Mathf.Clamp(pos.x, 0, numColumns-1), 
						gameObjectField.transform.position.y + 1, 0);

					// click the left mouse button to drop the piece into the selected column
					if(Input.GetMouseButtonDown(0) && !mouseButtonPressed && !isDropping)
					{
						mouseButtonPressed = true;
						StartCoroutine(dropPiece(gameObjectTurn));
					}
					else
					{
						mouseButtonPressed = false;
					}
				}
			}
			else
			{
				if(gameObjectTurn == null)
				{
					gameObjectTurn = SpawnPiece();
				}
				else
				{
					if(!isDropping)
						StartCoroutine(dropPiece(gameObjectTurn));
				}
			}
		}

		/// <summary>
		/// This method searches for a empty cell and lets 
		/// the object fall down into this cell
		/// </summary>
		/// <param name="gObject">Game Object.</param>
		IEnumerator dropPiece(GameObject gObject)
		{
			isDropping = true;

			Vector3 startPosition = gObject.transform.position;
			Vector3 endPosition;

			// Get the grid cell where the piece is to be placed.
			int column = Mathf.RoundToInt(startPosition.x);
			int row = state.MakeMove(column);

			// Animate the move if it is valid.
			if (row >= 0)
			{
				startPosition = new Vector3(column, startPosition.y, startPosition.z);
				endPosition = new Vector3(column, row * -1, startPosition.z);

				// Instantiate a new Piece, disable the temporary
				GameObject g = Instantiate(gObject);
				gameObjectTurn.GetComponent<Renderer>().enabled = false;

				float distance = Vector3.Distance(startPosition, endPosition);

				float t = 0;
				while (t < 1)
				{
					t += Time.deltaTime * dropTime * (numRows - distance + 1);

					g.transform.position = Vector3.Lerp(startPosition, endPosition, t);
					yield return null;
				}

				g.transform.parent = gameObjectField.transform;
			}

			// Remove the temporary gameobject.
			DestroyImmediate(gameObjectTurn);

			isDropping = false;

			yield return 0;
		}
	}
}
